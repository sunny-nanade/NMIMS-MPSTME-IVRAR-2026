import argparse
import csv
import random
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from torch.utils.tensorboard import SummaryWriter

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import seed_everything, terrain_files
from mars_rover.rl.d3qn import DuelingQNetwork, LinearSchedule, PrioritizedReplayBuffer
from mars_rover.rl.precision import resolve_precision, autocast_context, assert_fp32_master_weights, print_precision_plan


def choose_device(requested):
    if requested == "cuda" and not torch.cuda.is_available():
        print("CUDA requested but unavailable; falling back to CPU")
        return torch.device("cpu")
    return torch.device(requested)


def _torch_load(path, map_location="cpu"):
    try:
        return torch.load(path, map_location=map_location, weights_only=True)
    except TypeError:
        return torch.load(path, map_location=map_location)


def _checkpoint_payload(online, target, optimizer, grad_updates, step, seed, planned_steps, precision, **extra):
    payload = {
        "model": online.state_dict(),
        "target_model": target.state_dict(),
        "optimizer": optimizer.state_dict(),
        "grad_updates": int(grad_updates),
        "step": int(step),
        "seed": int(seed),
        "planned_steps": int(planned_steps),
        "precision": str(precision),
        # Replay-buffer contents are intentionally not saved: they are large.
        # Therefore any restart remains a warm resume until the buffer refills.
        "resume_format": 2,
    }
    payload.update(extra)
    return payload


def _existing_episode_index(csv_path):
    if not csv_path.exists() or csv_path.stat().st_size == 0:
        return 0
    last_episode = -1
    try:
        with open(csv_path, "r", newline="") as f:
            for row in csv.DictReader(f):
                try:
                    last_episode = max(last_episode, int(row.get("episode", -1)))
                except (TypeError, ValueError):
                    pass
    except OSError:
        return 0
    return last_episode + 1


def _existing_best(ckpt_dir, max_step):
    best_path = ckpt_dir / "best_model.pt"
    if not best_path.exists():
        return -1.0, -1e30, -1e30
    try:
        payload = _torch_load(best_path, map_location="cpu")
        if not isinstance(payload, dict):
            return -1.0, -1e30, -1e30
        best_step = int(payload.get("step", -1))
        if best_step > max_step:
            print(f"[resume] Ignoring best_model.pt from later step {best_step:,} (> resume step {max_step:,}).")
            return -1.0, -1e30, -1e30
        return (
            float(payload.get("val_success", -1.0)),
            float(payload.get("val_progress", -1e30)),
            float(payload.get("val_reward", -1e30)),
        )
    except Exception as exc:
        print(f"[resume] Warning: could not read existing best_model.pt: {exc}")
        return -1.0, -1e30, -1e30


