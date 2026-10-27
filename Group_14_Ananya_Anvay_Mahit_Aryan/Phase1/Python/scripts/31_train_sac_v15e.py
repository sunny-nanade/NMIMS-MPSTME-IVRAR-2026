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
        v15e_mode=True,
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
            v15e_mode=True,
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
            v15e_mode=True,
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
            v15e_mode=True,
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
            v15e_mode=True,
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
            v15e_mode=True,
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


class PeriodicResumeSnapshotCallback(BaseCallback):
    """Disk-safe resume snapshot every N steps.

    Model checkpoints are still kept every 50k.  The replay buffer can be large,
    so Hotfix 3 overwrites ONE latest replay snapshot every 100k instead of filling
    the SSD with many multi-hundred-MB/GB copies.
    """
    def __init__(self, save_dir: Path, every_steps: int = 100_000, verbose: int = 1):
        super().__init__(verbose)
        self.save_dir = Path(save_dir)
        self.every_steps = int(every_steps)
        self.next_save = self.every_steps

    def _init_callback(self):
        (self.save_dir / "resume").mkdir(parents=True, exist_ok=True)

    def _save_atomic(self, step: int):
        folder = self.save_dir / "resume"
        model_path = folder / "resume_latest"
        replay_final = folder / "replay_buffer_latest.pkl"
        replay_tmp = folder / "replay_buffer_latest.tmp.pkl"

        self.model.save(model_path)
        try:
            if replay_tmp.exists():
                replay_tmp.unlink()
            self.model.save_replay_buffer(str(replay_tmp))
            os.replace(replay_tmp, replay_final)
            (folder / "resume_step.txt").write_text(str(int(step)), encoding="utf-8")
            if self.verbose:
                print(
                    f"[resume snapshot] step={step:,} | model={model_path}.zip | "
                    f"replay={replay_final}"
                )
        except Exception as exc:
            # Never crash a healthy RL run solely because a disk snapshot failed.
            print(f"[resume snapshot WARNING] replay-buffer save failed: {exc!r}")

    def _on_step(self) -> bool:
        if self.num_timesteps < self.next_save:
            return True
        while self.next_save <= self.num_timesteps:
            self._save_atomic(self.next_save)
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
    """Competence-gated difficulty + independent optimizer stabilization.

    Crucial V1.5e safeguard: even if Stage-A competence has not yet been reached,
    continued 3e-4 / gradient_steps=-1 updates are stopped at 100k. The terrain/DR
    distribution stays Stage A; only optimizer pressure drops. This directly guards
    against the 100k->200k policy destruction seen in V1.5c.
    """
    def __init__(self, eval_cb, lr_schedule, save_dir: Path, verbose: int = 1):
        super().__init__(verbose)
        self.eval_cb = eval_cb
        self.lr_schedule = lr_schedule
        self.save_dir = Path(save_dir)
        self.stage = 0
        self.last_eval_seen = -1
        self.consecutive_ready = 0
        self.stabilized = False
        self.history_path = self.save_dir / "stage_history.csv"

    def _set_optimizer_pressure(self, lr: float, grad: int):
        self.lr_schedule.value = float(lr)
        self.model.gradient_steps = int(grad)
        for optimizer in (self.model.actor.optimizer, self.model.critic.optimizer):
            for group in optimizer.param_groups:
                group["lr"] = float(lr)

    def _write_stage(self, name: str, lr: float, grad: int):
        info = self.training_env.env_method("v15d_stage_snapshot")[0]
        exists = self.history_path.exists()
        row = {
            "timesteps": int(self.num_timesteps),
            "stage": self.stage,
            "stage_name": info["name"],
            "event": name,
            "learning_rate": lr,
            "gradient_steps": grad,
        }
        with self.history_path.open("a", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(row.keys()))
            if not exists:
                w.writeheader()
            w.writerow(row)

    def _apply_stage(self, stage: int):
        self.stage = int(stage)
        self.training_env.env_method("set_v15d_stage", self.stage)
        if self.stage == 0:
            lr, grad = (1e-4, 1) if self.stabilized else (3e-4, -1)
        elif self.stage == 1:
            lr, grad = 1e-4, 1
        else:
            lr, grad = 5e-5, 1
        self._set_optimizer_pressure(lr, grad)
        info = self.training_env.env_method("v15d_stage_snapshot")[0]
        print(
            f"\n[competence curriculum] ENTER STAGE {self.stage} {info['name']} | "
            f"actor/critic lr={lr:.1e} | gradient_steps={grad}\n"
        )
        self._write_stage("stage_enter", lr, grad)

    def _init_callback(self):
        self._apply_stage(0)

    def _ready_for_optimizer_stabilization(self) -> bool:
        """Protect a useful Stage-A policy as soon as competence first appears.

        Seed-61 showed strong behaviour already at 50k, then degraded by 100k while
        still receiving the aggressive bootstrap optimizer pressure.  Stabilization
        is therefore competence-triggered, with 100k retained only as a fallback.
        """
        c = self.eval_cb.latest_clean
        d = self.eval_cb.latest_dr
        if c is None or d is None:
            return False

        combined_success = 0.5 * (c["success"] + d["success"])
        combined_route = 0.5 * (c["route_progress"] + d["route_progress"])
        combined_throttle = 0.5 * (c["throttle"] + d["throttle"])

        return bool(
            self.stage == 0
            and combined_route >= 0.45
            and combined_success >= 0.08
            and combined_throttle >= 0.45
        )

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
            return bool(
                self.num_timesteps >= 100_000
                and c["success"] >= 0.15
                and combined_success >= 0.10
                and combined_route >= 0.55
                and combined_stuck <= 0.55
                and combined_throttle >= 0.40
            )
        if self.stage == 1:
            return bool(
                self.num_timesteps >= 250_000
                and c["success"] >= 0.20
                and d["success"] >= 0.10
                and combined_success >= 0.15
                and combined_route >= 0.58
                and combined_stuck <= 0.50
                and combined_throttle >= 0.35
            )
        return False

    def _on_step(self) -> bool:
        step = int(self.eval_cb.latest_eval_step)

        # V1.5e Hotfix 3: deterministic optimizer protection.
        # Across repeated runs SAC learns useful movement early and then risks
        # policy drift under the aggressive bootstrap update pressure.  Therefore
        # protect the learned policy at 50k unconditionally instead of making this
        # decision depend on a noisy 16-episode competence estimate.
        if not self.stabilized and self.num_timesteps >= 50_000:
            self.stabilized = True
            if self.stage == 0:
                self._set_optimizer_pressure(1e-4, 1)
                print(
                    "\n[stability safeguard] 50k reached: keeping Stage-A terrain/DR, "
                    "but reducing actor/critic LR to 1e-4 and gradient_steps to 1. "
                    "This protects early navigation while allowing continued refinement.\n"
                )
                self._write_stage("50k_optimizer_stabilization", 1e-4, 1)

        if step < 0 or step == self.last_eval_seen:
            return True
        self.last_eval_seen = step
        if self.stage >= 2:
            return True

        if self._ready_for_next():
            self.consecutive_ready += 1
            print(f"[competence curriculum] stage {self.stage} readiness {self.consecutive_ready}/2")
        else:
            self.consecutive_ready = 0

        if self.consecutive_ready >= 2:
            self.consecutive_ready = 0
            self._apply_stage(self.stage + 1)
        return True


