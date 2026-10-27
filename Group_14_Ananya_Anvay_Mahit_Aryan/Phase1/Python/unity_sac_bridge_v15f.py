from __future__ import annotations

import argparse
import json
import math
import socket
from pathlib import Path

import numpy as np
from scipy.ndimage import map_coordinates
from stable_baselines3 import SAC

from mars_rover.config import TerrainConfig
from mars_rover.terrain import build_cost_map


TC = TerrainConfig()


def _finite_pair(v, name: str) -> tuple[float, float]:
    if v is None or len(v) < 2:
        raise RuntimeError(f"packet field {name!r} must contain 2 values")
    a, b = float(v[0]), float(v[1])
    if not (math.isfinite(a) and math.isfinite(b)):
        raise RuntimeError(f"packet field {name!r} contains non-finite values: {v}")
    return a, b


class V15FUnityObservationAdapter:
    """Rebuild the V1.5f feed-forward SAC observation from Unity telemetry.

    V1.5f inference contract
    -----------------------
    terrain uint8 (5, 32, 32)
      0: relative elevation
      1: slope
      2: roughness
      3: obstacle occupancy (Unity LiDAR)
      4: all zeros (global A* route stays hidden)

    state float32 (14,)
      0: FINAL-goal distance / terrain diagonal
      1: sin(relative bearing to 6 m moving A* guidance target)
      2: cos(relative bearing to 6 m moving A* guidance target)
      3: forward speed / 1.3
      4: lateral speed / 1.0, positive rover-left
      5: yaw rate / 2.0, positive rover-left
      6: roll / 50 deg
      7: pitch / 50 deg
      8: slip
      9: synthetic battery
     10: previous SAC steer
     11: previous SAC throttle
     12: 0
     13: 0

    The PyTorch feature extractor itself divides terrain uint8 values by 255.
    Therefore this bridge MUST feed uint8 0..255, not pre-normalized 0..1.
    """

    def __init__(
        self,
        model: SAC,
        height_npy: str | Path,
        meta_json: str | Path,
        *,
        unity_origin_x: float = 0.0,
        unity_origin_z: float = 0.0,
        flip_x: bool = False,
        flip_z: bool = False,
    ):
        self.model = model
        self.height_path = Path(height_npy)
        self.meta_path = Path(meta_json)

        if not self.height_path.exists():
            raise FileNotFoundError(self.height_path)
        if not self.meta_path.exists():
            raise FileNotFoundError(self.meta_path)

        self.height = np.load(self.height_path).astype(np.float32, copy=False)
        self.meta = json.loads(self.meta_path.read_text(encoding="utf-8"))

        if self.height.ndim != 2:
            raise RuntimeError(
                f"height_m.npy must be 2-D, got shape {self.height.shape}"
            )
        if not np.isfinite(self.height).all():
            raise RuntimeError("height_m.npy contains NaN/Inf")

        self.H, self.W = self.height.shape
        self.size_x = float(self.meta["unity_size_x_m"])
        self.size_z = float(self.meta["unity_size_z_m"])
        self.origin_x = float(unity_origin_x)
        self.origin_z = float(unity_origin_z)
        self.flip_x = bool(flip_x)
        self.flip_z = bool(flip_z)

        self.dx = self.size_x / max(1, self.W - 1)
        self.dz = self.size_z / max(1, self.H - 1)
        self.res = 0.5 * (self.dx + self.dz)

        # Match RoverEnv static slope/roughness preprocessing. Unity obstacle
        # geometry is represented by LiDAR, so static obstacle input is zero here.
        zero_obstacles = np.zeros_like(self.height, dtype=bool)
        _, self.slope, rough = build_cost_map(
            self.height,
            zero_obstacles,
            self.res,
            TC.max_slope_deg,
            TC.slope_weight,
            TC.roughness_weight,
        )
        p95 = float(np.percentile(rough, 95))
        self.rough_norm = np.clip(
            rough / max(p95, 1e-6), 0.0, 1.0
        ).astype(np.float32)

        # RoverEnv uses hypot(rows, cols)*res rather than (n-1) physical diagonal.
        self.diag = max(1.0, math.hypot(self.H, self.W) * self.res)

        self.prev_action = np.zeros(2, dtype=np.float32)
        self.have_prev_action = False
        self.battery = 1.0
        self.last_seq: int | None = None
        self.last_sim_time: float | None = None

        sp = self.model.observation_space
        if not hasattr(sp, "spaces"):
            raise RuntimeError(f"Expected Dict observation space, got {sp!r}")
        terrain_shape = tuple(sp.spaces["terrain"].shape)
        state_shape = tuple(sp.spaces["state"].shape)
        if terrain_shape != (5, 32, 32) or state_shape != (14,):
            raise RuntimeError(
                "Model observation mismatch: "
                f"terrain={terrain_shape}, state={state_shape}"
            )

    def reset_episode_state(self) -> None:
        self.prev_action[:] = 0.0
        self.have_prev_action = False
        self.battery = 1.0

    def detect_unity_restart(self, packet: dict) -> bool:
        seq = int(packet.get("seq", 0))
        sim_time = float(packet.get("sim_time", 0.0))

        restarted = False
        if self.last_seq is not None and seq <= self.last_seq:
            restarted = True
        if (
            self.last_sim_time is not None
            and sim_time + 0.05 < self.last_sim_time
        ):
            restarted = True

        self.last_seq = seq
        self.last_sim_time = sim_time

        if restarted:
            self.reset_episode_state()
        return restarted

    def _map_world_to_array(self, X, Z):
        x = np.asarray(X, dtype=np.float32) - self.origin_x
        z = np.asarray(Z, dtype=np.float32) - self.origin_z

        if self.flip_x:
            x = self.size_x - x
        if self.flip_z:
            z = self.size_z - z

        col = x / max(self.size_x, 1e-6) * (self.W - 1)
        row = z / max(self.size_z, 1e-6) * (self.H - 1)
        return row, col

    def _sample(self, arr, X, Z, order: int):
        row, col = self._map_world_to_array(X, Z)
        coords = np.vstack([
            np.asarray(row).ravel(),
            np.asarray(col).ravel(),
        ])
        return map_coordinates(
            arr,
            coords,
            order=order,
            mode="nearest",
        ).reshape(np.asarray(X).shape)

    def _terrain_tensor(self, p: dict) -> np.ndarray:
        pos = p.get("position")
        if pos is None or len(pos) < 3:
            raise RuntimeError("packet field 'position' must contain x,y,z")
        x, z = float(pos[0]), float(pos[2])

        forward_x, forward_z = _finite_pair(p.get("forward_xz"), "forward_xz")
        norm = max(1e-8, math.hypot(forward_x, forward_z))
        forward_x /= norm
        forward_z /= norm

        # Unity object +X is rover-right when +Z is rover-forward.
        right_x = forward_z
        right_z = -forward_x

        n = 32
        half = 12.0  # trained local_span_m = 24 m
        forward = np.linspace(half, -half, n, dtype=np.float32)
        right = np.linspace(-half, half, n, dtype=np.float32)
        F, R = np.meshgrid(forward, right, indexing="ij")

        X = x + F * forward_x + R * right_x
        Z = z + F * forward_z + R * right_z

        elev = self._sample(self.height, X, Z, order=1)
        slope = self._sample(self.slope, X, Z, order=1)
        rough = self._sample(self.rough_norm, X, Z, order=1)

        ground = float(
            self._sample(
                self.height,
                np.array([[x]], dtype=np.float32),
                np.array([[z]], dtype=np.float32),
                order=1,
            )[0, 0]
        )

        elev01 = np.clip((elev - ground + 6.0) / 12.0, 0.0, 1.0)
        slope01 = np.clip(slope / 35.0, 0.0, 1.0)
        rough01 = np.clip(rough, 0.0, 1.0)

        lidar = np.asarray(p.get("occupancy_32x32", []), dtype=np.float32)
        if lidar.size != 32 * 32:
            raise RuntimeError(
                f"Unity LiDAR grid has {lidar.size} cells; expected 1024"
            )
        lidar = (lidar.reshape(32, 32) > 0.5).astype(np.float32)

        # V1.5f does NOT expose the global route image to the CNN.
        route = np.zeros((32, 32), dtype=np.float32)

        channels = np.stack(
            [elev01, slope01, rough01, lidar, route],
            axis=0,
        )
        terrain = np.rint(np.clip(channels, 0.0, 1.0) * 255.0).astype(np.uint8)
        return terrain

    def _advance_battery(self) -> None:
        if not self.have_prev_action:
            return
        steer = float(self.prev_action[0])
        throttle = float(self.prev_action[1])
        # Nominal V1.5f max speed = 0.95 m/s.
        command_effort = abs(throttle) * 0.95 + 0.30 * abs(steer)
        self.battery = max(
            0.0,
            self.battery - 0.00065 * command_effort,
        )

    def _state(self, p: dict) -> tuple[np.ndarray, dict]:
        self._advance_battery()

        pos = p.get("position")
        if pos is None or len(pos) < 3:
            raise RuntimeError("packet field 'position' must contain x,y,z")
        x, z = float(pos[0]), float(pos[2])

        # FINAL goal is used ONLY for state[0].
        gx, gz = _finite_pair(p.get("goal_xz"), "goal_xz")
        final_goal_dist = math.hypot(gx - x, gz - z)

        # Moving A* lookahead target is used for state[1:3]. Unity local +X is
        # rover-right, while PyBullet positive heading error/steer is rover-left.
        target_right, target_forward = _finite_pair(
            p.get("goal_local_xz"), "goal_local_xz"
        )
        rel_target = math.atan2(-target_right, target_forward)

        lv = p.get("local_velocity")
        if lv is None or len(lv) < 3:
            raise RuntimeError("packet field 'local_velocity' must contain x,y,z")
        v_lateral_left = -float(lv[0])
        v_forward = float(lv[2])

        yaw_rate_left = float(p.get("yaw_rate_rad_s", 0.0))
        roll = math.radians(float(p.get("roll_deg", 0.0)))
        pitch = math.radians(float(p.get("pitch_deg", 0.0)))
        slip = float(np.clip(float(p.get("slip", 0.0)), 0.0, 1.0))

        state = np.array(
            [
                np.clip(final_goal_dist / self.diag, 0.0, 1.0),
                math.sin(rel_target),
                math.cos(rel_target),
                np.clip(v_forward / 1.3, -1.0, 1.0),
                np.clip(v_lateral_left / 1.0, -1.0, 1.0),
                np.clip(yaw_rate_left / 2.0, -1.0, 1.0),
                np.clip(roll / math.radians(50.0), -1.0, 1.0),
                np.clip(pitch / math.radians(50.0), -1.0, 1.0),
                slip,
                np.clip(self.battery, 0.0, 1.0),
                float(
                    np.clip(
                        p.get("last_steer", self.prev_action[0]),
                        -1.0,
                        1.0,
                    )
                ),
                float(
                    np.clip(
                        p.get("last_throttle", self.prev_action[1]),
                        -1.0,
                        1.0,
                    )
                ),
                0.0,
                0.0,
            ],
            dtype=np.float32,
        )

        if not np.isfinite(state).all():
            raise RuntimeError(f"non-finite V1.5f state: {state}")

        dbg = {
            "final_goal_dist": final_goal_dist,
            "target_bearing_deg": math.degrees(rel_target),
            "target_local_right": target_right,
            "target_local_forward": target_forward,
            "v_forward": v_forward,
            "v_lateral_left": v_lateral_left,
            "slip": slip,
            "battery": self.battery,
            "lidar_min_m": float(p.get("lidar_min_m", 99.0)),
            "wheel_contacts": int(p.get("wheel_contacts", 0)),
            "applied_prev_steer": float(p.get("last_steer", 0.0)),
            "applied_prev_throttle": float(p.get("last_throttle", 0.0)),
        }
        return state, dbg

    def build(self, p: dict) -> tuple[dict[str, np.ndarray], dict]:
        restarted = self.detect_unity_restart(p)
        terrain = self._terrain_tensor(p)
        state, dbg = self._state(p)

        dbg.update(
            {
                "unity_restart": restarted,
                "lidar_cells": int(np.count_nonzero(terrain[3])),
                "elev_minmax": (int(terrain[0].min()), int(terrain[0].max())),
                "slope_minmax": (int(terrain[1].min()), int(terrain[1].max())),
                "rough_minmax": (int(terrain[2].min()), int(terrain[2].max())),
            }
        )
        return {"terrain": terrain, "state": state}, dbg

    def record_action(self, action: np.ndarray) -> None:
        a = np.asarray(action, dtype=np.float32).reshape(2)
        if not np.isfinite(a).all():
            raise RuntimeError(f"non-finite action: {a}")
        self.prev_action[:] = a
        self.have_prev_action = True


