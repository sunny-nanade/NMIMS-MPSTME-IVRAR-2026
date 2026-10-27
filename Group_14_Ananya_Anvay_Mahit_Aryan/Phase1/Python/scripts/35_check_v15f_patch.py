from __future__ import annotations

import argparse
import time
from functools import partial
from pathlib import Path
import numpy as np
import torch
from stable_baselines3 import SAC
from stable_baselines3.common.vec_env import SubprocVecEnv

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files
from mars_rover.rl.features import TerrainStateExtractor
from mars_rover.rl.protected_replay import ProtectedDemoDictReplayBuffer


def make_env(folder,seed):
    e=RoverEnv(terrain_files(folder),action_mode="continuous",route_observation=False,render_mode=None,randomize_physics=True,sensor_preview=False,curriculum_enabled=True,v15f_mode=True)
    e.set_v15f_stage(0); e.reset(seed=seed); return e


def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--terrain-dir",default="data/phase2/train"); ap.add_argument("--demo-file",default="data/v15f_teacher_demos.npz"); ap.add_argument("--device",default="cuda"); args=ap.parse_args()
    print("V1.5f PRE-FLIGHT\n"+"="*72)
    env=make_env(args.terrain_dir,7001)
    try:
        for stage,(lo,hi) in enumerate([(20,30),(30,50),(50,75),(75,110)]):
            env.set_v15f_stage(stage); vals=[]
            for i in range(5):
                _,info=env.reset(seed=7001+stage*100+i); vals.append(float(info["reference_path_length_m"]))
            med=float(np.median(vals)); print(f"stage {stage}: median mission {med:.1f}m | samples "+", ".join(f"{x:.1f}" for x in vals))
            if not (0.80*lo <= med <= 1.20*hi):raise RuntimeError(f"mission selector failed stage {stage}: {vals}")
        env.set_v15f_stage(0); obs,_=env.reset(seed=7777)
        if obs["terrain"].shape!=(5,32,32) or obs["state"].shape!=(14,):raise RuntimeError("observation shape changed")
        if np.any(obs["terrain"][4]!=0):raise RuntimeError("A* route leaked into terrain channel")
        if abs(float(obs["state"][12]))>1e-7 or abs(float(obs["state"][13]))>1e-7:raise RuntimeError("route leaked into state[12:14]")
        a=env.v15f_teacher_action()
        if a.shape!=(2,) or not np.isfinite(a).all() or np.max(np.abs(a))>1.00001:raise RuntimeError(f"bad teacher action {a}")
        print("environment + teacher contract: PASS")
    finally:env.close()

    demo=Path(args.demo_file)
    if not demo.exists():raise FileNotFoundError(f"{demo} missing; generate demos first")
    with np.load(demo,allow_pickle=False) as d:
        req={"terrain","state","actions","next_terrain","next_state","rewards","dones"}; missing=req-set(d.files)
        if missing:raise RuntimeError(f"demo missing keys {missing}")
        n=len(d["actions"])
        if n<5000:raise RuntimeError(f"demo dataset too small: {n}")
        if d["terrain"].shape[1:]!=(5,32,32) or d["state"].shape[1:]!=(14,) or d["actions"].shape[1:]!=(2,):raise RuntimeError("demo shapes wrong")
        if not np.isfinite(d["state"]).all() or not np.isfinite(d["actions"]).all():raise RuntimeError("demo NaN/Inf")
        print(f"teacher dataset integrity: PASS ({n:,} transitions)")

    # Hotfix-3 performance regression test: materialize the FULL compressed
    # teacher dataset once. This is the exact path that used to appear hung.
    t0 = time.perf_counter()
    with np.load(demo, allow_pickle=False) as data:
        terrain_full = np.asarray(data["terrain"], dtype=np.uint8).copy()
        state_full = np.asarray(data["state"], dtype=np.float32).copy()
        actions_full = np.asarray(data["actions"], dtype=np.float32).copy()
        next_terrain_full = np.asarray(data["next_terrain"], dtype=np.uint8).copy()
        next_state_full = np.asarray(data["next_state"], dtype=np.float32).copy()
        rewards_full = np.asarray(data["rewards"], dtype=np.float32).copy()
        dones_full = np.asarray(data["dones"], dtype=np.bool_).copy()

    eager_seconds = time.perf_counter() - t0
    if len(actions_full) != n:
        raise RuntimeError(
            f"full NPZ materialization length mismatch: {len(actions_full)} != {n}"
        )
    obs_mib = (terrain_full.nbytes + next_terrain_full.nbytes) / (1024 ** 2)
    print(
        f"full 20k NPZ eager materialization: PASS "
        f"({eager_seconds:.2f}s, terrain+next={obs_mib:.1f} MiB)"
    )
    del (
        terrain_full, state_full, actions_full, next_terrain_full,
        next_state_full, rewards_full, dones_full
    )

    device=args.device
    if device=="cuda" and not torch.cuda.is_available():device="cpu"
    vec=SubprocVecEnv([partial(make_env,args.terrain_dir,8101),partial(make_env,args.terrain_dir,8102)],start_method="spawn")
    try:
        model=SAC("MultiInputPolicy",vec,learning_rate=1e-4,buffer_size=4000,learning_starts=0,batch_size=128,tau=.005,gamma=.998,train_freq=(1,"step"),gradient_steps=1,ent_coef="auto_0.1",target_entropy=-1.0,policy_kwargs=dict(features_extractor_class=TerrainStateExtractor,features_extractor_kwargs=dict(features_dim=256,precision="bf16" if device=="cuda" else "fp32"),normalize_images=False,net_arch=[256,256],use_sde=False),replay_buffer_class=ProtectedDemoDictReplayBuffer,replay_buffer_kwargs=dict(demo_capacity=1000,demo_fraction=.25),device=device,seed=8111,verbose=0)
        loaded=model.replay_buffer.load_teacher_npz(demo,max_items=512)
        if loaded!=512:raise RuntimeError("protected demo load failed")
        # This explicitly exercises the n_envs=2 vector-buffer prefill path that
        # previously failed with: "cannot reshape array of size 2 into shape (2,2)".
        seeded=model.replay_buffer.seed_normal_from_teacher_npz(demo,max_items=256)
        if seeded!=256:
            raise RuntimeError(
                f"normal replay teacher prefill failed: seeded={seeded}, expected=256"
            )
        pre=model.replay_buffer.stats()
        if pre["normal_n_envs"] != 2:
            raise RuntimeError(f"expected n_envs=2 replay, got {pre}")
        if pre["normal_size"] < 256:
            raise RuntimeError(
                f"vector replay prefill stored too few transitions: {pre}"
            )
        print("n_envs=2 normal-replay teacher prefill: PASS", pre)

        # Exercise mixed protected/normal sampling BEFORE model.learn().
        # This specifically catches SB3 optional fields such as discounts=None.
        sample=model.replay_buffer.sample(128)
        if sample.actions.shape[0]!=128:
            raise RuntimeError(
                f"mixed replay batch size wrong: {sample.actions.shape}"
            )
        optional_none = [
            name for name in getattr(sample, "_fields", ())
            if getattr(sample, name) is None
        ]
        print(
            "mixed replay sample contract: PASS | "
            f"optional None fields={optional_none}"
        )

        # Real mixed replay + gradient update path.
        model.learn(total_timesteps=256,progress_bar=False)
        st=model.replay_buffer.stats(); print("integration replay stats:",st)
        if st["protected_size"]<=0 or st["normal_size"]<=0:
            raise RuntimeError("custom replay integration failed")

        # Re-sample after real online collection/updates.
        sample2=model.replay_buffer.sample(128)
        if sample2.actions.shape[0]!=128:
            raise RuntimeError("post-learn mixed replay batch size wrong")
        print("SubprocVecEnv + protected replay + SAC updates: PASS")
    finally:vec.close()
    print("\nV1.5f PRE-FLIGHT: PASS")
    print("The 1M run can be started.")


if __name__=="__main__":main()
