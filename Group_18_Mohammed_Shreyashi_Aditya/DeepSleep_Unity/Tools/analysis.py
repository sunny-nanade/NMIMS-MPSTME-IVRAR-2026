#!/usr/bin/env python3
"""
analysis.py: within-subject analysis of deepsleep_telemetry.csv (Protocol: Deep Sleep).

Question: does spatial (HRTF) guidance from the exit door (condition S) cut evacuation time and wrong turns compared with stereo (condition M)
in smoke-filled, low-visibility egress? Every participant has two rows (one per block): one S, one M, with layouts A and B counterbalanced.

For each outcome: paired difference S - M per participant, mean and 95 % CI, paired t-test, Wilcoxon signed-rank, Cohen's dz, and Holm correction over
the primary outcomes. Sensitivity: linear mixed model (condition + layout + block order, random intercept per participant), exclusion of timed-out blocks,
an order (carry-over) check, and a balance check of the counterbalancing.

Usage
  python Tools/analysis.py [--csv Analysis/data/bot_telemetry.csv] [--out Analysis/results] [--label "text for the report header"]
  python Tools/analysis.py --selftest            # synthetic data with a known effect; exits non-zero if the pipeline misses it
"""
import argparse
import math
import os
import sys
import warnings

import numpy as np
import pandas as pd
from scipy import stats

PRIMARY = {
    "evac_time_s": "Evacuation time (s)",
    "wrong_turns": "Wrong turns",
    "dead_end_entries": "Dead-end entries",
    "backtracks": "Backtracks",
    "path_efficiency": "Path efficiency (shortest / travelled)",
    "mean_head_turn_latency_ms": "Head-turn latency (ms)",
}
SECONDARY = {
    "TLX_raw_mean": "Raw NASA-TLX (0 to 100)",
    "TLX_mental": "TLX mental demand",
    "TLX_physical": "TLX physical demand",
    "TLX_temporal": "TLX temporal demand",
    "TLX_performance": "TLX performance",
    "TLX_effort": "TLX effort",
    "TLX_frustration": "TLX frustration",
    "wall_contacts": "Wall contacts",
    "idle_time_s": "Idle time (s)",
    "path_length_m": "Path length (m)",
    "mean_fps": "Mean frame rate (fps)",
    "min_fps": "Minimum frame rate (fps)",
    "p1_low_fps": "1 % low frame rate (fps)",
}
LOWER_IS_BETTER = {"evac_time_s", "wrong_turns", "dead_end_entries", "backtracks", "mean_head_turn_latency_ms", "wall_contacts", "idle_time_s",
                   "path_length_m", "TLX_raw_mean", "TLX_mental", "TLX_physical", "TLX_temporal", "TLX_effort", "TLX_frustration", "TLX_performance"}


def load(path):
    df = pd.read_csv(path)
    df.columns = [c.strip() for c in df.columns]
    needed = ["participant_id", "condition", "layout", "block_order", "evac_time_s"]
    missing = [c for c in needed if c not in df.columns]
    if missing:
        raise SystemExit(f"missing columns: {missing}")
    for c in df.columns:
        if c not in ("participant_id", "session_id", "condition", "layout", "timestamp"):
            df[c] = pd.to_numeric(df[c], errors="coerce")
    df["participant_id"] = df["participant_id"].astype(str)
    df["condition"] = df["condition"].astype(str).str.strip()
    return df


def balance_report(df):
    lines = []
    n_part = df["participant_id"].nunique()
    per = df.groupby("participant_id").size()
    lines.append(f"- participants: {n_part}; rows per participant: {sorted(set(per))}")
    cond = df.groupby("participant_id")["condition"].apply(lambda s: "".join(sorted(s)))
    ok = (cond == "MS").all()
    lines.append(f"- every participant has one S block and one M block: {'yes' if ok else 'NO: ' + str(cond[cond != 'MS'].to_dict())}")
    first = df[df["block_order"] == 1].groupby("participant_id")["condition"].first()
    lines.append(f"- first block spatial for {int((first == 'S').sum())} participants, stereo for {int((first == 'M').sum())}")
    lay = df.groupby(["condition", "layout"]).size().unstack(fill_value=0)
    lines.append("- condition x layout counts: " + "; ".join(f"{c}-{l}: {int(lay.loc[c, l])}" for c in lay.index for l in lay.columns))
    return lines, ok


