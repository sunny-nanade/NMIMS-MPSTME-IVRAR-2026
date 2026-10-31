from __future__ import annotations

import argparse
import csv
import json
import os
from functools import partial
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from stable_baselines3 import SAC
from stable_baselines3.common.callbacks import BaseCallback, CallbackList
from stable_baselines3.common.vec_env import SubprocVecEnv

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import seed_everything, terrain_files
from mars_rover.rl.features import TerrainStateExtractor
from mars_rover.rl.protected_replay import ProtectedDemoDictReplayBuffer


def _limit_threads():
    os.environ["OMP_NUM_THREADS"]="1"; os.environ["MKL_NUM_THREADS"]="1"; os.environ["NUMEXPR_NUM_THREADS"]="1"


def _make_train_env(folder: str, seed: int):
    _limit_threads()
    e=RoverEnv(terrain_files(folder),action_mode="continuous",route_observation=False,render_mode=None,
               randomize_physics=True,sensor_preview=False,curriculum_enabled=True,v15f_mode=True)
    e.set_v15f_stage(0); e.reset(seed=int(seed)); return e


class StageEvalEnv(RoverEnv):
    def __init__(self,*args,seeds,stage:int,**kwargs):
        self._seeds=list(map(int,seeds)); self._i=0; self._s=self._seeds[0]; self._stage=int(stage)
        super().__init__(*args,**kwargs); self.set_v15f_stage(self._stage)
    def reset(self,*,seed=None,options=None):
        seed=self._seeds[self._i] if self._i<len(self._seeds) else self._seeds[-1]; self._i+=1; self._s=int(seed)
        self.set_v15f_stage(self._stage); o,i=super().reset(seed=self._s,options=options); i=dict(i); i["eval_seed"]=self._s; return o,i
    def step(self,a):
        o,r,t,tr,i=super().step(a); i=dict(i); i["eval_seed"]=self._s; return o,r,t,tr,i


def _divisor(n,w):
    w=max(1,min(int(n),int(w)))
    while n%w: w-=1
    return w


def _make_eval_env(folder,seeds,stage,dr):
    _limit_threads(); strength=[0.15,0.25,0.40,0.55][int(stage)]
    return StageEvalEnv(terrain_files(folder),action_mode="continuous",route_observation=False,render_mode=None,
                        randomize_physics=bool(dr),sensor_preview=False,curriculum_enabled=True,v15f_mode=True,
                        dr_force_episode=True if dr else False,dr_fixed_strength=strength if dr else 0.0,
                        seeds=seeds,stage=int(stage))


def evaluate_stage(model,folder,stage,dr,episodes,base_seed,workers):
    workers=_divisor(episodes,workers); seeds=list(range(base_seed,base_seed+episodes)); schedules=[seeds[i::workers] for i in range(workers)]
    env=SubprocVecEnv([partial(_make_eval_env,folder,s,stage,dr) for s in schedules],start_method="spawn")
    done={}
    try:
        obs=env.reset()
        while len(done)<episodes:
            a,_=model.predict(obs,deterministic=True); obs,_,ds,infos=env.step(a)
            for d,i in zip(ds,infos):
                if d and i.get("episode_metrics") is not None:
                    s=int(i["eval_seed"])
                    if s in seeds and s not in done: done[s]=dict(i["episode_metrics"])
        return [done[s] for s in sorted(done)]
    finally: env.close()


def aggregate(rs):
    reasons=[str(r.get("terminal_reason","")) for r in rs]
    return dict(episodes=len(rs),success=float(np.mean([r.get("success",0) for r in rs])),
                route_progress=float(np.mean([r.get("route_progress",0) for r in rs])),
                goal_progress=float(np.mean([r.get("goal_progress",0) for r in rs])),
                collision=float(np.mean([r.get("collision",0) for r in rs])),
                stuck=float(np.mean([x=="stuck" for x in reasons])),
                reward=float(np.mean([r.get("reward",0) for r in rs])),
                throttle=float(np.mean([r.get("mean_signed_throttle",0) for r in rs])),
                route_deviation=float(np.mean([r.get("mean_route_deviation_m",0) for r in rs])))