def main() -> None:
    ap = argparse.ArgumentParser(
        description="V1.5f best-model Unity UDP inference bridge"
    )
    ap.add_argument("--model", required=True)
    ap.add_argument("--height-npy", required=True)
    ap.add_argument("--meta", required=True)
    ap.add_argument("--listen-port", type=int, default=5005)
    ap.add_argument("--unity-port", type=int, default=5006)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--unity-origin-x", type=float, default=0.0)
    ap.add_argument("--unity-origin-z", type=float, default=0.0)
    ap.add_argument("--flip-x", action="store_true")
    ap.add_argument("--flip-z", action="store_true")
    ap.add_argument("--debug-every", type=int, default=10)
    ap.add_argument(
        "--self-test",
        action="store_true",
        help="Load model/terrain and validate contracts without opening UDP sockets.",
    )
    args = ap.parse_args()

    print("[V1.5f bridge] loading model:", Path(args.model).resolve())
    model = SAC.load(args.model, device=args.device)

    adapter = V15FUnityObservationAdapter(
        model,
        args.height_npy,
        args.meta,
        unity_origin_x=args.unity_origin_x,
        unity_origin_z=args.unity_origin_z,
        flip_x=args.flip_x,
        flip_z=args.flip_z,
    )

    print("[V1.5f bridge] model num_timesteps:", getattr(model, "num_timesteps", "?"))
    print("[V1.5f bridge] heightmap:", adapter.height_path.resolve())
    print(
        f"[V1.5f bridge] Unity terrain size X/Z = "
        f"{adapter.size_x:.2f}/{adapter.size_z:.2f} m"
    )
    print(
        f"[V1.5f bridge] grid={adapter.H}x{adapter.W} "
        f"res~{adapter.res:.4f} m, diag norm={adapter.diag:.2f} m"
    )
    print("[V1.5f bridge] observation terrain=(5,32,32) uint8; state=(14,) float32")
    print("[V1.5f bridge] ch4=ZERO, state[12:14]=ZERO")
    print("[V1.5f bridge] state[1:3] expects Unity 6 m moving A* target")

    if args.self_test:
        print("V1.5f UNITY BRIDGE SELF-TEST: PASS")
        return

    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", args.listen_port))
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    print(f"[V1.5f bridge] listening 127.0.0.1:{args.listen_port}")
    print(f"[V1.5f bridge] replies -> 127.0.0.1:{args.unity_port}")
    print("[V1.5f bridge] start Unity Play mode now; CTRL+C stops bridge")

    count = 0
    try:
        while True:
            raw, _ = rx.recvfrom(65535)
            p: dict = {}
            try:
                p = json.loads(raw.decode("utf-8"))
                obs, dbg = adapter.build(p)

                action, _ = model.predict(obs, deterministic=True)
                a = np.asarray(action, dtype=np.float32).reshape(2)
                if not np.isfinite(a).all():
                    raise RuntimeError(f"non-finite SAC action: {a}")
                a = np.clip(a, -1.0, 1.0)
                adapter.record_action(a)

                reply = {
                    "seq": int(p["seq"]),
                    "steer": float(a[0]),
                    "throttle": float(a[1]),
                    "ok": True,
                    "message": "ok",
                }

                count += 1
                if dbg["unity_restart"]:
                    print("[V1.5f bridge] detected Unity episode restart; battery/prev_action reset")

                if abs(dbg["v_forward"]) > 1.30:
                    print(
                        f"[V1.5f bridge] WARNING Unity forward speed "
                        f"{dbg['v_forward']:.2f} m/s is outside the training envelope "
                        f"(nominal max ~0.95 m/s)"
                    )

                if args.debug_every > 0 and (
                    count == 1 or count % args.debug_every == 0
                ):
                    s = obs["state"]
                    print(
                        f"[{count:05d}] seq={int(p['seq'])} "
                        f"final={dbg['final_goal_dist']:.1f}m "
                        f"target={dbg['target_bearing_deg']:+.1f}deg "
                        f"sin/cos=({s[1]:+.3f},{s[2]:+.3f}) "
                        f"vF={dbg['v_forward']:+.2f} "
                        f"vL={dbg['v_lateral_left']:+.2f} "
                        f"slip={dbg['slip']:.2f} "
                        f"lidar={dbg['lidar_cells']} "
                        f"lidarMin={dbg['lidar_min_m']:.2f}m "
                        f"contacts={dbg['wheel_contacts']} "
                        f"prevApplied=({dbg['applied_prev_steer']:+.2f},"
                        f"{dbg['applied_prev_throttle']:+.2f}) "
                        f"elev={dbg['elev_minmax']} "
                        f"rawAction=({a[0]:+.3f},{a[1]:+.3f})"
                    )

            except Exception as exc:
                print("[V1.5f bridge] ERROR:", repr(exc))
                reply = {
                    "seq": int(p.get("seq", 0)) if isinstance(p, dict) else 0,
                    "steer": 0.0,
                    "throttle": 0.0,
                    "ok": False,
                    "message": repr(exc),
                }

            tx.sendto(
                json.dumps(reply).encode("utf-8"),
                ("127.0.0.1", args.unity_port),
            )

    except KeyboardInterrupt:
        print("\n[V1.5f bridge] stopped")
    finally:
        rx.close()
        tx.close()


if __name__ == "__main__":
    main()
