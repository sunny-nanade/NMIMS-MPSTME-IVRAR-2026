import argparse
import math
import time
from pathlib import Path

import numpy as np
import pandas as pd
import torch
from stable_baselines3 import PPO, SAC

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files
from mars_rover.rl.d3qn import DuelingQNetwork


def load_d3qn(path, device):
    obj = torch.load(path, map_location=device)
    net = DuelingQNetwork().to(device)
    net.load_state_dict(obj["model"] if isinstance(obj, dict) and "model" in obj else obj)
    net.eval()
    return net


def wilson_interval(k: int, n: int, z: float = 1.96):
    if n <= 0:
        return float("nan"), float("nan")
    p = k / n
    den = 1.0 + z * z / n
    center = (p + z * z / (2.0 * n)) / den
    half = z * math.sqrt(p * (1.0 - p) / n + z * z / (4.0 * n * n)) / den
    return max(0.0, center - half), min(1.0, center + half)


def run_model(name, model, env, episodes, base_seed, device=None):
    rows = []
    for ep in range(episodes):
        episode_seed = base_seed + ep
        obs, _ = env.reset(seed=episode_seed)
        done = False
        inference = []
        while not done:
            t0 = time.perf_counter()
            if name == "A*+D3QN":
                with torch.no_grad():
                    terr = torch.as_tensor(obs["terrain"][None], device=device)
                    state = torch.as_tensor(obs["state"][None], device=device)
                    action = int(model(terr, state).argmax(dim=1).item())
            else:
                action, _ = model.predict(obs, deterministic=True)
            inference.append((time.perf_counter() - t0) * 1000.0)
            obs, _, term, trunc, info = env.step(action)
            done = term or trunc

        rec = dict(info["episode_metrics"])
        # A step-1 out-of-bounds event is an environment/start-state failure, not a
        # policy result.  With the boundary-safe patch this should never happen.
        if (
            rec.get("terminal_reason") == "out_of_bounds"
            and int(rec.get("steps", 999)) <= 2
            and float(rec.get("distance_m", 999.0)) < 0.25
        ):
            raise RuntimeError(
                f"Invalid immediate out-of-bounds episode: seed={episode_seed}, "
                f"terrain={rec.get('terrain')}. Re-run the boundary-safe patch/smoke test."
            )
        rec.update(
            {
                "model": name,
                "episode": ep,
                "episode_seed": episode_seed,
                "inference_ms_mean": float(np.mean(inference)) if inference else float("nan"),
            }
        )
        rows.append(rec)
    return rows


def _flat_agg(df, group_cols, metrics):
    out = df.groupby(group_cols)[metrics].agg(["mean", "std", "count"])
    out.columns = [f"{metric}_{stat}" for metric, stat in out.columns]
    return out.reset_index()


