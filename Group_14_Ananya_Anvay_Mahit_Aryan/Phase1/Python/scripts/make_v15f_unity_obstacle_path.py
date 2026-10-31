from __future__ import annotations

import argparse
import csv
import json
import math
from pathlib import Path

import numpy as np
from scipy.ndimage import distance_transform_edt

from mars_rover.config import TerrainConfig
from mars_rover.terrain import build_cost_map
from mars_rover.planners import astar


TC = TerrainConfig()

# Match V1.5f RoverEnv geometry.
ROVER_FOOTPRINT_RADIUS_M = 1.95
PLANNER_SAFETY_MARGIN_M = 0.35
DEFAULT_HARD_CLEARANCE_M = ROVER_FOOTPRINT_RADIUS_M + PLANNER_SAFETY_MARGIN_M  # 2.30 m
CLEARANCE_BAND_M = 1.75
CLEARANCE_WEIGHT = 3.5
BOUNDARY_EXTRA_MARGIN_M = 1.25
DEFAULT_BOUNDARY_MARGIN_M = ROVER_FOOTPRINT_RADIUS_M + BOUNDARY_EXTRA_MARGIN_M  # 3.20 m


def clamp_index(v: int, n: int) -> int:
    return max(0, min(n - 1, int(v)))


def world_to_rc(
    x: float,
    z: float,
    *,
    origin_x: float,
    origin_z: float,
    size_x: float,
    size_z: float,
    h: int,
    w: int,
) -> tuple[int, int]:
    col_f = (x - origin_x) / max(size_x, 1e-9) * (w - 1)
    row_f = (z - origin_z) / max(size_z, 1e-9) * (h - 1)
    return clamp_index(round(row_f), h), clamp_index(round(col_f), w)


def rc_to_world(
    row: int,
    col: int,
    *,
    origin_x: float,
    origin_z: float,
    size_x: float,
    size_z: float,
    h: int,
    w: int,
) -> tuple[float, float]:
    x = origin_x + (float(col) / max(1, w - 1)) * size_x
    z = origin_z + (float(row) / max(1, h - 1)) * size_z
    return x, z


def mark_aabb(
    mask: np.ndarray,
    *,
    min_x: float,
    max_x: float,
    min_z: float,
    max_z: float,
    origin_x: float,
    origin_z: float,
    size_x: float,
    size_z: float,
) -> None:
    h, w = mask.shape

    c0f = (min_x - origin_x) / max(size_x, 1e-9) * (w - 1)
    c1f = (max_x - origin_x) / max(size_x, 1e-9) * (w - 1)
    r0f = (min_z - origin_z) / max(size_z, 1e-9) * (h - 1)
    r1f = (max_z - origin_z) / max(size_z, 1e-9) * (h - 1)

    c0 = clamp_index(math.floor(min(c0f, c1f)), w)
    c1 = clamp_index(math.ceil(max(c0f, c1f)), w)
    r0 = clamp_index(math.floor(min(r0f, r1f)), h)
    r1 = clamp_index(math.ceil(max(r0f, r1f)), h)

    if c1 >= c0 and r1 >= r0:
        mask[r0 : r1 + 1, c0 : c1 + 1] = True


def nearest_finite(cost: np.ndarray, rc: tuple[int, int]) -> tuple[int, int]:
    r, c = rc
    if np.isfinite(cost[r, c]):
        return r, c

    finite = np.argwhere(np.isfinite(cost))
    if len(finite) == 0:
        raise RuntimeError("No finite/navigation-safe cells exist.")

    dr = finite[:, 0].astype(np.float64) - float(r)
    dc = finite[:, 1].astype(np.float64) - float(c)
    j = int(np.argmin(dr * dr + dc * dc))
    return int(finite[j, 0]), int(finite[j, 1])


