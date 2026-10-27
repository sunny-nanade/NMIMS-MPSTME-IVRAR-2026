from __future__ import annotations

import argparse
import csv
import math
import os
import sys
import time
from dataclasses import dataclass
from functools import partial
from pathlib import Path
from typing import Any

import numpy as np
import torch
from stable_baselines3 import SAC
from stable_baselines3.common.vec_env import SubprocVecEnv

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


# ---------------------------------------------------------------------------
# Scheduled-seed environment
# ---------------------------------------------------------------------------

class ScheduledSeedRoverEnv(RoverEnv):
    """RoverEnv whose automatic VecEnv resets follow an exact fixed seed schedule.

    This lets every checkpoint see precisely the same episodes, even though
    SubprocVecEnv automatically resets environments after terminal states.
    """

    def __init__(self, *args, seed_schedule: list[int], **kwargs):
        self._benchmark_seed_schedule = [int(x) for x in seed_schedule]
        if not self._benchmark_seed_schedule:
            raise ValueError("seed_schedule must not be empty")
        self._benchmark_seed_index = 0
        self._benchmark_current_seed = self._benchmark_seed_schedule[0]
        super().__init__(*args, **kwargs)

    def reset(self, *, seed=None, options=None):
        # Ignore VecEnv's own seed and consume our deterministic benchmark schedule.
        if self._benchmark_seed_index < len(self._benchmark_seed_schedule):
            chosen = self._benchmark_seed_schedule[self._benchmark_seed_index]
            self._benchmark_seed_index += 1
        else:
            # Should not matter for the recommended N divisible by worker count.
            # Keep a deterministic sentinel reset rather than introducing randomness.
            chosen = self._benchmark_seed_schedule[-1]

        self._benchmark_current_seed = int(chosen)
        obs, info = super().reset(seed=self._benchmark_current_seed, options=options)
        info = dict(info)
        info["benchmark_episode_seed"] = self._benchmark_current_seed
        return obs, info

    def step(self, action):
        obs, reward, terminated, truncated, info = super().step(action)
        info = dict(info)
        info["benchmark_episode_seed"] = self._benchmark_current_seed
        return obs, reward, terminated, truncated, info