def bc_bootstrap(model,demo_path,epochs=6,batch_size=256,lr=1e-4,seed=1):
    with np.load(demo_path,allow_pickle=False) as d:
        terrain=np.asarray(d["terrain"],dtype=np.uint8); state=np.asarray(d["state"],dtype=np.float32); actions=np.asarray(d["actions"],dtype=np.float32)
    n=len(actions)
    if n<1000: raise RuntimeError("Need >=1000 teacher transitions for BC")
    rng=np.random.default_rng(seed); order=rng.permutation(n); vn=max(512,int(.1*n)); vi=order[:vn]; ti=order[vn:]
    device=model.device; opt=torch.optim.Adam(model.actor.parameters(),lr=lr)
    def run(indices,train):
        indices=np.array(indices,copy=True)
        if train: rng.shuffle(indices)
        ls=[]
        for st in range(0,len(indices),batch_size):
            ix=indices[st:st+batch_size]
            obs={"terrain":torch.as_tensor(terrain[ix],device=device),"state":torch.as_tensor(state[ix],device=device)}
            target=torch.as_tensor(actions[ix],dtype=torch.float32,device=device); pred=model.actor(obs,deterministic=True); loss=F.mse_loss(pred.float(),target)
            if train:
                opt.zero_grad(set_to_none=True); loss.backward(); torch.nn.utils.clip_grad_norm_(model.actor.parameters(),1.0); opt.step()
            ls.append(float(loss.detach().cpu()))
        return float(np.mean(ls))
    model.actor.eval()
    with torch.no_grad(): initial=run(vi,False)
    hist=[]
    for ep in range(epochs):
        model.actor.train(); tr=run(ti,True); model.actor.eval()
        with torch.no_grad(): va=run(vi,False)
        hist.append((tr,va)); print(f"[BC] {ep+1}/{epochs} train_mse={tr:.5f} val_mse={va:.5f}")
    final=hist[-1][1]; improvement=1-final/max(initial,1e-12)
    if not np.isfinite(final) or improvement<0.15: raise RuntimeError(f"BC failed: initial={initial:.5f} final={final:.5f} improvement={improvement:.1%}")
    return dict(initial_val_mse=initial,final_val_mse=final,relative_improvement=improvement,epochs=epochs,transitions=n)


class EvalCallback(BaseCallback):
    def __init__(self,folder,freq,episodes,workers,run_dir):
        super().__init__(1); self.folder=str(folder); self.freq=int(freq); self.episodes=int(episodes); self.workers=int(workers); self.run_dir=Path(run_dir); self.next=self.freq
        self.latest_step=-1; self.latest_stage=0; self.clean=None; self.dr=None; self.best={}; self.path=self.run_dir/"eval_history.csv"
    def _init_callback(self):
        for s in range(4): (self.run_dir/f"best_stage{s}").mkdir(parents=True,exist_ok=True); self.best[s]=(-1,-1e30,-1e30,-1e30)
        (self.run_dir/"best").mkdir(parents=True,exist_ok=True)
    def _write(self,row):
        ex=self.path.exists()
        with self.path.open("a",newline="",encoding="utf-8") as f:
            w=csv.DictWriter(f,fieldnames=list(row));
            if not ex:w.writeheader()
            w.writerow(row)
    def _on_step(self):
        if self.num_timesteps<self.next:return True
        while self.next<=self.num_timesteps:self.next+=self.freq
        stage=int(self.training_env.get_attr("v15f_stage")[0]); base=90000+stage*10000
        c=aggregate(evaluate_stage(self.model,self.folder,stage,False,self.episodes,base,self.workers)); d=aggregate(evaluate_stage(self.model,self.folder,stage,True,self.episodes,base,self.workers))
        self.latest_step=int(self.num_timesteps); self.latest_stage=stage; self.clean=c; self.dr=d
        suc=.5*(c["success"]+d["success"]); route=.5*(c["route_progress"]+d["route_progress"]); stuck=.5*(c["stuck"]+d["stuck"]); rew=.5*(c["reward"]+d["reward"])
        key=(suc,route,-stuck,rew)
        if key>self.best.get(stage,(-1,-1e30,-1e30,-1e30)):
            self.best[stage]=key; self.model.save(self.run_dir/f"best_stage{stage}"/"best_model"); self.model.save(self.run_dir/"best"/"best_model")
        stats=self.model.replay_buffer.stats()
        row=dict(timesteps=self.num_timesteps,stage=stage,clean_success=c["success"],dr_success=d["success"],combined_success=suc,
                 clean_route_progress=c["route_progress"],dr_route_progress=d["route_progress"],combined_route_progress=route,
                 combined_stuck=stuck,clean_collision=c["collision"],dr_collision=d["collision"],clean_throttle=c["throttle"],dr_throttle=d["throttle"],
                 protected_size=stats["protected_size"],demo_fraction=stats["demo_fraction"],online_protected_episodes=stats["online_protected_episodes"])
        self._write(row)
        print(f"[eval {self.num_timesteps:,} stage {stage}] clean success={c['success']:.3f} route={c['route_progress']:.3f} stuck={c['stuck']:.3f} | DR success={d['success']:.3f} route={d['route_progress']:.3f} stuck={d['stuck']:.3f} | protected={stats['protected_size']}")
        return True


