import argparse
from pathlib import Path

import imageio.v2 as imageio
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
    ap.add_argument("--out", default=None)
    ap.add_argument("--device", default="cuda")
    args = ap.parse_args()

    device = torch.device(args.device if args.device != "cuda" or torch.cuda.is_available() else "cpu")
    files = terrain_files(args.terrain_dir)
    route = args.model == "d3qn"
    env = RoverEnv(files, action_mode="discrete" if route else "continuous", route_observation=route, render_mode="rgb_array", randomize_physics=False)

    if args.model == "d3qn":
        path = Path(f"checkpoints/d3qn/seed_{args.seed}/best_model.pt")
        obj = torch.load(path, map_location=device)
        model = DuelingQNetwork().to(device)
        model.load_state_dict(obj["model"])
        model.eval()
    elif args.model == "ppo":
        path = Path(f"checkpoints/ppo/seed_{args.seed}/best/best_model.zip")
        model = PPO.load(path, device=device)
    else:
        path = Path(f"checkpoints/sac/seed_{args.seed}/best/best_model.zip")
        model = SAC.load(path, device=device)

    out = Path(args.out or f"outputs/videos/{args.model}_seed{args.seed}.mp4")
    out.parent.mkdir(parents=True, exist_ok=True)
    writer = imageio.get_writer(out, fps=20, codec="libx264", quality=7)
    obs, _ = env.reset(seed=args.episode_seed)
    done = False
    frame_repeat = max(1, int(round(env.control_dt * 20)))
    while not done:
        if args.model == "d3qn":
            with torch.no_grad():
                action = int(model(torch.as_tensor(obs["terrain"][None], device=device), torch.as_tensor(obs["state"][None], device=device)).argmax(1).item())
        else:
            action, _ = model.predict(obs, deterministic=True)
        obs, _, term, trunc, info = env.step(action)
        frame = env.render_frame()
        for _ in range(frame_repeat):
            writer.append_data(frame)
        done = term or trunc
    writer.close()
    env.close()
    print("Saved", out)
    print("Episode metrics:", info.get("episode_metrics"))


if __name__ == "__main__":
    main()