def _build_env(
    terrain_dir: str,
    seed_schedule: list[int],
    regime: str,
):
    """Factory executed inside each spawned PyBullet worker."""
    # Avoid numerical-library oversubscription in PyBullet subprocesses.
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    os.environ["NUMEXPR_NUM_THREADS"] = "1"

    regime = str(regime).lower()
    if regime not in {"clean", "dr"}:
        raise ValueError(f"Unknown regime: {regime}")

    # V1.5 patch is opt-in through this environment variable.
    os.environ["MARS_ROVER_UNITY_DR"] = "1" if regime == "dr" else "0"

    return ScheduledSeedRoverEnv(
        terrain_files(terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,              # PyBullet DIRECT: no rendering overhead
        randomize_physics=(regime == "dr"),
        sensor_preview=False,          # no camera generation; policy does not use it
        curriculum_enabled=False,      # DR benchmark = full-strength balanced DR
        seed_schedule=seed_schedule,
    )


# ---------------------------------------------------------------------------
# Statistics
# ---------------------------------------------------------------------------

RATE_FIELDS = {
    "success_rate": "success",
    "collision_rate": "collision",
    "rollover_rate": "rollover",
    "out_of_bounds_rate": "out_of_bounds",
}

MEAN_FIELDS = [
    "goal_progress",
    "goal_progress_m",
    "final_goal_distance_m",
    "min_clearance_m",
    "reward",
    "steps",
    "sim_time_s",
    "distance_m",
    "energy_proxy",
    "mean_action_change",
    "mean_abs_steer",
    "mean_signed_steer",
    "mean_abs_throttle",
    "mean_signed_throttle",
    "positive_progress_fraction",
    "mean_slip",
    "mean_wheel_contacts",
    "low_contact_fraction",
    "max_roll_deg",
    "max_pitch_deg",
]

TERMINAL_REASONS = [
    "goal",
    "collision",
    "rollover",
    "out_of_bounds",
    "stuck",
    "time_limit",
]


def _finite(values):
    arr = np.asarray(values, dtype=np.float64)
    return arr[np.isfinite(arr)]


def _mean(values) -> float:
    x = _finite(values)
    return float(np.mean(x)) if x.size else float("nan")


def _median(values) -> float:
    x = _finite(values)
    return float(np.median(x)) if x.size else float("nan")


def _wilson_ci(k: int, n: int, z: float = 1.959963984540054) -> tuple[float, float]:
    if n <= 0:
        return float("nan"), float("nan")
    p = k / n
    denom = 1.0 + z * z / n
    center = (p + z * z / (2.0 * n)) / denom
    half = z * math.sqrt((p * (1.0 - p) / n) + z * z / (4.0 * n * n)) / denom
    return max(0.0, center - half), min(1.0, center + half)


def _bootstrap_mean_ci(
    values,
    rng: np.random.Generator,
    n_boot: int = 5000,
) -> tuple[float, float]:
    x = _finite(values)
    if x.size < 2:
        v = float(x[0]) if x.size == 1 else float("nan")
        return v, v
    idx = rng.integers(0, x.size, size=(n_boot, x.size))
    means = x[idx].mean(axis=1)
    return float(np.quantile(means, 0.025)), float(np.quantile(means, 0.975))


def _bootstrap_paired_diff_ci(
    a: np.ndarray,
    b: np.ndarray,
    rng: np.random.Generator,
    n_boot: int = 5000,
) -> tuple[float, float, float]:
    mask = np.isfinite(a) & np.isfinite(b)
    d = a[mask] - b[mask]
    if d.size == 0:
        return float("nan"), float("nan"), float("nan")
    point = float(np.mean(d))
    if d.size == 1:
        return point, point, point
    idx = rng.integers(0, d.size, size=(n_boot, d.size))
    boots = d[idx].mean(axis=1)
    return point, float(np.quantile(boots, 0.025)), float(np.quantile(boots, 0.975))


def _safe_float(x: Any) -> float:
    try:
        return float(x)
    except Exception:
        return float("nan")


def _flatten_episode(
    metrics: dict[str, Any],
    info: dict[str, Any],
    model_label: str,
    model_path: str,
    model_timesteps: int,
    regime: str,
) -> dict[str, Any]:
    rec = dict(metrics)
    rec["model"] = model_label
    rec["model_path"] = model_path
    rec["model_num_timesteps"] = int(model_timesteps)
    rec["regime"] = regime
    rec["episode_seed"] = int(info["benchmark_episode_seed"])

    reason = str(rec.get("terminal_reason", ""))
    rec["stuck"] = int(reason == "stuck")
    rec["time_limit"] = int(reason == "time_limit")
    rec["goal_terminal"] = int(reason == "goal")

    # Avoid embedding nested Python dicts in CSV.
    cd = rec.get("collision_details")
    if isinstance(cd, dict):
        rec["collision_link_name"] = cd.get("rover_link_name", "")
        rec["collision_normal_force_n"] = cd.get("normal_force_n", "")
        rec["collision_contact_distance_m"] = cd.get("contact_distance_m", "")
    else:
        rec["collision_link_name"] = ""
        rec["collision_normal_force_n"] = ""
        rec["collision_contact_distance_m"] = ""
    rec.pop("collision_details", None)

    return rec


def summarize(records: list[dict[str, Any]], bootstrap_seed: int = 20260919) -> dict[str, Any]:
    if not records:
        return {}

    out: dict[str, Any] = {
        "model": records[0]["model"],
        "model_num_timesteps": records[0]["model_num_timesteps"],
        "regime": records[0]["regime"],
        "episodes": len(records),
    }

    rng = np.random.default_rng(bootstrap_seed)

    for summary_name, field in RATE_FIELDS.items():
        vals = np.asarray([int(r.get(field, 0)) for r in records], dtype=np.int64)
        k = int(vals.sum())
        n = int(vals.size)
        lo, hi = _wilson_ci(k, n)
        out[summary_name] = k / n
        out[f"{summary_name}_ci95_low"] = lo
        out[f"{summary_name}_ci95_high"] = hi

    for reason in TERMINAL_REASONS:
        vals = np.asarray(
            [int(str(r.get("terminal_reason", "")) == reason) for r in records],
            dtype=np.int64,
        )
        k = int(vals.sum())
        n = int(vals.size)
        lo, hi = _wilson_ci(k, n)
        key = f"terminal_{reason}_rate"
        out[key] = k / n
        out[f"{key}_ci95_low"] = lo
        out[f"{key}_ci95_high"] = hi

    for field in MEAN_FIELDS:
        vals = [_safe_float(r.get(field, float("nan"))) for r in records]
        out[f"mean_{field}"] = _mean(vals)
        out[f"median_{field}"] = _median(vals)
        lo, hi = _bootstrap_mean_ci(vals, rng)
        out[f"mean_{field}_ci95_low"] = lo
        out[f"mean_{field}_ci95_high"] = hi

    # Steering bias is absolute signed-steer average per episode, then averaged.
    bias = [abs(_safe_float(r.get("mean_signed_steer", float("nan")))) for r in records]
    out["mean_abs_steer_bias"] = _mean(bias)
    lo, hi = _bootstrap_mean_ci(bias, rng)
    out["mean_abs_steer_bias_ci95_low"] = lo
    out["mean_abs_steer_bias_ci95_high"] = hi

    # Path efficiency only makes sense on successful episodes.
    pe = [
        _safe_float(r.get("path_efficiency", float("nan")))
        for r in records
        if int(r.get("success", 0)) == 1
    ]
    out["mean_success_path_efficiency"] = _mean(pe)
    out["median_success_path_efficiency"] = _median(pe)
    out["successful_episodes_for_path_efficiency"] = len(_finite(pe))

    return out


# ---------------------------------------------------------------------------
# Evaluation engine
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class ModelSpec:
    label: str
    path: Path


def _choose_effective_workers(episodes: int, requested: int) -> int:
    requested = max(1, min(int(requested), int(episodes)))
    # Dividing episodes evenly prevents a fast worker from exhausting its scheduled
    # seed list before another worker completes.
    for w in range(requested, 0, -1):
        if episodes % w == 0:
            return w
    return 1


def _seed_schedules(base_seed: int, episodes: int, workers: int) -> list[list[int]]:
    seeds = list(range(int(base_seed), int(base_seed) + int(episodes)))
    schedules = [seeds[i::workers] for i in range(workers)]
    assert all(schedules)
    assert sum(len(x) for x in schedules) == episodes
    return schedules


def evaluate_model_regime(
    model_spec: ModelSpec,
    terrain_dir: str,
    regime: str,
    episodes: int,
    base_seed: int,
    workers: int,
    device: str,
) -> tuple[list[dict[str, Any]], int]:
    schedules = _seed_schedules(base_seed, episodes, workers)

    env_fns = [
        partial(
            _build_env,
            terrain_dir=terrain_dir,
            seed_schedule=schedule,
            regime=regime,
        )
        for schedule in schedules
    ]

    vec = SubprocVecEnv(env_fns, start_method="spawn")
    try:
        # Validate that the V1.5 patch is actually behaving as intended.
        has_unity_dr_attr = True
        try:
            enabled_flags = vec.get_attr("unity_dr_enabled")
            strengths = vec.get_attr("dr_strength")
        except Exception:
            has_unity_dr_attr = False
            enabled_flags = []
            strengths = []

        if regime == "dr":
            if not has_unity_dr_attr:
                raise RuntimeError(
                    "The loaded RoverEnv does not expose unity_dr_enabled. "
                    "Apply the V1.5 balanced domain-randomization rover_env.py first."
                )
            if not all(bool(x) for x in enabled_flags):
                raise RuntimeError(
                    f"DR benchmark requested but workers report unity_dr_enabled={enabled_flags}"
                )

        model = SAC.load(model_spec.path, device=device)
        model_timesteps = int(getattr(model, "num_timesteps", -1))

        obs = vec.reset()
        completed: dict[int, dict[str, Any]] = {}

        t0 = time.perf_counter()
        while len(completed) < episodes:
            actions, _ = model.predict(obs, deterministic=True)
            obs, rewards, dones, infos = vec.step(actions)

            for done, info in zip(dones, infos):
                if not done:
                    continue
                metrics = info.get("episode_metrics")
                seed = info.get("benchmark_episode_seed")
                if metrics is None or seed is None:
                    continue

                seed = int(seed)
                if seed < base_seed or seed >= base_seed + episodes:
                    continue
                if seed in completed:
                    continue

                completed[seed] = _flatten_episode(
                    metrics=metrics,
                    info=info,
                    model_label=model_spec.label,
                    model_path=str(model_spec.path),
                    model_timesteps=model_timesteps,
                    regime=regime,
                )

                if len(completed) % max(1, episodes // 10) == 0:
                    elapsed = time.perf_counter() - t0
                    print(
                        f"  {model_spec.label:>8s} | {regime:5s} | "
                        f"{len(completed):3d}/{episodes} episodes | {elapsed:7.1f}s",
                        flush=True,
                    )

        records = [completed[s] for s in sorted(completed)]
        elapsed = time.perf_counter() - t0
        print(
            f"  finished {model_spec.label} / {regime}: "
            f"{episodes} episodes in {elapsed:.1f}s",
            flush=True,
        )
        return records, model_timesteps
    finally:
        vec.close()


# ---------------------------------------------------------------------------
# Output / paired comparisons
# ---------------------------------------------------------------------------

def _write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        path.write_text("", encoding="utf-8")
        return

    keys = []
    seen = set()
    for row in rows:
        for k in row:
            if k not in seen:
                seen.add(k)
                keys.append(k)

    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=keys)
        w.writeheader()
        w.writerows(rows)


def make_pairwise(
    records: list[dict[str, Any]],
    reference_label: str,
    bootstrap_seed: int = 20260920,
) -> list[dict[str, Any]]:
    rng = np.random.default_rng(bootstrap_seed)
    rows: list[dict[str, Any]] = []

    regimes = sorted({r["regime"] for r in records})
    models = sorted({r["model"] for r in records})

    metrics = [
        "success",
        "collision",
        "stuck",
        "goal_progress",
        "final_goal_distance_m",
        "min_clearance_m",
        "mean_abs_steer",
        "mean_signed_steer",
        "mean_abs_throttle",
        "mean_signed_throttle",
        "positive_progress_fraction",
        "mean_slip",
        "energy_proxy",
    ]

    for regime in regimes:
        ref = {
            int(r["episode_seed"]): r
            for r in records
            if r["regime"] == regime and r["model"] == reference_label
        }
        if not ref:
            continue

        for model in models:
            if model == reference_label:
                continue
            cur = {
                int(r["episode_seed"]): r
                for r in records
                if r["regime"] == regime and r["model"] == model
            }
            seeds = sorted(set(ref) & set(cur))
            for metric in metrics:
                a = np.asarray([_safe_float(cur[s].get(metric, np.nan)) for s in seeds], dtype=float)
                b = np.asarray([_safe_float(ref[s].get(metric, np.nan)) for s in seeds], dtype=float)
                diff, lo, hi = _bootstrap_paired_diff_ci(a, b, rng)
                rows.append(
                    {
                        "regime": regime,
                        "reference_model": reference_label,
                        "comparison_model": model,
                        "metric": metric,
                        "paired_episodes": len(seeds),
                        "comparison_minus_reference": diff,
                        "ci95_low": lo,
                        "ci95_high": hi,
                    }
                )
    return rows


def _format_pct(x: float) -> str:
    return f"{100.0 * x:5.1f}%" if np.isfinite(x) else "  nan"


def print_summary(rows: list[dict[str, Any]]) -> None:
    print("\n" + "=" * 106)
    print("SAC CHECKPOINT BENCHMARK SUMMARY")
    print("=" * 106)
    print(
        f"{'regime':6s} {'model':9s} {'steps':>9s} "
        f"{'success':>9s} {'collision':>10s} {'stuck':>8s} "
        f"{'progress':>9s} {'final_m':>9s} {'clear_m':>9s} "
        f"{'|steer|':>9s} {'steerBias':>10s} {'throttle':>9s}"
    )
    print("-" * 106)

    for r in rows:
        print(
            f"{str(r['regime']):6s} "
            f"{str(r['model']):9s} "
            f"{int(r['model_num_timesteps']):9d} "
            f"{_format_pct(float(r['success_rate'])):>9s} "
            f"{_format_pct(float(r['collision_rate'])):>10s} "
            f"{_format_pct(float(r['terminal_stuck_rate'])):>8s} "
            f"{float(r['mean_goal_progress']):9.3f} "
            f"{float(r['mean_final_goal_distance_m']):9.2f} "
            f"{float(r['mean_min_clearance_m']):9.2f} "
            f"{float(r['mean_mean_abs_steer']):9.3f} "
            f"{float(r['mean_abs_steer_bias']):10.3f} "
            f"{float(r['mean_mean_signed_throttle']):9.3f}"
        )
    print("=" * 106)


def parse_args():
    ap = argparse.ArgumentParser(
        description=(
            "Fair paired benchmark of SAC 100k-best, 500k, 750k and 1M "
            "on identical clean and V1.5-DR PyBullet episodes."
        )
    )

    ap.add_argument("--terrain-dir", default="data/phase2/test")
    ap.add_argument("--seed-dir", default="checkpoints/sac/seed_21")

    ap.add_argument("--best", default=None)
    ap.add_argument("--ckpt-500k", default=None)
    ap.add_argument("--ckpt-750k", default=None)
    ap.add_argument("--ckpt-1m", default=None)

    ap.add_argument(
        "--episodes",
        type=int,
        default=100,
        help="Episodes PER MODEL PER REGIME. Recommended: 100.",
    )
    ap.add_argument(
        "--base-seed",
        type=int,
        default=120000,
        help="First deterministic episode seed; same seeds are used for every checkpoint and both regimes.",
    )
    ap.add_argument(
        "--workers",
        type=int,
        default=4,
        help="Parallel PyBullet DIRECT workers. Recommended on your laptop: 4.",
    )
    ap.add_argument(
        "--device",
        default="cuda",
        choices=["cuda", "cpu"],
        help="Single central SAC inference device. PyBullet still runs in CPU subprocesses.",
    )
    ap.add_argument("--output-dir", default="results/sac_v15_checkpoint_benchmark")
    return ap.parse_args()


def main():
    args = parse_args()

    if args.episodes < 10:
        raise ValueError("--episodes must be at least 10 for a meaningful benchmark")

    device = args.device
    if device == "cuda" and not torch.cuda.is_available():
        print("CUDA not available; falling back to CPU.", flush=True)
        device = "cpu"

    # The model itself lives in the main process. PyBullet workers do not each
    # create a CUDA context.
    if device == "cpu":
        torch.set_num_threads(1)
        try:
            torch.set_num_interop_threads(1)
        except RuntimeError:
            pass
    else:
        torch.backends.cudnn.benchmark = False
        torch.backends.cudnn.deterministic = True

    np.random.seed(12345)
    torch.manual_seed(12345)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(12345)

    seed_dir = Path(args.seed_dir)

    specs = [
        ModelSpec(
            "best100k",
            Path(args.best) if args.best else seed_dir / "best" / "best_model.zip",
        ),
        ModelSpec(
            "500k",
            Path(args.ckpt_500k) if args.ckpt_500k else seed_dir / "sac_500000_steps.zip",
        ),
        ModelSpec(
            "750k",
            Path(args.ckpt_750k) if args.ckpt_750k else seed_dir / "sac_750000_steps.zip",
        ),
        ModelSpec(
            "1m",
            Path(args.ckpt_1m) if args.ckpt_1m else seed_dir / "sac_1000000_steps.zip",
        ),
    ]

    missing = [str(s.path) for s in specs if not s.path.exists()]
    if missing:
        raise FileNotFoundError(
            "Missing checkpoint(s):\n  " + "\n  ".join(missing)
            + "\n\nUse --best/--ckpt-500k/--ckpt-750k/--ckpt-1m if your files are elsewhere."
        )

    terrain_dir = str(Path(args.terrain_dir))
    _ = terrain_files(terrain_dir)  # fail early if directory is wrong

    workers = _choose_effective_workers(args.episodes, args.workers)
    if workers != args.workers:
        print(
            f"Adjusted workers {args.workers} -> {workers} so {args.episodes} "
            "episodes divide evenly across workers.",
            flush=True,
        )

    print("\nBenchmark configuration")
    print("-----------------------")
    print(f"terrain dir            : {terrain_dir}")
    print(f"episodes/model/regime  : {args.episodes}")
    print(f"episode seeds          : {args.base_seed} .. {args.base_seed + args.episodes - 1}")
    print(f"parallel PyBullet envs : {workers}")
    print(f"SAC inference device   : {device}")
    print("policy action           : deterministic=True")
    print("clean regime            : randomize_physics=False")
    print("DR regime               : balanced Unity DR, full strength")
    print("rendering/camera        : OFF")
    print("\nModels:")
    for s in specs:
        print(f"  {s.label:8s}: {s.path}")

    all_records: list[dict[str, Any]] = []
    summary_rows: list[dict[str, Any]] = []

    overall_start = time.perf_counter()

    # Evaluate clean and DR with the exact same seed list for every checkpoint.
    for regime in ("clean", "dr"):
        print(f"\n### REGIME: {regime.upper()} ###")
        for spec in specs:
            records, model_timesteps = evaluate_model_regime(
                model_spec=spec,
                terrain_dir=terrain_dir,
                regime=regime,
                episodes=args.episodes,
                base_seed=args.base_seed,
                workers=workers,
                device=device,
            )

            # Scenario identity checks within this checkpoint.
            assert len({int(r["episode_seed"]) for r in records}) == args.episodes
            all_records.extend(records)
            summary_rows.append(summarize(records))

    # Cross-model scenario consistency check: same seed must map to same terrain,
    # difficulty and initial goal distance in each regime.
    for regime in ("clean", "dr"):
        reference = {
            int(r["episode_seed"]): r
            for r in all_records
            if r["regime"] == regime and r["model"] == "best100k"
        }
        for r in all_records:
            if r["regime"] != regime or r["model"] == "best100k":
                continue
            rr = reference[int(r["episode_seed"])]
            if str(r.get("terrain")) != str(rr.get("terrain")):
                raise RuntimeError(
                    f"Scenario mismatch at seed {r['episode_seed']} ({regime}): terrain differs"
                )
            a = _safe_float(r.get("initial_goal_distance_m"))
            b = _safe_float(rr.get("initial_goal_distance_m"))
            if np.isfinite(a) and np.isfinite(b) and abs(a - b) > 1e-5:
                raise RuntimeError(
                    f"Scenario mismatch at seed {r['episode_seed']} ({regime}): "
                    f"initial goal distance differs ({a} vs {b})"
                )

    pairwise = make_pairwise(all_records, reference_label="best100k")

    outdir = Path(args.output_dir)
    outdir.mkdir(parents=True, exist_ok=True)

    _write_csv(outdir / "benchmark_episodes.csv", all_records)
    _write_csv(outdir / "benchmark_summary.csv", summary_rows)
    _write_csv(outdir / "benchmark_pairwise_vs_best100k.csv", pairwise)

    elapsed = time.perf_counter() - overall_start
    print_summary(summary_rows)

    print("\nSaved:")
    print(f"  {outdir / 'benchmark_episodes.csv'}")
    print(f"  {outdir / 'benchmark_summary.csv'}")
    print(f"  {outdir / 'benchmark_pairwise_vs_best100k.csv'}")
    print(f"\nTotal benchmark time: {elapsed / 60.0:.1f} minutes")
    print("\nDONE. Upload benchmark_summary.csv and benchmark_pairwise_vs_best100k.csv for interpretation.")


if __name__ == "__main__":
    # Required for Windows multiprocessing / SubprocVecEnv spawn.
    main()