class MissionCurriculumCallback(BaseCallback):
    def __init__(self,eval_cb,run_dir):
        super().__init__(1); self.eval=eval_cb; self.run_dir=Path(run_dir); self.stage=0; self.last=-1; self.ready=0; self.path=self.run_dir/"stage_history.csv"
    def _threshold_ok(self):
        c,d=self.eval.clean,self.eval.dr
        if c is None or d is None or self.stage>=3:return False
        suc=.5*(c["success"]+d["success"]); route=.5*(c["route_progress"]+d["route_progress"]); stuck=.5*(c["stuck"]+d["stuck"])
        req=[(.40,.75,.45),(.30,.68,.55),(.20,.60,.65)][self.stage]; return suc>=req[0] and route>=req[1] and stuck<=req[2]
    def _apply(self,s):
        self.stage=int(np.clip(s,0,3)); self.training_env.env_method("set_v15f_stage",self.stage); snap=self.training_env.env_method("v15f_stage_snapshot")[0]
        self.model.replay_buffer.set_demo_fraction(snap["demo_fraction"]); lr=5e-5 if self.stage>=3 else 1e-4
        for opt in (self.model.actor.optimizer,self.model.critic.optimizer):
            for g in opt.param_groups:g["lr"]=lr
        ex=self.path.exists(); row=dict(timesteps=self.num_timesteps,stage=self.stage,name=snap["name"],mission_min_m=snap["mission_min_m"],mission_max_m=snap["mission_max_m"],demo_fraction=snap["demo_fraction"],learning_rate=lr)
        with self.path.open("a",newline="",encoding="utf-8") as f:
            w=csv.DictWriter(f,fieldnames=list(row));
            if not ex:w.writeheader()
            w.writerow(row)
        print(f"\n[curriculum] ENTER {snap['name']} {snap['mission_min_m']:.0f}-{snap['mission_max_m']:.0f}m demo={snap['demo_fraction']:.2f} lr={lr:.1e}\n")
    def _init_callback(self):self._apply(0)
    def _on_step(self):
        step=self.eval.latest_step
        if step<0 or step==self.last:return True
        self.last=step
        if self._threshold_ok():self.ready+=1; print(f"[curriculum] stage {self.stage} competence {self.ready}/2")
        else:self.ready=0
        if self.ready>=2:self.ready=0; self._apply(self.stage+1)
        return True


class SaveCallback(BaseCallback):
    def __init__(self,run_dir,every=50000):super().__init__(0); self.run=Path(run_dir); self.every=int(every); self.next=self.every
    def _snapshot(self,step):
        self.model.save(self.run/f"sac_{step}_steps"); folder=self.run/"resume"; folder.mkdir(parents=True,exist_ok=True); self.model.save(folder/"resume_latest")
        try:
            tmp=folder/"replay_buffer_latest.tmp.pkl"; final=folder/"replay_buffer_latest.pkl"; self.model.save_replay_buffer(str(tmp)); os.replace(tmp,final)
            (folder/"resume_state.json").write_text(json.dumps(dict(timesteps=int(self.model.num_timesteps),stage=int(self.training_env.get_attr("v15f_stage")[0])),indent=2),encoding="utf-8")
        except Exception as e: print("[resume WARNING]",repr(e))
    def _on_step(self):
        while self.num_timesteps>=self.next:self._snapshot(self.next); self.next+=self.every
        return True


class CatastrophicGuard(BaseCallback):
    def __init__(self,eval_cb,run_dir,strikes=4):super().__init__(0); self.eval=eval_cb; self.run=Path(run_dir); self.need=strikes; self.n=0; self.last=-1
    def _on_step(self):
        if self.eval.latest_step<200000 or self.eval.latest_step==self.last:return True
        self.last=self.eval.latest_step; c,d=self.eval.clean,self.eval.dr; suc=.5*(c["success"]+d["success"]); route=.5*(c["route_progress"]+d["route_progress"]); stuck=.5*(c["stuck"]+d["stuck"])
        bad=suc==0 and route<.08 and stuck>.92; self.n=self.n+1 if bad else 0
        if bad:print(f"[catastrophic guard] strike {self.n}/{self.need}")
        if self.n>=self.need:
            folder=self.run/"catastrophic_stop"; folder.mkdir(parents=True,exist_ok=True); self.model.save(folder/"model"); self.model.save_replay_buffer(str(folder/"replay.pkl")); return False
        return True


