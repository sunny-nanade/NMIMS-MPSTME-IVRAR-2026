from __future__ import annotations
from pathlib import Path
import numpy as np
from scipy.ndimage import gaussian_filter, zoom, uniform_filter, label


def _crater(height: np.ndarray, cx: float, cy: float, radius: float, depth: float) -> None:
    """Add a smooth bowl + raised rim crater to a heightfield."""
    rows, cols = height.shape
    yy, xx = np.mgrid[0:rows, 0:cols]
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    q = r / max(radius, 1e-6)
    inside = q < 1.0
    bowl = np.zeros_like(height, dtype=np.float64)
    bowl[inside] = -depth * (1.0 - q[inside] ** 2) ** 1.7
    rim = 0.32 * depth * np.exp(-0.5 * ((q - 1.0) / 0.10) ** 2)
    ejecta = np.where(q >= 1.0, 0.06 * depth * np.exp(-(q - 1.0) / 0.55), 0.0)
    height += bowl + rim + ejecta


def generate_procedural(
    size=129,
    meters_per_cell=1.0,
    seed=7,
    crater_count=7,
    boulder_count=35,
    difficulty: str | None = None,
):
    """Generate repeatable Mars-like DEMs.

    difficulty=None preserves the validated Phase-1 generator.  When difficulty is
    easy/medium/hard, relief, crater morphology and obstacle density are adjusted
    for the Phase-2 curriculum while keeping terrain potentially traversable.
    """
    rng = np.random.default_rng(seed)
    difficulty = None if difficulty is None else difficulty.lower()
    if difficulty not in (None, "easy", "medium", "hard"):
        raise ValueError("difficulty must be None, 'easy', 'medium' or 'hard'")

    if difficulty == "easy":
        broad_amp, medium_amp, final_scale = 0.72, 0.12, 0.60
        crater_count = max(3, int(round(crater_count * 0.60)))
        boulder_count = max(12, int(round(boulder_count * 0.55)))
        crater_depth = (0.55, 1.25)
        crater_radius = (size * 0.040, size * 0.070)
        smooth_sigma = 0.75
        boulder_radius = (0.8, 1.9)
    elif difficulty == "hard":
        broad_amp, medium_amp, final_scale = 1.55, 0.38, 0.90
        crater_count = max(8, int(round(crater_count * 1.35)))
        boulder_count = max(38, int(round(boulder_count * 1.35)))
        crater_depth = (1.20, 2.80)
        crater_radius = (size * 0.035, size * 0.095)
        smooth_sigma = 0.42
        boulder_radius = (1.0, 3.0)
    elif difficulty == "medium":
        broad_amp, medium_amp, final_scale = 1.15, 0.22, 0.72
        crater_depth = (0.9, 2.2)
        crater_radius = (size * 0.035, size * 0.085)
        smooth_sigma = 0.55
        boulder_radius = (1.0, 2.7)
    else:
        broad_amp, medium_amp, final_scale = 1.15, 0.22, 0.72
        crater_depth = (0.9, 2.2)
        crater_radius = (size * 0.035, size * 0.085)
        smooth_sigma = 0.55
        boulder_radius = (1.0, 2.7)

    noise1 = rng.normal(0, 1, (size, size))
    noise2 = rng.normal(0, 1, (size, size))
    broad = gaussian_filter(noise1, sigma=size / 15)
    medium = gaussian_filter(noise2, sigma=max(1.5, size / 38))
    broad /= max(np.std(broad), 1e-8)
    medium /= max(np.std(medium), 1e-8)
    height = broad_amp * broad + medium_amp * medium

    # Hard terrains gain broad ridges/saddles without introducing vertical cliffs.
    if difficulty == "hard":
        yy, xx = np.mgrid[0:size, 0:size]
        phase = rng.uniform(0, 2 * np.pi)
        ridge = np.sin((xx + 0.45 * yy) * (2 * np.pi / max(size * 0.70, 1)) + phase)
        ridge = gaussian_filter(ridge, sigma=max(1.0, size / 70))
        height += 0.35 * ridge

    for _ in range(crater_count):
        radius = rng.uniform(*crater_radius)
        cx = rng.uniform(radius + 5, size - radius - 5)
        cy = rng.uniform(radius + 5, size - radius - 5)
        depth = rng.uniform(*crater_depth)
        _crater(height, cx, cy, radius, depth)

    height = gaussian_filter(height, sigma=smooth_sigma)
    height -= height.min()
    height *= final_scale

    obstacle = np.zeros((size, size), dtype=bool)
    yy, xx = np.mgrid[0:size, 0:size]
    for _ in range(boulder_count):
        radius = rng.uniform(*boulder_radius)
        cx = rng.uniform(8, size - 8)
        cy = rng.uniform(8, size - 8)
        obstacle |= ((xx - cx) ** 2 + (yy - cy) ** 2) <= radius**2

    return {
        "height": height.astype(np.float32),
        "obstacle": obstacle,
        "meters_per_cell": float(meters_per_cell),
    }


