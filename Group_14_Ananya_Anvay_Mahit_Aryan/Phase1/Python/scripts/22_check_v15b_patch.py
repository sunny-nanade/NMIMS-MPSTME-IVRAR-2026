from __future__ import annotations

from pathlib import Path
import numpy as np

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def main():
    files = terrain_files("data/phase2/train")
    env = RoverEnv(
        files,
        action_mode="continuous",
        route_observation=False,
        randomize_physics=True,
        sensor_preview=False,
        curriculum_enabled=True,
        v15b_mode=True,
    )
    env.set_curriculum_progress(0.0)
    obs, info = env.reset(seed=31001)

    assert obs["terrain"].shape == (5, 32, 32)
    assert obs["terrain"].dtype == np.uint8
    assert obs["state"].shape == (14,)
    assert obs["state"].dtype == np.float32

    print("V1.5b patch loaded: PASS")
    print("terrain:", info["terrain"], "difficulty:", info["difficulty"])
    print("DR episode:", info["unity_domain_randomization"])
    print("\nZero-action loophole test: the rover should terminate for NO GOAL PROGRESS")

    done = False
    steps = 0
    last = None
    while not done and steps < 150:
        obs, reward, term, trunc, last = env.step(np.array([0.0, 0.0], dtype=np.float32))
        done = term or trunc
        steps += 1

    env.close()
    if not done:
        raise RuntimeError("Zero-action policy was not terminated within 150 controls.")
    reason = last.get("terminal_reason")
    if reason != "stuck":
        raise RuntimeError(f"Expected stuck/no-progress termination, got {reason!r}")

    metrics = last.get("episode_metrics", {})
    print(f"terminated after {steps} controls ({steps * 0.12:.1f} s)")
    print("terminal_reason:", reason)
    print("goal_progress_window_m:", metrics.get("goal_progress_window_m"))
    print("episode reward:", metrics.get("reward"))
    print("ZERO-ACTION LOOPHOLE BLOCKED: PASS")
    print("\nV1.5b SELF-TEST: PASS")


if __name__ == "__main__":
    main()