def paired(df, metric, exclude_timeouts=False):
    d = df.copy()
    if exclude_timeouts and "timeout_flag" in d.columns:
        bad = set(d.loc[d["timeout_flag"] == 1, "participant_id"])
        d = d[~d["participant_id"].isin(bad)]
    wide = d.pivot_table(index="participant_id", columns="condition", values=metric, aggfunc="first")
    if "S" not in wide or "M" not in wide:
        return None
    wide = wide.dropna(subset=["S", "M"])
    n = len(wide)
    if n < 2:
        return None
    diff = (wide["S"] - wide["M"]).to_numpy(dtype=float)
    mean_d = float(diff.mean())
    sd_d = float(diff.std(ddof=1))
    se = sd_d / math.sqrt(n)
    tcrit = stats.t.ppf(0.975, n - 1)
    dz = mean_d / sd_d if sd_d > 0 else float("nan")
    if sd_d > 0:
        t_res = stats.ttest_rel(wide["S"], wide["M"])
        t_p = float(t_res.pvalue)
    else:
        t_p = 1.0 if mean_d == 0 else 0.0
    try:
        w_p = float(stats.wilcoxon(diff, zero_method="wilcox", method="auto").pvalue) if np.any(diff != 0) else 1.0
    except ValueError:
        w_p = 1.0
    return {
        "n": n, "mean_S": float(wide["S"].mean()), "mean_M": float(wide["M"].mean()), "mean_diff": mean_d, "sd_diff": sd_d,
        "ci_low": mean_d - tcrit * se, "ci_high": mean_d + tcrit * se, "dz": dz, "t_p": t_p, "wilcoxon_p": w_p,
    }


def holm(pvals):
    order = np.argsort(pvals)
    m = len(pvals)
    adj = np.empty(m)
    running = 0.0
    for rank, idx in enumerate(order):
        running = max(running, (m - rank) * pvals[idx])
        adj[idx] = min(1.0, running)
    return adj


def mixed_model(df, metric):
    try:
        import statsmodels.formula.api as smf
        d = df[["participant_id", "condition", "layout", "block_order", metric]].dropna().copy()
        d["cond_S"] = (d["condition"] == "S").astype(int)
        d["layout_B"] = (d["layout"] == "B").astype(int)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            m = smf.mixedlm(f"{metric} ~ cond_S + layout_B + block_order", d, groups=d["participant_id"]).fit(reml=True)
        return float(m.params["cond_S"]), float(m.pvalues["cond_S"])
    except Exception:
        return float("nan"), float("nan")


def order_check(df, metric):
    """Carry-over: do participants who had S first show a different S - M difference from those who had M first?"""
    first = df[df["block_order"] == 1].set_index("participant_id")["condition"]
    wide = df.pivot_table(index="participant_id", columns="condition", values=metric, aggfunc="first").dropna()
    wide["diff"] = wide["S"] - wide["M"]
    a = wide.loc[wide.index.isin(first[first == "S"].index), "diff"]
    b = wide.loc[wide.index.isin(first[first == "M"].index), "diff"]
    if len(a) < 2 or len(b) < 2:
        return float("nan")
    return float(stats.mannwhitneyu(a, b, alternative="two-sided").pvalue)


