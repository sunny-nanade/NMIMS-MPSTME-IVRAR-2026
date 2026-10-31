from __future__ import annotations

import argparse, csv
from functools import partial
from pathlib import Path
import numpy as np
from stable_baselines3 import SAC
from stable_baselines3.common.vec_env import SubprocVecEnv
from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files

class E(RoverEnv):
    def __init__(self,*args,seeds,**kwargs):
        self._seeds=list(map(int,seeds)); self._i=0; self._s=self._seeds[0]; super().__init__(*args,**kwargs); self.set_v15f_stage(3)
    def reset(self,*,seed=None,options=None):
        seed=self._seeds[self._i] if self._i<len(self._seeds) else self._seeds[-1]; self._i+=1; self._s=int(seed); self.set_v15f_stage(3)
        o,i=super().reset(seed=self._s,options=options); i=dict(i); i["seed"]=self._s; return o,i
    def step(self,a):
        o,r,t,tr,i=super().step(a); i=dict(i); i["seed"]=self._s; return o,r,t,tr,i

def make(folder,seeds,dr):
    return E(terrain_files(folder),action_mode="continuous",route_observation=False,render_mode=None,randomize_physics=dr,sensor_preview=False,curriculum_enabled=True,v15f_mode=True,dr_force_episode=True if dr else False,dr_fixed_strength=.55 if dr else 0.0,seeds=seeds)

def run(model,folder,dr,n,seed,workers):
    workers=max(1,min(workers,n))
    while n%workers:workers-=1
    seeds=list(range(seed,seed+n)); schedules=[seeds[i::workers] for i in range(workers)]
    env=SubprocVecEnv([partial(make,folder,s,dr) for s in schedules],start_method="spawn"); done={}
    try:
        obs=env.reset()
        while len(done)<n:
            a,_=model.predict(obs,deterministic=True); obs,_,ds,infos=env.step(a)
            for d,i in zip(ds,infos):
                if d and i.get("episode_metrics") is not None:
                    s=int(i["seed"])
                    if s in seeds and s not in done:done[s]=dict(i["episode_metrics"])
        return [done[s] for s in sorted(done)]
    finally:env.close()

def agg(rs):
    reasons=[str(r.get("terminal_reason","")) for r in rs]
    return dict(episodes=len(rs),success=float(np.mean([r.get("success",0) for r in rs])),route_progress=float(np.mean([r.get("route_progress",0) for r in rs])),goal_progress=float(np.mean([r.get("goal_progress",0) for r in rs])),collision=float(np.mean([r.get("collision",0) for r in rs])),stuck=float(np.mean([x=="stuck" for x in reasons])),throttle=float(np.mean([r.get("mean_signed_throttle",0) for r in rs])),reward=float(np.mean([r.get("reward",0) for r in rs])))

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--seed",type=int,default=71); ap.add_argument("--terrain-dir",default="data/phase2/test"); ap.add_argument("--episodes",type=int,default=100); ap.add_argument("--workers",type=int,default=4); ap.add_argument("--device",default="cuda"); args=ap.parse_args()
    root=Path(f"checkpoints/sac_v15f/seed_{args.seed}"); c=[("best",root/"best"/"best_model.zip"),("500k",root/"sac_500000_steps.zip"),("750k",root/"sac_750000_steps.zip"),("1m",root/"sac_1000000_steps.zip")]; c=[x for x in c if x[1].exists()]
    if not c:raise FileNotFoundError(root)
    rows=[]
    for name,path in c:
        model=SAC.load(path,device=args.device)
        for dr in (False,True):
            a=agg(run(model,args.terrain_dir,dr,args.episodes,120000,args.workers)); row={"model":name,"regime":"dr" if dr else "clean",**a}; rows.append(row); print(row)
    out=Path(f"results/sac_v15f_seed_{args.seed}"); out.mkdir(parents=True,exist_ok=True)
    with (out/"benchmark_summary.csv").open("w",newline="",encoding="utf-8") as f:
        w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    print("saved",out/"benchmark_summary.csv")
if __name__=="__main__":main()