def path_length_m(path_rc: np.ndarray, dx: float, dz: float) -> float:
    if len(path_rc) < 2:
        return 0.0
    d = np.diff(path_rc.astype(np.float64), axis=0)
    return float(np.sum(np.hypot(d[:, 0] * dz, d[:, 1] * dx)))


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Generate a V1.5f-compatible Unity A* path that includes Unity rock colliders."
    )
    ap.add_argument("--height-npy", required=True)
    ap.add_argument("--meta", required=True)
    ap.add_argument("--scene-json", required=True)
    ap.add_argument("--output", default="global_path.csv")
    ap.add_argument("--hard-clearance-m", type=float, default=DEFAULT_HARD_CLEARANCE_M)
    ap.add_argument("--boundary-margin-m", type=float, default=DEFAULT_BOUNDARY_MARGIN_M)
    ap.add_argument("--stride", type=int, default=1)
    args = ap.parse_args()

    height = np.load(args.height_npy).astype(np.float32)
    if height.ndim != 2:
        raise RuntimeError(f"height map must be 2-D; got {height.shape}")

    meta = json.loads(Path(args.meta).read_text(encoding="utf-8"))
    scene = json.loads(Path(args.scene_json).read_text(encoding="utf-8"))

    h, w = height.shape

    # Prefer the exact Unity Terrain transform/size from the scene export.
    origin_x = float(scene.get("terrain_origin_x", 0.0))
    origin_z = float(scene.get("terrain_origin_z", 0.0))
    size_x = float(scene.get("terrain_size_x", meta["unity_size_x_m"]))
    size_z = float(scene.get("terrain_size_z", meta["unity_size_z_m"]))

    dx = size_x / max(1, w - 1)
    dz = size_z / max(1, h - 1)

    # RoverEnv uses one scalar terrain resolution; this terrain is square/almost square.
    res = 0.5 * (dx + dz)

    raw_obstacle = np.zeros((h, w), dtype=bool)

    obstacles = scene.get("obstacles", [])
    for ob in obstacles:
        mark_aabb(
            raw_obstacle,
            min_x=float(ob["min_x"]),
            max_x=float(ob["max_x"]),
            min_z=float(ob["min_z"]),
            max_z=float(ob["max_z"]),
            origin_x=origin_x,
            origin_z=origin_z,
            size_x=size_x,
            size_z=size_z,
        )

    if raw_obstacle.any():
        # Physical-distance transform, then exact V1.5f-style configuration-space inflation.
        raw_distance_m = distance_transform_edt(
            ~raw_obstacle,
            sampling=(dz, dx),
        ).astype(np.float32)

        planning_obstacle = (
            raw_obstacle |
            (raw_distance_m <= float(args.hard_clearance_m))
        )
    else:
        raw_distance_m = np.full((h, w), np.inf, dtype=np.float32)
        planning_obstacle = raw_obstacle.copy()

    cost, slope, rough = build_cost_map(
        height,
        planning_obstacle,
        res,
        TC.max_slope_deg,
        TC.slope_weight,
        TC.roughness_weight,
    )

    # Match V1.5f boundary protection.
    boundary_cells_r = max(1, int(math.ceil(float(args.boundary_margin_m) / max(dz, 1e-6))))
    boundary_cells_c = max(1, int(math.ceil(float(args.boundary_margin_m) / max(dx, 1e-6))))

    cost[:boundary_cells_r, :] = np.inf
    cost[-boundary_cells_r:, :] = np.inf
    cost[:, :boundary_cells_c] = np.inf
    cost[:, -boundary_cells_c:] = np.inf

    # Match V1.5f's soft preference for extra clearance beyond the 2.30 m hard exclusion.
    if raw_obstacle.any():
        clearance_beyond_hard = raw_distance_m - float(args.hard_clearance_m)
        q = np.clip(1.0 - clearance_beyond_hard / CLEARANCE_BAND_M, 0.0, 1.0)
        clearance_penalty = CLEARANCE_WEIGHT * q * q
        finite = np.isfinite(cost)
        cost[finite] += clearance_penalty[finite]

    start_rc_raw = world_to_rc(
        float(scene["start_x"]),
        float(scene["start_z"]),
        origin_x=origin_x,
        origin_z=origin_z,
        size_x=size_x,
        size_z=size_z,
        h=h,
        w=w,
    )
    goal_rc_raw = world_to_rc(
        float(scene["goal_x"]),
        float(scene["goal_z"]),
        origin_x=origin_x,
        origin_z=origin_z,
        size_x=size_x,
        size_z=size_z,
        h=h,
        w=w,
    )

    start_rc = nearest_finite(cost, start_rc_raw)
    goal_rc = nearest_finite(cost, goal_rc_raw)

    if start_rc != start_rc_raw:
        print(f"[path] start moved from blocked {start_rc_raw} -> nearest safe {start_rc}")
    if goal_rc != goal_rc_raw:
        print(f"[path] goal moved from blocked {goal_rc_raw} -> nearest safe {goal_rc}")

    plan = astar(cost, start_rc, goal_rc)
    if not plan.success:
        raise RuntimeError(
            "Obstacle-aware A* could not reach the goal. "
            "Check rock placement/start/goal or reduce --hard-clearance-m slightly."
        )

    path_rc = np.asarray(plan.path, dtype=np.int32)
    stride = max(1, int(args.stride))
    selected = path_rc[::stride]
    if not np.array_equal(selected[-1], path_rc[-1]):
        selected = np.vstack([selected, path_rc[-1]])

    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)

    with out.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["x", "z"])
        for row, col in selected:
            x, z = rc_to_world(
                int(row),
                int(col),
                origin_x=origin_x,
                origin_z=origin_z,
                size_x=size_x,
                size_z=size_z,
                h=h,
                w=w,
            )
            writer.writerow([f"{x:.4f}", f"{z:.4f}"])

    # Clearance of centerline against raw Unity obstacle AABBs.
    path_clear = raw_distance_m[path_rc[:, 0], path_rc[:, 1]]
    min_center_to_raw = (
        float(np.min(path_clear))
        if np.isfinite(path_clear).any()
        else float("inf")
    )

    length_m = path_length_m(path_rc, dx, dz)

    print("\nV1.5f UNITY OBSTACLE-AWARE PATH: PASS")
    print(f"  exported Unity obstacle colliders : {len(obstacles)}")
    print(f"  raw obstacle cells                : {int(raw_obstacle.sum()):,}")
    print(f"  hard clearance                    : {float(args.hard_clearance_m):.2f} m")
    print(f"  boundary margin                   : {float(args.boundary_margin_m):.2f} m")
    print(f"  path cells                        : {len(path_rc):,}")
    print(f"  path length                       : {length_m:.1f} m")
    print(f"  min center->raw obstacle distance : {min_center_to_raw:.2f} m")
    print(f"  wrote                             : {out.resolve()}")

    if raw_obstacle.any() and min_center_to_raw < float(args.hard_clearance_m) - max(dx, dz):
        raise RuntimeError(
            "Generated path failed the hard-clearance check; do not use this CSV."
        )


if __name__ == "__main__":
    main()
