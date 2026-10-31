from __future__ import annotations

import os
from pathlib import Path

from mars_rover.envs import RoverEnv


def terrain_files(path: str | Path):
    p = Path(path)
    files = sorted(str(x) for x in p.glob("*.npz"))
    if not files:
        raise FileNotFoundError(f"No .npz terrain files found in {p}")
    return files


def make_env_fn(
    terrain_dir,
    action_mode,
    route_observation,
    seed,
    render_mode=None,
    randomize_physics=True,
    curriculum_enabled=False,
):
    """Lightweight subprocess-safe RoverEnv factory.

    Keep numerical libraries in each physics worker single-threaded. Parallelism is
    provided by one PyBullet process per environment, so nested BLAS/OpenMP threads
    only waste CPU/RAM and can make Windows SubprocVecEnv slower.
    """
    files = terrain_files(terrain_dir)

    def _factory():
        os.environ.setdefault("OMP_NUM_THREADS", "1")
        os.environ.setdefault("MKL_NUM_THREADS", "1")
        os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
        os.environ.setdefault("NUMEXPR_NUM_THREADS", "1")
        env = RoverEnv(
            files,
            action_mode=action_mode,
            route_observation=route_observation,
            render_mode=render_mode,
            randomize_physics=randomize_physics,
            curriculum_enabled=curriculum_enabled,
            # Training/validation workers are headless and never need synthetic
            # camera previews. GUI/live-demo callers still use RoverEnv directly.
            sensor_preview=(render_mode == "human"),
        )
        env.reset(seed=seed)
        return env

    return _factory
