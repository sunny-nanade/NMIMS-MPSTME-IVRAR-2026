#!/usr/bin/env python3
"""
generate_figures.py: figures for the Protocol: Deep Sleep study (PNG, matplotlib).

  fig_layouts.png          both maze layouts with the shortest path, junctions, dead ends, start and exit
  fig_paired_outcomes.png  paired S vs M lines per participant for the primary outcomes
  fig_effect_sizes.png     Cohen's dz with approximate 95 % CI for every analysed outcome
  fig_load_and_usability.png  raw TLX by condition and the SUS distribution
  fig_frame_rate.png       frame-time trace and histogram from profiler_log.csv (budget line)

Usage
  python Tools/generate_figures.py [--csv Analysis/data/bot_telemetry.csv] [--profiler Analysis/data/bot_profiler_log.csv] [--out Analysis/figures]
"""
import argparse
import json
import math
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analysis as an  # noqa: E402

COL = {"S": "#1f77b4", "M": "#d95f02"}


def fig_layouts(root, out):
    fig, axes = plt.subplots(1, 2, figsize=(11, 5.6))
    for ax, name in zip(axes, "AB"):
        path = os.path.join(root, "Assets", "_Project", "Data", f"MazeLayout{name}.json")
        if not os.path.exists(path):
            ax.set_visible(False)
            continue
        d = json.load(open(path))
        n = d["grid"]
        cells = d["cells"]
        mask = lambda c, r: cells[r * n + c]
        on_path = [(p["c"], p["r"]) for p in d["path"]]
        for r in range(n):
            for c in range(n):
                m = mask(c, r)
                used = m != 0 or (c, r) in ((d["start"]["c"], d["start"]["r"]), (d["exit"]["c"], d["exit"]["r"]))
                ax.add_patch(plt.Rectangle((c, r), 1, 1, facecolor="#eef3f7" if used else "#555555", edgecolor="none"))
                if used:
                    for bit, (x0, y0, x1, y1) in ((1, (c, r + 1, c + 1, r + 1)), (2, (c + 1, r, c + 1, r + 1)), (4, (c, r, c + 1, r)), (8, (c, r, c, r + 1))):
                        if not m & bit:
                            ax.plot([x0, x1], [y0, y1], color="black", lw=2.2, solid_capstyle="round")
        xs = [c + 0.5 for c, r in on_path]
        ys = [r + 0.5 for c, r in on_path]
        ax.plot(xs, ys, color="#2ca02c", lw=3, alpha=0.9, zorder=3, label="shortest path (36 m)")
        ax.scatter([j["c"] + 0.5 for j in d["junctions"]], [j["r"] + 0.5 for j in d["junctions"]], s=90, color="#ff7f0e", zorder=4, label="junction (6)")
        mis = [j for j in d["junctions"] if j["misleading"]]
        ax.scatter([j["c"] + 0.5 for j in mis], [j["r"] + 0.5 for j in mis], s=240, facecolors="none", edgecolors="#d62728", lw=2.2, zorder=5, label="misleading junction")
        ax.scatter([e["c"] + 0.5 for e in d["deadEnds"]], [e["r"] + 0.5 for e in d["deadEnds"]], marker="X", s=110, color="#9467bd", zorder=4, label="dead end (3)")
        ax.text(d["start"]["c"] + 0.5, d["start"]["r"] + 0.5, "S", ha="center", va="center", fontsize=13, weight="bold", zorder=6)
        ax.text(d["exit"]["c"] + 0.5, d["exit"]["r"] + 0.5, "E", ha="center", va="center", fontsize=13, weight="bold", zorder=6)
        ax.set_xlim(-0.1, n + 0.1)
        ax.set_ylim(-0.1, n + 0.1)
        ax.set_aspect("equal")
        ax.set_xticks([])
        ax.set_yticks([])
        ax.set_title(f"Layout {name}: start ({d['start']['c']},{d['start']['r']}), exit ({d['exit']['c']},{d['exit']['r']})")
    axes[0].legend(loc="upper center", bbox_to_anchor=(1.05, -0.02), ncol=4, frameon=False)
    fig.suptitle("Smoke-room layouts (6 x 6 cells of 2 m; door in the south wall, exit door in the north wall)")
    fig.tight_layout()
    fig.savefig(os.path.join(out, "fig_layouts.png"), dpi=150, bbox_inches="tight")
    plt.close(fig)


def fig_paired(df, out, label):
    keys = [k for k in an.PRIMARY if k in df.columns]
    fig, axes = plt.subplots(1, len(keys), figsize=(3.0 * len(keys), 4.2))
    axes = np.atleast_1d(axes)
    for ax, k in zip(axes, keys):
        wide = df.pivot_table(index="participant_id", columns="condition", values=k, aggfunc="first").dropna()
        for _, row in wide.iterrows():
            ax.plot([0, 1], [row["S"], row["M"]], color="#999999", lw=1, marker="o", ms=4, alpha=0.8)
        ax.plot([0, 1], [wide["S"].mean(), wide["M"].mean()], color="black", lw=3, marker="D", ms=7)
        ax.set_xticks([0, 1])
        ax.set_xticklabels(["Spatial", "Stereo"])
        ax.set_title(an.PRIMARY[k], fontsize=9)
        ax.spines[["top", "right"]].set_visible(False)
    fig.suptitle(f"Paired outcomes per participant ({label})", fontsize=10)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "fig_paired_outcomes.png"), dpi=150)
    plt.close(fig)


