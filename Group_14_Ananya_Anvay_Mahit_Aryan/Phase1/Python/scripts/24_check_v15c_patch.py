from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--seed", type=int, default=5151)
    args = ap.parse_args()

    files = terrain_files(args.terrain_dir)
    env = RoverEnv(
        files,
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=False,
        sensor_preview=False,
        curriculum_enabled=True,
        v15c_mode=True,
    )

    print("V1.5c PRE-FLIGHT CHECK")
    print("=" * 72)

    expected = {
        0.00: (0.60, 0.30, 0.10, 0.40, 0.10, 0.35),
        0.20: (0.475, 0.375, 0.15, 0.50, 0.15, 0.50),
        0.40: (0.35, 0.45, 0.20, 0.60, 0.20, 0.65),
        1.00: (0.35, 0.45, 0.20, 0.60, 0.20, 0.65),
    }

    for p, exp in expected.items():
        env.set_curriculum_progress(p)
        s = env.v15c_schedule_snapshot()
        got = (
            s["easy_weight"], s["medium_weight"], s["hard_weight"],
            s["dr_probability"], s["dr_strength_min"], s["dr_strength_max"],
        )
        if not np.allclose(got, exp, atol=1e-9):
            raise RuntimeError(f"Schedule mismatch at progress={p}: got={got}, expected={exp}")
        print(
            f"progress={p:4.0%} | terrain={got[0]:.3f}/{got[1]:.3f}/{got[2]:.3f} | "
            f"DR prob={got[3]:.3f} | strength={got[4]:.3f}-{got[5]:.3f}"
        )

    # Verify the new reward constants are present through a real zero-action episode.
    env.set_curriculum_progress(0.0)
    obs, _ = env.reset(seed=args.seed)
    done = False
    info = {}
    total = 0.0
    for i in range(220):
        obs, reward, term, trunc, info = env.step(
            np.array([0.0, 0.0], dtype=np.float32)
        )
        total += float(reward)
        if term or trunc:
            done = True
            break

    metrics = info.get("episode_metrics", {})
    reason = metrics.get("terminal_reason", info.get("terminal_reason", ""))
    progress = float(metrics.get("goal_progress", 0.0))
    print("\nZero-action anti-loophole probe")
    print(f"  terminal reason : {reason}")
    print(f"  goal progress   : {progress:.4f}")
    print(f"  total reward    : {total:.2f}")

    if not done:
        raise RuntimeError("Zero-action episode did not terminate within 220 controls.")
    if reason not in {"stuck", "collision", "rollover", "out_of_bounds"}:
        raise RuntimeError(
            f"Zero-action episode ended unexpectedly with reason={reason!r}."
        )
    if total >= 0.0:
        raise RuntimeError("Zero-action behavior obtained non-negative return.")

    env.close()

    print("\nReward structure")
    print("  full normalized progress : +400")
    print("  success terminal         : +300 (+0..40 efficiency)")
    print("  stuck/time terminal      : -225")
    print("  collision terminal       : -250")
    print("  out-of-bounds terminal   : -275")
    print("  rollover terminal        : -300")
    print("\nV1.5c PRE-FLIGHT: PASS")
    print("Next run scripts/25_train_sac_v15c.py. It will automatically gate itself at 200k.")


if __name__ == "__main__":
    main()
