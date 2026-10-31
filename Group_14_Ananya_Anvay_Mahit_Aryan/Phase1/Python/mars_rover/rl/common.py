from __future__ import annotations

import json
import random
from pathlib import Path
from typing import Callable

import numpy as np
import torch
from stable_baselines3.common.callbacks import BaseCallback

from mars_rover.envs import RoverEnv


def seed_everything(seed: int):
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def terrain_files(path: str | Path):
    p = Path(path)
    files = sorted(str(x) for x in p.glob("*.npz"))
    if not files:
        raise FileNotFoundError(f"No .npz terrain files found in {p}")
    return files


def make_env_fn(
    terrain_dir,
    action_mode,
    route_observation,
    seed,
    render_mode=None,
    randomize_physics=True,
    curriculum_enabled=False,
):
    files = terrain_files(terrain_dir)

    def _factory():
        env = RoverEnv(
            files,
            action_mode=action_mode,
            route_observation=route_observation,
            render_mode=render_mode,
            randomize_physics=randomize_physics,
            curriculum_enabled=curriculum_enabled,
        )
        env.reset(seed=seed)
        return env

    return _factory


class CurriculumCallback(BaseCallback):
    """Advance RoverEnv easy→medium→hard based on total RL timesteps."""
    def __init__(self, total_timesteps: int, verbose: int = 0):
        super().__init__(verbose)
        self.total_timesteps_target = max(1, int(total_timesteps))
        self._last_bucket = -1

    def _on_step(self) -> bool:
        progress = min(1.0, self.num_timesteps / self.total_timesteps_target)
        try:
            self.training_env.env_method("set_curriculum_progress", progress)
        except Exception:
            pass
        bucket = 0 if progress < 0.40 else (1 if progress < 0.75 else 2)
        if self.verbose and bucket != self._last_bucket:
            names = ["EASY", "MEDIUM", "HARD"]
            print(f"[curriculum] entering {names[bucket]} stage at {progress:.1%} training progress")
            self._last_bucket = bucket
        return True


class EpisodeMetricsCallback(BaseCallback):
    """Log RoverEnv episode outcomes from vectorized PPO/SAC environments."""
    def _on_step(self) -> bool:
        infos = self.locals.get("infos", [])
        for info in infos:
            metrics = info.get("episode_metrics") if isinstance(info, dict) else None
            if not metrics:
                continue
            for key in (
                "success", "collision", "rollover", "out_of_bounds",
                "goal_progress", "final_goal_distance_m", "mean_action_change",
                "mean_abs_steer", "mean_abs_throttle", "mean_signed_steer",
                "mean_signed_throttle", "mean_abs_yaw_rate", "mean_heading_alignment",
                "positive_progress_fraction", "mean_slip", "energy_proxy",
            ):
                value = metrics.get(key)
                if value is None:
                    continue
                try:
                    self.logger.record_mean(f"rover/{key}", float(value))
                except (TypeError, ValueError):
                    pass
            reason = metrics.get("terminal_reason", "")
            for name in ("goal", "collision", "rollover", "out_of_bounds", "stuck", "time_limit"):
                self.logger.record_mean(f"rover/terminal_{name}", 1.0 if reason == name else 0.0)
        return True



class SuccessFirstEvalCallback(BaseCallback):
    """Deterministic fixed-seed validation and success-first checkpoint selection.

    Stable-Baselines3's generic EvalCallback chooses the best checkpoint by mean
    episode reward and advances one persistent evaluation RNG between evaluations.
    For this navigation benchmark we care first about mission success, then net goal
    progress, then reward, and every checkpoint must see the exact same validation
    scenarios.  This callback enforces both requirements.
    """
    def __init__(
        self,
        terrain_dir,
        eval_freq: int,
        n_eval_episodes: int,
        best_model_save_path,
        base_seed: int = 90000,
        verbose: int = 1,
    ):
        super().__init__(verbose)
        self.terrain_dir = terrain_dir
        self.eval_freq = max(1, int(eval_freq))
        self.n_eval_episodes = max(1, int(n_eval_episodes))
        self.best_model_save_path = Path(best_model_save_path)
        self.base_seed = int(base_seed)
        self.best_key = (-1.0, -1e30, -1e30)
        self.eval_env = None
        self.next_eval_at = self.eval_freq

    def _init_callback(self) -> None:
        self.best_model_save_path.mkdir(parents=True, exist_ok=True)
        self.eval_env = RoverEnv(
            terrain_files(self.terrain_dir),
            action_mode="continuous",
            route_observation=False,
            randomize_physics=False,
            sensor_preview=False,
        )

    def _on_step(self) -> bool:
        if self.num_timesteps < self.next_eval_at:
            return True
        while self.next_eval_at <= self.num_timesteps:
            self.next_eval_at += self.eval_freq
        records = []
        for ep in range(self.n_eval_episodes):
            obs, _ = self.eval_env.reset(seed=self.base_seed + ep)
            done = False
            while not done:
                action, _ = self.model.predict(obs, deterministic=True)
                obs, _, term, trunc, info = self.eval_env.step(action)
                done = term or trunc
            records.append(dict(info.get("episode_metrics", {})))

        success = float(np.mean([r.get("success", 0.0) for r in records]))
        progress = float(np.mean([r.get("goal_progress", 0.0) for r in records]))
        reward = float(np.mean([r.get("reward", 0.0) for r in records]))
        collision = float(np.mean([r.get("collision", 0.0) for r in records]))
        final_dist = float(np.mean([r.get("final_goal_distance_m", 0.0) for r in records]))
        abs_steer = float(np.mean([r.get("mean_abs_steer", 0.0) for r in records]))
        steer_bias = float(np.mean([abs(r.get("mean_signed_steer", 0.0)) for r in records]))

        self.logger.record("eval/success_rate", success)
        self.logger.record("eval/mean_goal_progress", progress)
        self.logger.record("eval/mean_reward", reward)
        self.logger.record("eval/collision_rate", collision)
        self.logger.record("eval/mean_final_goal_distance_m", final_dist)
        self.logger.record("eval/mean_abs_steer", abs_steer)
        self.logger.record("eval/mean_abs_steer_bias", steer_bias)

        # Lexicographic score: mission completion dominates, then progress, then reward.
        key = (success, progress, reward)
        if key > self.best_key:
            self.best_key = key
            self.model.save(self.best_model_save_path / "best_model")
            if self.verbose:
                print(
                    f"[eval] new best: success={success:.3f} progress={progress:.3f} "
                    f"reward={reward:.2f} collision={collision:.3f} "
                    f"final_dist={final_dist:.2f} m"
                )
        elif self.verbose:
            print(
                f"[eval] success={success:.3f} progress={progress:.3f} reward={reward:.2f} "
                f"collision={collision:.3f} final_dist={final_dist:.2f} m"
            )
        return True

    def _on_training_end(self) -> None:
        if self.eval_env is not None:
            self.eval_env.close()
            self.eval_env = None

def evaluate_predictor(env: RoverEnv, predictor: Callable, episodes=20, base_seed=1000):
    records = []
    for ep in range(int(episodes)):
        obs, _ = env.reset(seed=base_seed + ep)
        done = False
        while not done:
            action = predictor(obs)
            obs, reward, terminated, truncated, info = env.step(action)
            done = terminated or truncated
        rec = dict(info.get("episode_metrics", {}))
        rec["episode"] = ep
        records.append(rec)
    return records


def save_json(path, obj):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2))