def fig_effects(df, out, label):
    rows = []
    for k, name in {**an.PRIMARY, **an.SECONDARY}.items():
        if k not in df.columns:
            continue
        r = an.paired(df, k)
        if r is None or not np.isfinite(r["dz"]):
            continue
        n = r["n"]
        se = math.sqrt(1.0 / n + r["dz"] ** 2 / (2.0 * n))
        rows.append((name, r["dz"], r["dz"] - 1.96 * se, r["dz"] + 1.96 * se, k in an.PRIMARY))
    if not rows:
        return
    rows.reverse()
    fig, ax = plt.subplots(figsize=(7, 0.38 * len(rows) + 1.2))
    for i, (name, dz, lo, hi, prim) in enumerate(rows):
        ax.plot([lo, hi], [i, i], color="#1f77b4" if prim else "#888888", lw=2)
        ax.plot(dz, i, "o", color="#1f77b4" if prim else "#888888")
    ax.axvline(0, color="black", lw=1)
    ax.set_yticks(range(len(rows)))
    ax.set_yticklabels([r[0] for r in rows], fontsize=8)
    ax.set_xlabel("Cohen's dz, spatial minus stereo (negative favours spatial where lower is better)")
    ax.set_title(f"Effect sizes ({label})", fontsize=10)
    ax.spines[["top", "right"]].set_visible(False)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "fig_effect_sizes.png"), dpi=150)
    plt.close(fig)


def fig_load(df, out, label):
    fig, axes = plt.subplots(1, 2, figsize=(9, 4))
    if "TLX_raw_mean" in df.columns:
        data = [df.loc[df["condition"] == c, "TLX_raw_mean"].dropna() for c in ("S", "M")]
        axes[0].boxplot(data, tick_labels=["Spatial", "Stereo"], widths=0.5)
        for i, c in enumerate(("S", "M"), start=1):
            axes[0].scatter(np.random.default_rng(1).normal(i, 0.04, len(data[i - 1])), data[i - 1], color=COL[c], zorder=3, s=22)
        axes[0].set_title("Raw NASA-TLX (0 to 100)")
    if "SUS_score" in df.columns:
        sus = df.groupby("participant_id")["SUS_score"].first().dropna()
        axes[1].hist(sus, bins=np.arange(0, 105, 10), color="#4c72b0", edgecolor="white")
        axes[1].axvline(68, color="black", ls="--", lw=1)
        axes[1].text(68.5, axes[1].get_ylim()[1] * 0.92, "68 benchmark", fontsize=8)
        axes[1].set_title("SUS score per participant")
        axes[1].set_xlabel("SUS")
    for ax in axes:
        ax.spines[["top", "right"]].set_visible(False)
    fig.suptitle(label, fontsize=9)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "fig_load_and_usability.png"), dpi=150)
    plt.close(fig)


def fig_fps(profiler, out):
    if not os.path.exists(profiler):
        return
    df = pd.read_csv(profiler)
    if df.empty:
        return
    fig, axes = plt.subplots(1, 2, figsize=(10, 3.8), gridspec_kw={"width_ratios": [2.2, 1]})
    x = np.arange(len(df))
    axes[0].plot(x, df["frame_ms_mean"], lw=1, color="#1f77b4", label="mean frame time per second")
    axes[0].plot(x, df["frame_ms_max"], lw=0.6, color="#d62728", alpha=0.6, label="worst frame in that second")
    budget = float(df["budget_ms"].iloc[0])
    axes[0].axhline(budget, color="black", ls="--", lw=1, label=f"budget {budget:.1f} ms")
    axes[0].set_yscale("log")
    axes[0].set_xlabel("sample (one per second of play; scene loads cause the spikes)")
    axes[0].set_ylabel("ms (log)")
    axes[0].legend(fontsize=7)
    axes[0].set_title("Frame time")
    axes[1].hist(np.log10(df["frame_ms_mean"].clip(lower=0.1)), bins=30, color="#4c72b0", edgecolor="white")
    axes[1].axvline(np.log10(budget), color="black", ls="--", lw=1)
    axes[1].set_title(f"median {df['frame_ms_mean'].median():.1f} ms ({100 * (df['frame_ms_mean'] <= budget).mean():.0f} % of seconds within budget)", fontsize=8)
    axes[1].set_xlabel("log10 ms")
    for ax in axes:
        ax.spines[["top", "right"]].set_visible(False)
    fig.suptitle("Frame rate (Editor run: not representative of the Quest)", fontsize=9)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "fig_frame_rate.png"), dpi=150)
    plt.close(fig)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--csv", default=os.path.join(root, "Analysis", "data", "bot_telemetry.csv"))
    ap.add_argument("--profiler", default=os.path.join(root, "Analysis", "data", "bot_profiler_log.csv"))
    ap.add_argument("--out", default=os.path.join(root, "Analysis", "figures"))
    ap.add_argument("--label", default="bot data, n = 8")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    fig_layouts(root, args.out)
    if os.path.exists(args.csv):
        df = an.load(args.csv)
        fig_paired(df, args.out, args.label)
        fig_effects(df, args.out, args.label)
        fig_load(df, args.out, args.label)
    else:
        print("no telemetry csv found; only the layout figure was drawn")
    fig_fps(args.profiler, args.out)
    print("figures written to", args.out, sorted(os.listdir(args.out)))


if __name__ == "__main__":
    main()
