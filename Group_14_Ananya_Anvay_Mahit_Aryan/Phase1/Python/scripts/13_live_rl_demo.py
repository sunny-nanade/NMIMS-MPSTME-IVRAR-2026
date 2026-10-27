import argparse
import time
from pathlib import Path

import torch
from stable_baselines3 import PPO, SAC

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files
from mars_rover.rl.d3qn import DuelingQNetwork


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", choices=["d3qn", "ppo", "sac"], required=True)
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--terrain-dir", default="data/phase2/test")
    ap.add_argument("--episode-seed", type=int, default=120000)
    ap.add_argument("--device", default="cuda")
    args = ap.parse_args()

    device = torch.device(args.device if args.device != "cuda" or torch.cuda.is_available() else "cpu")
    files = terrain_files(args.terrain_dir)

    if args.model == "d3qn":
        path = Path(f"checkpoints/d3qn/seed_{args.seed}/best_model.pt")
        obj = torch.load(path, map_location=device)
        model = DuelingQNetwork().to(device)
        model.load_state_dict(obj["model"])
        model.eval()
        env = RoverEnv(files, action_mode="discrete", route_observation=True, render_mode="human", randomize_physics=False)
    elif args.model == "ppo":
        path = Path(f"checkpoints/ppo/seed_{args.seed}/best/best_model.zip")
        model = PPO.load(path, device=device)
        env = RoverEnv(files, action_mode="continuous", route_observation=False, render_mode="human", randomize_physics=False)
    else:
        path = Path(f"checkpoints/sac/seed_{args.seed}/best/best_model.zip")
        model = SAC.load(path, device=device)
        env = RoverEnv(files, action_mode="continuous", route_observation=False, render_mode="human", randomize_physics=False)

    print("Loaded", path)
    print("Camera keys: 1=FREE, 2=CHASE, 3=TOP-DOWN, 4=OVERVIEW, F=FREE/CHASE, T=TOP/CHASE")
    obs, _ = env.reset(seed=args.episode_seed)
    done = False
    k = 0
    while not done:
        if args.model == "d3qn":
            with torch.no_grad():
                terr = torch.as_tensor(obs["terrain"][None], device=device)
                state = torch.as_tensor(obs["state"][None], device=device)
                action = int(model(terr, state).argmax(dim=1).item())
        else:
            action, _ = model.predict(obs, deterministic=True)
        obs, _, term, trunc, info = env.step(action)
        done = term or trunc
        k += 1
        if args.model != "d3qn" and k % 25 == 0:
            a = action.reshape(-1)
            print(
                f"step={k:4d} steer={float(a[0]):+.3f} throttle={float(a[1]):+.3f} "
                f"goal={float(info.get('goal_distance_m', float('nan'))):.1f} m "
                f"dGoal={env.last_progress_m:+.3f} m"
            )
        time.sleep(env.control_dt)
    print("Episode metrics:", info.get("episode_metrics"))
    time.sleep(2.0)
    env.close()


if __name__ == "__main__":
    main()
