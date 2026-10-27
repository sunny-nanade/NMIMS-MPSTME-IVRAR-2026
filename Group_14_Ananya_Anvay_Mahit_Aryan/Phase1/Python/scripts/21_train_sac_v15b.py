from __future__ import annotations

import argparse
import csv
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


def _env_factory(terrain_dir: str, seed: int):
    # Prevent CPU thread oversubscription inside each PyBullet subprocess.
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    os.environ["NUMEXPR_NUM_THREADS"] = "1"
    env = RoverEnv(
        terrain_files(terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=True,
        sensor_preview=False,
        curriculum_enabled=True,
        v15b_mode=True,
    )
    # Seed each worker explicitly before SubprocVecEnv performs its first automatic reset.
    env.reset(seed=int(seed))
    return env


class AnchoredScheduleCallback(BaseCallback):
    """Pass total training progress into the anchored V1.5b terrain/DR scheduler."""
    def __init__(self, total_timesteps: int, verbose: int = 1):
        super().__init__(verbose)
        self.total_timesteps_target = max(1, int(total_timesteps))
        self._phase = None

    def _on_step(self) -> bool:
        progress = min(1.0, self.num_timesteps / self.total_timesteps_target)
        self.training_env.env_method("set_curriculum_progress", progress)
        phase = "BOOTSTRAP" if progress < 0.10 else "ANCHORED-MIX"
        if phase != self._phase and self.verbose:
            if phase == "BOOTSTRAP":
                print("[v15b] BOOTSTRAP: terrain 60/30/10 easy/medium/hard; 50% nominal + 50% mild DR")
            else:
                print("[v15b] ANCHORED MIX: terrain 30/45/25; 30% nominal + 70% DR (strength 0.25-0.75)")
            self._phase = phase
        return True


class TimedCheckpointCallback(BaseCallback):
    def __init__(self, save_dir: Path, every_steps: int = 50000, verbose: int = 1):
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
    def __init__(self, *args, seed_schedule: list[int], **kwargs):
        self._schedule = [int(x) for x in seed_schedule]
        self._schedule_i = 0
        self._current_eval_seed = self._schedule[0]
        super().__init__(*args, **kwargs)

    def reset(self, *, seed=None, options=None):
        if self._schedule_i < len(self._schedule):
            seed = self._schedule[self._schedule_i]
            self._schedule_i += 1
        else:
            seed = self._schedule[-1]
        self._current_eval_seed = int(seed)
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
    for w in range(min(episodes, requested), 0, -1):
        if episodes % w == 0:
            return w
    return 1


def _eval_factory(terrain_dir: str, seeds: list[int], regime: str):
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    os.environ["NUMEXPR_NUM_THREADS"] = "1"
    is_dr = regime == "dr"
    return ScheduledEvalEnv(
        terrain_files(terrain_dir),
        action_mode="continuous",
        route_observation=False,
        render_mode=None,
        randomize_physics=is_dr,
        sensor_preview=False,
        curriculum_enabled=False,
        v15b_mode=True,
        dr_force_episode=True if is_dr else False,
        dr_fixed_strength=0.75 if is_dr else 0.0,
        seed_schedule=seeds,
    )


def evaluate_parallel(model, terrain_dir: str, regime: str, episodes: int, base_seed: int, workers: int):
    workers = _divisor_workers(episodes, workers)
    all_seeds = list(range(base_seed, base_seed + episodes))
    schedules = [all_seeds[i::workers] for i in range(workers)]
    env = SubprocVecEnv(
        [partial(_eval_factory, terrain_dir, s, regime) for s in schedules],
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
    return {
        "success": float(np.mean([r.get("success", 0.0) for r in records])),
        "progress": float(np.mean([r.get("goal_progress", 0.0) for r in records])),
        "collision": float(np.mean([r.get("collision", 0.0) for r in records])),
        "stuck": float(np.mean([r.get("terminal_reason") == "stuck" for r in records])),
        "reward": float(np.mean([r.get("reward", 0.0) for r in records])),
        "final_m": float(np.mean([r.get("final_goal_distance_m", 0.0) for r in records])),
        "throttle": float(np.mean([r.get("mean_signed_throttle", 0.0) for r in records])),
        "steer_bias": float(np.mean([abs(r.get("mean_signed_steer", 0.0)) for r in records])),
    }


class DualRegimeEvalCallback(BaseCallback):
    """Fixed-seed CLEAN + DR validation, with separate clean and robust best models."""
    def __init__(
        self,
        terrain_dir: str,
        eval_freq: int,
        n_eval_episodes: int,
        eval_workers: int,
        save_dir: Path,
        base_seed: int = 90000,
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
        combined_reward = 0.5 * (clean["reward"] + dr["reward"])

        # Success dominates, then actual progress, then fewer collisions, then reward.
        robust_key = (combined_success, combined_progress, -combined_collision, combined_reward)
        clean_key = (clean["success"], clean["progress"], -clean["collision"], clean["reward"])

        if clean_key > self.best_clean_key:
            self.best_clean_key = clean_key
            self.model.save(self.save_dir / "best_clean" / "best_model")

        if robust_key > self.best_robust_key:
            self.best_robust_key = robust_key
            self.model.save(self.save_dir / "best_robust" / "best_model")
            # Compatibility with scripts/13_live_rl_demo.py
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
            "eval_seconds": time.perf_counter() - t0,
        }
        self._write_history(row)

        for prefix, vals in (("clean", clean), ("dr", dr)):
            for k, v in vals.items():
                self.logger.record(f"eval_{prefix}/{k}", float(v))
        self.logger.record("eval_combined/success", combined_success)
        self.logger.record("eval_combined/progress", combined_progress)
        self.logger.record("eval_combined/collision", combined_collision)

        print(
            f"[eval @ {self.num_timesteps:,}] "
            f"CLEAN success={clean['success']:.3f} progress={clean['progress']:.3f} "
            f"collision={clean['collision']:.3f} stuck={clean['stuck']:.3f} throttle={clean['throttle']:+.3f} | "
            f"DR success={dr['success']:.3f} progress={dr['progress']:.3f} "
            f"collision={dr['collision']:.3f} stuck={dr['stuck']:.3f} throttle={dr['throttle']:+.3f}"
        )
        return True


def _resolve_eval_dir(arg: str) -> str:
    if arg != "auto":
        return arg
    val = Path("data/phase2/val")
    if val.exists() and list(val.glob("*.npz")):
        return str(val)
    # Keep the held-out test set untouched for the final 100+100 benchmark.
    # Fixed-seed validation on training terrains is still sufficient to detect
    # policy collapse and choose checkpoints without test-set leakage.
    return "data/phase2/train"


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
    ap.add_argument("--eval-workers", type=int, default=4)
    ap.add_argument("--seed", type=int, default=31)
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--precision", choices=["bf16", "fp32"], default="bf16")
    args = ap.parse_args()

    if args.n_envs != 2:
        print("[note] This V1.5b configuration was tuned for --n-envs 2; using your requested value.")

    device = args.device
    if device == "cuda" and not torch.cuda.is_available():
        print("CUDA unavailable: falling back to CPU/fp32.")
        device = "cpu"
        args.precision = "fp32"

    if args.precision == "bf16" and device == "cuda":
        bf16_ok = bool(getattr(torch.cuda, "is_bf16_supported", lambda: True)())
        if not bf16_ok:
            print("CUDA device does not support BF16 well; falling back to fp32.")
            args.precision = "fp32"

    # Keep FP32 SAC math genuinely FP32: no TF32 shortcut. BF16 is confined to the
    # feature encoder by TerrainStateExtractor and evaluation runs in FP32.
    if torch.cuda.is_available():
        torch.backends.cuda.matmul.allow_tf32 = False
        torch.backends.cudnn.allow_tf32 = False

    seed_everything(args.seed)
    eval_dir = _resolve_eval_dir(args.eval_dir)
    train_files = terrain_files(args.train_dir)
    _ = terrain_files(eval_dir)

    run_dir = Path(f"checkpoints/sac/seed_{args.seed}")
    run_dir.mkdir(parents=True, exist_ok=True)
    tb_dir = Path(f"logs/sac_v15b/seed_{args.seed}")
    tb_dir.mkdir(parents=True, exist_ok=True)

    env_fns = [
        partial(_env_factory, args.train_dir, args.seed + 1000 * i)
        for i in range(args.n_envs)
    ]
    env = SubprocVecEnv(env_fns, start_method="spawn")

    policy_kwargs = dict(
        features_extractor_class=TerrainStateExtractor,
        features_extractor_kwargs=dict(features_dim=256, precision=args.precision),
        normalize_images=False,
        net_arch=[256, 256],
        use_sde=False,
    )

    print("\nV1.5b SAC configuration")
    print("-----------------------")
    print(f"train terrains     : {args.train_dir} ({len(train_files)} files)")
    print(f"validation terrains: {eval_dir}")
    print(f"steps              : {args.steps:,}")
    print(f"training envs      : {args.n_envs}")
    print(f"replay buffer      : {args.buffer_size:,} transitions")
    print(f"device             : {device}")
    print(f"encoder precision  : {args.precision}")
    print("SAC heads/losses   : FP32")
    print("PyBullet physics   : full 240 Hz; rendering OFF")
    print("reward             : normalized real goal progress + tiny efficiency/safety costs")
    print("stuck detector     : <0.40 m net goal progress over 120 controls (14.4 s)")
    print("terrain after 100k : 30% easy / 45% medium / 25% hard")
    print("physics after 100k : 30% nominal / 70% moderate DR; DR strength 0.25-0.75")
    print(f"dual validation    : every {args.eval_every:,} steps, {args.eval_episodes} clean + {args.eval_episodes} DR")
    print(f"output             : {run_dir}\n")

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

    callbacks = CallbackList([
        AnchoredScheduleCallback(args.steps, verbose=1),
        EpisodeMetricsCallback(),
        TimedCheckpointCallback(run_dir, args.checkpoint_every, verbose=1),
        DualRegimeEvalCallback(
            terrain_dir=eval_dir,
            eval_freq=args.eval_every,
            n_eval_episodes=args.eval_episodes,
            eval_workers=args.eval_workers,
            save_dir=run_dir,
            base_seed=90000,
            verbose=1,
        ),
    ])

    try:
        model.learn(total_timesteps=args.steps, callback=callbacks, progress_bar=True)
        model.save(run_dir / f"sac_{args.steps}_steps")
        print(f"\nTraining complete. Final model: {run_dir / f'sac_{args.steps}_steps.zip'}")
        print(f"Robust deployment candidate: {run_dir / 'best/best_model.zip'}")
        print(f"Clean-best checkpoint:       {run_dir / 'best_clean/best_model.zip'}")
        print(f"Robust-best checkpoint:      {run_dir / 'best_robust/best_model.zip'}")
        print(f"Eval history:                {run_dir / 'eval_history.csv'}")
    finally:
        env.close()


if __name__ == "__main__":
    main()
