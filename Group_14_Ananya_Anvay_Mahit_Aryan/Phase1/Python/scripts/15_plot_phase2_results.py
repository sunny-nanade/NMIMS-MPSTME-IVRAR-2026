import argparse
import math
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd


def wilson_interval(k: int, n: int, z: float = 1.96):
    if n <= 0:
        return float("nan"), float("nan")
    p = k / n
    den = 1.0 + z * z / n
    center = (p + z * z / (2.0 * n)) / den
    half = z * math.sqrt(p * (1.0 - p) / n + z * z / (4.0 * n * n)) / den
    return max(0.0, center - half), min(1.0, center + half)


def rate_plot(df, metric, ylabel, path, title):
    models = list(dict.fromkeys(df["model"].tolist()))
    rates, lows, highs, labels = [], [], [], []
    for model in models:
        g = df[df["model"] == model]
        n = len(g)
        k = int(g[metric].sum())
        rate = k / n if n else float("nan")
        lo, hi = wilson_interval(k, n)
        rates.append(rate)
        lows.append(rate - lo)
        highs.append(hi - rate)
        labels.append(f"{k}/{n}")
    x = np.arange(len(models))
    fig, ax = plt.subplots(figsize=(8, 5))
    bars = ax.bar(x, rates, yerr=np.vstack([lows, highs]), capsize=5)
    ax.set_xticks(x, models)
    ax.set_ylim(0, 1)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    for bar, text in zip(bars, labels):
        ax.text(bar.get_x() + bar.get_width() / 2, min(0.96, bar.get_height() + 0.04), text,
                ha="center", va="bottom")
    fig.tight_layout()
    fig.savefig(path, dpi=180)
    plt.close(fig)
    print("Saved", path)


def continuous_plot(df, metric, ylabel, path, title, success_only=False):
    models = list(dict.fromkeys(df["model"].tolist()))
    means, errs, counts = [], [], []
    for model in models:
        g = df[df["model"] == model]
        if success_only:
            g = g[g["success"] == 1]
        vals = pd.to_numeric(g[metric], errors="coerce").dropna()
        counts.append(len(vals))
        means.append(float(vals.mean()) if len(vals) else np.nan)
        errs.append(float(vals.std(ddof=1)) if len(vals) > 1 else 0.0)
    x = np.arange(len(models))
    fig, ax = plt.subplots(figsize=(8, 5))
    bars = ax.bar(x, np.nan_to_num(means, nan=0.0), yerr=np.nan_to_num(errs, nan=0.0), capsize=4)
    ax.set_xticks(x, models)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    for i, (bar, mean, n) in enumerate(zip(bars, means, counts)):
        if np.isnan(mean):
            bar.set_height(0)
            ax.text(i, 0.02, "N/A\n(no successes)" if success_only else "N/A", ha="center", va="bottom")
        elif success_only:
            ax.text(i, mean + max(0.02, 0.03 * max(1.0, abs(mean))), f"n={n}", ha="center", va="bottom")
    fig.tight_layout()
    fig.savefig(path, dpi=180)
    plt.close(fig)
    print("Saved", path)


def success_by_difficulty(df, path):
    difficulties = [d for d in ["easy", "medium", "hard"] if d in set(df["difficulty"])]
    models = list(dict.fromkeys(df["model"].tolist()))
    x = np.arange(len(difficulties), dtype=float)
    width = 0.8 / max(1, len(models))

    fig, ax = plt.subplots(figsize=(9, 5))
    for j, model in enumerate(models):
        rates, lowerr, higherr, ns = [], [], [], []
        for diff in difficulties:
            g = df[(df["model"] == model) & (df["difficulty"] == diff)]
            n = len(g)
            k = int(g["success"].sum())
            r = k / n if n else 0.0
            lo, hi = wilson_interval(k, n)
            rates.append(r)
            lowerr.append(r - lo if n else 0.0)
            higherr.append(hi - r if n else 0.0)
            ns.append(f"{k}/{n}")
        pos = x - 0.4 + width / 2 + j * width
        bars = ax.bar(pos, rates, width=width, yerr=np.vstack([lowerr, higherr]), capsize=3, label=model)
        for bar, txt in zip(bars, ns):
            ax.text(bar.get_x() + bar.get_width()/2, min(0.97, bar.get_height()+0.035), txt,
                    ha="center", va="bottom", fontsize=8)
    ax.set_xticks(x, difficulties)
    ax.set_ylim(0, 1)
    ax.set_ylabel("Success rate")
    ax.set_xlabel("Terrain difficulty")
    ax.set_title("Phase 2 robustness across terrain difficulty")
    ax.legend(title="model")
    fig.tight_layout()
    fig.savefig(path, dpi=180)
    plt.close(fig)
    print("Saved", path)