def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--train-dir",default="data/phase2/train"); ap.add_argument("--eval-dir",default="auto"); ap.add_argument("--demo-file",default="data/v15f_teacher_demos.npz")
    ap.add_argument("--steps",type=int,default=1000000); ap.add_argument("--n-envs",type=int,default=2); ap.add_argument("--buffer-size",type=int,default=150000); ap.add_argument("--protected-capacity",type=int,default=30000); ap.add_argument("--demo-fraction",type=float,default=.25)
    ap.add_argument("--batch-size",type=int,default=256); ap.add_argument("--learning-starts",type=int,default=0); ap.add_argument("--bc-epochs",type=int,default=6); ap.add_argument("--eval-every",type=int,default=50000); ap.add_argument("--checkpoint-every",type=int,default=50000); ap.add_argument("--eval-episodes",type=int,default=16); ap.add_argument("--eval-workers",type=int,default=4); ap.add_argument("--seed",type=int,default=71); ap.add_argument("--device",default="cuda"); ap.add_argument("--precision",choices=["bf16","fp32"],default="bf16")
    args=ap.parse_args()
    if args.device=="cuda" and not torch.cuda.is_available():args.device="cpu";args.precision="fp32"
    if args.precision=="bf16" and args.device=="cuda" and not bool(getattr(torch.cuda,"is_bf16_supported",lambda:True)()):args.precision="fp32"
    if torch.cuda.is_available():torch.backends.cuda.matmul.allow_tf32=False;torch.backends.cudnn.allow_tf32=False
    seed_everything(args.seed); demo=Path(args.demo_file)
    if not demo.exists():raise FileNotFoundError(f"Missing {demo}; generate teacher demos first")
    eval_dir=args.eval_dir
    if eval_dir=="auto":
        val=Path("data/phase2/val"); eval_dir=str(val) if val.exists() and list(val.glob("*.npz")) else args.train_dir
    run=Path(f"checkpoints/sac_v15f/seed_{args.seed}"); run.mkdir(parents=True,exist_ok=True); tb=Path(f"logs/sac_v15f/seed_{args.seed}");tb.mkdir(parents=True,exist_ok=True)
    env=SubprocVecEnv([partial(_make_train_env,args.train_dir,args.seed+1000*i) for i in range(args.n_envs)],start_method="spawn")
    policy_kwargs=dict(features_extractor_class=TerrainStateExtractor,features_extractor_kwargs=dict(features_dim=256,precision=args.precision),normalize_images=False,net_arch=[256,256],use_sde=False)
    model=SAC("MultiInputPolicy",env,learning_rate=1e-4,buffer_size=args.buffer_size,learning_starts=args.learning_starts,batch_size=args.batch_size,tau=.005,gamma=.998,train_freq=(1,"step"),gradient_steps=1,ent_coef="auto_0.1",target_entropy=-1.0,policy_kwargs=policy_kwargs,replay_buffer_class=ProtectedDemoDictReplayBuffer,replay_buffer_kwargs=dict(demo_capacity=args.protected_capacity,demo_fraction=args.demo_fraction,protect_near_success=True,near_success_route_progress=.80),tensorboard_log=str(tb),verbose=1,seed=args.seed,device=args.device,optimize_memory_usage=False)
    print("\nV1.5f: teacher + BC + protected replay + mission curriculum + gamma .998")
    loaded=model.replay_buffer.load_teacher_npz(demo); print(f"[protected] loaded {loaded:,} teacher transitions")
    seeded=model.replay_buffer.seed_normal_from_teacher_npz(demo,max_items=5000); print(f"[normal replay] prefilled {seeded:,} successful teacher transitions")
    bc=bc_bootstrap(model,str(demo),args.bc_epochs,args.batch_size,1e-4,args.seed+7); (run/"bc_bootstrap.json").write_text(json.dumps(bc,indent=2),encoding="utf-8"); model.save(run/"bc_bootstrap_model"); print(f"[BC PASS] improvement {bc['relative_improvement']:.1%}")
    ev=EvalCallback(eval_dir,args.eval_every,args.eval_episodes,args.eval_workers,run); cur=MissionCurriculumCallback(ev,run)
    cbs=CallbackList([SaveCallback(run,args.checkpoint_every),ev,cur,CatastrophicGuard(ev,run,4)])
    try:
        model.learn(total_timesteps=args.steps,callback=cbs,progress_bar=True)
        actual=int(model.num_timesteps); model.save(run/f"sac_{actual}_steps_final"); model.save_replay_buffer(str(run/"replay_buffer_final.pkl")); print("\nTraining ended at",actual,"steps"); print("best:",run/"best/best_model.zip"); print("replay stats:",model.replay_buffer.stats())
    finally:env.close()


if __name__=="__main__":main()
