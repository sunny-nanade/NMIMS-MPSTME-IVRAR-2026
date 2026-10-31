from __future__ import annotations

import argparse
import csv
import os
import time
from functools import partial
from pathlib import Path

import numpy as np
from stable_baselines3 import SAC
from stable_baselines3.common.vec_env import SubprocVecEnv

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


class ScheduledEnv(RoverEnv):
    def __init__(self, *args, seed_schedule, **kwargs):
        self._schedule = list(map(int, seed_schedule))
        self._i = 0
        self._seed_now = self._schedule[0]
        super().__init__(*args, **kwargs)

    def reset(self, *, seed=None, options=None):
        if self._i < len(self._schedule):
            seed = self._schedule[self._i]
            self._i += 1
        else:
            seed = self._schedule[-1]
        self._seed_now = int(seed)
        obs, info = super().reset(seed=self._seed_now, options=options)
        info = dict(info)
        info["benchmark_seed"] = self._seed_now
        return obs, info

    def step(self, action):
        obs, r, t, tr, info = super().step(action)
        info = dict(info)
        info["benchmark_seed"] = self._seed_now
        return obs, r, t, tr, info


def factory(terrain_dir, schedule, regime):
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    os.environ["NUMEXPR_NUM_THREADS"] = "1"
    dr = regime == "dr"
    env = ScheduledEnv(
        terrain_files(terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=dr,
        sensor_preview=False,
        curriculum_enabled=False,
        v15e_mode=True,
        dr_force_episode=True if dr else False,
        dr_fixed_strength=0.50 if dr else 0.0,
        seed_schedule=schedule,
    )
    env.set_v15d_stage(2)
    return env


def divisor(n, requested):
    for w in range(min(n, requested), 0, -1):
        if n % w == 0:
            return w
    return 1


def run_one(path, label, terrain_dir, regime, episodes, base_seed, workers, device):
    workers = divisor(episodes, workers)
    seeds = list(range(base_seed, base_seed + episodes))
    schedules = [seeds[i::workers] for i in range(workers)]
    env = SubprocVecEnv(
        [partial(factory, terrain_dir, s, regime) for s in schedules],
        start_method="spawn",
    )
    model = SAC.load(path, device=device)
    completed = {}
    try:
        obs = env.reset()
        while len(completed) < episodes:
            actions, _ = model.predict(obs, deterministic=True)
            obs, _, dones, infos = env.step(actions)
            for done, info in zip(dones, infos):
                if done and info.get("episode_metrics") is not None:
                    s = int(info["benchmark_seed"])
                    if base_seed <= s < base_seed + episodes and s not in completed:
                        rec = dict(info["episode_metrics"])
                        rec.update(
                            model=label,
                            regime=regime,
                            episode_seed=s,
                            model_num_timesteps=int(getattr(model, "num_timesteps", -1)),
                        )
                        completed[s] = rec
        return [completed[s] for s in sorted(completed)]
    finally:
        env.close()


def mean(rs, key, default=0.0):
    return float(np.mean([r.get(key, default) for r in rs]))


def summarize(rs):
    return {
        "model": rs[0]["model"],
        "regime": rs[0]["regime"],
        "episodes": len(rs),
        "model_num_timesteps": rs[0]["model_num_timesteps"],
        "success_rate": mean(rs, "success"),
        "collision_rate": mean(rs, "collision"),
        "stuck_rate": float(np.mean([r.get("terminal_reason") == "stuck" for r in rs])),
        "time_limit_rate": float(np.mean([r.get("terminal_reason") == "time_limit" for r in rs])),
        "goal_progress": mean(rs, "goal_progress"),
        "route_progress": mean(rs, "route_progress"),
        "final_goal_distance_m": mean(rs, "final_goal_distance_m"),
        "final_route_remaining_m": mean(rs, "final_route_remaining_m"),
        "mean_route_deviation_m": mean(rs, "mean_route_deviation_m"),
        "min_clearance_m": mean(rs, "min_clearance_m"),
        "mean_abs_steer": mean(rs, "mean_abs_steer"),
        "mean_steer_bias": float(np.mean([abs(r.get("mean_signed_steer", 0.0)) for r in rs])),
        "mean_signed_throttle": mean(rs, "mean_signed_throttle"),
        "positive_route_progress_fraction": mean(rs, "positive_route_progress_fraction"),
        "mean_reward": mean(rs, "reward"),
    }


def write_csv(path, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    keys, seen = [], set()
    for r in rows:
        for k in r:
            if k not in seen:
                seen.add(k)
                keys.append(k)
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=keys)
        w.writeheader()
        w.writerows(rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=61)
    ap.add_argument("--terrain-dir", default="data/phase2/test")
    ap.add_argument("--episodes", type=int, default=100)
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--base-seed", type=int, default=120000)
    args = ap.parse_args()

    root = Path(f"checkpoints/sac/seed_{args.seed}")
    candidates = [
        ("best", root / "best" / "best_model.zip"),
        ("50k", root / "sac_50000_steps.zip"),
        ("100k", root / "sac_100000_steps.zip"),
        ("50k", root / "sac_50000_steps.zip"),
        ("100k", root / "sac_100000_steps.zip"),
        ("150k", root / "sac_150000_steps.zip"),
        ("250k", root / "sac_250000_steps.zip"),
        ("500k", root / "sac_500000_steps.zip"),
        ("750k", root / "sac_750000_steps.zip"),
        ("1m", root / "sac_1000000_steps.zip"),
    ]
    candidates = [x for x in candidates if x[1].exists()]
    if not candidates:
        raise FileNotFoundError(f"No checkpoints found under {root}")

    all_rows, summaries = [], []
    t0 = time.perf_counter()
    for regime in ("clean", "dr"):
        for label, path in candidates:
            print(f"{regime.upper():5s} {label:6s} ...", flush=True)
            rs = run_one(
                path, label, args.terrain_dir, regime,
                args.episodes, args.base_seed, args.workers, args.device
            )
            all_rows.extend(rs)
            summaries.append(summarize(rs))

    out = Path(f"results/sac_v15e_seed_{args.seed}")
    write_csv(out / "benchmark_episodes.csv", all_rows)
    write_csv(out / "benchmark_summary.csv", summaries)

    print("\nregime model success coll stuck goalProg routeProg final_m route_m dev_m throttle")
    for s in summaries:
        print(
            f"{s['regime']:6s} {s['model']:6s} "
            f"{s['success_rate']*100:6.1f}% {s['collision_rate']*100:5.1f}% "
            f"{s['stuck_rate']*100:5.1f}% {s['goal_progress']:8.3f} "
            f"{s['route_progress']:9.3f} {s['final_goal_distance_m']:7.2f} "
            f"{s['final_route_remaining_m']:7.2f} {s['mean_route_deviation_m']:6.2f} "
            f"{s['mean_signed_throttle']:8.3f}"
        )
    print(f"\nSaved to {out} | elapsed {(time.perf_counter()-t0)/60:.1f} min")


if __name__ == "__main__":
    main()