def termination_plot(df, path):
    order = ["goal", "collision", "stuck", "time_limit", "out_of_bounds", "rollover"]
    table = pd.crosstab(df["model"], df["terminal_reason"], normalize="index")
    cols = [c for c in order if c in table.columns] + [c for c in table.columns if c not in order]
    table = table[cols]
    ax = table.plot(kind="bar", stacked=True, figsize=(9, 5))
    ax.set_ylim(0, 1)
    ax.set_ylabel("Fraction of episodes")
    ax.set_xlabel("")
    ax.set_title("Phase 2 episode termination reasons")
    ax.legend(title="Termination", bbox_to_anchor=(1.02, 1), loc="upper left")
    plt.xticks(rotation=0)
    plt.tight_layout()
    plt.savefig(path, dpi=180)
    plt.close()
    print("Saved", path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", default="outputs/metrics/phase2_test_episodes.csv")
    args = ap.parse_args()
    df = pd.read_csv(args.csv)
    out = Path("outputs/figures")
    out.mkdir(parents=True, exist_ok=True)

    rate_plot(
        df, "success", "Success rate", out / "phase2_success.png",
        "Phase 2 test comparison — Success rate (95% Wilson CI)",
    )
    rate_plot(
        df, "collision", "Collision rate", out / "phase2_collision.png",
        "Phase 2 test comparison — Collision rate (95% Wilson CI)",
    )

    continuous_plot(
        df, "path_efficiency", "Path efficiency", out / "phase2_path_efficiency.png",
        "Phase 2 test comparison — Path efficiency (successful episodes only)", success_only=True,
    )
    continuous_plot(
        df, "goal_progress", "Goal progress fraction", out / "phase2_goal_progress.png",
        "Phase 2 test comparison — Goal progress (higher is better)",
    )
    continuous_plot(
        df, "energy_proxy", "Energy proxy (lower is better)", out / "phase2_energy_proxy.png",
        "Phase 2 test comparison — Energy proxy (lower is better)",
    )
    continuous_plot(
        df, "final_goal_distance_m", "Final goal distance (m)", out / "phase2_final_goal_distance_m.png",
        "Phase 2 test comparison — Final goal distance (m)",
    )
    if "mean_action_change" in df.columns:
        continuous_plot(
            df, "mean_action_change", "Mean action change", out / "phase2_action_smoothness.png",
            "Phase 2 test comparison — Control smoothness (lower is smoother)",
        )
    if "mean_abs_steer" in df.columns:
        continuous_plot(
            df, "mean_abs_steer", "Mean |steering|", out / "phase2_mean_abs_steer.png",
            "Phase 2 test comparison — Steering magnitude",
        )
    if "mean_signed_steer" in df.columns:
        steer_bias = df.copy()
        steer_bias["abs_steer_bias"] = pd.to_numeric(steer_bias["mean_signed_steer"], errors="coerce").abs()
        continuous_plot(
            steer_bias, "abs_steer_bias", "|Mean signed steering|", out / "phase2_steering_bias.png",
            "Phase 2 test comparison — Persistent steering bias (lower is better)",
        )
    if "positive_progress_fraction" in df.columns:
        continuous_plot(
            df, "positive_progress_fraction", "Fraction of steps with positive progress",
            out / "phase2_positive_progress_fraction.png",
            "Phase 2 test comparison — Goal-directed motion",
        )

    if "difficulty" in df.columns:
        success_by_difficulty(df, out / "phase2_success_by_difficulty.png")
    if "terminal_reason" in df.columns:
        termination_plot(df, out / "phase2_termination_reasons.png")


if __name__ == "__main__":
    main()