class MultiGateCallback(BaseCallback):
    """Early-learning, competence and robustness gates for V1.5e."""
    def __init__(self, eval_cb, stage_cb, terrain_dir, eval_workers, save_dir,
                 gate_steps=(50_000, 100_000, 150_000, 250_000), verbose=1):
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

        best = max(0.0, float(self.eval_cb.best_combined_route_progress_seen))
        latest = float(self.eval_cb.latest_combined_route_progress)
        best_thr = max(0.0, float(self.eval_cb.best_combined_throttle_seen))
        latest_thr = float(self.eval_cb.latest_combined_throttle)

        if gate_step <= 50_000:
            # Fail only an unmistakable non-learning run; do not over-prune a policy
            # that is already showing real route movement or any true success.
            obvious_failure = bool(
                easy["success"] <= 0.0
                and clean["success"] <= 0.0
                and current["success"] <= 0.0
                and max(easy["route_progress"], clean["route_progress"], current["route_progress"]) < 0.15
                and min(easy["stuck"], clean["stuck"], current["stuck"]) > 0.85
            )
            criteria = {"not_obvious_nonlearning": not obvious_failure}
            retention_floor = 0.0
            throttle_floor = 0.0

        elif gate_step <= 100_000:
            # 100k is now a DIAGNOSTIC transition, not a hair-trigger hard gate.
            # The optimizer has just been stabilized (or was stabilized earlier);
            # give that lower-pressure regime time to act. Stop only if the run is
            # unmistakably dead on all independent scenario sets.
            retention_floor = 0.0
            throttle_floor = 0.0
            successes = easy["success_count"] + clean["success_count"] + current["success_count"]
            max_route = max(easy["route_progress"], clean["route_progress"], current["route_progress"])
            max_thr = max(easy["throttle"], clean["throttle"], current["throttle"])
            min_stuck = min(easy["stuck"], clean["stuck"], current["stuck"])
            catastrophic = bool(
                successes == 0
                and max_route < 0.20
                and max_thr < 0.30
                and min_stuck > 0.85
            )
            criteria = {
                "not_catastrophic_at_optimizer_transition": not catastrophic,
            }

        elif gate_step <= 150_000:
            # First true retention gate: the policy has now had ~50k steps under
            # reduced optimizer pressure. Use both same-seed eval retention and the
            # larger smoke sets, but fail only on a coherent collapse pattern rather
            # than one noisy threshold.
            retention_floor = 0.60 * best
            throttle_floor = 0.55 * best_thr
            successes = easy["success_count"] + clean["success_count"] + current["success_count"]

            route_ok = bool(
                easy["route_progress"] >= 0.35
                and clean["route_progress"] >= 0.28
                and current["route_progress"] >= 0.25
            )
            throttle_ok = bool(
                easy["throttle"] >= 0.30
                and clean["throttle"] >= 0.28
                and current["throttle"] >= 0.28
            )
            retention_ok = bool(latest >= retention_floor)
            catastrophic_stuck = bool(
                successes == 0
                and min(easy["stuck"], clean["stuck"], current["stuck"]) > 0.90
            )

            criteria = {
                "route_learning_present": route_ok,
                "useful_throttle_present": throttle_ok,
                "retains_fixed_seed_progress": retention_ok,
                "not_universal_stuck_collapse": not catastrophic_stuck,
                "success_or_substantial_progress": bool(
                    successes >= 1
                    or (
                        easy["route_progress"] >= 0.50
                        and clean["route_progress"] >= 0.38
                        and current["route_progress"] >= 0.34
                    )
                ),
            }

        else:
            # 200k/250k robustness gate: now require stronger evidence, but still
            # avoid killing a healthy model because one scenario bucket misses one
            # arbitrary percentage by a few points.
            retention_floor = 0.65 * best
            throttle_floor = 0.50 * best_thr
            successes = easy["success_count"] + clean["success_count"] + current["success_count"]

            route_score = sum([
                easy["route_progress"] >= 0.45,
                clean["route_progress"] >= 0.35,
                current["route_progress"] >= 0.32,
            ])
            stuck_score = sum([
                easy["stuck"] <= 0.75,
                clean["stuck"] <= 0.75,
                current["stuck"] <= 0.80,
            ])
            throttle_score = sum([
                easy["throttle"] >= 0.30,
                clean["throttle"] >= 0.28,
                current["throttle"] >= 0.28,
            ])

            criteria = {
                "route_progress_2of3": route_score >= 2,
                "stuck_control_2of3": stuck_score >= 2,
                "throttle_2of3": throttle_score >= 2,
                "retains_progress": latest >= retention_floor,
                "success_evidence": successes >= 1,
            }

        passed = bool(all(criteria.values()))

        # Hotfix 3 principle:
        # Gates are DIAGNOSTIC. SAC evaluation is noisy/non-monotonic and a useful
        # policy must not be killed because one 16-24 episode bucket misses an
        # arbitrary threshold by a few percentage points. Only CollapseGuard below
        # is allowed to terminate training after REPEATED catastrophic evidence.
        report = {
            "gate_step": gate_step,
            "actual_step": int(self.num_timesteps),
            "passed": passed,
            "diagnostic_only": True,
            "hard_stop": False,
            "stage": stage,
            "earlier_best_combined_route_progress": best,
            "latest_combined_route_progress": latest,
            "retention_floor": retention_floor,
            "earlier_best_combined_throttle": best_thr,
            "latest_combined_throttle": latest_thr,
            "throttle_floor": throttle_floor,
            "easy_clean": easy,
            "clean_mixed": clean,
            "current_stage_mix": current,
            "criteria": criteria,
        }
        report_path = self.save_dir / f"smoke_gate_{gate_step}.json"
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

        print("\n" + "=" * 82)
        print(f"V1.5e BEHAVIOR GATE @ {gate_step:,} | current stage {stage}")
        print("=" * 82)
        for name, x in (("EASY CLEAN", easy), ("CLEAN MIXED", clean), ("STAGE MIX", current)):
            print(
                f"{name:12s}: success={x['success']:.3f} route={x['route_progress']:.3f} "
                f"goal={x['goal_progress']:.3f} collision={x['collision']:.3f} "
                f"stuck={x['stuck']:.3f} throttle={x['throttle']:+.3f}"
            )
        print(f"Earlier best route progress : {best:.3f}")
        print(f"Required retention floor    : {retention_floor:.3f}")
        print("\nGate criteria:")
        for name, ok in criteria.items():
            print(f"  {'PASS' if ok else 'FAIL':4s}  {name}")

        folder = self.save_dir / ("gate_pass" if passed else "gate_warning")
        folder.mkdir(parents=True, exist_ok=True)
        self.model.save(folder / f"model_at_{gate_step}")

        status = "PASS" if passed else "WARNING"
        print(f"\nGATE @ {gate_step:,}: {status} (diagnostic only)")
        if not passed:
            print(
                "Training CONTINUES. A single noisy gate is not a hard-stop condition; "
                "only repeated catastrophic collapse can stop the run."
            )
        print(f"Report: {report_path}")
        print("=" * 82 + "\n")
        return True

    def _on_step(self) -> bool:
        for gate in self.gate_steps:
            if gate in self.done or self.num_timesteps < gate:
                continue
            self.done.add(gate)
            self._run_gate(gate)
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
        if step < 100_000 or step == self.last_eval:
            return True
        self.last_eval = step

        success = float(self.eval_cb.latest_combined_success)
        route = float(self.eval_cb.latest_combined_route_progress)
        throttle = abs(float(self.eval_cb.latest_combined_throttle))
        stuck = float(self.eval_cb.latest_combined_stuck)

        # Only count an evaluation as catastrophic when the SAME fixed-seed
        # validation shows an unmistakable no-navigation attractor.
        bad = bool(
            success <= 0.0
            and stuck > 0.90
            and (
                route < 0.08
                or (route < 0.12 and throttle < 0.25)
            )
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
    ap.add_argument("--seed", type=int, default=61)
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
    tb_dir = Path(f"logs/sac_v15e/seed_{args.seed}")
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

    print("\nV1.5e SAC configuration")
    print("-----------------------")
    print("Directional observation: final distance + 6m moving-A* bearing")
    print("Full A* route in obs   : NO; route image/state[12:14] remain zero")
    print("Route progress         : continuous segment arc-length; full route ~= +400")
    print("Steering hint          : max +0.010/step only while actually route-moving")
    print("Throttle teaching      : route-aligned VELOCITY max +0.015/step, not raw command")
    print("Stuck detector         : 14.4s stall warning + ~24s recovery-aware hard stuck")
    print("Replay buffer          :", f"{args.buffer_size:,}")
    print("Stage A                : 70/25/5 terrain; 80/20 nominal/DR; LR starts 3e-4")
    print("50k stability          : Stage A stays; LR->1e-4 and gradient_steps->1")
    print("Stage B                : 45/40/15 terrain; 55/45 nominal/DR; LR 1e-4")
    print("Stage C                : 35/45/20 terrain; 40/60 nominal/DR; LR 5e-5")
    print("Curriculum progression : competence-gated")
    print("Gates                  : 50k/100k/150k/250k diagnostics; NOT hard stops")
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
        gate_steps=(50_000, 100_000, 150_000, 250_000),
        verbose=1,
    )

    callbacks = CallbackList([
        RouteMetricsCallback(),
        TimedCheckpointCallback(run_dir, args.checkpoint_every, verbose=1),
        PeriodicResumeSnapshotCallback(run_dir, every_steps=100_000, verbose=1),
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
        # Final resume snapshot as well.
        resume_dir = run_dir / "resume"
        resume_dir.mkdir(parents=True, exist_ok=True)
        model.save(resume_dir / "resume_latest")
        try:
            model.save_replay_buffer(str(resume_dir / "replay_buffer_latest.pkl"))
            (resume_dir / "resume_step.txt").write_text(
                str(int(model.num_timesteps)), encoding="utf-8"
            )
        except Exception as exc:
            print(f"[final replay save WARNING] {exc!r}")

        print("\nV1.5e training complete.")
        print("Robust best :", run_dir / "best/best_model.zip")
        print("Clean best  :", run_dir / "best_clean/best_model.zip")
        print("Eval history:", run_dir / "eval_history.csv")
        print("Stage history:", run_dir / "stage_history.csv")
    finally:
        env.close()


if __name__ == "__main__":
    main()