def save_terrain(path, terrain):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = dict(
        height=np.asarray(terrain["height"], np.float32),
        obstacle=np.asarray(terrain["obstacle"], np.uint8),
        meters_per_cell=np.asarray(float(terrain["meters_per_cell"]), np.float32),
    )
    if "difficulty" in terrain:
        payload["difficulty"] = np.asarray(str(terrain["difficulty"]))
    np.savez_compressed(path, **payload)


def load_terrain(path):
    d = np.load(path)
    out = {
        "height": d["height"].astype(np.float32),
        "obstacle": d["obstacle"].astype(bool),
        "meters_per_cell": float(d["meters_per_cell"]),
    }
    if "difficulty" in d.files:
        out["difficulty"] = str(d["difficulty"].item())
    return out


def load_geotiff_crop(tif_path, out_size=257, crop_size_px=None, center=None):
    import rasterio

    with rasterio.open(tif_path) as src:
        arr = src.read(1).astype(np.float32)
        nodata = src.nodata
        if nodata is not None:
            arr[arr == nodata] = np.nan
        transform = src.transform
        mpp = float((abs(transform.a) + abs(transform.e)) / 2.0)

    valid = np.isfinite(arr)
    if not valid.any():
        raise ValueError("GeoTIFF contains no finite elevation samples.")
    arr = np.where(valid, arr, float(np.nanmedian(arr)))
    h, w = arr.shape
    if crop_size_px is None:
        crop_size_px = min(h, w)
    crop_size_px = int(min(crop_size_px, h, w))
    cy, cx = (h // 2, w // 2) if center is None else center
    y0 = int(np.clip(cy - crop_size_px // 2, 0, h - crop_size_px))
    x0 = int(np.clip(cx - crop_size_px // 2, 0, w - crop_size_px))
    crop = arr[y0:y0 + crop_size_px, x0:x0 + crop_size_px]
    scale = out_size / crop.shape[0]
    resized = zoom(crop, zoom=scale, order=1)[:out_size, :out_size]
    resized -= resized.min()
    return {
        "height": resized.astype(np.float32),
        "obstacle": np.zeros_like(resized, dtype=bool),
        "meters_per_cell": float(mpp / scale),
    }


def slope_map(height, meters_per_cell):
    gy, gx = np.gradient(height, meters_per_cell, meters_per_cell)
    return np.degrees(np.arctan(np.sqrt(gx * gx + gy * gy))).astype(np.float32)


def roughness_map(height, window=5):
    mean = uniform_filter(height, size=window, mode="nearest")
    mean2 = uniform_filter(height * height, size=window, mode="nearest")
    return np.sqrt(np.maximum(0.0, mean2 - mean * mean)).astype(np.float32)


def terrain_difficulty_metrics(height, meters_per_cell, obstacle=None):
    """Return physically interpretable terrain difficulty metrics and a ranking score."""
    height = np.asarray(height, dtype=np.float32)
    slope = slope_map(height, meters_per_cell)
    rough = roughness_map(height)
    relief = float(np.percentile(height, 98) - np.percentile(height, 2))
    p90_slope = float(np.percentile(slope, 90))
    p95_slope = float(np.percentile(slope, 95))
    p90_rough = float(np.percentile(rough, 90))
    obstacle_fraction = float(np.mean(obstacle)) if obstacle is not None else 0.0
    # Ranking score only: relative quantiles are used to label HiRISE windows.
    score = (
        0.42 * p90_slope
        + 0.18 * p95_slope
        + 1.25 * relief
        + 3.0 * p90_rough
        + 30.0 * obstacle_fraction
    )
    return {
        "difficulty_score": float(score),
        "relief_m": relief,
        "slope_p90_deg": p90_slope,
        "slope_p95_deg": p95_slope,
        "roughness_p90_m": p90_rough,
        "obstacle_fraction": obstacle_fraction,
    }


def build_cost_map(height, obstacle, meters_per_cell, max_slope_deg=30.0, slope_weight=0.08, roughness_weight=2.0):
    slope = slope_map(height, meters_per_cell)
    rough = roughness_map(height)
    p95 = float(np.percentile(rough, 95))
    rough_norm = rough / max(p95, 1e-6)
    cost = 1.0 + slope_weight * slope + roughness_weight * np.clip(rough_norm, 0, 3)
    cost = cost.astype(np.float64)
    cost[obstacle | (slope > max_slope_deg)] = np.inf
    return cost, slope, rough


def nearest_free(cost, rc):
    r0, c0 = rc
    if np.isfinite(cost[r0, c0]):
        return r0, c0
    rows, cols = cost.shape
    for radius in range(1, max(rows, cols)):
        cand = []
        rmin, rmax = max(0, r0 - radius), min(rows, r0 + radius + 1)
        cmin, cmax = max(0, c0 - radius), min(cols, c0 + radius + 1)
        for r in range(rmin, rmax):
            for c in (cmin, cmax - 1):
                if 0 <= c < cols and np.isfinite(cost[r, c]):
                    cand.append((r, c))
        for c in range(cmin, cmax):
            for r in (rmin, rmax - 1):
                if 0 <= r < rows and np.isfinite(cost[r, c]):
                    cand.append((r, c))
        if cand:
            return min(cand, key=lambda q: (q[0] - r0) ** 2 + (q[1] - c0) ** 2)
    raise RuntimeError("No traversable cell exists.")


def nearest_safe(cost, slope, obstacle, rc, max_spawn_slope_deg=10.0, clearance_cells=2):
    rows, cols = cost.shape
    r0, c0 = rc

    def ok(r, c):
        if not (0 <= r < rows and 0 <= c < cols):
            return False
        if not np.isfinite(cost[r, c]) or slope[r, c] > max_spawn_slope_deg:
            return False
        r1, r2 = max(0, r - clearance_cells), min(rows, r + clearance_cells + 1)
        c1, c2 = max(0, c - clearance_cells), min(cols, c + clearance_cells + 1)
        return not obstacle[r1:r2, c1:c2].any()

    if ok(r0, c0):
        return r0, c0
    for radius in range(1, max(rows, cols)):
        candidates = []
        rmin, rmax = max(0, r0 - radius), min(rows, r0 + radius + 1)
        cmin, cmax = max(0, c0 - radius), min(cols, c0 + radius + 1)
        for r in range(rmin, rmax):
            for c in range(cmin, cmax):
                if max(abs(r-r0), abs(c-c0)) == radius and ok(r, c):
                    candidates.append((r, c))
        if candidates:
            return min(candidates, key=lambda q: (q[0]-r0)**2 + (q[1]-c0)**2)
    return nearest_free(cost, rc)


def grid_to_world(path, shape, meters_per_cell):
    rows, cols = shape
    path = np.asarray(path)
    r = path[:, 0].astype(float)
    c = path[:, 1].astype(float)
    x = (c - (cols - 1) / 2.0) * meters_per_cell
    y = (r - (rows - 1) / 2.0) * meters_per_cell
    return np.column_stack([x, y])


def world_to_grid(x, y, shape, meters_per_cell):
    rows, cols = shape
    c = x / meters_per_cell + (cols - 1) / 2.0
    r = y / meters_per_cell + (rows - 1) / 2.0
    return r, c


def sample_height_world(height, meters_per_cell, x, y):
    rows, cols = height.shape
    r, c = world_to_grid(x, y, height.shape, meters_per_cell)
    r = float(np.clip(r, 0, rows - 1))
    c = float(np.clip(c, 0, cols - 1))
    r0, c0 = int(np.floor(r)), int(np.floor(c))
    r1, c1 = min(r0 + 1, rows - 1), min(c0 + 1, cols - 1)
    fr, fc = r - r0, c - c0
    z0 = (1 - fc) * height[r0, c0] + fc * height[r0, c1]
    z1 = (1 - fc) * height[r1, c0] + fc * height[r1, c1]
    return float((1 - fr) * z0 + fr * z1)


def obstacle_components(obstacle, meters_per_cell, min_cells=2):
    labels, n = label(obstacle.astype(np.uint8))
    rocks = []
    for i in range(1, n + 1):
        pts = np.argwhere(labels == i)
        if len(pts) < min_cells:
            continue
        r, c = pts.mean(axis=0)
        equiv_radius_cells = np.sqrt(len(pts) / np.pi)
        rocks.append({
            "row": float(r),
            "col": float(c),
            "radius_m": float(equiv_radius_cells * meters_per_cell),
            "cells": int(len(pts)),
        })
    return rocks


def _read_geotiff_window_array(src, cx, cy, crop_size_px):
    from rasterio.windows import Window
    crop_size_px = int(crop_size_px)
    x0 = int(np.clip(cx - crop_size_px // 2, 0, max(0, src.width - crop_size_px)))
    y0 = int(np.clip(cy - crop_size_px // 2, 0, max(0, src.height - crop_size_px)))
    window = Window(x0, y0, crop_size_px, crop_size_px)
    arr = src.read(1, window=window).astype(np.float32)
    nodata = src.nodata
    if nodata is not None:
        arr[arr == nodata] = np.nan
    return arr, x0, y0


def scan_geotiff_difficulty_windows(tif_path, crop_size_px=129, candidate_count=240, seed=777):
    """Scan a reproducible set of HiRISE windows and rank them by terrain difficulty.

    Labels are relative to the supplied DTM: lowest third easy, middle third medium,
    highest third hard.  This automatically seeks the most rugged windows available
    in the DTM rather than accepting random mostly-flat crops.
    """
    import rasterio
    rng = np.random.default_rng(seed)
    records = []
    with rasterio.open(tif_path) as src:
        margin = crop_size_px // 2 + 4
        if src.width <= 2 * margin or src.height <= 2 * margin:
            raise ValueError("GeoTIFF is too small for the requested crop size")
        mpp = float((abs(src.transform.a) + abs(src.transform.e)) / 2.0)
        seen = set()
        attempts = 0
        target_attempts = max(candidate_count * 4, candidate_count + 20)
        while len(records) < candidate_count and attempts < target_attempts:
            attempts += 1
            cx = int(rng.integers(margin, src.width - margin))
            cy = int(rng.integers(margin, src.height - margin))
            key = (cx, cy)
            if key in seen:
                continue
            seen.add(key)
            arr, _, _ = _read_geotiff_window_array(src, cx, cy, crop_size_px)
            valid = np.isfinite(arr)
            if valid.mean() < 0.97:
                continue
            fill = float(np.nanmedian(arr))
            arr = np.where(valid, arr, fill).astype(np.float32)
            arr -= float(arr.min())
            metrics = terrain_difficulty_metrics(arr, mpp)
            # Reject windows dominated by terrain that no rover planner can traverse.
            slope = slope_map(arr, mpp)
            traversable = float(np.mean(slope <= 30.0))
            if traversable < 0.55:
                continue
            records.append({"center_x": cx, "center_y": cy, "meters_per_cell": mpp,
                            "traversable_fraction": traversable, **metrics})
    if len(records) < max(12, candidate_count // 4):
        raise RuntimeError(f"Only {len(records)} valid HiRISE windows found; reduce crop size or candidate count")
    records.sort(key=lambda r: r["difficulty_score"])
    n = len(records)
    for rank, rec in enumerate(records):
        frac = (rank + 0.5) / n
        rec["difficulty"] = "easy" if frac < 1/3 else ("medium" if frac < 2/3 else "hard")
        rec["difficulty_rank_fraction"] = frac
    return records


def load_geotiff_window(tif_path, center_xy, crop_size_px=129, out_size=129, seed=0, boulder_count=28):
    import rasterio
    crop_size_px = int(crop_size_px)
    out_size = int(out_size)
    cx, cy = map(int, center_xy)
    with rasterio.open(tif_path) as src:
        arr, _, _ = _read_geotiff_window_array(src, cx, cy, crop_size_px)
        mpp = float((abs(src.transform.a) + abs(src.transform.e)) / 2.0)

    if not np.isfinite(arr).any():
        raise ValueError("Selected GeoTIFF window contains no finite elevation values")
    fill = float(np.nanmedian(arr))
    arr = np.where(np.isfinite(arr), arr, fill).astype(np.float32)
    if out_size != arr.shape[0]:
        scale = out_size / arr.shape[0]
        arr = zoom(arr, zoom=scale, order=1)[:out_size, :out_size]
        mpp = mpp / scale
    arr -= float(arr.min())

    rng = np.random.default_rng(seed)
    obstacle = np.zeros(arr.shape, dtype=bool)
    yy, xx = np.mgrid[0:arr.shape[0], 0:arr.shape[1]]
    for _ in range(int(boulder_count)):
        radius = rng.uniform(1.0, 2.6)
        cx2 = rng.uniform(7, arr.shape[1] - 7)
        cy2 = rng.uniform(7, arr.shape[0] - 7)
        obstacle |= ((xx - cx2) ** 2 + (yy - cy2) ** 2) <= radius**2
    return {"height": arr.astype(np.float32), "obstacle": obstacle, "meters_per_cell": float(mpp)}
