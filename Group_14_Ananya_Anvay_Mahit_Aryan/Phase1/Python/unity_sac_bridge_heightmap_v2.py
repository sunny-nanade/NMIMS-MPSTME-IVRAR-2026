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


def wrap_pi(a: float) -> float:
    return (a + math.pi) % (2.0 * math.pi) - math.pi


class UnityHeightmapObservationAdapter:
    """
    Unity V0.1 SAC observation adapter.

    It uses the exact height_m.npy that was exported into Unity, not a separate NPZ
    terrain. That removes terrain-coordinate and "ghost obstacle" mismatches.

    Trained SAC contract:
      terrain uint8: (5,32,32)
        0 relative elevation
        1 slope
        2 roughness
        3 obstacle occupancy
        4 route (zero for standalone SAC)
      state float32: (14,)

    V0.1 obstacle channel is LiDAR-only because the Unity scene contains the
    physically spawned Unity rock prefabs. We do NOT inject an unrelated NPZ
    obstacle map that Unity is not physically simulating.
    """

    def __init__(
        self,
        model: SAC,
        height_npy: str,
        meta_json: str,
        flip_x: bool = False,
        flip_z: bool = False,
    ):
        self.model = model
        self.height = np.load(height_npy).astype(np.float32)
        self.meta = json.loads(Path(meta_json).read_text())

        if self.height.ndim != 2:
            raise RuntimeError(f"height_m.npy must be 2D, got {self.height.shape}")

        self.H, self.W = self.height.shape
        self.size_x = float(self.meta["unity_size_x_m"])
        self.size_z = float(self.meta["unity_size_z_m"])
        self.flip_x = bool(flip_x)
        self.flip_z = bool(flip_z)

        self.dx = self.size_x / max(1, self.W - 1)
        self.dz = self.size_z / max(1, self.H - 1)
        self.res = 0.5 * (self.dx + self.dz)

        # Recreate the same slope/roughness products used by RoverEnv.
        # Obstacles are intentionally zero here; Unity LiDAR supplies channel 3.
        zero_obstacle = np.zeros_like(self.height, dtype=bool)
        _, self.slope, rough = build_cost_map(
            self.height,
            zero_obstacle,
            self.res,
            TC.max_slope_deg,
            TC.slope_weight,
            TC.roughness_weight,
        )
        p95 = float(np.percentile(rough, 95))
        self.rough_norm = np.clip(
            rough / max(p95, 1e-6), 0.0, 1.0
        ).astype(np.float32)

        # RoverEnv uses hypot(rows, cols) * res, not physical (n-1) diagonal.
        self.diag = max(1.0, math.hypot(self.H, self.W) * self.res)

        self.prev_action = np.zeros(2, dtype=np.float32)
        self.have_prev = False
        self.battery = 1.0

        sp = model.observation_space
        tshape = tuple(sp.spaces["terrain"].shape)
        sshape = tuple(sp.spaces["state"].shape)
        if tshape != (5, 32, 32) or sshape != (14,):
            raise RuntimeError(
                f"Model observation mismatch: terrain={tshape}, state={sshape}"
            )

        print("[v2] exact Unity heightmap:", Path(height_npy).resolve())
        print(f"[v2] grid={self.H}x{self.W} size=({self.size_x:.3f},{self.size_z:.3f}) m")
        print(f"[v2] dx={self.dx:.4f}, dz={self.dz:.4f}, observation res={self.res:.4f}")
        print(f"[v2] trained local window reproduced as 32x32 over 24.0 m")
        print("[v2] obstacle channel = UNITY LIDAR ONLY")
        print("[v2] route channel/state = zero (standalone SAC)")

    def _map_world_to_array(self, X, Z):
        x = np.asarray(X, dtype=np.float32)
        z = np.asarray(Z, dtype=np.float32)

        if self.flip_x:
            x = self.size_x - x
        if self.flip_z:
            z = self.size_z - z

        col = x / max(self.size_x, 1e-6) * (self.W - 1)
        row = z / max(self.size_z, 1e-6) * (self.H - 1)
        return row, col

    def _sample(self, arr, X, Z, order, cval=0.0):
        row, col = self._map_world_to_array(X, Z)
        coords = np.vstack(
            [np.asarray(row).ravel(), np.asarray(col).ravel()]
        )
        return map_coordinates(
            arr,
            coords,
            order=order,
            mode="nearest" if order == 1 else "constant",
            cval=cval,
        ).reshape(np.asarray(X).shape)

    def _static_terrain_tensor(self, p: dict):
        x, _, z = map(float, p["position"])
        forward_x, forward_z = map(float, p["forward_xz"])

        nrm = max(1e-8, math.hypot(forward_x, forward_z))
        forward_x /= nrm
        forward_z /= nrm

        # Unity rover-right for a +Z-forward object.
        right_x = forward_z
        right_z = -forward_x

        n = 32
        half = 12.0
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

        lidar = np.asarray(p["occupancy_32x32"], dtype=np.float32)
        if lidar.size != 1024:
            raise RuntimeError(f"LiDAR grid size {lidar.size}, expected 1024")
        lidar = (lidar.reshape(32, 32) > 0.5).astype(np.float32)

        route = np.zeros((32, 32), dtype=np.float32)

        channels = np.stack(
            [elev01, slope01, rough01, lidar, route],
            axis=0,
        )
        return np.rint(np.clip(channels, 0, 1) * 255.0).astype(np.uint8)

    def _advance_battery(self):
        if not self.have_prev:
            return
        steer = float(self.prev_action[0])
        throttle = float(self.prev_action[1])
        command_effort = abs(throttle) * 0.95 + 0.30 * abs(steer)
        self.battery = max(
            0.0,
            self.battery - 0.00065 * command_effort
        )

    def _state(self, p: dict):
        self._advance_battery()

        x, _, z = map(float, p["position"])
        gx, gz = map(float, p["goal_xz"])
        goal_dist = math.hypot(gx - x, gz - z)

        goal_local = p["goal_local_xz"]
        goal_right = float(goal_local[0])
        goal_forward = float(goal_local[1])

        # RoverEnv positive relative-goal angle is rover-left.
        rel_goal = math.atan2(-goal_right, goal_forward)

        lv = p["local_velocity"]
        # Unity local +X is right; RoverEnv lateral feature is +left.
        v_lateral = -float(lv[0])
        v_forward = float(lv[2])

        yaw_rate = float(p.get("yaw_rate_rad_s", 0.0))
        roll = math.radians(float(p.get("roll_deg", 0.0)))
        pitch = math.radians(float(p.get("pitch_deg", 0.0)))
        slip = float(np.clip(float(p.get("slip", 0.0)), 0.0, 1.0))

        state = np.array(
            [
                np.clip(goal_dist / self.diag, 0.0, 1.0),
                math.sin(rel_goal),
                math.cos(rel_goal),
                np.clip(v_forward / 1.3, -1.0, 1.0),
                np.clip(v_lateral / 1.0, -1.0, 1.0),
                np.clip(yaw_rate / 2.0, -1.0, 1.0),
                np.clip(roll / math.radians(50.0), -1.0, 1.0),
                np.clip(pitch / math.radians(50.0), -1.0, 1.0),
                slip,
                np.clip(self.battery, 0.0, 1.0),
                float(self.prev_action[0]),
                float(self.prev_action[1]),
                0.0,
                0.0,
            ],
            dtype=np.float32,
        )

        return state, {
            "goal_dist": goal_dist,
            "rel_goal_deg": math.degrees(rel_goal),
            "v_forward": v_forward,
            "v_lateral": v_lateral,
            "slip": slip,
        }

    def build(self, p):
        terrain = self._static_terrain_tensor(p)
        state, dbg = self._state(p)
        dbg["lidar_cells"] = int(
            np.count_nonzero(terrain[3])
        )
        dbg["elev_minmax"] = (
            int(terrain[0].min()),
            int(terrain[0].max()),
        )
        dbg["slope_minmax"] = (
            int(terrain[1].min()),
            int(terrain[1].max()),
        )
        return {"terrain": terrain, "state": state}, dbg

    def record_action(self, a):
        self.prev_action[:] = np.asarray(a, dtype=np.float32).reshape(2)
        self.have_prev = True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--height-npy", required=True)
    ap.add_argument("--meta", required=True)
    ap.add_argument("--listen-port", type=int, default=5005)
    ap.add_argument("--unity-port", type=int, default=5006)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--flip-x", action="store_true")
    ap.add_argument("--flip-z", action="store_true")
    ap.add_argument("--debug-every", type=int, default=1)
    args = ap.parse_args()

    model = SAC.load(args.model, device=args.device)
    adapter = UnityHeightmapObservationAdapter(
        model,
        args.height_npy,
        args.meta,
        flip_x=args.flip_x,
        flip_z=args.flip_z,
    )

    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", args.listen_port))
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    print(f"[bridge-v2] listening 127.0.0.1:{args.listen_port}")
    print(f"[bridge-v2] Unity replies -> 127.0.0.1:{args.unity_port}")

    count = 0
    try:
        while True:
            raw, _ = rx.recvfrom(65535)
            p = {}
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
                if args.debug_every > 0 and (
                    count == 1 or count % args.debug_every == 0
                ):
                    s = obs["state"]
                    print(
                        f"[{count:05d}] "
                        f"goal={dbg['goal_dist']:.1f}m "
                        f"rel={dbg['rel_goal_deg']:+.1f}deg "
                        f"sin/cos=({s[1]:+.3f},{s[2]:+.3f}) "
                        f"vF={dbg['v_forward']:+.2f} "
                        f"vL={dbg['v_lateral']:+.2f} "
                        f"slip={dbg['slip']:.2f} "
                        f"lidarCells={dbg['lidar_cells']} "
                        f"elev={dbg['elev_minmax']} "
                        f"slope={dbg['slope_minmax']} "
                        f"action=({a[0]:+.3f},{a[1]:+.3f})"
                    )

            except Exception as e:
                print("[bridge-v2] ERROR:", repr(e))
                reply = {
                    "seq": int(p.get("seq", 0)) if isinstance(p, dict) else 0,
                    "steer": 0.0,
                    "throttle": 0.0,
                    "ok": False,
                    "message": repr(e),
                }

            tx.sendto(
                json.dumps(reply).encode("utf-8"),
                ("127.0.0.1", args.unity_port),
            )

    except KeyboardInterrupt:
        print("\n[bridge-v2] stopped")
    finally:
        rx.close()
        tx.close()


if __name__ == "__main__":
    main()
