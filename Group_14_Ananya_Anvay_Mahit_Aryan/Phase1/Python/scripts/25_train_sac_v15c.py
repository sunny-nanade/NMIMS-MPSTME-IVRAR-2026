from __future__ import annotations

import argparse
import csv
import json
import math
import os
import time
from functools import partial
from pathlib import Path

import numpy as np
import torch
from stable_baselines3 import SAC
from stable_baselines3.common.callbacks import BaseCallback, CallbackList
from stable_baselines3.common.vec_env import SubprocVecEnv

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import EpisodeMetricsCallback, seed_everything, terrain_files
from mars_rover.rl.features import TerrainStateExtractor


def _limit_worker_threads():
    # PyBullet is process-parallel here. Prevent BLAS/OpenMP oversubscription.
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    os.environ["NUMEXPR_NUM_THREADS"] = "1"


def _env_factory(terrain_dir: str, seed: int):
    _limit_worker_threads()
    env = RoverEnv(
        terrain_files(terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=True,
        sensor_preview=False,
        curriculum_enabled=True,
        v15c_mode=True,
    )
    env.reset(seed=int(seed))
    return env


class SmoothScheduleCallback(BaseCallback):
    """Drive the V1.5c schedule with absolute progress through the 1M target."""
    def __init__(self, total_timesteps: int, verbose: int = 1):
        super().__init__(verbose)
        self.total_timesteps_target = max(1, int(total_timesteps))
        self._bucket = -1

    def _on_step(self) -> bool:
        p = min(1.0, self.num_timesteps / self.total_timesteps_target)
        self.training_env.env_method("set_curriculum_progress", p)

        # Log every 10 percentage points during the 0-40% smooth ramp.
        bucket = min(4, int(p / 0.10))
        if self.verbose and bucket != self._bucket:
            ramp = min(1.0, p / 0.40)
            easy = 0.60 + (0.35 - 0.60) * ramp
            med = 0.30 + (0.45 - 0.30) * ramp
            hard = 0.10 + (0.20 - 0.10) * ramp
            dr_p = 0.40 + (0.60 - 0.40) * ramp
            dr_lo = 0.10 + (0.20 - 0.10) * ramp
            dr_hi = 0.35 + (0.65 - 0.35) * ramp
            print(
                f"[v15c schedule] step={self.num_timesteps:,} "
                f"terrain easy/med/hard={easy:.2f}/{med:.2f}/{hard:.2f} | "
                f"nominal/DR={1-dr_p:.2f}/{dr_p:.2f} | "
                f"DR strength={dr_lo:.2f}-{dr_hi:.2f}"
            )
            self._bucket = bucket
        return True


class TimedCheckpointCallback(BaseCallback):
    def __init__(self, save_dir: Path, every_steps: int = 50_000, verbose: int = 1):
        super().__init__(verbose)
        self.save_dir = Path(save_dir)
        self.every_steps = int(every_steps)
        self.next_save = self.every_steps

    def _init_callback(self):
        self.save_dir.mkdir(parents=True, exist_ok=True)

    def _on_step(self) -> bool:
        if self.num_timesteps < self.next_save:
            return True
        while self.next_save <= self.num_timesteps:
            step = self.next_save
            path = self.save_dir / f"sac_{step}_steps"
            self.model.save(path)
            if self.verbose:
                print(f"[checkpoint] saved {path}.zip")
            self.next_save += self.every_steps
        return True


class ScheduledEvalEnv(RoverEnv):
    def __init__(
        self,
        *args,
        seed_schedule: list[int],
        forced_curriculum_progress: float | None = None,
        **kwargs,
    ):
        self._schedule = [int(x) for x in seed_schedule]
        self._schedule_i = 0
        self._current_eval_seed = self._schedule[0]
        self._forced_curriculum_progress = forced_curriculum_progress
        super().__init__(*args, **kwargs)
        if forced_curriculum_progress is not None:
            self.set_curriculum_progress(float(forced_curriculum_progress))

    def reset(self, *, seed=None, options=None):
        if self._schedule_i < len(self._schedule):
            seed = self._schedule[self._schedule_i]
            self._schedule_i += 1
        else:
            seed = self._schedule[-1]
        self._current_eval_seed = int(seed)

        if self._forced_curriculum_progress is not None:
            self.set_curriculum_progress(float(self._forced_curriculum_progress))

        obs, info = super().reset(seed=int(seed), options=options)
        info = dict(info)
        info["eval_seed"] = self._current_eval_seed
        return obs, info

    def step(self, action):
        obs, reward, term, trunc, info = super().step(action)
        info = dict(info)
        info["eval_seed"] = self._current_eval_seed
        return obs, reward, term, trunc, info


def _divisor_workers(episodes: int, requested: int) -> int:
    for w in range(min(int(episodes), int(requested)), 0, -1):
        if episodes % w == 0:
            return w
    return 1


def _easy_files(terrain_dir: str) -> list[str]:
    files = terrain_files(terrain_dir)
    easy = [f for f in files if "_easy_" in Path(f).name.lower()]
    return easy or files


def _eval_factory(terrain_dir: str, seeds: list[int], regime: str):
    _limit_worker_threads()

    regime = str(regime).lower()
    if regime == "easy":
        files = _easy_files(terrain_dir)
        return ScheduledEvalEnv(
            files,
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=False,
            sensor_preview=False,
            curriculum_enabled=False,
            v15c_mode=True,
            seed_schedule=seeds,
        )

    if regime == "clean":
        return ScheduledEvalEnv(
            terrain_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=False,
            sensor_preview=False,
            curriculum_enabled=False,
            v15c_mode=True,
            seed_schedule=seeds,
        )

    if regime == "dr":
        # Stress regime: every episode randomized at the top of the V1.5c range.
        return ScheduledEvalEnv(
            terrain_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=True,
            sensor_preview=False,
            curriculum_enabled=False,
            v15c_mode=True,
            dr_force_episode=True,
            dr_fixed_strength=0.65,
            seed_schedule=seeds,
        )

    if regime == "target":
        # Final intended TRAINING MIX:
        # 35/45/20 easy/medium/hard; 40% nominal + 60% DR; strength 0.20-0.65.
        return ScheduledEvalEnv(
            terrain_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=True,
            sensor_preview=False,
            curriculum_enabled=True,
            v15c_mode=True,
            dr_force_episode=None,
            dr_fixed_strength=None,
            seed_schedule=seeds,
            forced_curriculum_progress=1.0,
        )

    raise ValueError(f"Unknown evaluation regime: {regime}")


def evaluate_parallel(
    model,
    terrain_dir: str,
    regime: str,
    episodes: int,
    base_seed: int,
    workers: int,
):
    workers = _divisor_workers(episodes, workers)
    all_seeds = list(range(base_seed, base_seed + episodes))
    schedules = [all_seeds[i::workers] for i in range(workers)]

    env = SubprocVecEnv(
        [partial(_eval_factory, terrain_dir, sched, regime) for sched in schedules],
        start_method="spawn",
    )
    completed = {}
    try:
        obs = env.reset()
        while len(completed) < episodes:
            action, _ = model.predict(obs, deterministic=True)
            obs, _, dones, infos = env.step(action)
            for done, info in zip(dones, infos):
                if not done:
                    continue
                metrics = info.get("episode_metrics")
                seed = info.get("eval_seed")
                if metrics is None or seed is None:
                    continue
                seed = int(seed)
                if seed in completed or seed not in all_seeds:
                    continue
                completed[seed] = dict(metrics)
        return [completed[s] for s in sorted(completed)]
    finally:
        env.close()


def _aggregate(records):
    reasons = [str(r.get("terminal_reason", "")) for r in records]
    return {
        "episodes": int(len(records)),
        "success_count": int(sum(int(r.get("success", 0)) for r in records)),
        "success": float(np.mean([r.get("success", 0.0) for r in records])),
        "progress": float(np.mean([r.get("goal_progress", 0.0) for r in records])),
        "collision": float(np.mean([r.get("collision", 0.0) for r in records])),
        "stuck": float(np.mean([x == "stuck" for x in reasons])),
        "time_limit": float(np.mean([x == "time_limit" for x in reasons])),
        "reward": float(np.mean([r.get("reward", 0.0) for r in records])),
        "final_m": float(np.mean([r.get("final_goal_distance_m", 0.0) for r in records])),
        "throttle": float(np.mean([r.get("mean_signed_throttle", 0.0) for r in records])),
        "steer_bias": float(np.mean([abs(r.get("mean_signed_steer", 0.0)) for r in records])),
    }


class DualRegimeEvalCallback(BaseCallback):
    """Fixed-seed clean + full-DR monitoring throughout training."""
    def __init__(
        self,
        terrain_dir: str,
        eval_freq: int,
        n_eval_episodes: int,
        eval_workers: int,
        save_dir: Path,
        base_seed: int = 90_000,
        verbose: int = 1,
    ):
        super().__init__(verbose)
        self.terrain_dir = str(terrain_dir)
        self.eval_freq = int(eval_freq)
        self.n_eval_episodes = int(n_eval_episodes)
        self.eval_workers = int(eval_workers)
        self.save_dir = Path(save_dir)
        self.base_seed = int(base_seed)
        self.next_eval = self.eval_freq
        self.best_clean_key = (-1.0, -1e30, -1e30, -1e30)
        self.best_robust_key = (-1.0, -1e30, -1e30, -1e30)
        self.history_path = self.save_dir / "eval_history.csv"

        self.latest_eval_step = -1
        self.latest_combined_progress = float("nan")
        self.latest_combined_throttle = float("nan")
        self.latest_combined_stuck = float("nan")
        self.latest_combined_success = float("nan")
        self.best_combined_progress_seen = -float("inf")

    def _init_callback(self):
        (self.save_dir / "best_clean").mkdir(parents=True, exist_ok=True)
        (self.save_dir / "best_robust").mkdir(parents=True, exist_ok=True)
        (self.save_dir / "best").mkdir(parents=True, exist_ok=True)

    def _write_history(self, row):
        exists = self.history_path.exists()
        with self.history_path.open("a", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(row.keys()))
            if not exists:
                w.writeheader()
            w.writerow(row)

    def _on_step(self) -> bool:
        if self.num_timesteps < self.next_eval:
            return True

        while self.next_eval <= self.num_timesteps:
            self.next_eval += self.eval_freq

        t0 = time.perf_counter()
        clean = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "clean", self.n_eval_episodes,
            self.base_seed, self.eval_workers,
        ))
        dr = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "dr", self.n_eval_episodes,
            self.base_seed, self.eval_workers,
        ))

        combined_success = 0.5 * (clean["success"] + dr["success"])
        combined_progress = 0.5 * (clean["progress"] + dr["progress"])
        combined_collision = 0.5 * (clean["collision"] + dr["collision"])
        combined_stuck = 0.5 * (clean["stuck"] + dr["stuck"])
        combined_throttle = 0.5 * (clean["throttle"] + dr["throttle"])
        combined_reward = 0.5 * (clean["reward"] + dr["reward"])

        self.latest_eval_step = int(self.num_timesteps)
        self.latest_combined_progress = combined_progress
        self.latest_combined_throttle = combined_throttle
        self.latest_combined_stuck = combined_stuck
        self.latest_combined_success = combined_success
        self.best_combined_progress_seen = max(
            self.best_combined_progress_seen, combined_progress
        )

        robust_key = (
            combined_success,
            combined_progress,
            -combined_collision,
            combined_reward,
        )
        clean_key = (
            clean["success"],
            clean["progress"],
            -clean["collision"],
            clean["reward"],
        )

        if clean_key > self.best_clean_key:
            self.best_clean_key = clean_key
            self.model.save(self.save_dir / "best_clean" / "best_model")

        if robust_key > self.best_robust_key:
            self.best_robust_key = robust_key
            self.model.save(self.save_dir / "best_robust" / "best_model")
            self.model.save(self.save_dir / "best" / "best_model")

        row = {
            "timesteps": int(self.num_timesteps),
            "clean_success": clean["success"],
            "clean_progress": clean["progress"],
            "clean_collision": clean["collision"],
            "clean_stuck": clean["stuck"],
            "clean_reward": clean["reward"],
            "clean_final_m": clean["final_m"],
            "clean_throttle": clean["throttle"],
            "dr_success": dr["success"],
            "dr_progress": dr["progress"],
            "dr_collision": dr["collision"],
            "dr_stuck": dr["stuck"],
            "dr_reward": dr["reward"],
            "dr_final_m": dr["final_m"],
            "dr_throttle": dr["throttle"],
            "combined_success": combined_success,
            "combined_progress": combined_progress,
            "combined_collision": combined_collision,
            "combined_stuck": combined_stuck,
            "combined_throttle": combined_throttle,
            "eval_seconds": time.perf_counter() - t0,
        }
        self._write_history(row)

        print(
            f"[eval @ {self.num_timesteps:,}] "
            f"CLEAN success={clean['success']:.3f} progress={clean['progress']:.3f} "
            f"collision={clean['collision']:.3f} stuck={clean['stuck']:.3f} "
            f"throttle={clean['throttle']:+.3f} | "
            f"DR success={dr['success']:.3f} progress={dr['progress']:.3f} "
            f"collision={dr['collision']:.3f} stuck={dr['stuck']:.3f} "
            f"throttle={dr['throttle']:+.3f}"
        )
        return True


