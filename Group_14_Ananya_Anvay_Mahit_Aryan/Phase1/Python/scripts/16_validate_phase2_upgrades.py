import argparse
import math
from pathlib import Path

import numpy as np
import pybullet as p

from mars_rover.envs import RoverEnv


def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def nearest_path_index(path, xy, last_idx=0, window_back=8, window_ahead=45):
    lo = max(0, last_idx - window_back)
    hi = min(len(path), max(last_idx + window_ahead, lo + 1))
    local = path[lo:hi]
    rel = int(np.argmin(np.linalg.norm(local - xy, axis=1)))
    return lo + rel


def lookahead_index(path, nearest, lookahead_m):
    idx = nearest
    distance = 0.0
    while idx < len(path) - 1 and distance < lookahead_m:
        distance += float(np.linalg.norm(path[idx + 1] - path[idx]))
        idx += 1
    return idx


def wheel_contacts(env):
    count = 0
    for j in env.drive_wheels:
        if p.getContactPoints(bodyA=env.rover_id, linkIndexA=j, physicsClientId=env.cid):
            count += 1
    return count


def main():
    ap = argparse.ArgumentParser(description="Validate six-wheel rover physics and A* tracking before RL training.")
    ap.add_argument("--terrain-dir", default="data/phase2/train")
    ap.add_argument("--difficulty", choices=["easy", "medium", "hard"], default="medium")
    ap.add_argument("--steps", type=int, default=650)
    ap.add_argument("--gui", action="store_true")
    ap.add_argument("--camera", choices=["free", "chase", "top", "overview"], default="chase")
    ap.add_argument("--lookahead", type=float, default=3.2)
    args = ap.parse_args()

    files = sorted(str(x) for x in Path(args.terrain_dir).glob(f"*_{args.difficulty}_*.npz"))
    if not files:
        raise FileNotFoundError(f"No {args.difficulty} terrain files found in {args.terrain_dir}")

    env = RoverEnv(
        files,
        action_mode="continuous",
        route_observation=True,
        render_mode="human" if args.gui else None,
        randomize_physics=False,
        max_episode_steps=max(args.steps + 20, 700),
    )
    obs, info = env.reset(seed=321)
    env.camera_mode = args.camera
    pos0, _, _, yaw0, _, _ = env._rover_pose()
    if args.gui:
        env._update_debug_camera(pos0, yaw0)

    print("Rover:", info.get("rover_model"))
    print("Difficulty:", info.get("difficulty"), "terrain:", info.get("terrain"))
    print("Reference A* path length:", round(info.get("reference_path_length_m", 0), 2), "m")
    print("Planner rover footprint radius:", round(info.get("planner_footprint_radius_m", 0), 2), "m")
    print("Planner hard obstacle clearance:", round(info.get("planner_hard_clearance_m", 0), 2), "m")
    print("Reference minimum physical clearance:", round(info.get("reference_min_clearance_m", float("inf")), 2), "m")
    print("Reference path = CYAN; actual rover trace = YELLOW")
    print("Camera keys: 1=FREE, 2=FOLLOW, 3=TOP-DOWN, 4=OVERVIEW, F=FREE/FOLLOW, T=TOP/FOLLOW")

    done = False
    nearest = 0
    cte_values = []
    contact_values = []
    clearance_values = []
    goal_values = []
    start_goal = None

    for step in range(args.steps):
        pos, _, _, yaw, lin, _ = env._rover_pose()
        xy = pos[:2]
        path = env.path_xy

        nearest = nearest_path_index(path, xy, nearest)
        target_idx = lookahead_index(path, nearest, args.lookahead)
        target = path[target_idx]

        # Pure-pursuit controller. Positive heading error means target is to rover-left;
        # positive steer rotates the front wheels left and rear wheels right.
        dx, dy = float(target[0] - xy[0]), float(target[1] - xy[1])
        alpha = wrap(math.atan2(dy, dx) - yaw)
        lookahead_actual = max(1.2, math.hypot(dx, dy))
        virtual_wheelbase = 1.85
        desired_delta = math.atan2(2.0 * virtual_wheelbase * math.sin(alpha), lookahead_actual)
        steer = float(np.clip(desired_delta / max(env.max_steer_rad, 1e-6), -1.0, 1.0))

        # Slow for large course corrections; do not reverse simply because a waypoint is behind.
        turn_factor = max(0.24, math.cos(min(abs(alpha), math.pi / 2.0)))
        throttle = 0.74 * turn_factor
        if abs(alpha) > math.radians(72):
            throttle = 0.20
        if np.linalg.norm(env.goal_xy - xy) < 4.0:
            throttle = min(throttle, 0.45)

        obs, reward, terminated, truncated, info = env.step(
            np.array([steer, throttle], dtype=np.float32)
        )
        done = terminated or truncated

        # Tracking diagnostics are measured after the physics/control step.
        post_pos, _, _, _, post_lin, _ = env._rover_pose()
        post_xy = post_pos[:2]
        cte = float(np.min(np.linalg.norm(path - post_xy, axis=1)))
        cte_values.append(cte)
        contacts = int(info.get("wheel_contacts", wheel_contacts(env)))
        contact_values.append(contacts)
        clearance = float(info.get("clearance_m", env._clearance_to_obstacles(post_xy)))
        if np.isfinite(clearance):
            clearance_values.append(clearance)
        goal_dist = float(info.get("goal_distance_m", np.nan))
        goal_values.append(goal_dist)
        if start_goal is None:
            start_goal = goal_dist

        if step % 25 == 0:
            speed = float(np.linalg.norm(post_lin[:2]))
            print(
                f"step={step:4d} goal={goal_dist:6.1f} m CTE={cte:4.2f} m "
                f"contacts={contacts}/6 clearance={clearance:4.2f} m "
                f"steer={steer:+.2f} throttle={throttle:.2f} "
                f"speed={speed:.2f} m/s slip={info.get('slip', 0):.2f}"
            )
        if done:
            print("Episode ended:", info.get("episode_metrics"))
            if info.get("terminal_reason") == "collision":
                print("Collision diagnostics:", info.get("collision_details"))
                print(f"Collision CTE={cte:.3f} m, map clearance={clearance:.3f} m")
            break

    mean_cte = float(np.mean(cte_values)) if cte_values else float("nan")
    max_cte = float(np.max(cte_values)) if cte_values else float("nan")
    within_1m = float(np.mean(np.asarray(cte_values) <= 1.0) * 100.0) if cte_values else float("nan")
    mean_contacts = float(np.mean(contact_values)) if contact_values else float("nan")
    min_contacts = int(np.min(contact_values)) if contact_values else -1
    low_contact_pct = float(np.mean(np.asarray(contact_values) < 5) * 100.0) if contact_values else float("nan")
    min_clearance = float(np.min(clearance_values)) if clearance_values else float("inf")
    final_goal = goal_values[-1] if goal_values else float("nan")
    goal_reduction = (start_goal - final_goal) if start_goal is not None else float("nan")

    print("\n--- Six-wheel validation summary ---")
    print(f"Mean cross-track error : {mean_cte:.3f} m")
    print(f"Max cross-track error  : {max_cte:.3f} m")
    print(f"Trajectory within 1 m  : {within_1m:.1f}%")
    print(f"Mean wheel contacts    : {mean_contacts:.2f}/6")
    print(f"Minimum wheel contacts : {min_contacts}/6")
    print(f"Steps with <5 contacts : {low_contact_pct:.1f}%")
    print(f"Minimum rover clearance: {min_clearance:.3f} m")
    print(f"Reference min clearance: {info.get('reference_min_clearance_m', env.reference_min_clearance_m):.3f} m")
    print(f"Goal-distance reduction: {goal_reduction:.2f} m")
    print(f"Final goal distance    : {final_goal:.2f} m")
    if not done:
        print("Validation stopped at requested step limit; rover remained simulated successfully.")
    env.close()


if __name__ == "__main__":
    main()
