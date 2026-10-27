import argparse
from pathlib import Path
import numpy as np
import pandas as pd
from mars_rover.config import TerrainConfig
from mars_rover.terrain import load_terrain, build_cost_map, nearest_safe
from mars_rover.planners import astar, dstar_lite, hybrid_astar
from mars_rover.metrics import path_metrics
from mars_rover.viz import plot_2d, plot_3d

p = argparse.ArgumentParser()
p.add_argument("--terrain", default="data/processed/procedural_mars.npz")
args = p.parse_args()

cfg = TerrainConfig()
t = load_terrain(args.terrain)
height, obstacle, res = t["height"], t["obstacle"], t["meters_per_cell"]
cost, slope, rough = build_cost_map(height, obstacle, res, cfg.max_slope_deg, cfg.slope_weight, cfg.roughness_weight)
rows, cols = cost.shape
start = nearest_safe(cost, slope, obstacle, (int(rows * .12), int(cols * .12)), max_spawn_slope_deg=12.0)
goal = nearest_safe(cost, slope, obstacle, (int(rows * .86), int(cols * .86)), max_spawn_slope_deg=12.0)

results = {
    "A*": astar(cost, start, goal),
    "D* Lite": dstar_lite(cost, start, goal),
    "Hybrid A*": hybrid_astar(cost, start, goal),
}

records, paths = [], {}
for name, result in results.items():
    paths[name] = result.path
    m = path_metrics(result.path, height, slope, res)
    records.append({
        "model": name,
        "success": int(result.success),
        "planning_time_ms": result.planning_time_ms,
        "nodes_expanded": result.nodes_expanded,
        **m,
    })
    Path("outputs/paths").mkdir(parents=True, exist_ok=True)
    np.save(Path("outputs/paths") / f"{name.lower().replace(' ', '_').replace('*', 'star')}.npy", result.path)

Path("outputs/metrics").mkdir(parents=True, exist_ok=True)
df = pd.DataFrame(records)
df.to_csv("outputs/metrics/classical_metrics.csv", index=False)
print(df.to_string(index=False))


def overlap(a, b):
    if a is None or b is None or not len(a) or not len(b):
        return float("nan")
    sa = set(map(tuple, a.tolist()))
    sb = set(map(tuple, b.tolist()))
    return 100.0 * len(sa & sb) / max(1, len(sa | sb))

print(f"\nA* / D* Lite grid-cell overlap: {overlap(paths['A*'], paths['D* Lite']):.1f}%")
print(f"A* / Hybrid A* grid-cell overlap: {overlap(paths['A*'], paths['Hybrid A*']):.1f}%")
print("High A*/D* overlap on a static map is expected: both optimize the same current cost map.")

plot_2d(height, obstacle, paths, start, goal, "outputs/figures/classical_paths_2d.png")
plot_3d(height, paths, res, "outputs/figures/classical_paths_3d.png")
print("\nSaved metrics and 2D/3D figures under outputs/.")