class SmokeGateCallback(BaseCallback):
    """High-confidence 200k gate before allowing the remaining 800k.

    It explicitly tests:
      * EASY CLEAN competence,
      * ordinary CLEAN mixed-terrain competence,
      * the FINAL target training distribution.

    If the policy still exhibits the old low-throttle/no-progress attractor,
    training stops automatically at ~200k instead of wasting the full 1M run.
    """
    def __init__(
        self,
        dual_eval: DualRegimeEvalCallback,
        terrain_dir: str,
        gate_at: int,
        eval_workers: int,
        save_dir: Path,
        mixed_episodes: int = 24,
        easy_episodes: int = 16,
        base_seed: int = 95_000,
        verbose: int = 1,
    ):
        super().__init__(verbose)
        self.dual_eval = dual_eval
        self.terrain_dir = str(terrain_dir)
        self.gate_at = int(gate_at)
        self.eval_workers = int(eval_workers)
        self.save_dir = Path(save_dir)
        self.mixed_episodes = int(mixed_episodes)
        self.easy_episodes = int(easy_episodes)
        self.base_seed = int(base_seed)
        self.done = False
        self.passed = False

    def _on_step(self) -> bool:
        if self.done or self.num_timesteps < self.gate_at:
            return True

        self.done = True
        print("\n" + "=" * 78)
        print(f"V1.5c BEHAVIORAL SMOKE GATE @ {self.num_timesteps:,} STEPS")
        print("=" * 78)

        easy = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "easy", self.easy_episodes,
            self.base_seed, self.eval_workers,
        ))
        clean = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "clean", self.mixed_episodes,
            self.base_seed + 1000, self.eval_workers,
        ))
        target = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "target", self.mixed_episodes,
            self.base_seed + 2000, self.eval_workers,
        ))

        total_successes = (
            easy["success_count"] + clean["success_count"] + target["success_count"]
        )

        latest_progress = float(self.dual_eval.latest_combined_progress)
        best_progress = float(self.dual_eval.best_combined_progress_seen)
        anti_collapse_floor = max(
            0.12,
            0.65 * best_progress if np.isfinite(best_progress) else 0.12,
        )

        criteria = {
            "easy_progress>=0.45": easy["progress"] >= 0.45,
            "clean_progress>=0.20": clean["progress"] >= 0.20,
            "target_progress>=0.15": target["progress"] >= 0.15,
            "easy_throttle>=0.35": easy["throttle"] >= 0.35,
            "clean_throttle>=0.25": clean["throttle"] >= 0.25,
            "target_throttle>=0.20": target["throttle"] >= 0.20,
            "easy_stuck<=0.55": easy["stuck"] <= 0.55,
            "clean_stuck<=0.70": clean["stuck"] <= 0.70,
            "target_stuck<=0.75": target["stuck"] <= 0.75,
            "no_early_collapse": (
                np.isfinite(latest_progress)
                and latest_progress >= anti_collapse_floor
            ),
            # Require at least one true mission completion, unless the policy is
            # already extremely close on all three regimes.
            "success_evidence": (
                total_successes >= 1
                or (
                    easy["progress"] >= 0.70
                    and clean["progress"] >= 0.30
                    and target["progress"] >= 0.22
                )
            ),
        }

        self.passed = bool(all(criteria.values()))

        report = {
            "gate_step": int(self.num_timesteps),
            "passed": self.passed,
            "total_successes_across_gate": int(total_successes),
            "latest_combined_progress": latest_progress,
            "best_combined_progress_seen": best_progress,
            "anti_collapse_floor": anti_collapse_floor,
            "easy_clean": easy,
            "clean_mixed": clean,
            "final_target_mix": target,
            "criteria": criteria,
        }
        report_path = self.save_dir / "smoke_gate_report.json"
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

        def p(name, x):
            print(
                f"{name:12s}: success={x['success']:.3f} progress={x['progress']:.3f} "
                f"collision={x['collision']:.3f} stuck={x['stuck']:.3f} "
                f"throttle={x['throttle']:+.3f}"
            )

        p("EASY CLEAN", easy)
        p("CLEAN MIXED", clean)
        p("TARGET MIX", target)
        print(f"Earlier best combined progress: {best_progress:.3f}")
        print(f"Required anti-collapse floor   : {anti_collapse_floor:.3f}")
        print("\nGate criteria:")
        for name, ok in criteria.items():
            print(f"  {'PASS' if ok else 'FAIL':4s}  {name}")

        if self.passed:
            (self.save_dir / "smoke_pass").mkdir(parents=True, exist_ok=True)
            self.model.save(self.save_dir / "smoke_pass" / "smoke_pass_model")
            print("\nSMOKE GATE: PASS")
            print("The old collapse pattern is NOT present at the 200k gate.")
            print("Training will continue in the SAME process/replay buffer to 1M.")
            print("=" * 78 + "\n")
            return True

        (self.save_dir / "smoke_failed").mkdir(parents=True, exist_ok=True)
        self.model.save(self.save_dir / "smoke_failed" / "smoke_failed_model")
        print("\nSMOKE GATE: FAIL")
        print("Training is stopping now to avoid wasting the remaining ~800k steps.")
        print(f"Read: {report_path}")
        print("=" * 78 + "\n")
        return False


