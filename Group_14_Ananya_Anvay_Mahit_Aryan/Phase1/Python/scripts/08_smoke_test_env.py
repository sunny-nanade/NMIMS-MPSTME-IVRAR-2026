import argparse
from pathlib import Path
import numpy as np
from gymnasium.utils.env_checker import check_env

from mars_rover.envs import RoverEnv


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--mode", choices=["continuous", "discrete"], default="continuous")
    ap.add_argument("--difficulty", choices=["all", "easy", "medium", "hard"], default="all")
    ap.add_argument("--steps", type=int, default=40)
    ap.add_argument("--gui", action="store_true")
    args = ap.parse_args()

    files = sorted(str(x) for x in Path(args.terrain_dir).glob("*.npz"))
    if args.difficulty != "all":
        files = [x for x in files if f"_{args.difficulty}_" in Path(x).name.lower()]
    if not files:
        raise FileNotFoundError(f"No terrain files for difficulty={args.difficulty} in {args.terrain_dir}")

    env = RoverEnv(
        files,
        action_mode=args.mode,
        route_observation=(args.mode == "discrete"),
        render_mode="human" if args.gui else None,
        randomize_physics=False,
    )
    if not args.gui:
        check_env(env, skip_render_check=True)
    else:
        print("GUI smoke test: skipping Gymnasium check_env reset sequence to avoid visual teleporting.")
    obs, info = env.reset(seed=123)
    print("rover model:", info.get("rover_model"))
    print("terrain difficulty:", info.get("difficulty"))
    print("terrain obs:", obs["terrain"].shape, obs["terrain"].dtype, obs["terrain"].min(), obs["terrain"].max())
    print("state obs:", obs["state"].shape, obs["state"].dtype)
    total = 0.0
    for i in range(args.steps):
        action = env.action_space.sample()
        obs, reward, terminated, truncated, info = env.step(action)
        total += reward
        if terminated or truncated:
            print("episode ended:", info.get("episode_metrics"))
            obs, info = env.reset(seed=124 + i)
    print("Smoke test complete. reward during sampled steps:", round(total, 3))
    env.close()


if __name__ == "__main__":
    main()
