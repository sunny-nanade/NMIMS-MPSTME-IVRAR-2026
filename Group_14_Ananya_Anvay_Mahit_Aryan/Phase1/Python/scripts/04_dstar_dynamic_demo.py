import numpy as np
import matplotlib.pyplot as plt
from mars_rover.config import TerrainConfig
from mars_rover.terrain import load_terrain, build_cost_map, nearest_safe
from mars_rover.planners import DStarLite

cfg = TerrainConfig()
t = load_terrain("data/processed/procedural_mars.npz")
height, obstacle, res = t["height"], t["obstacle"], t["meters_per_cell"]
cost, slope, _ = build_cost_map(height, obstacle, res, cfg.max_slope_deg, cfg.slope_weight, cfg.roughness_weight)
rows, cols = cost.shape
start = nearest_safe(cost, slope, obstacle, (int(rows * .12), int(cols * .12)), max_spawn_slope_deg=12.0)
goal = nearest_safe(cost, slope, obstacle, (int(rows * .86), int(cols * .86)), max_spawn_slope_deg=12.0)

planner = DStarLite(cost, start, goal)
first = planner.plan().path
if len(first) < 12:
    raise RuntimeError("Initial D* Lite path not found/too short.")

changed = []
mid = first[len(first) // 2]
for dr in range(-4, 5):
    for dc in range(-4, 5):
        rr, cc = int(mid[0] + dr), int(mid[1] + dc)
        if 0 <= rr < rows and 0 <= cc < cols and (rr, cc) not in (start, goal):
            changed.append((rr, cc, np.inf))

planner.update_costs(changed)
planner.compute_shortest_path()
second = planner.extract_path()

fig, ax = plt.subplots(figsize=(10, 9))
im = ax.imshow(height, origin="lower", cmap="viridis")
ax.plot(first[:, 1], first[:, 0], color="tab:blue", linewidth=4, alpha=0.65, label="Before new hazard")
if len(second):
    ax.plot(second[:, 1], second[:, 0], color="tab:orange", linestyle="--", linewidth=2.8, label="After D* Lite repair")
ax.scatter([x[1] for x in changed], [x[0] for x in changed], s=14, marker="s", c="crimson", alpha=0.75, label="New hazard")
ax.scatter([start[1]], [start[0]], s=70, c="deepskyblue", edgecolors="white", label="Start", zorder=8)
ax.scatter([goal[1]], [goal[0]], s=110, marker="*", c="orangered", edgecolors="white", label="Goal", zorder=8)
ax.legend()
ax.set_title("D* Lite incremental replanning")
ax.set_xlabel("grid x")
ax.set_ylabel("grid y")
fig.colorbar(im, ax=ax, label="Elevation (m, relative)")
fig.tight_layout()
fig.savefig("outputs/figures/dstar_dynamic.png", dpi=190)
print("Saved outputs/figures/dstar_dynamic.png")