class PostGateCollapseGuard(BaseCallback):
    """Abort only after repeated, unmistakable post-gate collapse."""
    def __init__(
        self,
        dual_eval: DualRegimeEvalCallback,
        smoke_gate: SmokeGateCallback,
        save_dir: Path,
        required_strikes: int = 3,
    ):
        super().__init__(verbose=0)
        self.dual_eval = dual_eval
        self.smoke_gate = smoke_gate
        self.save_dir = Path(save_dir)
        self.required_strikes = int(required_strikes)
        self.strikes = 0
        self.last_checked_eval = -1

    def _on_step(self) -> bool:
        if not self.smoke_gate.passed:
            return True
        step = int(self.dual_eval.latest_eval_step)
        if step <= self.smoke_gate.gate_at or step == self.last_checked_eval:
            return True
        self.last_checked_eval = step

        progress = float(self.dual_eval.latest_combined_progress)
        throttle = float(self.dual_eval.latest_combined_throttle)
        stuck = float(self.dual_eval.latest_combined_stuck)
        success = float(self.dual_eval.latest_combined_success)

        bad = (
            success <= 0.0
            and progress < 0.08
            and abs(throttle) < 0.18
            and stuck > 0.80
        )

        self.strikes = self.strikes + 1 if bad else 0
        if bad:
            print(
                f"[collapse guard] strike {self.strikes}/{self.required_strikes} "
                f"at {step:,}: progress={progress:.3f}, throttle={throttle:+.3f}, "
                f"stuck={stuck:.3f}"
            )

        if self.strikes >= self.required_strikes:
            folder = self.save_dir / "collapse_guard"
            folder.mkdir(parents=True, exist_ok=True)
            self.model.save(folder / "collapse_guard_model")
            print(
                "\n[collapse guard] Three consecutive evaluations show the old "
                "no-progress/low-throttle attractor. Stopping early."
            )
            return False
        return True


