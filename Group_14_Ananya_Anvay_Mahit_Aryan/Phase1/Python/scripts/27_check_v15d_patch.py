from __future__ import annotations

import argparse
import math

import numpy as np

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--seed", type=int, default=51515)
    args = ap.parse_args()

    env = RoverEnv(
        terrain_files(args.terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=False,
        sensor_preview=False,
        curriculum_enabled=True,
        v15d_mode=True,
    )
    env.set_v15d_stage(0)
    obs, info = env.reset(seed=args.seed)

    print("V1.5d PRE-FLIGHT")
    print("=" * 76)

    # 1) Scientific-integrity check: A* is hidden from SAC input.
    if int(obs["terrain"][4].max()) != 0:
        raise RuntimeError("Route channel leaked into standalone SAC terrain observation.")
    if not np.allclose(obs["state"][12:14], 0.0):
        raise RuntimeError("A* route state leaked into standalone SAC state observation.")
    print("PASS  A* route remains hidden from SAC observation")

    # 2) Stage schedule contract.
    expected = [
        (0, 0.60, 0.30, 0.10, 0.30, 0.10, 0.35),
        (1, 0.45, 0.40, 0.15, 0.45, 0.15, 0.50),
        (2, 0.35, 0.45, 0.20, 0.60, 0.20, 0.65),
    ]
    for row in expected:
        stage, e, m, h, dp, lo, hi = row
        env.set_v15d_stage(stage)
        s = env.v15d_stage_snapshot()
        got = (
            s["easy_weight"], s["medium_weight"], s["hard_weight"],
            s["dr_probability"], s["dr_strength_min"], s["dr_strength_max"],
        )
        want = (e, m, h, dp, lo, hi)
        if not np.allclose(got, want, atol=1e-12):
            raise RuntimeError(f"Stage {stage} mismatch: got={got} expected={want}")
        print(
            f"PASS  stage {stage}: terrain={e:.2f}/{m:.2f}/{h:.2f}, "
            f"DR={dp:.2f}, strength={lo:.2f}-{hi:.2f}"
        )

    # 3) Hidden A* potential must decrease along the stored A* path.
    path = env.path_xy
    if len(path) < 10:
        raise RuntimeError("Generated A* path unexpectedly short.")

    sample_indices = sorted(set([
        0,
        max(1, len(path)//4),
        max(2, len(path)//2),
        max(3, 3*len(path)//4),
        len(path)-1,
    ]))
    remaining = []
    for idx in sample_indices:
        env.reward_path_index = max(0, idx - 1)
        pos = path[idx].astype(np.float64)
        yaw = 0.0
        rem, dev, err, steer, got_idx = env._route_reward_features(pos, yaw)
        remaining.append(rem)
        if not (0.0 <= dev < 1e-5):
            raise RuntimeError(f"Path-point deviation should be ~0, got {dev}")
        if not (-1.0 <= steer <= 1.0):
            raise RuntimeError("Target steer outside [-1,1].")
    if any(remaining[i+1] > remaining[i] + 1e-6 for i in range(len(remaining)-1)):
        raise RuntimeError(f"Route remaining not monotone along A*: {remaining}")
    if remaining[-1] > 1e-4:
        raise RuntimeError(f"Goal route remaining should be ~0, got {remaining[-1]}")
    print("PASS  hidden A* feasible remaining distance decreases along route")

    # 4) Steering target should point toward lookahead with correct sign.
    env.reward_path_index = 0
    pos = env.path_xy[0].astype(np.float64)
    _, _, err, target, _ = env._route_reward_features(pos, 0.0)
    if abs(err) > 1e-4 and math.copysign(1.0, target) != math.copysign(1.0, err):
        raise RuntimeError("Target steering sign does not match route heading error.")
    print("PASS  route-target steering sign convention is correct")

    # 5) Zero-action loophole must still terminate negatively.
    env.set_v15d_stage(0)
    obs, _ = env.reset(seed=args.seed + 1)
    total = 0.0
    done = False
    last_info = {}
    for _ in range(240):
        obs, reward, term, trunc, last_info = env.step(
            np.array([0.0, 0.0], dtype=np.float32)
        )
        total += float(reward)
        if term or trunc:
            done = True
            break
    metrics = last_info.get("episode_metrics", {})
    reason = metrics.get("terminal_reason", last_info.get("terminal_reason", ""))
    if not done:
        raise RuntimeError("Zero-action policy did not terminate.")
    if reason != "stuck":
        raise RuntimeError(f"Expected zero-action stuck termination; got {reason!r}")
    if total >= 0.0:
        raise RuntimeError(f"Zero-action return should be negative, got {total:.2f}")
    print(f"PASS  zero-action loophole blocked: reason={reason}, return={total:.2f}")

    env.close()

    print("\nReward contract")
    print("  hidden A* feasible-route progress : dominant +400 / complete route")
    print("  steering alignment bonus          : <= +0.012/step, progress-gated")
    print("  forward throttle bonus            : <= +0.010/step, progress-gated")
    print("  success                            : +300 + up to +40 efficiency")
    print("  stuck/time                         : -225")
    print("  collision                          : -250")
    print("  out-of-bounds                      : -275")
    print("  rollover                           : -300")
    print("\nV1.5d PRE-FLIGHT: PASS")


if __name__ == "__main__":
    main()