def analyse(df, label):
    out_rows = []
    lines = [f"# Analysis report", "", f"Data: {label}", ""]
    bal, bal_ok = balance_report(df)
    lines += ["## Design and balance", ""] + bal + [""]
    n_to = int((df.get("timeout_flag", pd.Series(dtype=float)) == 1).sum())
    lines += [f"Timed-out blocks: {n_to} (sensitivity rows below exclude every participant with a timed-out block)", ""]

    results = {}
    for key, name in {**PRIMARY, **SECONDARY}.items():
        if key not in df.columns:
            continue
        r = paired(df, key)
        if r is None:
            continue
        r["sens_n"] = None
        s = paired(df, key, exclude_timeouts=True)
        r["sens_mean_diff"] = s["mean_diff"] if s else float("nan")
        r["sens_t_p"] = s["t_p"] if s else float("nan")
        r["sens_n"] = s["n"] if s else 0
        mm_b, mm_p = mixed_model(df, key)
        r["mixed_cond_coef"] = mm_b
        r["mixed_p"] = mm_p
        r["order_p"] = order_check(df, key)
        r["name"] = name
        r["metric"] = key
        r["primary"] = key in PRIMARY
        r["direction_better"] = "lower" if key in LOWER_IS_BETTER else "higher"
        results[key] = r

    prim = [k for k in PRIMARY if k in results]
    if prim:
        adj_t = holm(np.array([results[k]["t_p"] for k in prim]))
        adj_w = holm(np.array([results[k]["wilcoxon_p"] for k in prim]))
        for k, a, b in zip(prim, adj_t, adj_w):
            results[k]["t_p_holm"] = float(a)
            results[k]["wilcoxon_p_holm"] = float(b)

    def fmt(v, d=3):
        return "n/a" if v is None or (isinstance(v, float) and math.isnan(v)) else f"{v:.{d}f}"

    lines += ["## Primary outcomes (S minus M, paired by participant)", "",
              "| outcome | n | mean S | mean M | mean diff [95 % CI] | dz | t p | Holm p | Wilcoxon p | mixed-model p | excl. timeouts p |",
              "|---|---|---|---|---|---|---|---|---|---|---|"]
    for k in prim:
        r = results[k]
        lines.append(f"| {r['name']} | {r['n']} | {fmt(r['mean_S'], 2)} | {fmt(r['mean_M'], 2)} | {fmt(r['mean_diff'], 2)} [{fmt(r['ci_low'], 2)}, {fmt(r['ci_high'], 2)}] | {fmt(r['dz'], 2)} | {fmt(r['t_p'])} | {fmt(r.get('t_p_holm'))} | {fmt(r['wilcoxon_p'])} | {fmt(r['mixed_p'])} | {fmt(r['sens_t_p'])} (n={r['sens_n']}) |")
    lines += ["", "Negative differences favour spatial audio for outcomes where lower is better (time, errors, latency, load); positive for path efficiency.", ""]
    sec = [k for k in SECONDARY if k in results]
    if sec:
        lines += ["## Secondary outcomes (no multiplicity correction)", "", "| outcome | n | mean S | mean M | mean diff [95 % CI] | dz | t p | Wilcoxon p |", "|---|---|---|---|---|---|---|---|"]
        for k in sec:
            r = results[k]
            lines.append(f"| {r['name']} | {r['n']} | {fmt(r['mean_S'], 2)} | {fmt(r['mean_M'], 2)} | {fmt(r['mean_diff'], 2)} [{fmt(r['ci_low'], 2)}, {fmt(r['ci_high'], 2)}] | {fmt(r['dz'], 2)} | {fmt(r['t_p'])} | {fmt(r['wilcoxon_p'])} |")
        lines.append("")
    if "SUS_score" in df.columns:
        sus = df.groupby("participant_id")["SUS_score"].first().dropna()
        if len(sus):
            lines += ["## SUS", "", f"One SUS score per participant (asked once, after both blocks): mean {sus.mean():.1f}, SD {sus.std(ddof=1) if len(sus) > 1 else float('nan'):.1f}, range {sus.min():.1f} to {sus.max():.1f}, n = {len(sus)}. It cannot be compared between conditions.", ""]
    if "earcheck_mean_error_deg" in df.columns:
        ee = df.groupby("participant_id")["earcheck_mean_error_deg"].first().dropna()
        if len(ee) and (ee != 0).any():
            lines += ["## Ear Check", "", f"Mean pointing error {ee.mean():.1f} deg (SD {ee.std(ddof=1) if len(ee) > 1 else float('nan'):.1f}). Use as a covariate or an exclusion screen for poor spatial hearing; it was not used to filter the rows above.", ""]
    lines += ["## Order (carry-over) check", "", "Mann-Whitney p for the S - M difference between participants who had S first and those who had M first (small n: low power).", "",
              "| outcome | p |", "|---|---|"]
    for k in prim:
        lines.append(f"| {results[k]['name']} | {fmt(results[k]['order_p'])} |")
    lines.append("")
    out_rows = [results[k] for k in results]
    return lines, out_rows, bal_ok, results