def _resolve_eval_dir(arg: str) -> str:
    if arg != "auto":
        return arg
    val = Path("data/phase2/val")
    if val.exists() and list(val.glob("*.npz")):
        return str(val)
    # Keep final test terrains untouched until the final 100+100 benchmark.
    return "data/phase2/train"


def _available_ram_gb() -> float | None:
    try:
        import psutil
        return float(psutil.virtual_memory().available / (1024 ** 3))
    except Exception:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--train-dir", default="data/phase2/train")
    ap.add_argument("--eval-dir", default="auto")
    ap.add_argument("--steps", type=int, default=1_000_000)
    ap.add_argument("--n-envs", type=int, default=2)
    ap.add_argument("--buffer-size", type=int, default=150_000)
    ap.add_argument("--batch-size", type=int, default=256)
    ap.add_argument("--learning-starts", type=int, default=8_000)
    ap.add_argument("--eval-every", type=int, default=50_000)
    ap.add_argument("--checkpoint-every", type=int, default=50_000)
    ap.add_argument("--eval-episodes", type=int, default=16, help="episodes PER regime")
    ap.add_argument("--eval-workers", type=int, default=2)
    ap.add_argument("--smoke-gate-at", type=int, default=200_000)
    ap.add_argument("--smoke-mixed-episodes", type=int, default=24)
    ap.add_argument("--smoke-easy-episodes", type=int, default=16)
    ap.add_argument("--seed", type=int, default=41)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--precision", choices=["bf16", "fp32"], default="bf16")
    args = ap.parse_args()

    if args.n_envs != 2:
        print("[note] This configuration was tuned for two training environments.")

    device = args.device
    if device == "cuda" and not torch.cuda.is_available():
        print("CUDA unavailable: falling back to CPU/fp32.")
        device = "cpu"
        args.precision = "fp32"

    if args.precision == "bf16" and device == "cuda":
        bf16_ok = bool(getattr(torch.cuda, "is_bf16_supported", lambda: True)())
        if not bf16_ok:
            print("BF16 unsupported on this CUDA device; falling back to fp32.")
            args.precision = "fp32"

    # BF16 is confined to TerrainStateExtractor during TRAINING.
    # SAC heads, Bellman targets, entropy and losses stay FP32.
    if torch.cuda.is_available():
        torch.backends.cuda.matmul.allow_tf32 = False
        torch.backends.cudnn.allow_tf32 = False

    seed_everything(args.seed)
    eval_dir = _resolve_eval_dir(args.eval_dir)
    train_files = terrain_files(args.train_dir)
    _ = terrain_files(eval_dir)

    run_dir = Path(f"checkpoints/sac/seed_{args.seed}")
    run_dir.mkdir(parents=True, exist_ok=True)
    tb_dir = Path(f"logs/sac_v15c/seed_{args.seed}")
    tb_dir.mkdir(parents=True, exist_ok=True)

    ram = _available_ram_gb()
    if ram is not None:
        print(f"[memory preflight] available physical RAM: {ram:.2f} GB")
        if args.buffer_size >= 150_000 and ram < 4.5:
            print(
                "[memory warning] Less than 4.5 GB is currently available. "
                "Close browser/Unity/background apps before training. "
                "If you cannot free RAM, use --buffer-size 120000."
            )

    env = SubprocVecEnv(
        [
            partial(_env_factory, args.train_dir, args.seed + 1000 * i)
            for i in range(args.n_envs)
        ],
        start_method="spawn",
    )

    policy_kwargs = dict(
        features_extractor_class=TerrainStateExtractor,
        features_extractor_kwargs=dict(features_dim=256, precision=args.precision),
        normalize_images=False,
        net_arch=[256, 256],
        use_sde=False,
    )

    print("\nV1.5c SAC configuration")
    print("-----------------------")
    print(f"train terrains       : {args.train_dir} ({len(train_files)} files)")
    print(f"validation terrains  : {eval_dir}")
    print(f"steps                : {args.steps:,}")
    print(f"training envs        : {args.n_envs}")
    print(f"replay buffer        : {args.buffer_size:,}")
    print(f"device               : {device}")
    print(f"encoder precision    : {args.precision}")
    print("SAC heads/losses     : FP32")
    print("PyBullet physics     : full 240 Hz; rendering OFF")
    print("progress reward      : +400 normalized over a complete mission")
    print("success terminal     : +300 plus up to +40 efficiency")
    print("failure terminals    : stuck/time -225, collision -250, OOB -275, rollover -300")
    print("stuck detector       : <0.40 m net progress over 120 controls / 14.4 s")
    print("smooth curriculum    : ramp 0-400k, then permanently anchored")
    print("final terrain mix    : 35% easy / 45% medium / 20% hard")
    print("final physics mix    : 40% nominal / 60% moderate DR")
    print("final DR strength    : 0.20-0.65")
    print(f"routine validation   : {args.eval_episodes} clean + {args.eval_episodes} DR every {args.eval_every:,}")
    print(f"behavior smoke gate  : {args.smoke_gate_at:,} steps")
    print(f"eval workers         : {args.eval_workers} (RAM-safe; metrics unchanged)")
    print(f"output               : {run_dir}\n")

    model = SAC(
        "MultiInputPolicy",
        env,
        learning_rate=3e-4,
        buffer_size=args.buffer_size,
        learning_starts=args.learning_starts,
        batch_size=args.batch_size,
        tau=0.005,
        gamma=0.995,
        train_freq=(1, "step"),
        gradient_steps=-1,
        ent_coef="auto_0.1",
        target_entropy=-1.0,
        policy_kwargs=policy_kwargs,
        tensorboard_log=str(tb_dir),
        verbose=1,
        seed=args.seed,
        device=device,
        optimize_memory_usage=False,
    )

    dual_eval = DualRegimeEvalCallback(
        terrain_dir=eval_dir,
        eval_freq=args.eval_every,
        n_eval_episodes=args.eval_episodes,
        eval_workers=args.eval_workers,
        save_dir=run_dir,
        base_seed=90_000,
        verbose=1,
    )

    smoke_gate = SmokeGateCallback(
        dual_eval=dual_eval,
        terrain_dir=eval_dir,
        gate_at=args.smoke_gate_at,
        eval_workers=args.eval_workers,
        save_dir=run_dir,
        mixed_episodes=args.smoke_mixed_episodes,
        easy_episodes=args.smoke_easy_episodes,
        base_seed=95_000,
        verbose=1,
    )

    callbacks = CallbackList([
        SmoothScheduleCallback(args.steps, verbose=1),
        EpisodeMetricsCallback(),
        TimedCheckpointCallback(run_dir, args.checkpoint_every, verbose=1),
        dual_eval,
        smoke_gate,
        PostGateCollapseGuard(
            dual_eval=dual_eval,
            smoke_gate=smoke_gate,
            save_dir=run_dir,
            required_strikes=3,
        ),
    ])

    try:
        model.learn(total_timesteps=args.steps, callback=callbacks, progress_bar=True)

        actual = int(model.num_timesteps)
        if actual < args.steps:
            model.save(run_dir / f"sac_{actual}_steps_EARLY_STOP")
            print(
                f"\nTraining stopped early at {actual:,} / {args.steps:,} steps. "
                "Check smoke_gate_report.json or collapse_guard output."
            )
            return

        model.save(run_dir / f"sac_{args.steps}_steps")
        print(f"\nTraining complete: {run_dir / f'sac_{args.steps}_steps.zip'}")
        print(f"Robust best:        {run_dir / 'best/best_model.zip'}")
        print(f"Clean best:         {run_dir / 'best_clean/best_model.zip'}")
        print(f"Smoke report:       {run_dir / 'smoke_gate_report.json'}")
        print(f"Eval history:       {run_dir / 'eval_history.csv'}")
    finally:
        env.close()


if __name__ == "__main__":
    main()
