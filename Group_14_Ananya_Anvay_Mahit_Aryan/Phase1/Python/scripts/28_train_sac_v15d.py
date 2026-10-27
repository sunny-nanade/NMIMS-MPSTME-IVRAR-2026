from __future__ import annotations

import argparse
import csv
import json
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
from mars_rover.rl.common import seed_everything, terrain_files
from mars_rover.rl.features import TerrainStateExtractor


def _limit_worker_threads():
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    os.environ["NUMEXPR_NUM_THREADS"] = "1"


class AdaptiveLRSchedule:
    """Mutable SB3 learning-rate schedule controlled by competence stage."""
    def __init__(self, initial_lr: float = 3e-4):
        self.value = float(initial_lr)

    def __call__(self, _progress_remaining: float) -> float:
        return float(self.value)


def _train_env_factory(terrain_dir: str, seed: int):
    _limit_worker_threads()
    env = RoverEnv(
        terrain_files(terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=True,
        sensor_preview=False,
        curriculum_enabled=True,
        v15d_mode=True,
    )
    env.set_v15d_stage(0)
    env.reset(seed=int(seed))
    return env


class ScheduledEvalEnv(RoverEnv):
    def __init__(
        self,
        *args,
        seed_schedule: list[int],
        forced_stage: int | None = None,
        **kwargs,
    ):
        self._eval_seed_schedule = [int(x) for x in seed_schedule]
        self._eval_seed_i = 0
        self._current_eval_seed = self._eval_seed_schedule[0]
        self._forced_stage = forced_stage
        super().__init__(*args, **kwargs)
        if forced_stage is not None:
            self.set_v15d_stage(int(forced_stage))

    def reset(self, *, seed=None, options=None):
        if self._eval_seed_i < len(self._eval_seed_schedule):
            seed = self._eval_seed_schedule[self._eval_seed_i]
            self._eval_seed_i += 1
        else:
            seed = self._eval_seed_schedule[-1]
        self._current_eval_seed = int(seed)

        if self._forced_stage is not None:
            self.set_v15d_stage(int(self._forced_stage))

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


def _eval_factory(
    terrain_dir: str,
    seed_schedule: list[int],
    regime: str,
    stage: int | None,
):
    _limit_worker_threads()
    regime = str(regime).lower()

    if regime == "easy":
        return ScheduledEvalEnv(
            _easy_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=False,
            sensor_preview=False,
            curriculum_enabled=False,
            v15d_mode=True,
            seed_schedule=seed_schedule,
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
            v15d_mode=True,
            seed_schedule=seed_schedule,
        )

    if regime == "dr":
        return ScheduledEvalEnv(
            terrain_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=True,
            sensor_preview=False,
            curriculum_enabled=False,
            v15d_mode=True,
            dr_force_episode=True,
            dr_fixed_strength=0.50,
            seed_schedule=seed_schedule,
        )

    if regime == "stage":
        use_stage = int(0 if stage is None else stage)
        return ScheduledEvalEnv(
            terrain_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=True,
            sensor_preview=False,
            curriculum_enabled=True,
            v15d_mode=True,
            seed_schedule=seed_schedule,
            forced_stage=use_stage,
        )

    if regime == "deployment":
        return ScheduledEvalEnv(
            terrain_files(terrain_dir),
            action_mode="continuous",
            route_observation=False,
            render_mode=None,
            randomize_physics=True,
            sensor_preview=False,
            curriculum_enabled=True,
            v15d_mode=True,
            seed_schedule=seed_schedule,
            forced_stage=2,
        )

    raise ValueError(f"Unknown evaluation regime: {regime}")


def evaluate_parallel(
    model,
    terrain_dir: str,
    regime: str,
    episodes: int,
    base_seed: int,
    workers: int,
    stage: int | None = None,
):
    workers = _divisor_workers(episodes, workers)
    seeds = list(range(int(base_seed), int(base_seed) + int(episodes)))
    schedules = [seeds[i::workers] for i in range(workers)]

    env = SubprocVecEnv(
        [
            partial(_eval_factory, terrain_dir, schedule, regime, stage)
            for schedule in schedules
        ],
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
                if seed not in seeds or seed in completed:
                    continue
                completed[seed] = dict(metrics)
        return [completed[s] for s in sorted(completed)]
    finally:
        env.close()


def _aggregate(records):
    reasons = [str(r.get("terminal_reason", "")) for r in records]
    route_progress = [
        float(r.get("route_progress", r.get("goal_progress", 0.0)))
        for r in records
    ]
    return {
        "episodes": int(len(records)),
        "success_count": int(sum(int(r.get("success", 0)) for r in records)),
        "success": float(np.mean([r.get("success", 0.0) for r in records])),
        "goal_progress": float(np.mean([r.get("goal_progress", 0.0) for r in records])),
        "route_progress": float(np.mean(route_progress)),
        "collision": float(np.mean([r.get("collision", 0.0) for r in records])),
        "stuck": float(np.mean([x == "stuck" for x in reasons])),
        "time_limit": float(np.mean([x == "time_limit" for x in reasons])),
        "reward": float(np.mean([r.get("reward", 0.0) for r in records])),
        "final_m": float(np.mean([r.get("final_goal_distance_m", 0.0) for r in records])),
        "route_remaining_m": float(np.mean([
            r.get("final_route_remaining_m", r.get("final_goal_distance_m", 0.0))
            for r in records
        ])),
        "route_deviation_m": float(np.mean([
            r.get("mean_route_deviation_m", 0.0) for r in records
        ])),
        "throttle": float(np.mean([
            r.get("mean_signed_throttle", 0.0) for r in records
        ])),
        "steer_bias": float(np.mean([
            abs(r.get("mean_signed_steer", 0.0)) for r in records
        ])),
        "positive_route_progress_fraction": float(np.mean([
            r.get("positive_route_progress_fraction", 0.0) for r in records
        ])),
    }


class RouteMetricsCallback(BaseCallback):
    def _on_step(self) -> bool:
        infos = self.locals.get("infos", [])
        for info in infos:
            metrics = info.get("episode_metrics") if isinstance(info, dict) else None
            if not metrics:
                continue
            for key in (
                "success", "collision", "goal_progress", "route_progress",
                "final_goal_distance_m", "final_route_remaining_m",
                "mean_route_deviation_m", "positive_route_progress_fraction",
                "mean_abs_steer", "mean_signed_steer",
                "mean_abs_throttle", "mean_signed_throttle",
                "mean_slip", "energy_proxy",
            ):
                value = metrics.get(key)
                if value is None:
                    continue
                try:
                    self.logger.record_mean(f"rover/{key}", float(value))
                except (TypeError, ValueError):
                    pass
            reason = str(metrics.get("terminal_reason", ""))
            for name in ("goal", "collision", "rollover", "out_of_bounds", "stuck", "time_limit"):
                self.logger.record_mean(
                    f"rover/terminal_{name}",
                    1.0 if reason == name else 0.0,
                )
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


class DualRegimeEvalCallback(BaseCallback):
    """Fixed-seed clean + DR validation, selecting by success then A* route progress."""
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
        self.latest_clean = None
        self.latest_dr = None
        self.latest_combined_success = float("nan")
        self.latest_combined_route_progress = float("nan")
        self.latest_combined_stuck = float("nan")
        self.latest_combined_throttle = float("nan")
        self.best_combined_route_progress_seen = -float("inf")
        self.best_combined_throttle_seen = -float("inf")

    def _init_callback(self):
        for name in ("best_clean", "best_robust", "best"):
            (self.save_dir / name).mkdir(parents=True, exist_ok=True)

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

        self.latest_clean = clean
        self.latest_dr = dr
        combined_success = 0.5 * (clean["success"] + dr["success"])
        combined_route_progress = 0.5 * (clean["route_progress"] + dr["route_progress"])
        combined_collision = 0.5 * (clean["collision"] + dr["collision"])
        combined_stuck = 0.5 * (clean["stuck"] + dr["stuck"])
        combined_throttle = 0.5 * (clean["throttle"] + dr["throttle"])
        combined_reward = 0.5 * (clean["reward"] + dr["reward"])

        self.latest_eval_step = int(self.num_timesteps)
        self.latest_combined_success = combined_success
        self.latest_combined_route_progress = combined_route_progress
        self.latest_combined_stuck = combined_stuck
        self.latest_combined_throttle = combined_throttle
        self.best_combined_route_progress_seen = max(
            self.best_combined_route_progress_seen, combined_route_progress
        )
        self.best_combined_throttle_seen = max(
            self.best_combined_throttle_seen, combined_throttle
        )

        clean_key = (
            clean["success"], clean["route_progress"],
            -clean["collision"], clean["reward"]
        )
        robust_key = (
            combined_success, combined_route_progress,
            -combined_collision, combined_reward
        )

        if clean_key > self.best_clean_key:
            self.best_clean_key = clean_key
            self.model.save(self.save_dir / "best_clean" / "best_model")

        if robust_key > self.best_robust_key:
            self.best_robust_key = robust_key
            self.model.save(self.save_dir / "best_robust" / "best_model")
            self.model.save(self.save_dir / "best" / "best_model")

        stage = int(self.training_env.get_attr("v15d_stage")[0])

        row = {
            "timesteps": int(self.num_timesteps),
            "stage": stage,
            "clean_success": clean["success"],
            "clean_goal_progress": clean["goal_progress"],
            "clean_route_progress": clean["route_progress"],
            "clean_collision": clean["collision"],
            "clean_stuck": clean["stuck"],
            "clean_reward": clean["reward"],
            "clean_final_m": clean["final_m"],
            "clean_route_remaining_m": clean["route_remaining_m"],
            "clean_route_deviation_m": clean["route_deviation_m"],
            "clean_throttle": clean["throttle"],
            "dr_success": dr["success"],
            "dr_goal_progress": dr["goal_progress"],
            "dr_route_progress": dr["route_progress"],
            "dr_collision": dr["collision"],
            "dr_stuck": dr["stuck"],
            "dr_reward": dr["reward"],
            "dr_final_m": dr["final_m"],
            "dr_route_remaining_m": dr["route_remaining_m"],
            "dr_route_deviation_m": dr["route_deviation_m"],
            "dr_throttle": dr["throttle"],
            "combined_success": combined_success,
            "combined_route_progress": combined_route_progress,
            "combined_collision": combined_collision,
            "combined_stuck": combined_stuck,
            "combined_throttle": combined_throttle,
            "eval_seconds": time.perf_counter() - t0,
        }
        self._write_history(row)

        print(
            f"[eval @ {self.num_timesteps:,} | stage {stage}] "
            f"CLEAN success={clean['success']:.3f} route={clean['route_progress']:.3f} "
            f"goal={clean['goal_progress']:.3f} collision={clean['collision']:.3f} "
            f"stuck={clean['stuck']:.3f} throttle={clean['throttle']:+.3f} | "
            f"DR success={dr['success']:.3f} route={dr['route_progress']:.3f} "
            f"goal={dr['goal_progress']:.3f} collision={dr['collision']:.3f} "
            f"stuck={dr['stuck']:.3f} throttle={dr['throttle']:+.3f}"
        )
        return True


class CompetenceStageCallback(BaseCallback):
    """Advance difficulty only after repeated demonstrated competence.

    Stage A: LR 3e-4, SB3 gradient_steps=-1 (2 env transitions => 2 updates)
    Stage B: LR 1e-4, gradient_steps=1
    Stage C: LR 5e-5, gradient_steps=1
    """
    def __init__(
        self,
        eval_cb: DualRegimeEvalCallback,
        lr_schedule: AdaptiveLRSchedule,
        save_dir: Path,
        verbose: int = 1,
    ):
        super().__init__(verbose)
        self.eval_cb = eval_cb
        self.lr_schedule = lr_schedule
        self.save_dir = Path(save_dir)
        self.stage = 0
        self.last_eval_seen = -1
        self.consecutive_ready = 0
        self.history_path = self.save_dir / "stage_history.csv"

    def _apply_stage(self, stage: int):
        self.stage = int(stage)
        self.training_env.env_method("set_v15d_stage", self.stage)

        if self.stage == 0:
            lr, grad = 3e-4, -1
        elif self.stage == 1:
            lr, grad = 1e-4, 1
        else:
            lr, grad = 5e-5, 1

        self.lr_schedule.value = float(lr)
        self.model.gradient_steps = int(grad)

        # Apply immediately; SB3 will also query the mutable lr schedule on train().
        for optimizer in (
            self.model.actor.optimizer,
            self.model.critic.optimizer,
            getattr(self.model, "ent_coef_optimizer", None),
        ):
            if optimizer is None:
                continue
            # Do NOT change entropy optimizer LR: only actor/critic follow stage LR.
            if optimizer is getattr(self.model, "ent_coef_optimizer", None):
                continue
            for group in optimizer.param_groups:
                group["lr"] = float(lr)

        info = self.training_env.env_method("v15d_stage_snapshot")[0]
        print(
            f"\n[competence curriculum] ENTER STAGE {self.stage} {info['name']} | "
            f"actor/critic lr={lr:.1e} | gradient_steps={grad}\n"
        )

        exists = self.history_path.exists()
        row = {
            "timesteps": int(self.num_timesteps),
            "stage": self.stage,
            "stage_name": info["name"],
            "learning_rate": lr,
            "gradient_steps": grad,
        }
        with self.history_path.open("a", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(row.keys()))
            if not exists:
                w.writeheader()
            w.writerow(row)

    def _init_callback(self):
        self._apply_stage(0)

    def _ready_for_next(self) -> bool:
        c = self.eval_cb.latest_clean
        d = self.eval_cb.latest_dr
        if c is None or d is None:
            return False

        combined_success = 0.5 * (c["success"] + d["success"])
        combined_route = 0.5 * (c["route_progress"] + d["route_progress"])
        combined_stuck = 0.5 * (c["stuck"] + d["stuck"])
        combined_throttle = 0.5 * (c["throttle"] + d["throttle"])

        if self.stage == 0:
            # Earliest transition after two 50k evaluations => ~100k.
            if self.num_timesteps < 100_000:
                return False
            return bool(
                c["success"] >= 0.125
                and combined_success >= 0.10
                and combined_route >= 0.50
                and combined_stuck <= 0.60
                and combined_throttle >= 0.45
            )

        if self.stage == 1:
            if self.num_timesteps < 250_000:
                return False
            return bool(
                c["success"] >= 0.20
                and d["success"] >= 0.10
                and combined_success >= 0.15
                and combined_route >= 0.55
                and combined_stuck <= 0.55
                and combined_throttle >= 0.40
            )

        return False

    def _on_step(self) -> bool:
        step = int(self.eval_cb.latest_eval_step)
        if step < 0 or step == self.last_eval_seen:
            return True
        self.last_eval_seen = step

        if self.stage >= 2:
            return True

        if self._ready_for_next():
            self.consecutive_ready += 1
            print(
                f"[competence curriculum] stage {self.stage} readiness "
                f"{self.consecutive_ready}/2"
            )
        else:
            self.consecutive_ready = 0

        if self.consecutive_ready >= 2:
            self.consecutive_ready = 0
            self._apply_stage(self.stage + 1)

        return True


class MultiGateCallback(BaseCallback):
    """150k stability gate + 250k robustness gate.

    The gates evaluate real behavior, not merely reward. A failed gate stops the
    run immediately so another 1M collapse is not allowed to continue.
    """
    def __init__(
        self,
        eval_cb: DualRegimeEvalCallback,
        stage_cb: CompetenceStageCallback,
        terrain_dir: str,
        eval_workers: int,
        save_dir: Path,
        gate_steps=(150_000, 250_000),
        verbose: int = 1,
    ):
        super().__init__(verbose)
        self.eval_cb = eval_cb
        self.stage_cb = stage_cb
        self.terrain_dir = str(terrain_dir)
        self.eval_workers = int(eval_workers)
        self.save_dir = Path(save_dir)
        self.gate_steps = list(map(int, gate_steps))
        self.done = set()

    def _run_gate(self, gate_step: int) -> bool:
        stage = int(self.stage_cb.stage)
        easy = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "easy", 16,
            95_000 + gate_step, self.eval_workers,
        ))
        clean = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "clean", 24,
            96_000 + gate_step, self.eval_workers,
        ))
        current = _aggregate(evaluate_parallel(
            self.model, self.terrain_dir, "stage", 24,
            97_000 + gate_step, self.eval_workers, stage=stage,
        ))

        latest = float(self.eval_cb.latest_combined_route_progress)
        best = float(self.eval_cb.best_combined_route_progress_seen)
        best_thr = float(self.eval_cb.best_combined_throttle_seen)

        if gate_step <= 150_000:
            retention_floor = max(0.25, 0.75 * best)
            throttle_floor = max(0.25, 0.65 * best_thr)
            criteria = {
                "easy_route_progress>=0.45": easy["route_progress"] >= 0.45,
                "clean_route_progress>=0.30": clean["route_progress"] >= 0.30,
                "current_stage_route_progress>=0.30": current["route_progress"] >= 0.30,
                "easy_throttle>=0.35": easy["throttle"] >= 0.35,
                "clean_throttle>=0.30": clean["throttle"] >= 0.30,
                "current_stage_throttle>=0.30": current["throttle"] >= 0.30,
                "easy_stuck<=0.65": easy["stuck"] <= 0.65,
                "clean_stuck<=0.75": clean["stuck"] <= 0.75,
                "current_stage_stuck<=0.75": current["stuck"] <= 0.75,
                "retains_earlier_route_progress": (
                    np.isfinite(latest) and latest >= retention_floor
                ),
                "retains_earlier_throttle": (
                    np.isfinite(self.eval_cb.latest_combined_throttle)
                    and self.eval_cb.latest_combined_throttle >= throttle_floor
                ),
            }
            successes = easy["success_count"] + clean["success_count"] + current["success_count"]
            criteria["success_evidence"] = bool(
                successes >= 1
                or (
                    easy["route_progress"] >= 0.70
                    and clean["route_progress"] >= 0.45
                    and current["route_progress"] >= 0.40
                )
            )
        else:
            retention_floor = max(0.35, 0.80 * best)
            throttle_floor = max(0.30, 0.70 * best_thr)
            criteria = {
                "easy_route_progress>=0.55": easy["route_progress"] >= 0.55,
                "clean_route_progress>=0.40": clean["route_progress"] >= 0.40,
                "current_stage_route_progress>=0.38": current["route_progress"] >= 0.38,
                "easy_throttle>=0.40": easy["throttle"] >= 0.40,
                "clean_throttle>=0.32": clean["throttle"] >= 0.32,
                "current_stage_throttle>=0.32": current["throttle"] >= 0.32,
                "easy_stuck<=0.55": easy["stuck"] <= 0.55,
                "clean_stuck<=0.70": clean["stuck"] <= 0.70,
                "current_stage_stuck<=0.70": current["stuck"] <= 0.70,
                "retains_earlier_route_progress": (
                    np.isfinite(latest) and latest >= retention_floor
                ),
                "retains_earlier_throttle": (
                    np.isfinite(self.eval_cb.latest_combined_throttle)
                    and self.eval_cb.latest_combined_throttle >= throttle_floor
                ),
            }
            successes = easy["success_count"] + clean["success_count"] + current["success_count"]
            criteria["success_evidence"] = bool(
                successes >= 2
                or (
                    easy["route_progress"] >= 0.75
                    and clean["route_progress"] >= 0.50
                    and current["route_progress"] >= 0.45
                )
            )

        passed = bool(all(criteria.values()))
        report = {
            "gate_step": gate_step,
            "actual_step": int(self.num_timesteps),
            "passed": passed,
            "stage": stage,
            "earlier_best_combined_route_progress": best,
            "latest_combined_route_progress": latest,
            "retention_floor": retention_floor,
            "earlier_best_combined_throttle": best_thr,
            "latest_combined_throttle": float(self.eval_cb.latest_combined_throttle),
            "throttle_floor": throttle_floor,
            "easy_clean": easy,
            "clean_mixed": clean,
            "current_stage_mix": current,
            "criteria": criteria,
        }
        report_path = self.save_dir / f"smoke_gate_{gate_step}.json"
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

        print("\n" + "=" * 82)
        print(f"V1.5d BEHAVIOR GATE @ {gate_step:,} | current stage {stage}")
        print("=" * 82)
        for name, x in (("EASY CLEAN", easy), ("CLEAN MIXED", clean), ("STAGE MIX", current)):
            print(
                f"{name:12s}: success={x['success']:.3f} "
                f"route={x['route_progress']:.3f} goal={x['goal_progress']:.3f} "
                f"collision={x['collision']:.3f} stuck={x['stuck']:.3f} "
                f"throttle={x['throttle']:+.3f}"
            )
        print(f"Earlier best route progress : {best:.3f}")
        print(f"Required retention floor    : {retention_floor:.3f}")
        print("\nGate criteria:")
        for name, ok in criteria.items():
            print(f"  {'PASS' if ok else 'FAIL':4s}  {name}")

        folder = self.save_dir / ("gate_pass" if passed else "gate_failed")
        folder.mkdir(parents=True, exist_ok=True)
        self.model.save(folder / f"model_at_{gate_step}")

        print(f"\nGATE @ {gate_step:,}: {'PASS' if passed else 'FAIL'}")
        print(f"Report: {report_path}")
        print("=" * 82 + "\n")
        return passed

    def _on_step(self) -> bool:
        for gate in self.gate_steps:
            if gate in self.done or self.num_timesteps < gate:
                continue
            self.done.add(gate)
            if not self._run_gate(gate):
                print(
                    f"[V1.5d] stopping at ~{gate:,} to avoid wasting the remaining run."
                )
                return False
        return True


