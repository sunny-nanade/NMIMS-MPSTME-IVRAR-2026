import argparse
import time

import numpy as np
from stable_baselines3.common.vec_env import DummyVecEnv, SubprocVecEnv

from mars_rover.rl.env_factory import make_env_fn
from mars_rover.rl.performance import print_resource_plan


def benchmark(train_dir: str, n_envs: int, transitions: int, seed: int):
    factories = [
        make_env_fn(train_dir, "continuous", False, seed + i * 1000,
                    randomize_physics=True, curriculum_enabled=True)
        for i in range(n_envs)
    ]
    vec_cls = SubprocVecEnv if n_envs > 1 else DummyVecEnv
    env = vec_cls(factories)
    env.reset()
    # Warm up Bullet/terrain caches before timing.
    for _ in range(max(5, 40 // n_envs)):
        actions = np.stack([env.action_space.sample() for _ in range(n_envs)])
        env.step(actions)

    vector_steps = max(1, int(np.ceil(transitions / n_envs)))
    start = time.perf_counter()
    for _ in range(vector_steps):
        actions = np.stack([env.action_space.sample() for _ in range(n_envs)])
        env.step(actions)
    elapsed = time.perf_counter() - start
    actual = vector_steps * n_envs
    env.close()
    return actual / elapsed, elapsed, actual


def main():
    ap = argparse.ArgumentParser(description="Benchmark raw headless PyBullet vector-environment throughput.")
    ap.add_argument("--train-dir", default="data/phase2/train")
    ap.add_argument("--workers", nargs="+", type=int, default=[1, 2, 4, 6])
    ap.add_argument("--transitions", type=int, default=2000)
    ap.add_argument("--seed", type=int, default=4411)
    args = ap.parse_args()

    print("Raw environment benchmark only (no neural-network updates).")
    print("Use it to reject worker counts that cause CPU/RAM contention; final PPO/SAC fps may differ.\n")
    rows = []
    for n in args.workers:
        if n not in (1, 2, 4, 6):
            raise ValueError("workers must be chosen from 1, 2, 4, 6")
        print_resource_plan("benchmark", n)
        fps, elapsed, actual = benchmark(args.train_dir, n, args.transitions, args.seed)
        rows.append((n, fps, elapsed, actual))
        print(f"[benchmark] {n} workers: {fps:.1f} transitions/s ({actual} transitions in {elapsed:.1f}s)\n")

    best = max(rows, key=lambda x: x[1])
    print("Summary")
    for n, fps, elapsed, actual in rows:
        print(f"  {n} worker(s): {fps:8.1f} transitions/s")
    print(f"Fastest raw PyBullet setting in this run: {best[0]} worker(s)")
    print("For a 16-GB laptop, prefer 4 or fewer for PPO and 2 for SAC unless 6 is clearly faster without paging.")


if __name__ == "__main__":
    main()
