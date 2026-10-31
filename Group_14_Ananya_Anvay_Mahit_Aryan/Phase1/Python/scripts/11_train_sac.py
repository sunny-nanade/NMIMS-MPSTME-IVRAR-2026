import argparse
from pathlib import Path

import torch
from stable_baselines3 import SAC
from stable_baselines3.common.callbacks import CheckpointCallback, CallbackList
from stable_baselines3.common.vec_env import DummyVecEnv, SubprocVecEnv, VecMonitor

from mars_rover.rl.common import (
    seed_everything, CurriculumCallback, EpisodeMetricsCallback, SuccessFirstEvalCallback,
)
from mars_rover.rl.env_factory import make_env_fn
from mars_rover.rl.performance import resolve_n_envs, print_resource_plan
from mars_rover.rl.features import TerrainStateExtractor
from mars_rover.rl.precision import resolve_precision, assert_fp32_master_weights, print_precision_plan


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--train-dir", default="data/phase2/train")
    ap.add_argument("--val-dir", default="data/phase2/val")
    ap.add_argument("--steps", type=int, default=1_000_000)
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--n-envs", default="auto", choices=["auto", "1", "2", "4", "6"])
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--precision", default="auto", choices=["auto", "bf16", "fp32"],
                    help="Safe training precision: auto uses BF16 on supported CUDA GPUs, otherwise FP32. Validation/predict remains FP32.")
    ap.add_argument("--buffer-size", type=int, default=35_000)
    ap.add_argument("--eval-every", type=int, default=50_000)
    ap.add_argument("--checkpoint-every", type=int, default=100_000)
    ap.add_argument("--eval-episodes", type=int, default=8)
    ap.add_argument("--target-entropy", type=float, default=-1.0)
    args = ap.parse_args()

    seed_everything(args.seed)
    if args.device == "cuda" and not torch.cuda.is_available():
        print("CUDA unavailable; using CPU")
        args.device = "cpu"

    precision = resolve_precision(args.precision, args.device)
    print_precision_plan(precision, args.device, "PPO feature encoder" if "ppo" in __file__.lower() else "SAC feature encoder")

    n_envs = resolve_n_envs(args.n_envs, "sac")
    print_resource_plan("sac", n_envs)
    print("[performance] SAC uses gradient_steps=-1 so each collected transition keeps the original 1:1 gradient-update ratio under vectorized collection.")

    factories = [
        make_env_fn(args.train_dir, "continuous", False, args.seed + i * 1000, randomize_physics=True, curriculum_enabled=True)
        for i in range(n_envs)
    ]
    vec_cls = SubprocVecEnv if n_envs > 1 else DummyVecEnv
    train_env = VecMonitor(vec_cls(factories))
    root = Path(f"checkpoints/sac/seed_{args.seed}")
    root.mkdir(parents=True, exist_ok=True)
    (root / "best").mkdir(exist_ok=True)
    (root / "eval_logs").mkdir(exist_ok=True)
    Path(f"runs/sac/seed_{args.seed}").mkdir(parents=True, exist_ok=True)

    policy_kwargs = dict(
        features_extractor_class=TerrainStateExtractor,
        features_extractor_kwargs=dict(features_dim=256, precision=precision),
        normalize_images=False,
        net_arch=[256, 256],
    )
    model = SAC(
        "MultiInputPolicy",
        train_env,
        learning_rate=3e-4,
        buffer_size=args.buffer_size,
        learning_starts=8_000,
        batch_size=256,
        tau=0.005,
        gamma=0.995,
        train_freq=(1, "step"),
        gradient_steps=-1,
        ent_coef="auto",
        target_entropy=args.target_entropy,
        policy_kwargs=policy_kwargs,
        tensorboard_log=f"runs/sac/seed_{args.seed}",
        seed=args.seed,
        device=args.device,
        verbose=1,
    )
    assert_fp32_master_weights(model.policy, name="SB3 policy")

    checkpoint = CheckpointCallback(
        save_freq=max(1, args.checkpoint_every // n_envs),
        save_path=str(root),
        name_prefix="sac",
        save_replay_buffer=False,
        save_vecnormalize=False,
    )
    evaluator = SuccessFirstEvalCallback(
        args.val_dir,
        eval_freq=max(1, args.eval_every),
        n_eval_episodes=args.eval_episodes,
        best_model_save_path=root / "best",
        base_seed=args.seed + 91000,
        verbose=1,
    )
    curriculum = CurriculumCallback(args.steps, verbose=1)
    episode_metrics = EpisodeMetricsCallback()
    model.learn(total_timesteps=args.steps, callback=CallbackList([checkpoint, evaluator, curriculum, episode_metrics]), progress_bar=True)
    model.save(root / "final_model")
    train_env.close()
    print("Saved SAC models to", root)


if __name__ == "__main__":
    main()