def eval_q(net, terrain_dir, device, episodes, base_seed=70000):
    env = RoverEnv(terrain_files(terrain_dir), action_mode="discrete", route_observation=True, randomize_physics=False)
    records = []
    net.eval()
    with torch.no_grad():
        for ep in range(episodes):
            obs, _ = env.reset(seed=base_seed + ep)
            done = False
            while not done:
                t = torch.as_tensor(obs["terrain"][None], device=device)
                s = torch.as_tensor(obs["state"][None], device=device)
                action = int(net(t, s).argmax(dim=1).item())
                obs, _, term, trunc, info = env.step(action)
                done = term or trunc
            records.append(info["episode_metrics"])
    env.close()
    net.train()
    success = float(np.mean([r["success"] for r in records]))
    mean_reward = float(np.mean([r["reward"] for r in records]))
    return success, mean_reward, records


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--train-dir", default="data/phase2/train")
    ap.add_argument("--val-dir", default="data/phase2/val")
    ap.add_argument("--steps", type=int, default=600_000,
                    help="TOTAL desired global training steps, not additional steps after resume.")
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--device", choices=["cuda", "cpu"], default="cuda")
    ap.add_argument("--precision", default="auto", choices=["auto", "bf16", "fp32"],
                    help="Safe training precision: auto uses BF16 on supported CUDA GPUs, otherwise FP32. Validation remains FP32.")
    ap.add_argument("--buffer-size", type=int, default=40_000)
    ap.add_argument("--batch-size", type=int, default=128)
    ap.add_argument("--learning-starts", type=int, default=8_000)
    ap.add_argument("--eval-every", type=int, default=50_000)
    ap.add_argument("--checkpoint-every", type=int, default=100_000)
    ap.add_argument("--eval-episodes", type=int, default=8)
    ap.add_argument("--resume", type=str, default=None,
                    help="Warm-resume from a D3QN checkpoint. --steps remains the TOTAL target (e.g. resume 150k with --steps 300000).")
    args = ap.parse_args()

    if args.steps <= 0:
        raise ValueError("--steps must be positive")

    device = choose_device(args.device)
    resume_payload = None
    resume_path = None
    start_step = 0
    resume_has_target = False
    resume_has_optimizer = False

    if args.resume:
        resume_path = Path(args.resume).expanduser()
        if not resume_path.exists():
            raise FileNotFoundError(f"Resume checkpoint not found: {resume_path}")
        resume_payload = _torch_load(resume_path, map_location="cpu")
        if not isinstance(resume_payload, dict) or "model" not in resume_payload:
            raise ValueError(f"Unsupported D3QN checkpoint format: {resume_path}")
        start_step = int(resume_payload.get("step", 0))
        ckpt_seed = resume_payload.get("seed", None)
        if ckpt_seed is not None and int(ckpt_seed) != args.seed:
            raise ValueError(
                f"Checkpoint seed is {ckpt_seed}, but --seed is {args.seed}. "
                "Use the checkpoint's original seed for a continuation run."
            )
        if start_step <= 0:
            raise ValueError("Resume checkpoint does not contain a valid positive 'step'.")
        if start_step >= args.steps:
            raise ValueError(
                f"Checkpoint is already at step {start_step:,}, but --steps={args.steps:,}. "
                "--steps is the TOTAL desired training step count and must be larger than the checkpoint step."
            )
        planned = resume_payload.get("planned_steps", None)
        if planned is not None and int(planned) != args.steps:
            print(
                f"[resume] NOTE: checkpoint was created with planned_steps={int(planned):,}, "
                f"but this run targets {args.steps:,}. Schedules will continue using the new total target."
            )
        resume_has_target = "target_model" in resume_payload
        resume_has_optimizer = "optimizer" in resume_payload

    # For a warm resume, use a deterministic but different runtime RNG stream so
    # we do not replay the beginning of the interrupted run's random sequence.
    runtime_seed = args.seed if start_step == 0 else args.seed + start_step
    seed_everything(runtime_seed)

    precision = resolve_precision(args.precision, device)
    print_precision_plan(precision, device, "D3QN neural forward")
    run_dir = Path(f"runs/d3qn/seed_{args.seed}")
    ckpt_dir = Path(f"checkpoints/d3qn/seed_{args.seed}")
    run_dir.mkdir(parents=True, exist_ok=True)
    ckpt_dir.mkdir(parents=True, exist_ok=True)
    writer = SummaryWriter(str(run_dir))

    env = RoverEnv(
        terrain_files(args.train_dir),
        action_mode="discrete",
        route_observation=True,
        randomize_physics=True,
        curriculum_enabled=True,
    )
    obs, _ = env.reset(seed=runtime_seed)
    n_actions = env.action_space.n
    online = DuelingQNetwork(n_actions=n_actions).to(device)
    target = DuelingQNetwork(n_actions=n_actions).to(device)
    optimizer = torch.optim.AdamW(online.parameters(), lr=2.5e-4, weight_decay=1e-5)

    grad_updates = 0
    if resume_payload is not None:
        online.load_state_dict(resume_payload["model"])
        if resume_has_target:
            target.load_state_dict(resume_payload["target_model"])
            grad_updates = int(resume_payload.get("grad_updates", 0))
        else:
            # Old checkpoints only contain online-network weights. Starting the
            # target network from the same learned weights is the safest warm resume.
            target.load_state_dict(online.state_dict())

        if resume_has_optimizer:
            try:
                optimizer.load_state_dict(resume_payload["optimizer"])
            except Exception as exc:
                print(f"[resume] Warning: optimizer state could not be restored ({exc}); using a fresh optimizer.")
                resume_has_optimizer = False

        print("\n[resume] D3QN continuation enabled")
        print(f"[resume] checkpoint : {resume_path}")
        print(f"[resume] completed  : {start_step:,} global steps")
        print(f"[resume] target     : {args.steps:,} global steps")
        print(f"[resume] remaining  : {args.steps - start_step:,} transitions")
        if resume_has_target and resume_has_optimizer:
            print("[resume] restored online + target networks and optimizer state")
        else:
            print("[resume] OLD weights-only checkpoint detected -> warm resume")
            print("[resume] online weights restored; target initialized from online; optimizer is fresh")
        print(
            f"[resume] replay buffer is intentionally fresh; gradient training will restart after "
            f"{args.learning_starts:,} new transitions refill the buffer"
        )
        print("[resume] curriculum / epsilon / PER-beta schedules continue from the GLOBAL checkpoint step")
        print(f"[resume] runtime RNG seed = {runtime_seed} (deterministic continuation stream)\n")
    else:
        target.load_state_dict(online.state_dict())

    target.eval()
    assert_fp32_master_weights(online, name="D3QN online network")
    assert_fp32_master_weights(target, name="D3QN target network")

    replay = PrioritizedReplayBuffer(
        args.buffer_size,
        terrain_shape=env.observation_space["terrain"].shape,
        state_dim=env.observation_space["state"].shape[0],
        alpha=0.6,
    )
    eps = LinearSchedule(1.0, 0.05, int(args.steps * 0.45))
    beta = LinearSchedule(0.4, 1.0, args.steps)
    gamma = 0.995
    train_every = 4
    target_every_updates = 1500
    episode_return = 0.0
    episode_len = 0

    csv_path = run_dir / "episodes.csv"
    append_csv = resume_payload is not None and csv_path.exists() and csv_path.stat().st_size > 0
    episode_idx = _existing_episode_index(csv_path) if append_csv else 0
    csv_file = open(csv_path, "a" if append_csv else "w", newline="")
    csv_writer = csv.DictWriter(csv_file, fieldnames=[
        "step", "episode", "reward", "length", "success", "collision", "reason",
        "goal_progress", "final_goal_distance_m", "mean_action_change"
    ])
    if not append_csv:
        csv_writer.writeheader()

    if resume_payload is not None:
        best_success, best_progress, best_reward = _existing_best(ckpt_dir, start_step)
        # If the selected resume checkpoint itself is an evaluated best checkpoint,
        # include its score as another candidate.
        checkpoint_score = (
            float(resume_payload.get("val_success", -1.0)),
            float(resume_payload.get("val_progress", -1e30)),
            float(resume_payload.get("val_reward", -1e30)),
        )
        if checkpoint_score > (best_success, best_progress, best_reward):
            best_success, best_progress, best_reward = checkpoint_score
    else:
        best_success = -1.0
        best_progress = -1e30
        best_reward = -1e30

    start_time = time.time()
    last_stage = None
    for step in range(start_step + 1, args.steps + 1):
        progress = step / max(1, args.steps)
        env.set_curriculum_progress(progress)
        stage = "EASY" if progress < 0.40 else ("MEDIUM" if progress < 0.75 else "HARD")
        if stage != last_stage:
            print(f"[curriculum] entering {stage} stage at {progress:.1%} training progress")
            last_stage = stage
        epsilon = eps.value(step)
        if random.random() < epsilon:
            action = env.action_space.sample()
        else:
            with torch.no_grad():
                t = torch.as_tensor(obs["terrain"][None], device=device)
                s = torch.as_tensor(obs["state"][None], device=device)
                # Training-time action selection may use BF16 network compute, but
                # Q-values are returned to FP32 before the discrete decision.
                with autocast_context(precision, enabled=(precision == "bf16")):
                    q_action = online(t, s)
                action = int(q_action.float().argmax(dim=1).item())

        next_obs, reward, term, trunc, info = env.step(action)
        done = term or trunc
        replay.add(obs, action, reward, next_obs, done)
        obs = next_obs
        episode_return += reward
        episode_len += 1

        if len(replay) >= args.learning_starts and step % train_every == 0:
            batch = replay.sample(args.batch_size, beta.value(step), device)
            # Safe BF16 AMP: only neural forward compute is autocast. Q-values are
            # promoted back to FP32 before Double-DQN action selection, Bellman/TD
            # targets, Huber loss, PER priorities, gradient clipping and optimizer
            # updates. Master network/optimizer weights remain FP32.
            with autocast_context(precision, enabled=(precision == "bf16")):
                q_all_amp = online(batch["terrain"], batch["state"])
            q_all = q_all_amp.float()
            q = q_all.gather(1, batch["action"].unsqueeze(1)).squeeze(1)
            with torch.no_grad():
                with autocast_context(precision, enabled=(precision == "bf16")):
                    next_online_amp = online(batch["next_terrain"], batch["next_state"])
                    next_target_amp = target(batch["next_terrain"], batch["next_state"])
                next_online = next_online_amp.float()
                next_target = next_target_amp.float()
                next_actions = next_online.argmax(dim=1)
                next_q = next_target.gather(1, next_actions.unsqueeze(1)).squeeze(1)
                y = batch["reward"].float() + gamma * (1.0 - batch["done"].float()) * next_q
            td = y - q
            loss_each = F.smooth_l1_loss(q, y, reduction="none")
            loss = (batch["weights"].float() * loss_each).mean()
            if not torch.isfinite(loss):
                raise FloatingPointError(f"Non-finite D3QN loss at step {step}: {loss.detach().cpu().item()}")
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            grad_norm = torch.nn.utils.clip_grad_norm_(online.parameters(), 10.0)
            if not torch.isfinite(torch.as_tensor(grad_norm)):
                raise FloatingPointError(f"Non-finite D3QN gradient norm at step {step}: {grad_norm}")
            optimizer.step()
            replay.update_priorities(batch["indices"], td.detach().abs().cpu().numpy() + 1e-5)
            grad_updates += 1
            if grad_updates % target_every_updates == 0:
                target.load_state_dict(online.state_dict())
            if grad_updates % 50 == 0:
                writer.add_scalar("train/loss", float(loss.item()), step)
                writer.add_scalar("train/q_mean", float(q.mean().item()), step)
                writer.add_scalar("train/epsilon", epsilon, step)
                writer.add_scalar("train/beta", beta.value(step), step)

        if done:
            m = info.get("episode_metrics", {})
            writer.add_scalar("rollout/episode_reward", episode_return, step)
            writer.add_scalar("rollout/episode_length", episode_len, step)
            writer.add_scalar("rollout/success", m.get("success", 0), step)
            writer.add_scalar("rollout/collision", m.get("collision", 0), step)
            writer.add_scalar("rollout/goal_progress", m.get("goal_progress", 0), step)
            writer.add_scalar("rollout/final_goal_distance_m", m.get("final_goal_distance_m", 0), step)
            writer.add_scalar("rollout/mean_action_change", m.get("mean_action_change", 0), step)
            csv_writer.writerow({
                "step": step,
                "episode": episode_idx,
                "reward": episode_return,
                "length": episode_len,
                "success": m.get("success", 0),
                "collision": m.get("collision", 0),
                "reason": m.get("terminal_reason", ""),
                "goal_progress": m.get("goal_progress", 0),
                "final_goal_distance_m": m.get("final_goal_distance_m", 0),
                "mean_action_change": m.get("mean_action_change", 0),
            })
            csv_file.flush()
            episode_idx += 1
            episode_return = 0.0
            episode_len = 0
            obs, _ = env.reset()

        if step % args.checkpoint_every == 0:
            torch.save(
                _checkpoint_payload(
                    online, target, optimizer, grad_updates, step, args.seed, args.steps, precision
                ),
                ckpt_dir / f"checkpoint_{step}.pt",
            )

        if step % args.eval_every == 0:
            success, mean_reward, eval_records = eval_q(online, args.val_dir, device, args.eval_episodes, base_seed=80000)
            mean_progress = float(np.mean([r.get("goal_progress", 0.0) for r in eval_records]))
            writer.add_scalar("eval/success_rate", success, step)
            writer.add_scalar("eval/mean_reward", mean_reward, step)
            writer.add_scalar("eval/mean_goal_progress", mean_progress, step)
            print(f"step={step:,}  val_success={success:.3f}  val_progress={mean_progress:.3f}  val_reward={mean_reward:.2f}  epsilon={epsilon:.3f}")
            score = (success, mean_progress, mean_reward)
            if score > (best_success, best_progress, best_reward):
                best_success, best_progress, best_reward = score
                torch.save(
                    _checkpoint_payload(
                        online, target, optimizer, grad_updates, step, args.seed, args.steps, precision,
                        val_success=success, val_progress=mean_progress, val_reward=mean_reward,
                    ),
                    ckpt_dir / "best_model.pt",
                )

    torch.save(
        _checkpoint_payload(
            online, target, optimizer, grad_updates, args.steps, args.seed, args.steps, precision
        ),
        ckpt_dir / "final_model.pt",
    )
    elapsed = time.time() - start_time
    segment_label = "resumed segment" if resume_payload is not None else "run"
    print(f"Finished D3QN {segment_label} in {elapsed/3600:.2f} h. Best val success={best_success:.3f}")
    csv_file.close()
    writer.close()
    env.close()


if __name__ == "__main__":
    main()