def synthetic(n=24, seed=7):
    rng = np.random.default_rng(seed)
    rows = []
    for p in range(1, n + 1):
        order = p % 4
        plan = [("S", "A", "M", "B"), ("S", "B", "M", "A"), ("M", "A", "S", "B"), ("M", "B", "S", "A")][order]
        base = rng.normal(95, 15)
        wt = max(0, rng.normal(2.2, 1.0))
        for b, (c, l) in enumerate([(plan[0], plan[1]), (plan[2], plan[3])], start=1):
            spatial = c == "S"
            t = base * (0.82 if spatial else 1.0) * (0.95 if b == 2 else 1.0) + rng.normal(0, 6)
            wrong = max(0, round(wt * (0.55 if spatial else 1.0) + rng.normal(0, 0.7)))
            rows.append({
                "participant_id": f"P{p}", "session_id": "x", "condition": c, "block_order": b, "layout": l, "evac_time_s": t, "wrong_turns": wrong,
                "dead_end_entries": max(0, wrong - rng.integers(0, 2)), "backtracks": max(0, wrong + rng.integers(-1, 2)), "path_length_m": 36 + 3 * wrong + rng.normal(0, 1),
                "path_efficiency": 36 / (36 + 3 * wrong + abs(rng.normal(0, 1))), "wall_contacts": rng.integers(0, 5), "idle_time_s": abs(rng.normal(3, 1)), "timeout_flag": 0,
                "mean_head_turn_latency_ms": rng.normal(900 if spatial else 1300, 150), "TLX_raw_mean": rng.normal(35 if spatial else 45, 8), "SUS_score": 80.0,
                "earcheck_mean_error_deg": abs(rng.normal(12, 3)), "mean_fps": 72.0, "min_fps": 68.0, "p1_low_fps": 66.0,
            })
    return pd.DataFrame(rows)


def run(df, label, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    lines, rows, bal_ok, results = analyse(df, label)
    pd.DataFrame(rows).to_csv(os.path.join(out_dir, "analysis_summary.csv"), index=False)
    with open(os.path.join(out_dir, "analysis_report.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    return results, bal_ok


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--csv", default=os.path.join(root, "Analysis", "data", "bot_telemetry.csv"))
    ap.add_argument("--out", default=os.path.join(root, "Analysis", "results"))
    ap.add_argument("--label", default=None)
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()
    if args.selftest:
        df = synthetic()
        out = os.path.join(args.out, "selftest")
        results, ok = run(df, "SYNTHETIC data with a built-in effect (spatial 18 % faster, 45 % fewer wrong turns, lower latency and load)", out)
        checks = [
            ("balance check passes", ok),
            ("evacuation time effect detected (Holm p < 0.05)", results["evac_time_s"]["t_p_holm"] < 0.05),
            ("wrong turns effect detected (Holm p < 0.05)", results["wrong_turns"]["t_p_holm"] < 0.05),
            ("head-turn latency effect detected (Holm p < 0.05)", results["mean_head_turn_latency_ms"]["t_p_holm"] < 0.05),
            ("effect has the right sign (S faster)", results["evac_time_s"]["mean_diff"] < 0),
            ("no effect planted in mean fps stays non-significant", results["mean_fps"]["t_p"] > 0.05 if "mean_fps" in results else True),
        ]
        bad = 0
        for name, passed in checks:
            print(("PASS " if passed else "FAIL ") + name)
            bad += 0 if passed else 1
        print(f"report written to {out}")
        sys.exit(1 if bad else 0)
    df = load(args.csv)
    label = args.label or f"{os.path.relpath(args.csv, root)} ({df['participant_id'].nunique()} participants)"
    results, ok = run(df, label, args.out)
    print(f"analysed {len(df)} rows, {df['participant_id'].nunique()} participants; balance {'OK' if ok else 'PROBLEM'}; report in {args.out}")
    for k in PRIMARY:
        if k in results:
            r = results[k]
            print(f"{k:28s} S {r['mean_S']:.2f}  M {r['mean_M']:.2f}  diff {r['mean_diff']:+.2f}  p {r['t_p']:.3f} (Holm {r.get('t_p_holm', float('nan')):.3f})")


if __name__ == "__main__":
    main()