def _rate_table(df, group_cols):
    rows = []
    keys = group_cols if isinstance(group_cols, list) else [group_cols]
    grouper = keys[0] if len(keys) == 1 else keys
    for group_values, g in df.groupby(grouper):
        if not isinstance(group_values, tuple):
            group_values = (group_values,)
        base = dict(zip(keys, group_values))
        for metric in ("success", "collision", "rollover", "out_of_bounds"):
            n = int(len(g))
            k = int(g[metric].sum())
            lo, hi = wilson_interval(k, n)
            rows.append(
                {
                    **base,
                    "metric": metric,
                    "events": k,
                    "episodes": n,
                    "rate": k / n if n else float("nan"),
                    "ci95_low": lo,
                    "ci95_high": hi,
                }
            )
    return pd.DataFrame(rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/test")
    ap.add_argument("--episodes", type=int, default=30)
    ap.add_argument("--seeds", type=int, nargs="+", default=[11])
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--out", default="outputs/metrics/phase2_test_episodes.csv")
    args = ap.parse_args()

    device = torch.device(args.device if args.device != "cuda" or torch.cuda.is_available() else "cpu")
    files = terrain_files(args.terrain_dir)
    all_rows = []

    for seed in args.seeds:
        candidates = [
            ("A*+D3QN", Path(f"checkpoints/d3qn/seed_{seed}/best_model.pt")),
            ("PPO-CNN", Path(f"checkpoints/ppo/seed_{seed}/best/best_model.zip")),
            ("SAC-CNN", Path(f"checkpoints/sac/seed_{seed}/best/best_model.zip")),
        ]
        for name, path in candidates:
            if not path.exists():
                print("SKIP", name, "seed", seed, "missing", path)
                continue
            print("Evaluating", name, "seed", seed)
            if name == "A*+D3QN":
                model = load_d3qn(path, device)
                env = RoverEnv(files, action_mode="discrete", route_observation=True, randomize_physics=False)
            elif name == "PPO-CNN":
                model = PPO.load(path, device=device)
                env = RoverEnv(files, action_mode="continuous", route_observation=False, randomize_physics=False)
            else:
                model = SAC.load(path, device=device)
                env = RoverEnv(files, action_mode="continuous", route_observation=False, randomize_physics=False)
            rows = run_model(name, model, env, args.episodes, base_seed=120000, device=device)
            for r in rows:
                r["model_seed"] = seed
            all_rows.extend(rows)
            env.close()

    if not all_rows:
        raise RuntimeError("No trained checkpoints were found")

    df = pd.DataFrame(all_rows)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    df.to_csv(out, index=False)

    metrics = [
        "success", "collision", "rollover", "out_of_bounds", "reward", "distance_m",
        "goal_progress", "goal_progress_m", "path_efficiency", "energy_proxy",
        "mean_action_change", "mean_abs_steer", "mean_abs_throttle",
        "mean_signed_steer", "mean_signed_throttle", "mean_abs_yaw_rate",
        "mean_heading_alignment", "positive_progress_fraction",
        "mean_slip", "mean_wheel_contacts", "min_clearance_m",
        "max_roll_deg", "max_pitch_deg", "final_goal_distance_m", "inference_ms_mean",
    ]
    metrics = [m for m in metrics if m in df.columns]

    summary = _flat_agg(df, ["model"], metrics).round(4)
    summary_path = out.with_name(out.stem.replace("episodes", "summary") + ".csv")
    summary.to_csv(summary_path, index=False)

    by_diff = _flat_agg(df, ["model", "difficulty"], metrics).round(4)
    diff_path = out.with_name(out.stem.replace("episodes", "by_difficulty") + ".csv")
    by_diff.to_csv(diff_path, index=False)

    rates = _rate_table(df, ["model"]).round(4)
    rates_path = out.with_name(out.stem.replace("episodes", "rates") + ".csv")
    rates.to_csv(rates_path, index=False)

    rates_diff = _rate_table(df, ["model", "difficulty"]).round(4)
    rates_diff_path = out.with_name(out.stem.replace("episodes", "rates_by_difficulty") + ".csv")
    rates_diff.to_csv(rates_diff_path, index=False)

    term = (
        df.groupby(["model", "terminal_reason"]).size().rename("count").reset_index()
    )
    totals = df.groupby("model").size().rename("episodes").reset_index()
    term = term.merge(totals, on="model", how="left")
    term["rate"] = term["count"] / term["episodes"]
    term_path = out.with_name(out.stem.replace("episodes", "termination_rates") + ".csv")
    term.to_csv(term_path, index=False)

    print("\nSummary:\n", summary.to_string(index=False))
    print("\nRates with 95% Wilson intervals:\n", rates.to_string(index=False))
    print("\nTermination reasons:\n", term.to_string(index=False))
    print("\nSaved:", out)
    for pth in (summary_path, diff_path, rates_path, rates_diff_path, term_path):
        print("Saved:", pth)


if __name__ == "__main__":
    main()