class CollapseGuardCallback(BaseCallback):
    """Stop after three consecutive unmistakable post-gate collapses."""
    def __init__(
        self,
        eval_cb: DualRegimeEvalCallback,
        save_dir: Path,
        required_strikes: int = 3,
    ):
        super().__init__(verbose=0)
        self.eval_cb = eval_cb
        self.save_dir = Path(save_dir)
        self.required_strikes = int(required_strikes)
        self.strikes = 0
        self.last_eval = -1

    def _on_step(self) -> bool:
        step = int(self.eval_cb.latest_eval_step)
        if step < 300_000 or step == self.last_eval:
            return True
        self.last_eval = step

        bad = bool(
            self.eval_cb.latest_combined_success <= 0.0
            and self.eval_cb.latest_combined_route_progress < 0.10
            and abs(self.eval_cb.latest_combined_throttle) < 0.18
            and self.eval_cb.latest_combined_stuck > 0.80
        )
        self.strikes = self.strikes + 1 if bad else 0
        if bad:
            print(f"[collapse guard] strike {self.strikes}/{self.required_strikes} at {step:,}")

        if self.strikes >= self.required_strikes:
            folder = self.save_dir / "collapse_guard"
            folder.mkdir(parents=True, exist_ok=True)
            self.model.save(folder / "collapse_guard_model")
            print("[collapse guard] repeated low-throttle/no-route-progress collapse. STOP.")
            return False
        return True


