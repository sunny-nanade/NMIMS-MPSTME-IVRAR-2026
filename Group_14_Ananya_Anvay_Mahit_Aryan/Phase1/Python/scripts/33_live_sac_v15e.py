import argparse
import time
from pathlib import Path

import torch
from stable_baselines3 import SAC

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--seed', type=int, default=61)
    ap.add_argument('--terrain-dir', default='data/phase2/test')
    ap.add_argument('--episode-seed', type=int, default=120000)
    ap.add_argument('--device', default='cuda')
    ap.add_argument('--checkpoint', default='best',
                    help="'best' or explicit .zip path")
    args = ap.parse_args()

    device = args.device if args.device != 'cuda' or torch.cuda.is_available() else 'cpu'
    if args.checkpoint == 'best':
        path = Path(f'checkpoints/sac/seed_{args.seed}/best/best_model.zip')
    else:
        path = Path(args.checkpoint)

    model = SAC.load(path, device=device)
    env = RoverEnv(
        terrain_files(args.terrain_dir),
        action_mode='continuous',
        route_observation=False,
        render_mode='human',
        randomize_physics=False,
        sensor_preview=False,
        curriculum_enabled=False,
        v15e_mode=True,
    )

    print('Loaded:', path)
    print('IMPORTANT: V1.5e live demo uses moving-A* bearing in state[1:3].')
    obs, _ = env.reset(seed=args.episode_seed)
    done = False
    k = 0
    while not done:
        action, _ = model.predict(obs, deterministic=True)
        obs, _, term, trunc, info = env.step(action)
        done = bool(term or trunc)
        k += 1
        if k % 25 == 0:
            a = action.reshape(-1)
            print(
                f"step={k:4d} steer={float(a[0]):+.3f} throttle={float(a[1]):+.3f} "
                f"goal={float(info.get('goal_distance_m', float('nan'))):.1f}m "
                f"routeProg={float(info.get('route_progress_m', 0.0)):+.3f}m "
                f"routeSpeed={float(info.get('route_forward_speed_mps', 0.0)):+.3f}m/s"
            )
        time.sleep(env.control_dt)

    print('Episode metrics:', info.get('episode_metrics'))
    time.sleep(2.0)
    env.close()


if __name__ == '__main__':
    main()
