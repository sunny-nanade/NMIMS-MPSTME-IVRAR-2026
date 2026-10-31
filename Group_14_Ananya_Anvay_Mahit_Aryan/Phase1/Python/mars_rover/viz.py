from pathlib import Path
import numpy as np
import matplotlib.pyplot as plt

COLORS = {"A*": "tab:blue", "D* Lite": "tab:orange", "Hybrid A*": "tab:green"}
STYLES = {"A*": "-", "D* Lite": "--", "Hybrid A*": "-."}


def _draw_base(ax, height, obstacle, start, goal, title):
    im = ax.imshow(height, origin="lower", cmap="viridis")
    if obstacle is not None:
        mask = np.ma.masked_where(~obstacle, obstacle)
        ax.imshow(mask, origin="lower", cmap="gray_r", alpha=0.55, vmin=0, vmax=1)
    ax.scatter([start[1]], [start[0]], s=65, marker="o", c="deepskyblue", edgecolors="white", linewidths=0.7, zorder=9, label="Start")
    ax.scatter([goal[1]], [goal[0]], s=115, marker="*", c="orangered", edgecolors="white", linewidths=0.6, zorder=9, label="Goal")
    ax.set_title(title)
    ax.set_xlabel("grid x")
    ax.set_ylabel("grid y")
    return im


def plot_2d(height, obstacle, paths, start, goal, out_path=None):
    """Overlay plus separate panels so coincident A*/D* paths are never hidden."""
    fig, axes = plt.subplots(2, 2, figsize=(14, 12), constrained_layout=True)

    ax = axes[0, 0]
    im = _draw_base(ax, height, obstacle, start, goal, "Overlay — all classical planners")

    # Draw A* as a broad halo first so it remains visible even if D* Lite is identical.
    if "A*" in paths and paths["A*"] is not None and len(paths["A*"]):
        pth = paths["A*"]
        ax.plot(pth[:, 1], pth[:, 0], color=COLORS["A*"], linewidth=6.0, alpha=0.50, label="A*", zorder=5)

    for name in ("D* Lite", "Hybrid A*"):
        path = paths.get(name)
        if path is not None and len(path):
            ax.plot(path[:, 1], path[:, 0], color=COLORS[name], linestyle=STYLES[name], linewidth=2.7, label=name, zorder=6)

    ax.legend(loc="upper left")
    ax.text(0.02, 0.02, "If A* and D* Lite choose the same static optimum,\norange dashes sit on top of the blue A* halo.", transform=ax.transAxes,
            fontsize=9, va="bottom", ha="left", bbox=dict(boxstyle="round", fc="white", alpha=0.78, ec="0.6"))

    panels = [("A*", axes[0, 1]), ("D* Lite", axes[1, 0]), ("Hybrid A*", axes[1, 1])]
    for name, panel in panels:
        _draw_base(panel, height, obstacle, start, goal, name)
        path = paths.get(name)
        if path is not None and len(path):
            panel.plot(path[:, 1], path[:, 0], color=COLORS[name], linestyle=STYLES[name], linewidth=3.0, label=name, zorder=6)
        panel.legend(loc="upper left")

    cbar = fig.colorbar(im, ax=axes.ravel().tolist(), shrink=0.90, pad=0.02)
    cbar.set_label("Elevation (m, relative)")
    fig.suptitle("Planetary terrain and planned paths", fontsize=16)

    if out_path:
        Path(out_path).parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(out_path, dpi=190, bbox_inches="tight")
    return fig


def plot_3d(height, paths, meters_per_cell, out_path=None, stride=2):
    rows, cols = height.shape
    x = (np.arange(cols) - (cols - 1) / 2.0) * meters_per_cell
    y = (np.arange(rows) - (rows - 1) / 2.0) * meters_per_cell
    X, Y = np.meshgrid(x, y)

    fig = plt.figure(figsize=(12, 9))
    ax = fig.add_subplot(111, projection="3d")
    surf = ax.plot_surface(
        X[::stride, ::stride], Y[::stride, ::stride], height[::stride, ::stride],
        cmap="terrain", alpha=0.68, linewidth=0, antialiased=True
    )

    for name in ("A*", "D* Lite", "Hybrid A*"):
        path = paths.get(name)
        if path is None or not len(path):
            continue
        px = x[path[:, 1]]
        py = y[path[:, 0]]
        pz = height[path[:, 0], path[:, 1]] + 0.10
        ax.plot(px, py, pz, color=COLORS[name], linestyle=STYLES[name], linewidth=3.2, label=name)

    first_valid = next((p for p in paths.values() if p is not None and len(p)), None)
    if first_valid is not None:
        s, g = first_valid[0], first_valid[-1]
        ax.scatter(x[s[1]], y[s[0]], height[s[0], s[1]] + 0.18, s=55, c="deepskyblue", marker="o")
        ax.scatter(x[g[1]], y[g[0]], height[g[0], g[1]] + 0.18, s=100, c="orangered", marker="*")

    ax.set_xlabel("X (m)")
    ax.set_ylabel("Y (m)")
    ax.set_zlabel("Elevation (m)")
    ax.set_title("3D planetary terrain paths")
    ax.legend(loc="upper right")
    fig.colorbar(surf, ax=ax, shrink=0.65, pad=0.08, label="Elevation (m)")
    fig.tight_layout()

    if out_path:
        Path(out_path).parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(out_path, dpi=190, bbox_inches="tight")
    return fig