def _resolve_eval_dir(arg: str) -> str:
    if arg != "auto":
        return arg
    val = Path("data/phase2/val")
    if val.exists() and list(val.glob("*.npz")):
        return str(val)
    # Do not select checkpoints on the final held-out test set.
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
    ap.add_argument("--eval-episodes", type=int, default=16)
    ap.add_argument("--eval-workers", type=int, default=4)
    ap.add_argument("--seed", type=int, default=51)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--precision", choices=["bf16", "fp32"], default="bf16")
    args = ap.parse_args()

    device = args.device
    if device == "cuda" and not torch.cuda.is_available():
        print("CUDA unavailable; falling back to CPU/fp32.")
        device = "cpu"
        args.precision = "fp32"

    if args.precision == "bf16" and device == "cuda":
        bf16_ok = bool(getattr(torch.cuda, "is_bf16_supported", lambda: True)())
        if not bf16_ok:
            print("BF16 unsupported; falling back to fp32.")
            args.precision = "fp32"

    if torch.cuda.is_available():
        # Keep the established precision contract: BF16 only in encoder,
        # full SAC optimization math in FP32; no hidden TF32 shortcut.
        torch.backends.cuda.matmul.allow_tf32 = False
        torch.backends.cudnn.allow_tf32 = False

    seed_everything(args.seed)
    eval_dir = _resolve_eval_dir(args.eval_dir)
    _ = terrain_files(args.train_dir)
    _ = terrain_files(eval_dir)

    run_dir = Path(f"checkpoints/sac/seed_{args.seed}")
    run_dir.mkdir(parents=True, exist_ok=True)
    tb_dir = Path(f"logs/sac_v15d/seed_{args.seed}")
    tb_dir.mkdir(parents=True, exist_ok=True)

    ram = _available_ram_gb()
    if ram is not None:
        print(f"[memory preflight] available physical RAM: {ram:.2f} GB")
        if args.buffer_size >= 150_000 and ram < 4.5:
            print(
                "[memory warning] <4.5 GB available. Close background apps or "
                "use --buffer-size 120000. Do not rely on paging."
            )

    env = SubprocVecEnv(
        [
            partial(_train_env_factory, args.train_dir, args.seed + 1000 * i)
            for i in range(args.n_envs)
        ],
        start_method="spawn",
    )

    lr_schedule = AdaptiveLRSchedule(3e-4)
    policy_kwargs = dict(
        features_extractor_class=TerrainStateExtractor,
        features_extractor_kwargs=dict(features_dim=256, precision=args.precision),
        normalize_images=False,
        net_arch=[256, 256],
        use_sde=False,
    )

    print("\nV1.5d SAC configuration")
    print("-----------------------")
    print("Reward potential       : hidden A* feasible remaining distance")
    print("Route progress         : complete feasible route ~= +400")
    print("Steering bonus         : max +0.012/step, only while route progress > 0")
    print("Throttle bonus         : max +0.010/step, only while route progress > 0")
    print("A* in SAC observation  : NO (route channels/state remain zero)")
    print("Stuck detector         : <0.40 m feasible-route progress over 14.4 s")
    print("Replay buffer          :", f"{args.buffer_size:,}")
    print("Stage A                : 60/30/10 terrain; 70/30 nominal/DR; LR 3e-4")
    print("Stage B                : 45/40/15 terrain; 55/45 nominal/DR; LR 1e-4")
    print("Stage C                : 35/45/20 terrain; 40/60 nominal/DR; LR 5e-5")
    print("Curriculum progression : competence-gated, NOT timestep-gated")
    print("Gradient updates       : Stage A=-1, Stage B/C=1")
    print("Gates                  : 150k stability + 250k robustness")
    print("CUDA                   :", device)
    print("Encoder precision      :", args.precision)
    print("SAC heads/losses       : FP32")
    print("Training envs          :", args.n_envs)
    print("Evaluation workers     :", args.eval_workers)
    print("Validation directory   :", eval_dir)
    print("Run directory          :", run_dir, "\n")

    model = SAC(
        "MultiInputPolicy",
        env,
        learning_rate=lr_schedule,
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

    eval_cb = DualRegimeEvalCallback(
        terrain_dir=eval_dir,
        eval_freq=args.eval_every,
        n_eval_episodes=args.eval_episodes,
        eval_workers=args.eval_workers,
        save_dir=run_dir,
        base_seed=90_000,
        verbose=1,
    )
    stage_cb = CompetenceStageCallback(
        eval_cb=eval_cb,
        lr_schedule=lr_schedule,
        save_dir=run_dir,
        verbose=1,
    )
    gates = MultiGateCallback(
        eval_cb=eval_cb,
        stage_cb=stage_cb,
        terrain_dir=eval_dir,
        eval_workers=args.eval_workers,
        save_dir=run_dir,
        gate_steps=(150_000, 250_000),
        verbose=1,
    )

    callbacks = CallbackList([
        RouteMetricsCallback(),
        TimedCheckpointCallback(run_dir, args.checkpoint_every, verbose=1),
        eval_cb,
        stage_cb,
        gates,
        CollapseGuardCallback(eval_cb, run_dir, required_strikes=3),
    ])

    try:
        model.learn(
            total_timesteps=args.steps,
            callback=callbacks,
            progress_bar=True,
        )

        actual = int(model.num_timesteps)
        if actual < args.steps:
            model.save(run_dir / f"sac_{actual}_steps_EARLY_STOP")
            print(
                f"\nTraining stopped early at {actual:,}/{args.steps:,}. "
                "Inspect gate JSON/eval_history.csv before another run."
            )
            return

        model.save(run_dir / f"sac_{args.steps}_steps")
        print("\nV1.5d training complete.")
        print("Robust best :", run_dir / "best/best_model.zip")
        print("Clean best  :", run_dir / "best_clean/best_model.zip")
        print("Eval history:", run_dir / "eval_history.csv")
        print("Stage history:", run_dir / "stage_history.csv")
    finally:
        env.close()


if __name__ == "__main__":
    main()
