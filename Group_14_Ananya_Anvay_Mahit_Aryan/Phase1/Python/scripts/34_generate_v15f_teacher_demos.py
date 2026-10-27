from __future__ import annotations

import argparse
from pathlib import Path
import numpy as np

from mars_rover.envs import RoverEnv
from mars_rover.rl.common import terrain_files


def easy_files(folder: str) -> list[str]:
    files = terrain_files(folder)
    easy = [f for f in files if "_easy_" in Path(f).name.lower()]
    return easy or files


def run_teacher_episode(env: RoverEnv, seed: int, keep: bool):
    obs, _ = env.reset(seed=int(seed))
    transitions = []
    while True:
        action = env.v15f_teacher_action()
        nxt, reward, term, trunc, info = env.step(action)
        done = bool(term or trunc)
        if keep:
            transitions.append((
                np.array(obs["terrain"], copy=True),
                np.array(obs["state"], copy=True),
                np.array(action, copy=True),
                np.array(nxt["terrain"], copy=True),
                np.array(nxt["state"], copy=True),
                float(reward), done,
            ))
        obs = nxt
        if done:
            return dict(info.get("episode_metrics", {})), transitions


def qualify(folder: str, seed: int):
    env = RoverEnv(
        easy_files(folder), action_mode="continuous", route_observation=False,
        render_mode=None, randomize_physics=False, sensor_preview=False,
        curriculum_enabled=True, v15f_mode=True,
    )
    out = {}
    try:
        for stage, episodes in ((0, 12), (1, 8)):
            env.set_v15f_stage(stage)
            success, prog = 0, []
            for i in range(episodes):
                m, _ = run_teacher_episode(env, seed + stage * 10000 + i, False)
                success += int(m.get("success", 0))
                prog.append(float(m.get("route_progress", 0.0)))
            out[stage] = {
                "episodes": episodes,
                "success_rate": success / episodes,
                "mean_route_progress": float(np.mean(prog)),
            }
    finally:
        env.close()
    out["passed"] = bool(
        out[0]["success_rate"] >= 0.80
        and out[1]["success_rate"] >= 0.65
        and out[0]["mean_route_progress"] >= 0.85
        and out[1]["mean_route_progress"] >= 0.75
    )
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--output", default="data/v15f_teacher_demos.npz")
    ap.add_argument("--target-transitions", type=int, default=20_000)
    ap.add_argument("--seed", type=int, default=15001)
    ap.add_argument("--max-attempts", type=int, default=500)
    args = ap.parse_args()

    print("V1.5f teacher qualification")
    print("=" * 72)
    q = qualify(args.terrain_dir, args.seed)
    for s in (0, 1):
        x = q[s]
        print(
            f"stage {s}: success={x['success_rate']:.1%} "
            f"route_progress={x['mean_route_progress']:.3f} ({x['episodes']} eps)"
        )
    if not q["passed"]:
        raise RuntimeError(
            "TEACHER QUALIFICATION FAILED. Do not start SAC. "
            "Send this output back so the teacher can be tuned first."
        )
    print("TEACHER QUALIFICATION: PASS\n")

    env = RoverEnv(
        easy_files(args.terrain_dir), action_mode="continuous", route_observation=False,
        render_mode=None, randomize_physics=False, sensor_preview=False,
        curriculum_enabled=True, v15f_mode=True,
    )
    terrain=[]; state=[]; actions=[]; next_terrain=[]; next_state=[]
    rewards=[]; dones=[]; stages=[]; episode_ids=[]
    good_eps=0; attempts=0
    try:
        while len(actions) < args.target_transitions and attempts < args.max_attempts:
            stage = 0 if (attempts % 10) < 7 else 1
            env.set_v15f_stage(stage)
            m, episode = run_teacher_episode(env, args.seed + 50000 + attempts, True)
            attempts += 1
            if not bool(m.get("success", 0)):
                continue
            eid = good_eps; good_eps += 1
            for tr in episode:
                t,s,a,nt,ns,r,d = tr
                terrain.append(t); state.append(s); actions.append(a)
                next_terrain.append(nt); next_state.append(ns)
                rewards.append(r); dones.append(d); stages.append(stage); episode_ids.append(eid)
                if len(actions) >= args.target_transitions:
                    break
            if good_eps % 10 == 0:
                print(f"successful_eps={good_eps:4d} transitions={len(actions):6d}/{args.target_transitions}")
    finally:
        env.close()

    if len(actions) < args.target_transitions:
        raise RuntimeError(
            f"Only collected {len(actions)} successful transitions after {attempts} attempts. "
            "Do not train yet."
        )

    out = Path(args.output); out.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(
        out,
        terrain=np.asarray(terrain,dtype=np.uint8), state=np.asarray(state,dtype=np.float32),
        actions=np.asarray(actions,dtype=np.float32), next_terrain=np.asarray(next_terrain,dtype=np.uint8),
        next_state=np.asarray(next_state,dtype=np.float32), rewards=np.asarray(rewards,dtype=np.float32),
        dones=np.asarray(dones,dtype=np.bool_), stages=np.asarray(stages,dtype=np.int8),
        episode_ids=np.asarray(episode_ids,dtype=np.int32),
    )

    with np.load(out, allow_pickle=False) as d:
        n=len(d["actions"])
        assert d["terrain"].shape==(n,5,32,32)
        assert d["state"].shape==(n,14)
        assert d["actions"].shape==(n,2)
        assert d["next_terrain"].shape==(n,5,32,32)
        assert d["next_state"].shape==(n,14)
        assert np.isfinite(d["state"]).all() and np.isfinite(d["actions"]).all()
        assert np.max(np.abs(d["actions"])) <= 1.00001

    print("\nTEACHER DEMO DATASET: PASS")
    print("saved:", out)
    print("transitions:", f"{len(actions):,}")
    print("successful episodes:", good_eps)
    print("attempts:", attempts)


if __name__ == "__main__":
    main()
