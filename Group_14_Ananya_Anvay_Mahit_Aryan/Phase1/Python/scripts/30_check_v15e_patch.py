from __future__ import annotations

import argparse
import math
import numpy as np

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--seed", type=int, default=6161)
    args = ap.parse_args()

    env = RoverEnv(
        terrain_files(args.terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=False,
        sensor_preview=False,
        curriculum_enabled=True,
        v15e_mode=True,
    )
    env.set_v15d_stage(0)
    obs, info = env.reset(seed=args.seed)

    print("V1.5e PRE-FLIGHT")
    print("=" * 76)
    stage = env.v15d_stage_snapshot()
    print("Stage A:", stage)
    assert np.allclose(
        [stage["easy_weight"], stage["medium_weight"], stage["hard_weight"], stage["dr_probability"]],
        [0.70, 0.25, 0.05, 0.20],
    )

    # Observation contract: final distance remains state[0], while bearing must match
    # the moving A* lookahead; path slots and route image remain hidden.
    pos, _r, _p, yaw, *_ = env._rover_pose()
    s, rem, dev, err, target_steer, tangent, idx = env._route_guidance_features(
        pos[:2], yaw, lookahead_m=env.v15e_guidance_lookahead_m, update_index=False
    )
    state = obs["state"]
    bearing_from_state = math.atan2(float(state[1]), float(state[2]))
    bearing_error = abs((bearing_from_state - err + math.pi) % (2*math.pi) - math.pi)

    print(f"Initial route s          : {s:.3f} m")
    print(f"Initial remaining        : {rem:.3f} m")
    print(f"Initial cross-track      : {dev:.3f} m")
    print(f"A* moving bearing       : {math.degrees(err):+.2f} deg")
    print(f"state[1:3] bearing      : {math.degrees(bearing_from_state):+.2f} deg")
    print(f"bearing contract error  : {math.degrees(bearing_error):.4f} deg")
    print(f"target steer            : {target_steer:+.3f}")

    if bearing_error > math.radians(0.2):
        raise RuntimeError("state[1:3] does not match moving-A* guidance bearing")
    if abs(float(state[12])) > 1e-7 or abs(float(state[13])) > 1e-7:
        raise RuntimeError("A* route leaked into state[12:14]")
    if np.any(obs["terrain"][4] != 0):
        raise RuntimeError("A* route leaked into terrain channel 4")

    # Continuous arc projection should monotonically increase on synthetic points
    # sampled forward along the reference path.
    samples = []
    for frac in (0.0, 0.15, 0.30, 0.50, 0.75):
        target_s = frac * env.reference_path_length_m
        j = int(np.searchsorted(env.path_cumulative_m, target_s, side="right") - 1)
        j = int(np.clip(j, 0, len(env.path_xy)-2))
        a, b = env.path_xy[j], env.path_xy[j+1]
        seg = max(float(np.linalg.norm(b-a)), 1e-9)
        t = float(np.clip((target_s - env.path_cumulative_m[j]) / seg, 0, 1))
        xy = a + t*(b-a)
        ss, *_ = env._route_guidance_features(xy, yaw, update_index=False)
        samples.append(ss)
    print("Synthetic arc samples    :", [round(x,2) for x in samples])
    if not all(b >= a - 1e-5 for a,b in zip(samples, samples[1:])):
        raise RuntimeError("continuous route arc-length projection is not monotonic")

    # Hotfix-3 recovery contract:
    # zero action must NOT be killed at the old 120-step / 14.4-s warning,
    # but it must still terminate as stuck around the new ~24-s hard limit.
    total = 0.0
    reason = ""
    terminal_step = None
    saw_warning_after_120 = False

    for i in range(280):
        obs, reward, term, trunc, step_info = env.step(
            np.array([0.0, 0.0], np.float32)
        )
        total += float(reward)

        if i + 1 >= env.v15e_stall_warning_steps:
            saw_warning_after_120 = (
                saw_warning_after_120
                or bool(step_info.get("v15e_stall_warning_active", False))
            )

        if term or trunc:
            terminal_step = i + 1
            m = step_info.get("episode_metrics", {})
            reason = str(
                m.get("terminal_reason", step_info.get("terminal_reason", ""))
            )
            break

    print(f"Stall warning observed   : {saw_warning_after_120}")
    print(f"Zero-action terminal     : {reason} @ step {terminal_step}")
    print(f"Zero-action return       : {total:.2f}")

    if not saw_warning_after_120:
        raise RuntimeError("14.4-s stall warning was not observed")
    if terminal_step is not None and terminal_step <= env.v15e_stall_warning_steps:
        raise RuntimeError("recovery grace was not honored; terminated at warning stage")
    if reason != "stuck":
        raise RuntimeError(f"zero-action probe should eventually end stuck, got {reason!r}")
    if terminal_step is None or terminal_step < env.v15e_stuck_total_steps:
        raise RuntimeError(
            f"hard stuck fired too early: terminal_step={terminal_step}, "
            f"required >= {env.v15e_stuck_total_steps}"
        )
    if total >= 0:
        raise RuntimeError("zero-action behavior obtained non-negative return")

    env.close()
    # Exact trainer/environment interface contract that previously failed at 50k.
    # Verify the stage alias and the canonical reused V1.5d stage machinery agree.
    if int(env.v15d_stage) != 0 or int(env.v15e_stage) != 0:
        raise RuntimeError(
            f"Stage interface mismatch at init: v15d={env.v15d_stage}, v15e={env.v15e_stage}"
        )
    env.set_v15d_stage(1)
    if int(env.v15d_stage) != 1 or int(env.v15e_stage) != 1:
        raise RuntimeError(
            f"Stage interface mismatch after set: v15d={env.v15d_stage}, v15e={env.v15e_stage}"
        )
    snap = env.v15d_stage_snapshot()
    if int(snap["stage"]) != 1:
        raise RuntimeError(f"Stage snapshot mismatch: {snap}")
    env.set_v15d_stage(0)
    print("Trainer/stage interface contract: PASS")

    print("\nV1.5e PRE-FLIGHT: PASS")
    print("Observation/reward guidance aligned; 14.4s warning + ~24s recovery grace verified.")


if __name__ == "__main__":
    main()
