from __future__ import annotations

import argparse
import os
from pathlib import Path

import numpy as np

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--seed", type=int, default=123)
    args = ap.parse_args()

    os.environ["MARS_ROVER_UNITY_DR"] = "1"

    files = terrain_files(args.terrain_dir)
    env = RoverEnv(
        files,
        action_mode="continuous",
        route_observation=False,
        randomize_physics=True,
        sensor_preview=False,
        curriculum_enabled=True,
    )

    print("Balanced Unity DR smoke test")
    print("=" * 72)

    for progress in (0.0, 0.5, 1.0):
        env.set_curriculum_progress(progress)
        obs, info = env.reset(seed=args.seed + int(progress * 1000))
        dr = info.get("unity_domain_randomization", {})
        print(f"\nprogress={progress:.0%}")
        for k, v in dr.items():
            print(f"  {k:28s}: {v}")
        print("  terrain shape/dtype/range   :", obs["terrain"].shape, obs["terrain"].dtype,
              int(obs["terrain"].min()), int(obs["terrain"].max()))
        print("  state shape/dtype/range     :", obs["state"].shape, obs["state"].dtype,
              float(obs["state"].min()), float(obs["state"].max()))

        assert obs["terrain"].shape == (5, 32, 32)
        assert obs["terrain"].dtype == np.uint8
        assert obs["state"].shape == (14,)
        assert obs["state"].dtype == np.float32
        assert np.isfinite(obs["state"]).all()

        action = np.array([0.10, 0.50], dtype=np.float32)
        for _ in range(5):
            obs, reward, term, trunc, step_info = env.step(action)
            assert np.isfinite(float(reward))
            assert np.isfinite(obs["state"]).all()
            if term or trunc:
                break

    env.close()

    # Confirm evaluation mode ignores the environment variable.
    eval_env = RoverEnv(
        files,
        action_mode="continuous",
        route_observation=False,
        randomize_physics=False,
        sensor_preview=False,
        curriculum_enabled=False,
    )
    _, eval_info = eval_env.reset(seed=999)
    eval_dr = eval_info.get("unity_domain_randomization", {})
    eval_env.close()

    if eval_dr.get("enabled", True):
        raise RuntimeError("Evaluation env unexpectedly has Unity DR enabled.")

    print("\nEvaluation isolation: PASS")
    print("BALANCED UNITY DOMAIN RANDOMIZATION SELF-TEST: PASS")


if __name__ == "__main__":
    main()
