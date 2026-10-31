from __future__ import annotations

import math
import os
from collections import OrderedDict, deque
from pathlib import Path
from typing import Iterable, Optional

import gymnasium as gym
from gymnasium import spaces
import numpy as np
from scipy.ndimage import binary_dilation, distance_transform_edt, map_coordinates
import pybullet as p
import pybullet_data

from mars_rover.config import TerrainConfig, RoverConfig

SURROGATE_URDF = Path(__file__).resolve().parents[2] / "assets" / "urdf" / "perseverance_surrogate.urdf"
SURROGATE_WHEEL_RADIUS = 0.2625
SURROGATE_MAX_WHEEL_SPEED = 10.0
SURROGATE_MAX_STEER_RAD = math.radians(38.0)

# Nominal rover geometry from the simplified six-wheel URDF.  A* is a point-robot
# planner, so we convert the rover into a conservative circular configuration-space
# footprint before planning.  The radius includes the outer wheel envelope; the
# additional margin keeps suspension/wheels away from boulders during steering.
SURROGATE_FRONT_X_M = 0.92
SURROGATE_MIDDLE_X_M = -0.24
SURROGATE_REAR_X_M = -1.26
SURROGATE_WHEEL_Y_M = 1.13
SURROGATE_STEERING_CENTER_X_M = 0.5 * (SURROGATE_FRONT_X_M + SURROGATE_REAR_X_M)
SURROGATE_HALF_WHEELBASE_M = 0.5 * (SURROGATE_FRONT_X_M - SURROGATE_REAR_X_M)
SURROGATE_FOOTPRINT_RADIUS_M = math.hypot(
    max(abs(SURROGATE_FRONT_X_M), abs(SURROGATE_REAR_X_M)) + SURROGATE_WHEEL_RADIUS,
    SURROGATE_WHEEL_Y_M + 0.5 * 0.18,
)
PLANNER_SAFETY_MARGIN_M = 0.35
PLANNER_CLEARANCE_BAND_M = 1.75
PLANNER_CLEARANCE_WEIGHT = 3.5
BOUNDARY_EXTRA_MARGIN_M = 1.25
COLLISION_MIN_FORCE_N = 2.0
COLLISION_MIN_PENETRATION_M = 0.001

SURROGATE_WHEEL_POSITIONS = {
    "fl_wheel_joint": (SURROGATE_FRONT_X_M, +SURROGATE_WHEEL_Y_M),
    "fr_wheel_joint": (SURROGATE_FRONT_X_M, -SURROGATE_WHEEL_Y_M),
    "ml_wheel_joint": (SURROGATE_MIDDLE_X_M, +SURROGATE_WHEEL_Y_M),
    "mr_wheel_joint": (SURROGATE_MIDDLE_X_M, -SURROGATE_WHEEL_Y_M),
    "rl_wheel_joint": (SURROGATE_REAR_X_M, +SURROGATE_WHEEL_Y_M),
    "rr_wheel_joint": (SURROGATE_REAR_X_M, -SURROGATE_WHEEL_Y_M),
}
from mars_rover.planners import astar
from mars_rover.terrain import (
    build_cost_map,
    grid_to_world,
    load_terrain,
    obstacle_components,
    sample_height_world,
    world_to_grid,
)


TC = TerrainConfig()
RC = RoverConfig()


# Per-process LRU cache for static terrain products. Each SubprocVecEnv worker owns
# its own small cache, avoiding repeated npz I/O, distance transforms, slope/roughness
# computation and obstacle connected-components on every episode reset.
_TERRAIN_STATIC_CACHE: OrderedDict[tuple, dict] = OrderedDict()
_TERRAIN_CACHE_MAX_ITEMS = max(1, int(os.environ.get("MARS_ROVER_TERRAIN_CACHE_SIZE", "8")))
_TERRAIN_CACHE_HITS = 0
_TERRAIN_CACHE_MISSES = 0


def _terrain_cache_key(path: str, hard_clearance: float, footprint_radius: float, boundary_margin: float) -> tuple:
    return (
        str(Path(path).resolve()),
        round(float(hard_clearance), 6),
        round(float(footprint_radius), 6),
        round(float(boundary_margin), 6),
        round(float(TC.max_slope_deg), 6),
        round(float(TC.slope_weight), 6),
        round(float(TC.roughness_weight), 6),
    )


def _get_static_terrain_products(path: str, hard_clearance: float, footprint_radius: float, boundary_margin: float) -> dict:
    global _TERRAIN_CACHE_HITS, _TERRAIN_CACHE_MISSES
    key = _terrain_cache_key(path, hard_clearance, footprint_radius, boundary_margin)
    cached = _TERRAIN_STATIC_CACHE.get(key)
    if cached is not None:
        _TERRAIN_STATIC_CACHE.move_to_end(key)
        _TERRAIN_CACHE_HITS += 1
        return cached

    _TERRAIN_CACHE_MISSES += 1
    t = load_terrain(path)
    height = t["height"]
    obstacle = t["obstacle"]
    res = float(t["meters_per_cell"])

    if obstacle.any():
        raw_obstacle_distance_m = distance_transform_edt(~obstacle) * res
        planning_obstacle = obstacle | (raw_obstacle_distance_m <= hard_clearance)
    else:
        raw_obstacle_distance_m = np.full(obstacle.shape, np.inf, dtype=np.float32)
        planning_obstacle = obstacle.copy()

    cost, slope, rough = build_cost_map(
        height, planning_obstacle, res, TC.max_slope_deg, TC.slope_weight, TC.roughness_weight
    )

    boundary_cells = max(1, int(math.ceil(boundary_margin / max(res, 1e-6))))
    boundary_forbidden = np.zeros(height.shape, dtype=bool)
    if 2 * boundary_cells < min(height.shape):
        boundary_forbidden[:boundary_cells, :] = True
        boundary_forbidden[-boundary_cells:, :] = True
        boundary_forbidden[:, :boundary_cells] = True
        boundary_forbidden[:, -boundary_cells:] = True
        cost[boundary_forbidden] = np.inf
        planning_obstacle = planning_obstacle | boundary_forbidden

    if obstacle.any():
        clearance_beyond_hard = raw_obstacle_distance_m - hard_clearance
        q = np.clip(1.0 - clearance_beyond_hard / PLANNER_CLEARANCE_BAND_M, 0.0, 1.0)
        clearance_penalty = PLANNER_CLEARANCE_WEIGHT * q * q
        finite = np.isfinite(cost)
        cost[finite] += clearance_penalty[finite]
        clearance_from_footprint_m = (
            raw_obstacle_distance_m - footprint_radius - 0.5 * res
        ).astype(np.float32)
    else:
        clearance_from_footprint_m = np.full(obstacle.shape, np.inf, dtype=np.float32)

    p95 = float(np.percentile(rough, 95))
    rough_norm = np.clip(rough / max(p95, 1e-6), 0.0, 1.0).astype(np.float32)

    clear = ~binary_dilation(planning_obstacle, iterations=1)
    safe = np.isfinite(cost) & (slope <= 12.0) & clear
    safe_cells = np.argwhere(safe)
    if len(safe_cells) < 20:
        safe_cells = np.argwhere(np.isfinite(cost) & clear)

    rocks = sorted(obstacle_components(obstacle, res), key=lambda d: d["cells"], reverse=True)[:40]
    height_f32 = height.astype(np.float32, copy=False)
    products = {
        "height": height,
        "heightfield_data": height_f32.ravel(order="C").tolist(),
        "height_mid_z": 0.5 * (float(height_f32.min()) + float(height_f32.max())),
        "obstacle": obstacle,
        "obstacle_float": obstacle.astype(np.float32),
        "res": res,
        "raw_obstacle_distance_m": raw_obstacle_distance_m,
        "planning_obstacle": planning_obstacle,
        "cost": cost,
        "slope": slope,
        "rough": rough,
        "rough_norm": rough_norm,
        "boundary_forbidden": boundary_forbidden,
        "clearance_from_footprint_m": clearance_from_footprint_m,
        "safe_cells": safe_cells,
        "rocks": rocks,
    }
    _TERRAIN_STATIC_CACHE[key] = products
    _TERRAIN_STATIC_CACHE.move_to_end(key)
    while len(_TERRAIN_STATIC_CACHE) > _TERRAIN_CACHE_MAX_ITEMS:
        _TERRAIN_STATIC_CACHE.popitem(last=False)
    return products


def _wrap(angle: float) -> float:
    return (angle + math.pi) % (2.0 * math.pi) - math.pi


def _path_length_xy(path_xy: np.ndarray) -> float:
    if path_xy is None or len(path_xy) < 2:
        return 0.0
    return float(np.linalg.norm(np.diff(path_xy, axis=0), axis=1).sum())


class RoverEnv(gym.Env):
    """3-D PyBullet planetary rover environment with compact 2.5-D CNN observations.

    The physical rover, terrain contacts, gravity, friction, roll/pitch and rocks are 3-D.
    The neural network receives a rover-centric local terrain tensor plus rover state.

    action_mode="continuous": [steering, throttle] in [-1, 1]^2 (PPO/SAC)
    action_mode="discrete": 9 local motion primitives (A*+D3QN)
    """

    metadata = {"render_modes": ["human", "rgb_array"], "render_fps": 20}

    def __init__(
        self,
        terrain_paths: Iterable[str | Path],
        action_mode: str = "continuous",
        route_observation: bool = False,
        render_mode: Optional[str] = None,
        local_map_size: int = 32,
        local_span_m: float = 24.0,
        max_episode_steps: int = 650,
        control_dt: float = 0.12,
        physics_hz: int = 240,
        randomize_physics: bool = True,
        fixed_terrain: Optional[str | Path] = None,
        sensor_preview: bool = True,
        curriculum_enabled: bool = False,
        v15b_mode: bool = False,
        dr_force_episode: Optional[bool] = None,
        dr_fixed_strength: Optional[float] = None,
    ):
        super().__init__()
        self.terrain_paths = [str(Path(x)) for x in terrain_paths]
        if not self.terrain_paths:
            raise ValueError("terrain_paths is empty")
        self.action_mode = action_mode
        self.route_observation = bool(route_observation)
        self.render_mode = render_mode
        self.local_map_size = int(local_map_size)
        self.local_span_m = float(local_span_m)
        self.max_episode_steps = int(max_episode_steps)
        self.control_dt = float(control_dt)
        self.physics_hz = int(physics_hz)
        self.physics_dt = 1.0 / self.physics_hz
        self.physics_substeps = max(1, int(round(self.control_dt * self.physics_hz)))
        self.randomize_physics = bool(randomize_physics)
        self.fixed_terrain = str(fixed_terrain) if fixed_terrain is not None else None
        self.sensor_preview = bool(sensor_preview)
        self.curriculum_enabled = bool(curriculum_enabled)
        self.curriculum_progress = 0.0

        # V1.5b is opt-in so older PPO/D3QN/benchmark scripts keep their legacy
        # behaviour.  The dedicated V1.5b SAC trainer passes v15b_mode=True.
        self.v15b_mode = bool(v15b_mode)
        self.dr_force_episode = dr_force_episode
        self.dr_fixed_strength = (
            None if dr_fixed_strength is None
            else float(np.clip(dr_fixed_strength, 0.0, 1.0))
        )

        # Backward compatibility with the earlier V1.5 DR patch: MARS_ROVER_UNITY_DR=1
        # still enables the original DR mode when v15b_mode is False.
        legacy_dr_requested = (
            os.environ.get("MARS_ROVER_UNITY_DR", "0").strip().lower()
            in {"1", "true", "yes", "on"}
        )
        self.unity_dr_enabled = bool(
            self.randomize_physics and (self.v15b_mode or legacy_dr_requested)
        )
        self.dr_episode_enabled = False
        self._set_nominal_domain_params()

        # V1.5b no-progress detector: 120 control transitions = 14.4 s at 0.12 s.
        # It watches NET GOAL PROGRESS rather than physical movement, so spinning,
        # creeping and forward/backward oscillation cannot reset it.
        self.no_progress_window_steps = 120
        self.no_progress_required_m = 0.40
        self.goal_progress_window = deque(maxlen=self.no_progress_window_steps + 1)
        self.last_window_progress_m = float("inf")

        # Terrain is compact uint8 to keep SAC/D3QN replay buffers practical on a laptop.
        self.observation_space = spaces.Dict(
            {
                "terrain": spaces.Box(
                    low=0,
                    high=255,
                    shape=(5, self.local_map_size, self.local_map_size),
                    dtype=np.uint8,
                ),
                "state": spaces.Box(low=-1.0, high=1.0, shape=(14,), dtype=np.float32),
            }
        )
        if action_mode == "continuous":
            self.action_space = spaces.Box(-1.0, 1.0, shape=(2,), dtype=np.float32)
        elif action_mode == "discrete":
            self.action_space = spaces.Discrete(9)
        else:
            raise ValueError("action_mode must be 'continuous' or 'discrete'")

        mode = p.GUI if render_mode == "human" else p.DIRECT
        self.cid = p.connect(mode)
        if self.cid < 0:
            raise RuntimeError("Could not connect to PyBullet")
        p.setAdditionalSearchPath(pybullet_data.getDataPath(), physicsClientId=self.cid)

        self.np_random = np.random.default_rng()
        self.terrain_id = -1
        self.rover_id = -1
        self.rock_ids: list[int] = []
        self.drive_wheels: list[int] = []
        self.drive_wheel_joints: dict[str, int] = {}
        self.steer_joints: dict[str, int] = {}
        self.passive_suspension: list[int] = []
        self.link_map: dict[str, int] = {}
        self.link_names: dict[int, str] = {}
        self.rocker_joints: dict[str, int] = {}
        self.planner_footprint_radius_m = SURROGATE_FOOTPRINT_RADIUS_M
        self.planner_hard_clearance_m = SURROGATE_FOOTPRINT_RADIUS_M + PLANNER_SAFETY_MARGIN_M
        self.boundary_margin_m = SURROGATE_FOOTPRINT_RADIUS_M + BOUNDARY_EXTRA_MARGIN_M
        self.planning_obstacle = None
        self.raw_obstacle_distance_m = None
        self.clearance_from_footprint_m = None
        self.reference_min_clearance_m = float("inf")
        self.min_clearance_m = float("inf")
        self.wheel_contact_sum = 0.0
        self.min_wheel_contacts = 6
        self.low_contact_steps = 0
        self.last_collision_details = None
        self.camera_mode = "chase"  # free, chase, top, overview
        self._camera_mode_before_top = "chase"
        self._camera_help_printed = False
        self.current_difficulty = "unknown"
        self.terrain_groups = {"easy": [], "medium": [], "hard": [], "unknown": []}
        for tp in self.terrain_paths:
            name = Path(tp).name.lower()
            diff = next((d for d in ("easy", "medium", "hard") if f"_{d}_" in name), "unknown")
            self.terrain_groups[diff].append(tp)
        self.prev_action = np.zeros(2, dtype=np.float32)
        self.action_change_sum = 0.0
        self.abs_steer_sum = 0.0
        self.abs_throttle_sum = 0.0
        self.signed_steer_sum = 0.0
        self.signed_throttle_sum = 0.0
        self.abs_yaw_rate_sum = 0.0
        self.heading_alignment_sum = 0.0
        self.positive_progress_steps = 0
        self.last_progress_m = 0.0
        self.episode_steps = 0
        self.total_reward = 0.0
        self.distance_travelled = 0.0
        self.energy_proxy = 0.0
        self.slip_sum = 0.0
        self.max_roll_deg = 0.0
        self.max_pitch_deg = 0.0
        self.battery = 1.0
        self.last_pos_xy = np.zeros(2, dtype=np.float64)
        self.start_xy = np.zeros(2, dtype=np.float64)
        self.goal_xy = np.zeros(2, dtype=np.float64)
        self.prev_goal_dist = 0.0
        self.initial_goal_distance_m = 0.0
        self.current_episode_step_limit = self.max_episode_steps
        self.stuck_counter = 0
        self.collision_count = 0
        self.rollover = False
        self.out_of_bounds = False
        self.success = False
        self.current_terrain_path = ""
        self.path_xy = np.empty((0, 2), dtype=float)
        self.path_mask = None
        self.reference_path_length_m = 0.0
        self.path_index = 0
        self._trace_last = None
        self._hud_id = -1


    # ---------------------------------------------------------- V1.5 domain randomization
    def _set_nominal_domain_params(self) -> None:
        """Nominal PyBullet values used when Unity-oriented DR is disabled."""
        self.dr_strength = 0.0
        self.dr_terrain_mu = 1.05
        self.dr_wheel_mu = 1.30
        self.dr_rolling_friction = 0.025
        self.dr_spinning_friction = 0.018
        self.dr_linear_damping = 0.08
        self.dr_angular_damping = 0.26
        self.dr_motor_force = 1500.0
        self.dr_max_speed_mps = 0.95
        self.dr_steer_force = 1100.0
        self.dr_steer_max_velocity = 1.55
        self.dr_initial_yaw_jitter_rad = 0.0

        # Observation-side randomization. These are deliberately mild.
        self.dr_terrain_noise_std_u8 = 0.0
        self.dr_obstacle_dropout_p = 0.0
        self.dr_obstacle_dilate = False
        self.dr_goal_bearing_noise_rad = 0.0
        self.dr_goal_distance_noise_std = 0.0
        self.dr_velocity_noise_std = 0.0
        self.dr_yaw_rate_noise_std = 0.0
        self.dr_attitude_noise_std = 0.0
        self.dr_slip_noise_std = 0.0

    def _sample_unity_domain_randomization(self) -> None:
        """Sample the episode physics/sensor domain.

        V1.5b deliberately keeps a permanent nominal anchor:
          * first 10% of training: 50% nominal, 50% mild DR
          * afterwards:           30% nominal, 70% moderate DR

        Randomized episodes never exceed strength 0.75 in normal V1.5b training.
        Validation may force a deterministic strength through dr_fixed_strength.
        """
        self._set_nominal_domain_params()
        self.dr_episode_enabled = False

        if not self.unity_dr_enabled:
            return

        if self.v15b_mode:
            if self.dr_force_episode is not None:
                use_dr = bool(self.dr_force_episode)
            else:
                # Bootstrap locomotion for the first 100k of a 1M run, then keep
                # 30% nominal episodes forever so basic driving is never forgotten.
                dr_probability = 0.50 if self.curriculum_progress < 0.10 else 0.70
                use_dr = bool(self.np_random.random() < dr_probability)

            if not use_dr:
                return

            self.dr_episode_enabled = True
            if self.dr_fixed_strength is not None:
                self.dr_strength = self.dr_fixed_strength
            elif self.curriculum_progress < 0.10:
                self.dr_strength = float(self.np_random.uniform(0.10, 0.40))
            else:
                self.dr_strength = float(self.np_random.uniform(0.25, 0.75))
        else:
            # Legacy V1.5 behaviour retained for old scripts.
            self.dr_episode_enabled = True
            if self.curriculum_enabled:
                self.dr_strength = float(
                    np.clip(0.25 + 0.75 * self.curriculum_progress, 0.25, 1.0)
                )
            else:
                self.dr_strength = 1.0

        s = self.dr_strength

        def frac(center: float, half_fraction: float) -> float:
            return float(center * (1.0 + self.np_random.uniform(-half_fraction, half_fraction) * s))

        # Contact / vehicle dynamics: moderate Unity-representative spreads.
        self.dr_terrain_mu = frac(1.05, 0.12)
        self.dr_wheel_mu = frac(1.30, 0.12)
        self.dr_rolling_friction = frac(0.025, 0.20)
        self.dr_spinning_friction = frac(0.018, 0.22)
        self.dr_linear_damping = frac(0.08, 0.18)
        self.dr_angular_damping = frac(0.26, 0.16)
        self.dr_motor_force = frac(1500.0, 0.10)
        self.dr_max_speed_mps = frac(0.95, 0.07)
        self.dr_steer_force = frac(1100.0, 0.09)
        self.dr_steer_max_velocity = frac(1.55, 0.09)

        self.dr_initial_yaw_jitter_rad = float(
            self.np_random.uniform(-math.radians(3.0), math.radians(3.0)) * s
        )

        # Mild observation differences: enough to bridge Unity discretization/LiDAR,
        # but deliberately too small to destroy terrain structure.
        self.dr_terrain_noise_std_u8 = 2.0 * s
        self.dr_obstacle_dropout_p = float(self.np_random.uniform(0.0, 0.04) * s)
        self.dr_obstacle_dilate = bool(self.np_random.random() < 0.20 * s)
        self.dr_goal_bearing_noise_rad = math.radians(0.8) * s
        self.dr_goal_distance_noise_std = 0.0015 * s
        self.dr_velocity_noise_std = 0.015 * s
        self.dr_yaw_rate_noise_std = 0.010 * s
        self.dr_attitude_noise_std = 0.008 * s
        self.dr_slip_noise_std = 0.010 * s

    def _apply_terrain_observation_dr(self, terrain_u8: np.ndarray) -> np.ndarray:
        if not self.dr_episode_enabled:
            return terrain_u8

        out = terrain_u8.copy()

        # Very small pixel-domain noise on elevation/slope/roughness only.
        if self.dr_terrain_noise_std_u8 > 0.0:
            noise = self.np_random.normal(
                0.0, self.dr_terrain_noise_std_u8, size=out[:3].shape
            )
            out[:3] = np.clip(
                out[:3].astype(np.float32) + noise, 0.0, 255.0
            ).astype(np.uint8)

        # Unity LiDAR produces an imperfect/inflated occupancy view. Train SAC to
        # tolerate a small amount of missing obstacle surface plus occasional
        # one-cell inflation, but keep the majority of episodes close to nominal.
        obstacle = out[3] > 127
        if self.dr_obstacle_dropout_p > 0.0 and obstacle.any():
            drop = (self.np_random.random(obstacle.shape) < self.dr_obstacle_dropout_p) & obstacle
            obstacle[drop] = False
        if self.dr_obstacle_dilate and obstacle.any():
            obstacle = binary_dilation(obstacle, iterations=1)
        out[3] = np.where(obstacle, 255, 0).astype(np.uint8)

        # Route channel remains untouched (zero for standalone SAC).
        return out

    def _apply_state_observation_dr(self, state: np.ndarray) -> np.ndarray:
        if not self.dr_episode_enabled:
            return state

        out = state.astype(np.float32, copy=True)

        # Goal-distance scale perturbation.
        out[0] += float(self.np_random.normal(0.0, self.dr_goal_distance_noise_std))

        # Perturb bearing as an angle, then rebuild sin/cos to keep them consistent.
        angle = math.atan2(float(out[1]), float(out[2]))
        angle += float(self.np_random.normal(0.0, self.dr_goal_bearing_noise_rad))
        out[1] = math.sin(angle)
        out[2] = math.cos(angle)

        # Small normalized sensor noise.
        out[3] += float(self.np_random.normal(0.0, self.dr_velocity_noise_std))
        out[4] += float(self.np_random.normal(0.0, self.dr_velocity_noise_std))
        out[5] += float(self.np_random.normal(0.0, self.dr_yaw_rate_noise_std))
        out[6] += float(self.np_random.normal(0.0, self.dr_attitude_noise_std))
        out[7] += float(self.np_random.normal(0.0, self.dr_attitude_noise_std))
        out[8] += float(self.np_random.normal(0.0, self.dr_slip_noise_std))

        out[:] = np.clip(out, -1.0, 1.0)
        out[0] = np.clip(out[0], 0.0, 1.0)
        out[8] = np.clip(out[8], 0.0, 1.0)
        out[9] = np.clip(out[9], 0.0, 1.0)
        return out

    def domain_randomization_info(self) -> dict:
        return {
            "available": bool(self.unity_dr_enabled),
            "episode_enabled": bool(self.dr_episode_enabled),
            "v15b_mode": bool(self.v15b_mode),
            "strength": float(self.dr_strength),
            "terrain_mu": float(self.dr_terrain_mu),
            "wheel_mu": float(self.dr_wheel_mu),
            "rolling_friction": float(self.dr_rolling_friction),
            "spinning_friction": float(self.dr_spinning_friction),
            "linear_damping": float(self.dr_linear_damping),
            "angular_damping": float(self.dr_angular_damping),
            "motor_force": float(self.dr_motor_force),
            "max_speed_mps": float(self.dr_max_speed_mps),
            "steer_force": float(self.dr_steer_force),
            "steer_max_velocity": float(self.dr_steer_max_velocity),
            "initial_yaw_jitter_deg": float(math.degrees(self.dr_initial_yaw_jitter_rad)),
            "terrain_noise_std_u8": float(self.dr_terrain_noise_std_u8),
            "obstacle_dropout_p": float(self.dr_obstacle_dropout_p),
            "obstacle_dilate": bool(self.dr_obstacle_dilate),
        }

    # ------------------------------------------------------------------ physics
    def _reset_bullet(self) -> None:
        p.resetSimulation(physicsClientId=self.cid)
        p.setGravity(0, 0, -RC.mars_gravity, physicsClientId=self.cid)
        p.setTimeStep(self.physics_dt, physicsClientId=self.cid)
        p.setPhysicsEngineParameter(
            numSolverIterations=100,
            enableConeFriction=1,
            physicsClientId=self.cid,
        )
        if self.render_mode == "human":
            p.configureDebugVisualizer(p.COV_ENABLE_SHADOWS, 1, physicsClientId=self.cid)
            p.configureDebugVisualizer(p.COV_ENABLE_WIREFRAME, 0, physicsClientId=self.cid)
            p.configureDebugVisualizer(
                p.COV_ENABLE_RGB_BUFFER_PREVIEW,
                1 if self.sensor_preview else 0,
                physicsClientId=self.cid,
            )
            p.configureDebugVisualizer(
                p.COV_ENABLE_DEPTH_BUFFER_PREVIEW,
                1 if self.sensor_preview else 0,
                physicsClientId=self.cid,
            )
            p.configureDebugVisualizer(
                p.COV_ENABLE_SEGMENTATION_MARK_PREVIEW,
                1 if self.sensor_preview else 0,
                physicsClientId=self.cid,
            )

    def _make_heightfield(self) -> None:
        # Identical vertical-centering correction to the validated Phase-1 V2 demo.
        h = self.height
        rows, cols = h.shape
        mid_z = self.height_mid_z
        flags = getattr(p, "GEOM_CONCAVE_INTERNAL_EDGE", 0)
        shape = p.createCollisionShape(
            p.GEOM_HEIGHTFIELD,
            flags=flags,
            meshScale=[self.res, self.res, 1.0],
            heightfieldTextureScaling=(rows - 1) / 2,
            heightfieldData=self.heightfield_data,
            numHeightfieldRows=rows,
            numHeightfieldColumns=cols,
            physicsClientId=self.cid,
        )
        self.terrain_id = p.createMultiBody(
            baseMass=0,
            baseCollisionShapeIndex=shape,
            basePosition=[0, 0, mid_z],
            physicsClientId=self.cid,
        )
        if self.render_mode in ("human", "rgb_array"):
            p.changeVisualShape(
                self.terrain_id,
                -1,
                rgbaColor=[0.52, 0.22, 0.09, 1],
                physicsClientId=self.cid,
            )
        if self.v15b_mode or self.unity_dr_enabled:
            terrain_mu = self.dr_terrain_mu
        else:
            terrain_mu = float(self.np_random.uniform(0.82, 1.22)) if self.randomize_physics else 1.05
        p.changeDynamics(
            self.terrain_id,
            -1,
            lateralFriction=terrain_mu,
            restitution=0.0,
            physicsClientId=self.cid,
        )

    def _spawn_rocks(self) -> None:
        self.rock_ids = []
        rocks = self.cached_rocks
        for rock in rocks:
            rc = np.array([[rock["row"], rock["col"]]], dtype=float)
            x, y = grid_to_world(rc, self.height.shape, self.res)[0]
            gz = sample_height_world(self.height, self.res, float(x), float(y))
            r = float(np.clip(rock["radius_m"], 0.38, 1.35))
            sx = r * float(self.np_random.uniform(0.82, 1.12))
            sy = r * float(self.np_random.uniform(0.78, 1.08))
            sz = r * float(self.np_random.uniform(0.45, 0.72))
            # A simple sphere is cheap, stable and exactly aligned in both collision and rendering.
            col = p.createCollisionShape(
                p.GEOM_SPHERE, radius=r, physicsClientId=self.cid
            )
            vis = -1
            if self.render_mode in ("human", "rgb_array"):
                vis = p.createVisualShape(
                    p.GEOM_SPHERE,
                    radius=r,
                    rgbaColor=[0.26, 0.11, 0.06, 1],
                    physicsClientId=self.cid,
                )
            body = p.createMultiBody(
                baseMass=0,
                baseCollisionShapeIndex=col,
                baseVisualShapeIndex=vis,
                basePosition=[float(x), float(y), gz + r + 0.015],
                physicsClientId=self.cid,
            )
            self.rock_ids.append(body)

    def _aggregate_bottom_z(self, body: int) -> float:
        mins = [p.getAABB(body, -1, physicsClientId=self.cid)[0][2]]
        for j in range(p.getNumJoints(body, physicsClientId=self.cid)):
            mins.append(p.getAABB(body, j, physicsClientId=self.cid)[0][2])
        return float(min(mins))

    def _spawn_rover(self, yaw: float) -> None:
        sx, sy = map(float, self.start_xy)
        ground = sample_height_world(self.height, self.res, sx, sy)
        if not SURROGATE_URDF.exists():
            raise FileNotFoundError(f"Missing six-wheel rover URDF: {SURROGATE_URDF}")
        self.rover_id = p.loadURDF(
            str(SURROGATE_URDF),
            [sx, sy, ground + 2.0],
            p.getQuaternionFromEuler([0, 0, yaw]),
            flags=p.URDF_USE_INERTIA_FROM_FILE,
            physicsClientId=self.cid,
        )
        joint_map = {}
        self.link_map = {}
        self.link_names = {}
        for j in range(p.getNumJoints(self.rover_id, physicsClientId=self.cid)):
            info = p.getJointInfo(self.rover_id, j, physicsClientId=self.cid)
            joint_name = info[1].decode("utf-8")
            link_name = info[12].decode("utf-8")
            joint_map[joint_name] = j
            self.link_map[link_name] = j
            self.link_names[j] = link_name
        wheel_names = [
            "fl_wheel_joint", "fr_wheel_joint", "ml_wheel_joint",
            "mr_wheel_joint", "rl_wheel_joint", "rr_wheel_joint"
        ]
        self.drive_wheel_joints = {name: joint_map[name] for name in wheel_names}
        self.drive_wheels = [self.drive_wheel_joints[name] for name in wheel_names]
        self.steer_joints = {n: joint_map[n] for n in [
            "fl_steer_joint", "fr_steer_joint", "rl_steer_joint", "rr_steer_joint"
        ]}
        self.passive_suspension = [joint_map[n] for n in [
            "left_rocker_joint", "right_rocker_joint", "left_bogie_joint", "right_bogie_joint"
        ]]
        self.rocker_joints = {
            "left": joint_map["left_rocker_joint"],
            "right": joint_map["right_rocker_joint"],
        }
        for j in self.passive_suspension:
            p.setJointMotorControl2(self.rover_id, j, p.VELOCITY_CONTROL, force=0, physicsClientId=self.cid)

        base_pos, base_orn = p.getBasePositionAndOrientation(self.rover_id, physicsClientId=self.cid)
        bottom = self._aggregate_bottom_z(self.rover_id)
        bottom_offset = float(base_pos[2] - bottom)
        p.resetBasePositionAndOrientation(
            self.rover_id, [sx, sy, ground + bottom_offset + 0.035], base_orn, physicsClientId=self.cid
        )
        p.resetBaseVelocity(self.rover_id, [0, 0, 0], [0, 0, 0], physicsClientId=self.cid)

        if self.v15b_mode or self.unity_dr_enabled:
            wheel_mu = self.dr_wheel_mu
            rolling_friction = self.dr_rolling_friction
            spinning_friction = self.dr_spinning_friction
            linear_damping = self.dr_linear_damping
            angular_damping = self.dr_angular_damping
        else:
            wheel_mu = float(self.np_random.uniform(1.00, 1.55)) if self.randomize_physics else 1.30
            rolling_friction = 0.025
            spinning_friction = 0.018
            linear_damping = 0.08
            angular_damping = 0.26

        for j in self.drive_wheels:
            # Tiny left/right wheel-to-wheel spread in robust mode approximates
            # WheelCollider/contact asymmetry without destabilizing the rover.
            this_mu = (
                wheel_mu * float(self.np_random.uniform(0.97, 1.03))
                if self.dr_episode_enabled else wheel_mu
            )
            p.changeDynamics(
                self.rover_id, j, lateralFriction=this_mu, rollingFriction=rolling_friction,
                spinningFriction=spinning_friction, restitution=0.0, contactStiffness=90000,
                contactDamping=2200, physicsClientId=self.cid,
            )
        p.changeDynamics(
            self.rover_id, -1, linearDamping=linear_damping, angularDamping=angular_damping,
            restitution=0.0, physicsClientId=self.cid,
        )

        if self.v15b_mode or self.unity_dr_enabled:
            self.motor_force = self.dr_motor_force
            self.max_speed_mps = self.dr_max_speed_mps
            self.steer_force = self.dr_steer_force
            self.steer_max_velocity = self.dr_steer_max_velocity
        else:
            self.motor_force = float(self.np_random.uniform(1250, 1850)) if self.randomize_physics else 1500.0
            # Simulation is deliberately time-scaled versus the real rover so 100-m episodes remain tractable.
            self.max_speed_mps = float(self.np_random.uniform(0.82, 1.10)) if self.randomize_physics else 0.95
            self.steer_force = 1100.0
            self.steer_max_velocity = 1.55

        self.max_steer_rad = SURROGATE_MAX_STEER_RAD
        self._set_rover_control(0.0, 0.0)
        for _ in range(220):
            self._apply_rocker_differential()
            p.stepSimulation(physicsClientId=self.cid)

        # A second placement pass after suspension settling prevents a wheel from
        # beginning a few centimetres above/below the local surface on uneven starts.
        base_pos, base_orn = p.getBasePositionAndOrientation(self.rover_id, physicsClientId=self.cid)
        bottom = self._aggregate_bottom_z(self.rover_id)
        if bottom < ground - 0.04 or bottom > ground + 0.09:
            dz = (ground + 0.035) - bottom
            p.resetBasePositionAndOrientation(
                self.rover_id, [base_pos[0], base_pos[1], base_pos[2] + dz], base_orn, physicsClientId=self.cid
            )
            p.resetBaseVelocity(self.rover_id, [0, 0, 0], [0, 0, 0], physicsClientId=self.cid)
            for _ in range(100):
                self._apply_rocker_differential()
                p.stepSimulation(physicsClientId=self.cid)

    def _apply_rocker_differential(self) -> None:
        """Soft approximation of the left/right rocker differential.

        The previous coupling was intentionally stiff and could unload a wheel during
        tight steering.  This lower-gain spring-damper still discourages excessive
        chassis roll while allowing the passive rocker/bogie suspension to articulate.
        """
        if not self.rocker_joints or self.rover_id < 0:
            return
        jl = self.rocker_joints["left"]
        jr = self.rocker_joints["right"]
        ql, vl = p.getJointState(self.rover_id, jl, physicsClientId=self.cid)[:2]
        qr, vr = p.getJointState(self.rover_id, jr, physicsClientId=self.cid)[:2]
        err = float(ql + qr)
        derr = float(vl + vr)
        torque = float(np.clip(-185.0 * err - 18.0 * derr, -150.0, 150.0))
        p.setJointMotorControl2(self.rover_id, jl, p.TORQUE_CONTROL, force=torque, physicsClientId=self.cid)
        p.setJointMotorControl2(self.rover_id, jr, p.TORQUE_CONTROL, force=torque, physicsClientId=self.cid)

    def _four_wheel_steering_targets(self, steer: float) -> tuple[dict[str, float], dict[str, float]]:
        """Return Ackermann-like four-wheel steering angles and six wheel speed scales.

        Positive steer is rover-left.  Front and rear axles counter-steer around a
        common instantaneous centre of rotation.  Inner wheels receive a larger
        steering angle and lower wheel speed than outer wheels, which greatly reduces
        lateral tyre scrub and turn-induced wheel unloading.
        """
        steer = float(np.clip(steer, -1.0, 1.0))
        if abs(steer) < 1e-5:
            angles = {
                "fl_steer_joint": 0.0, "fr_steer_joint": 0.0,
                "rl_steer_joint": 0.0, "rr_steer_joint": 0.0,
            }
            return angles, {name: 1.0 for name in SURROGATE_WHEEL_POSITIONS}

        # Choose curvature so the inner corner wheel reaches (but never exceeds)
        # the URDF steering limit at |steer| == 1.
        tmax = math.tan(self.max_steer_rad)
        kmax = tmax / (SURROGATE_HALF_WHEELBASE_M + tmax * SURROGATE_WHEEL_Y_M)
        curvature = steer * kmax

        angles: dict[str, float] = {}
        speed_scales: dict[str, float] = {}
        steer_name_for_wheel = {
            "fl_wheel_joint": "fl_steer_joint",
            "fr_wheel_joint": "fr_steer_joint",
            "rl_wheel_joint": "rl_steer_joint",
            "rr_wheel_joint": "rr_steer_joint",
        }
        for wheel_name, (x, y) in SURROGATE_WHEEL_POSITIONS.items():
            x_rel = x - SURROGATE_STEERING_CENTER_X_M
            longitudinal_scale = 1.0 - curvature * y
            lateral_scale = curvature * x_rel
            speed_scales[wheel_name] = float(max(0.10, math.hypot(longitudinal_scale, lateral_scale)))
            if wheel_name in steer_name_for_wheel:
                delta = math.atan2(lateral_scale, longitudinal_scale)
                angles[steer_name_for_wheel[wheel_name]] = float(
                    np.clip(delta, -self.max_steer_rad, self.max_steer_rad)
                )

        return angles, speed_scales

    def _set_rover_control(self, steer: float, throttle: float) -> None:
        steer = float(np.clip(steer, -1.0, 1.0))
        throttle = float(np.clip(throttle, -1.0, 1.0))
        steer_targets, speed_scales = self._four_wheel_steering_targets(steer)

        for name, target_angle in steer_targets.items():
            p.setJointMotorControl2(
                self.rover_id, self.steer_joints[name], p.POSITION_CONTROL,
                targetPosition=target_angle, force=self.steer_force, maxVelocity=self.steer_max_velocity,
                physicsClientId=self.cid,
            )

        # The outer wheels travel a longer arc and the inner wheels a shorter one.
        # Overall speed is also reduced for tight turns to keep the tall surrogate stable.
        base_speed = throttle * self.max_speed_mps * (1.0 - 0.38 * abs(steer))
        for wheel_name, joint_idx in self.drive_wheel_joints.items():
            wheel_speed = base_speed * speed_scales[wheel_name]
            omega = float(np.clip(
                wheel_speed / SURROGATE_WHEEL_RADIUS,
                -SURROGATE_MAX_WHEEL_SPEED,
                SURROGATE_MAX_WHEEL_SPEED,
            ))
            p.setJointMotorControl2(
                self.rover_id, joint_idx, p.VELOCITY_CONTROL, targetVelocity=omega,
                force=self.motor_force, physicsClientId=self.cid,
            )

    # -------------------------------------------------------------- terrain/goal
    def set_curriculum_progress(self, progress: float) -> None:
        self.curriculum_progress = float(np.clip(progress, 0.0, 1.0))

    def _choose_curriculum_path(self) -> str:
        if not self.curriculum_enabled:
            return self.terrain_paths[int(self.np_random.integers(len(self.terrain_paths)))]

        if self.v15b_mode:
            # Anchored distribution. Difficulty never becomes "all hard", so successful
            # nominal locomotion remains represented throughout the replay buffer.
            if self.curriculum_progress < 0.10:
                weights = {"easy": 0.60, "medium": 0.30, "hard": 0.10}
            else:
                weights = {"easy": 0.30, "medium": 0.45, "hard": 0.25}

            available = [(d, w) for d, w in weights.items() if self.terrain_groups.get(d)]
            if not available:
                return self.terrain_paths[int(self.np_random.integers(len(self.terrain_paths)))]
            probs = np.asarray([w for _, w in available], dtype=np.float64)
            probs /= probs.sum()
            idx = int(self.np_random.choice(len(available), p=probs))
            preferred = available[idx][0]
            pool = self.terrain_groups[preferred]
            return pool[int(self.np_random.integers(len(pool)))]

        # Legacy easy→medium→hard schedule retained outside V1.5b mode.
        pgr = self.curriculum_progress
        if pgr < 0.40:
            preferred = "easy"
        elif pgr < 0.75:
            preferred = "medium" if self.np_random.random() < 0.80 else "easy"
        else:
            preferred = "hard" if self.np_random.random() < 0.72 else "medium"
        pool = self.terrain_groups.get(preferred) or self.terrain_paths
        return pool[int(self.np_random.integers(len(pool)))]

    def _load_random_terrain(self) -> None:
        path = self.fixed_terrain or self._choose_curriculum_path()
        self.current_terrain_path = str(path)
        name = Path(path).name.lower()
        self.current_difficulty = next((d for d in ("easy", "medium", "hard") if f"_{d}_" in name), "unknown")

        static = _get_static_terrain_products(
            str(path), self.planner_hard_clearance_m, self.planner_footprint_radius_m, self.boundary_margin_m
        )
        self.height = static["height"]
        self.heightfield_data = static["heightfield_data"]
        self.height_mid_z = float(static["height_mid_z"])
        self.obstacle = static["obstacle"]
        self.obstacle_float = static["obstacle_float"]
        self.res = float(static["res"])
        self.raw_obstacle_distance_m = static["raw_obstacle_distance_m"]
        self.planning_obstacle = static["planning_obstacle"]
        self.cost = static["cost"]
        self.slope = static["slope"]
        self.rough = static["rough"]
        self.rough_norm = static["rough_norm"]
        self.boundary_forbidden = static["boundary_forbidden"]
        self.clearance_from_footprint_m = static["clearance_from_footprint_m"]
        self.safe_cells = static["safe_cells"]
        self.cached_rocks = static["rocks"]

    def _safe_cells(self) -> np.ndarray:
        return self.safe_cells

    def _choose_start_goal(self) -> None:
        pts = self._safe_cells()
        rows, cols = self.height.shape
        # Six-wheel rover is physically larger; choose substantial but episode-feasible traverses.
        min_sep = 0.30 * math.hypot(rows, cols)
        plan = None
        for _ in range(60):
            a = pts[int(self.np_random.integers(len(pts)))]
            b = pts[int(self.np_random.integers(len(pts)))]
            if np.linalg.norm(a - b) < min_sep:
                continue
            candidate = astar(self.cost, tuple(map(int, a)), tuple(map(int, b)))
            if candidate.success and len(candidate.path) >= 12:
                plan = candidate
                break
        if plan is None:
            # Deterministic fallback used only on unusually difficult tiles.
            a = pts[0]
            d = np.linalg.norm(pts - a, axis=1)
            b = pts[int(np.argmax(d))]
            plan = astar(self.cost, tuple(map(int, a)), tuple(map(int, b)))
            if not plan.success:
                raise RuntimeError(f"Could not generate reachable start/goal on {self.current_terrain_path}")

        self.start_rc = tuple(map(int, plan.path[0]))
        self.goal_rc = tuple(map(int, plan.path[-1]))
        self.path_rc = plan.path
        self.path_xy = grid_to_world(plan.path, self.height.shape, self.res)
        self.start_xy = self.path_xy[0].astype(np.float64)
        self.goal_xy = self.path_xy[-1].astype(np.float64)
        self.reference_path_length_m = _path_length_xy(self.path_xy)
        if self.clearance_from_footprint_m is not None and len(plan.path):
            vals = self.clearance_from_footprint_m[plan.path[:, 0], plan.path[:, 1]]
            self.reference_min_clearance_m = float(np.min(vals)) if np.isfinite(vals).any() else float("inf")
        else:
            self.reference_min_clearance_m = float("inf")

        mask = np.zeros(self.height.shape, dtype=bool)
        mask[plan.path[:, 0], plan.path[:, 1]] = True
        self.path_mask = binary_dilation(mask, iterations=2).astype(np.float32)

    # -------------------------------------------------------------- observations
    def _rover_pose(self):
        pos, orn = p.getBasePositionAndOrientation(self.rover_id, physicsClientId=self.cid)
        roll, pitch, yaw = p.getEulerFromQuaternion(orn)
        lin, ang = p.getBaseVelocity(self.rover_id, physicsClientId=self.cid)
        return np.asarray(pos, float), float(roll), float(pitch), float(yaw), np.asarray(lin, float), np.asarray(ang, float)

    def _sample_local_map(self, pos: np.ndarray, yaw: float) -> np.ndarray:
        n = self.local_map_size
        half = self.local_span_m / 2.0
        # Image top is in front of the rover; image right is rover-right.
        forward = np.linspace(half, -half, n, dtype=np.float32)
        right = np.linspace(-half, half, n, dtype=np.float32)
        F, R = np.meshgrid(forward, right, indexing="ij")
        fwd = np.array([math.cos(yaw), math.sin(yaw)], dtype=np.float32)
        # Rover frame convention: +X forward, +Y left, therefore rover-right is
        # (sin(yaw), -cos(yaw)).  The previous vector was rover-left, which mirrored
        # every local terrain image horizontally relative to goal-bearing features.
        rgt = np.array([math.sin(yaw), -math.cos(yaw)], dtype=np.float32)
        X = pos[0] + F * fwd[0] + R * rgt[0]
        Y = pos[1] + F * fwd[1] + R * rgt[1]
        rr, cc = world_to_grid(X, Y, self.height.shape, self.res)
        coords = np.vstack([np.asarray(rr).ravel(), np.asarray(cc).ravel()])

        elev = map_coordinates(self.height, coords, order=1, mode="nearest").reshape(n, n)
        slope = map_coordinates(self.slope, coords, order=1, mode="nearest").reshape(n, n)
        rough = map_coordinates(self.rough_norm, coords, order=1, mode="nearest").reshape(n, n)
        obs = map_coordinates(self.obstacle_float, coords, order=0, mode="constant", cval=1.0).reshape(n, n)
        if self.route_observation:
            path = map_coordinates(self.path_mask, coords, order=0, mode="constant", cval=0.0).reshape(n, n)
        else:
            path = np.zeros((n, n), dtype=np.float32)

        ground = sample_height_world(self.height, self.res, float(pos[0]), float(pos[1]))
        elev01 = np.clip((elev - ground + 6.0) / 12.0, 0.0, 1.0)
        slope01 = np.clip(slope / 35.0, 0.0, 1.0)
        rough01 = np.clip(rough, 0.0, 1.0)
        channels = np.stack([elev01, slope01, rough01, obs, path], axis=0)
        terrain_u8 = np.rint(np.clip(channels, 0, 1) * 255.0).astype(np.uint8)
        return self._apply_terrain_observation_dr(terrain_u8)

    def _nearest_path_features(self, pos_xy: np.ndarray, yaw: float) -> tuple[float, float]:
        if not self.route_observation or len(self.path_xy) == 0:
            return 0.0, 0.0
        lo = max(0, self.path_index - 8)
        hi = min(len(self.path_xy), self.path_index + 32)
        local = self.path_xy[lo:hi]
        rel = int(np.argmin(np.linalg.norm(local - pos_xy, axis=1)))
        self.path_index = lo + rel
        dist = float(np.linalg.norm(self.path_xy[self.path_index] - pos_xy))
        j = min(len(self.path_xy) - 1, self.path_index + 2)
        vec = self.path_xy[j] - pos_xy
        desired = math.atan2(float(vec[1]), float(vec[0])) if np.linalg.norm(vec) > 1e-6 else yaw
        return float(np.clip(dist / 8.0, 0, 1)), math.sin(_wrap(desired - yaw))

    def _wheel_slip(self, v_forward: float | None = None) -> float:
        """Estimate longitudinal wheel slip without mistaking normal turning for slip.

        Each wheel follows a different arc in a turn, so comparing all six wheel RPMs
        to one chassis forward speed falsely reports slip.  We instead estimate the
        rigid-body velocity at each nominal wheel location and project it along that
        wheel's rolling direction.
        """
        pos, orn = p.getBasePositionAndOrientation(self.rover_id, physicsClientId=self.cid)
        _, _, yaw = p.getEulerFromQuaternion(orn)
        lin, ang = p.getBaseVelocity(self.rover_id, physicsClientId=self.cid)
        vx, vy = float(lin[0]), float(lin[1])
        wz = float(ang[2])
        cy, sy = math.cos(yaw), math.sin(yaw)
        steer_for_wheel = {
            "fl_wheel_joint": "fl_steer_joint",
            "fr_wheel_joint": "fr_steer_joint",
            "rl_wheel_joint": "rl_steer_joint",
            "rr_wheel_joint": "rr_steer_joint",
        }
        slips = []
        for wheel_name, j in self.drive_wheel_joints.items():
            x_local, y_local = SURROGATE_WHEEL_POSITIONS[wheel_name]
            rx = cy * x_local - sy * y_local
            ry = sy * x_local + cy * y_local
            wheel_vx = vx - wz * ry
            wheel_vy = vy + wz * rx
            steer_angle = 0.0
            if wheel_name in steer_for_wheel:
                sj = self.steer_joints[steer_for_wheel[wheel_name]]
                steer_angle = float(p.getJointState(self.rover_id, sj, physicsClientId=self.cid)[0])
            heading = yaw + steer_angle
            rolling_speed = abs(wheel_vx * math.cos(heading) + wheel_vy * math.sin(heading))
            joint_speed = abs(float(p.getJointState(self.rover_id, j, physicsClientId=self.cid)[1]) * SURROGATE_WHEEL_RADIUS)
            denom = max(joint_speed, rolling_speed, 0.15)
            slips.append(abs(joint_speed - rolling_speed) / denom)
        if not slips:
            return 0.0
        return float(np.clip(np.mean(slips), 0.0, 1.0))

    def _get_obs(self) -> dict[str, np.ndarray]:
        pos, roll, pitch, yaw, lin, ang = self._rover_pose()
        fwd = np.array([math.cos(yaw), math.sin(yaw), 0.0])
        # Keep signed lateral velocity positive to rover-left, matching the sign of
        # sin(relative_goal_heading).  Only the image sampling basis uses rover-right.
        left = np.array([-math.sin(yaw), math.cos(yaw), 0.0])
        v_forward = float(np.dot(lin, fwd))
        v_lateral = float(np.dot(lin, left))
        goal_vec = self.goal_xy - pos[:2]
        goal_dist = float(np.linalg.norm(goal_vec))
        goal_heading = math.atan2(float(goal_vec[1]), float(goal_vec[0]))
        rel_goal = _wrap(goal_heading - yaw)
        slip = self._wheel_slip(v_forward)
        # Standalone PPO/SAC receive no A*-derived route state.  Keep the last two
        # state slots at zero for them so the observation layout remains identical
        # across algorithms without leaking planner information.
        if self.route_observation:
            path_dist, path_heading_sin = self._nearest_path_features(pos[:2], yaw)
        else:
            path_dist, path_heading_sin = 0.0, 0.0
        diag = max(1.0, math.hypot(self.height.shape[0], self.height.shape[1]) * self.res)
        state = np.array(
            [
                np.clip(goal_dist / diag, 0, 1),
                math.sin(rel_goal),
                math.cos(rel_goal),
                np.clip(v_forward / 1.3, -1, 1),
                np.clip(v_lateral / 1.0, -1, 1),
                np.clip(float(ang[2]) / 2.0, -1, 1),
                np.clip(roll / math.radians(50), -1, 1),
                np.clip(pitch / math.radians(50), -1, 1),
                np.clip(slip, 0, 1),
                np.clip(self.battery, 0, 1),
                float(self.prev_action[0]),
                float(self.prev_action[1]),
                path_dist,
                path_heading_sin,
            ],
            dtype=np.float32,
        )
        state = self._apply_state_observation_dr(state)
        return {"terrain": self._sample_local_map(pos, yaw), "state": state}

    # ------------------------------------------------------------------- rewards
    def _decode_action(self, action) -> tuple[float, float, np.ndarray]:
        if self.action_mode == "continuous":
            a = np.asarray(action, dtype=np.float32).reshape(2)
            steer = float(np.clip(a[0], -1, 1))
            throttle = float(np.clip(a[1], -1, 1))
        else:
            table = np.array(
                [
                    [0.00, 0.90], [-0.35, 0.78], [-0.90, 0.58],
                    [0.35, 0.78], [0.90, 0.58], [0.00, 0.38],
                    [0.00, 0.00], [-0.55, -0.35], [0.55, -0.35],
                ], dtype=np.float32,
            )
            steer, throttle = map(float, table[int(action)])
        return steer, throttle, np.array([steer, throttle], dtype=np.float32)

    def _wheel_contact_count(self) -> int:
        count = 0
        for j in self.drive_wheels:
            contacts = p.getContactPoints(
                bodyA=self.rover_id, linkIndexA=j, physicsClientId=self.cid
            )
            if contacts:
                count += 1
        return count

    def _clearance_to_obstacles(self, pos_xy: np.ndarray) -> float:
        if self.clearance_from_footprint_m is None:
            return float("inf")
        rr, cc = world_to_grid(
            float(pos_xy[0]), float(pos_xy[1]), self.height.shape, self.res
        )
        value = map_coordinates(
            self.clearance_from_footprint_m,
            np.array([[float(rr)], [float(cc)]], dtype=np.float64),
            order=1, mode="nearest",
        )[0]
        return float(value)

    def _collision_details(self) -> Optional[dict]:
        contacts = p.getContactPoints(bodyA=self.rover_id, physicsClientId=self.cid)
        rocks = set(self.rock_ids)
        rock_contacts = [c for c in contacts if int(c[2]) in rocks]
        if not rock_contacts:
            return None

        # Bullet may return a positive-distance, zero-force near-contact manifold.
        # Count a collision only when there is meaningful penetration or contact force;
        # near misses are already captured separately by min_clearance_m.
        meaningful = [
            c for c in rock_contacts
            if float(c[8]) <= -COLLISION_MIN_PENETRATION_M or float(c[9]) >= COLLISION_MIN_FORCE_N
        ]
        if not meaningful:
            return None

        c = min(meaningful, key=lambda x: (float(x[8]), -float(x[9])))
        link_idx = int(c[3])
        return {
            "rock_body_id": int(c[2]),
            "rover_link_index": link_idx,
            "rover_link_name": self.link_names.get(link_idx, "base_link" if link_idx == -1 else str(link_idx)),
            "contact_distance_m": float(c[8]),
            "normal_force_n": float(c[9]),
            "position_on_rover": [float(v) for v in c[5]],
            "position_on_rock": [float(v) for v in c[6]],
        }

    def _collision_with_rock(self) -> bool:
        self.last_collision_details = self._collision_details()
        return self.last_collision_details is not None

    def _outside(self, pos_xy: np.ndarray) -> bool:
        rows, cols = self.height.shape
        half_x = 0.5 * (cols - 1) * self.res
        half_y = 0.5 * (rows - 1) * self.res
        margin = self.boundary_margin_m
        return not (-half_x + margin <= pos_xy[0] <= half_x - margin and -half_y + margin <= pos_xy[1] <= half_y - margin)

    # ---------------------------------------------------------------- gym methods
    def reset(self, *, seed: Optional[int] = None, options: Optional[dict] = None):
        super().reset(seed=seed)
        if seed is not None:
            self.np_random = np.random.default_rng(seed)
        self._load_random_terrain()
        self._choose_start_goal()
        self._sample_unity_domain_randomization()
        self._reset_bullet()
        self._make_heightfield()
        self._spawn_rocks()

        # Initial heading is derived only from start→goal geometry, not from the
        # hidden A* route.  This removes subtle planner leakage into standalone PPO/SAC.
        initial = self.goal_xy - self.start_xy
        yaw = math.atan2(float(initial[1]), float(initial[0]))
        yaw += self.dr_initial_yaw_jitter_rad
        self._spawn_rover(yaw)

        self.prev_action[:] = 0
        self.action_change_sum = 0.0
        self.abs_steer_sum = 0.0
        self.abs_throttle_sum = 0.0
        self.signed_steer_sum = 0.0
        self.signed_throttle_sum = 0.0
        self.abs_yaw_rate_sum = 0.0
        self.heading_alignment_sum = 0.0
        self.positive_progress_steps = 0
        self.last_progress_m = 0.0
        self.episode_steps = 0
        self.total_reward = 0.0
        self.distance_travelled = 0.0
        self.energy_proxy = 0.0
        self.slip_sum = 0.0
        self.max_roll_deg = 0.0
        self.max_pitch_deg = 0.0
        self.battery = 1.0
        self.stuck_counter = 0
        self.collision_count = 0
        self.rollover = False
        self.out_of_bounds = False
        self.success = False
        self.path_index = 0
        self.min_clearance_m = float("inf")
        self.wheel_contact_sum = 0.0
        self.min_wheel_contacts = 6
        self.low_contact_steps = 0
        self.last_collision_details = None

        pos, *_ = self._rover_pose()
        self.last_pos_xy = pos[:2].copy()
        self.prev_goal_dist = float(np.linalg.norm(self.goal_xy - self.last_pos_xy))
        self.initial_goal_distance_m = self.prev_goal_dist
        self.goal_progress_window.clear()
        self.goal_progress_window.append(self.prev_goal_dist)
        self.last_window_progress_m = float("inf")

        # A fixed 650-step horizon made some 80-120 m traverses physically impossible
        # at the validated rover speed.  Give each episode a path-length-aware budget,
        # while retaining max_episode_steps as the minimum safety horizon.
        effective_speed = max(0.45, 0.75 * self.max_speed_mps)
        nominal_seconds = self.reference_path_length_m / effective_speed
        budget_seconds = 1.55 * nominal_seconds + 18.0
        adaptive_steps = int(math.ceil(budget_seconds / self.control_dt))
        self.current_episode_step_limit = int(np.clip(
            max(self.max_episode_steps, adaptive_steps), self.max_episode_steps, 2400
        ))
        self._trace_last = self.last_pos_xy.copy()

        if self.render_mode == "human":
            self._draw_scene()
            self.camera_mode = "chase"
            self._camera_mode_before_top = "chase"
            self._update_debug_camera(pos, yaw)
            if not self._camera_help_printed:
                print("Camera controls: 1=FREE, 2=FOLLOW/CHASE, 3=TOP-DOWN, 4=OVERVIEW, "
                      "F=toggle FREE/FOLLOW, T=toggle TOP/FOLLOW")
                print("FREE mode leaves PyBullet mouse camera controls untouched.")
                self._camera_help_printed = True

        obs = self._get_obs()
        info = {
            "terrain": Path(self.current_terrain_path).name,
            "reference_path_length_m": self.reference_path_length_m,
            "reference_min_clearance_m": self.reference_min_clearance_m,
            "planner_footprint_radius_m": self.planner_footprint_radius_m,
            "planner_hard_clearance_m": self.planner_hard_clearance_m,
            "boundary_margin_m": self.boundary_margin_m,
            "episode_step_limit": self.current_episode_step_limit,
            "difficulty": self.current_difficulty,
            "rover_model": "six_wheel_rocker_bogie_surrogate",
            "terrain_cache_hits": int(_TERRAIN_CACHE_HITS),
            "terrain_cache_misses": int(_TERRAIN_CACHE_MISSES),
            "unity_domain_randomization": self.domain_randomization_info(),
        }
        return obs, info

    def step(self, action):
        self.episode_steps += 1
        steer, throttle, action_vec = self._decode_action(action)
        self._set_rover_control(steer, throttle)
        for _ in range(self.physics_substeps):
            self._apply_rocker_differential()
            p.stepSimulation(physicsClientId=self.cid)

        pos, roll, pitch, yaw, lin, ang = self._rover_pose()
        pos_xy = pos[:2]
        step_dist = float(np.linalg.norm(pos_xy - self.last_pos_xy))
        self.distance_travelled += step_dist
        self.last_pos_xy = pos_xy.copy()
        self.max_roll_deg = max(self.max_roll_deg, abs(math.degrees(roll)))
        self.max_pitch_deg = max(self.max_pitch_deg, abs(math.degrees(pitch)))

        fwd = np.array([math.cos(yaw), math.sin(yaw), 0.0])
        v_forward = float(np.dot(lin, fwd))
        slip = self._wheel_slip(v_forward)
        self.slip_sum += slip

        wheel_contacts = self._wheel_contact_count()
        self.wheel_contact_sum += wheel_contacts
        self.min_wheel_contacts = min(self.min_wheel_contacts, wheel_contacts)
        if wheel_contacts < 5:
            self.low_contact_steps += 1

        clearance_m = self._clearance_to_obstacles(pos_xy)
        if np.isfinite(clearance_m):
            self.min_clearance_m = min(self.min_clearance_m, clearance_m)

        command_effort = abs(throttle) * self.max_speed_mps + 0.30 * abs(steer)
        self.energy_proxy += command_effort * self.control_dt
        self.battery = max(0.0, self.battery - 0.00065 * command_effort)

        goal_dist = float(np.linalg.norm(self.goal_xy - pos_xy))
        progress = self.prev_goal_dist - goal_dist
        self.prev_goal_dist = goal_dist
        goal_vec = self.goal_xy - pos_xy
        rel_goal = _wrap(math.atan2(float(goal_vec[1]), float(goal_vec[0])) - yaw)
        self.last_progress_m = float(progress)
        self.abs_steer_sum += abs(float(steer))
        self.abs_throttle_sum += abs(float(throttle))
        self.signed_steer_sum += float(steer)
        self.signed_throttle_sum += float(throttle)
        self.abs_yaw_rate_sum += abs(float(ang[2]))
        self.heading_alignment_sum += math.cos(rel_goal)
        if progress > 0.002:
            self.positive_progress_steps += 1

        action_delta = action_vec - self.prev_action
        action_change = float(np.linalg.norm(action_delta))
        self.action_change_sum += action_change

        terminated = False
        truncated = False
        terminal_reason = ""
        success_efficiency_bonus = 0.0

        if self.v15b_mode and self.action_mode == "continuous":
            # -----------------------------------------------------------------
            # V1.5b reward: actual mission progress is the dominant signal.
            # Normalization makes a complete traverse worth ~+300 progress reward
            # regardless of whether the start→goal mission is 50 m or 100 m.
            # -----------------------------------------------------------------
            denom = max(self.initial_goal_distance_m, 1e-6)
            reward = 300.0 * (progress / denom)

            # Tiny costs only. None can create a positive alternative objective.
            reward -= 0.005  # time
            wasted_motion = max(0.0, step_dist - max(progress, 0.0))
            reward -= 0.20 * wasted_motion
            reward -= 0.015 * slip
            reward -= 0.005 * float(np.dot(action_delta, action_delta))

            # Mild footprint-danger cost only very near obstacles. There is NO
            # positive reward for simply staying far away from obstacles.
            if np.isfinite(clearance_m) and clearance_m < 0.50:
                q = float(np.clip((0.50 - clearance_m) / 0.50, 0.0, 1.0))
                reward -= 0.03 * q * q

            # Goal-progress window blocks the previous loopholes: standing still,
            # spinning, creeping, lateral motion and forward/back oscillation.
            self.goal_progress_window.append(goal_dist)
            no_progress_failure = False
            if len(self.goal_progress_window) >= self.no_progress_window_steps + 1:
                self.last_window_progress_m = float(
                    self.goal_progress_window[0] - self.goal_progress_window[-1]
                )
                no_progress_failure = bool(
                    goal_dist > 3.0
                    and self.last_window_progress_m < self.no_progress_required_m
                )

            remaining = float(np.clip(goal_dist / denom, 0.0, 1.0))

            # Safety failures keep priority over goal if both happen on one step.
            collision = self._collision_with_rock()
            if collision:
                self.collision_count += 1
                reward -= 220.0 + 120.0 * remaining
                terminated = True
                terminal_reason = "collision"

            if not terminated and (abs(math.degrees(roll)) > 58.0 or abs(math.degrees(pitch)) > 58.0):
                self.rollover = True
                reward -= 280.0 + 120.0 * remaining
                terminated = True
                terminal_reason = "rollover"

            if not terminated and self._outside(pos_xy):
                self.out_of_bounds = True
                reward -= 250.0 + 120.0 * remaining
                terminated = True
                terminal_reason = "out_of_bounds"

            if not terminated and goal_dist <= 2.2:
                self.success = True
                ref = max(float(self.reference_path_length_m), 1e-6)
                efficiency = float(np.clip(ref / max(self.distance_travelled, ref), 0.0, 1.0))
                success_efficiency_bonus = 40.0 * efficiency
                reward += 250.0 + success_efficiency_bonus
                terminated = True
                terminal_reason = "goal"

            if not terminated and no_progress_failure:
                reward -= 180.0 + 120.0 * remaining
                terminated = True
                terminal_reason = "stuck"

            if not terminated and self.episode_steps >= self.current_episode_step_limit:
                reward -= 180.0 + 120.0 * remaining
                truncated = True
                terminal_reason = "time_limit"
        else:
            # ------------------------------ legacy reward ---------------------
            reward = 1.75 * progress
            forward_fraction = float(np.clip(v_forward / max(self.max_speed_mps, 1e-6), 0.0, 1.0))
            reward += 0.025 * forward_fraction * math.cos(rel_goal)
            reward -= 0.016
            reward -= 0.014 * command_effort
            reward -= 0.070 * slip
            reward -= 0.032 * max(0.0, abs(math.degrees(roll)) - 15.0) / 20.0
            reward -= 0.032 * max(0.0, abs(math.degrees(pitch)) - 15.0) / 20.0
            if action_vec[1] < -0.1:
                reward -= 0.025 * abs(float(action_vec[1]))

            smooth_coeff = 0.035 if self.action_mode == "continuous" else 0.015
            reward -= smooth_coeff * float(np.dot(action_delta, action_delta))
            if self.action_mode == "continuous" and progress <= 0.002:
                reward -= 0.008 * abs(float(steer))
            if self.route_observation:
                path_dist, path_heading_sin = self._nearest_path_features(pos_xy, yaw)
                reward -= 0.035 * path_dist
                reward -= 0.010 * abs(path_heading_sin)

            if step_dist < 0.006 and goal_dist > 2.5:
                self.stuck_counter += 1
            else:
                self.stuck_counter = max(0, self.stuck_counter - 2)

            collision = self._collision_with_rock()
            if collision:
                self.collision_count += 1
                reward -= 100.0
                terminated = True
                terminal_reason = "collision"
            if not terminated and (abs(math.degrees(roll)) > 58.0 or abs(math.degrees(pitch)) > 58.0):
                self.rollover = True
                reward -= 180.0
                terminated = True
                terminal_reason = "rollover"
            if not terminated and self._outside(pos_xy):
                self.out_of_bounds = True
                reward -= 120.0
                terminated = True
                terminal_reason = "out_of_bounds"
            if not terminated and goal_dist <= 2.2:
                self.success = True
                reward += 300.0
                terminated = True
                terminal_reason = "goal"
            if not terminated and self.stuck_counter >= 70:
                reward -= 70.0
                terminated = True
                terminal_reason = "stuck"
            if not terminated and self.episode_steps >= self.current_episode_step_limit:
                reward -= 50.0
                truncated = True
                terminal_reason = "time_limit"

        self.prev_action = action_vec
        self.total_reward += float(reward)

        if self.render_mode == "human":
            self._handle_camera_keys()
            self._update_debug_camera(pos, yaw)
            self._live_debug(pos, roll, pitch, yaw, goal_dist, slip)

        obs = self._get_obs()
        info = {
            "terrain": Path(self.current_terrain_path).name,
            "goal_distance_m": goal_dist,
            "slip": slip,
            "battery": self.battery,
            "wheel_contacts": int(wheel_contacts),
            "clearance_m": float(clearance_m),
            "terminal_reason": terminal_reason,
            "difficulty": self.current_difficulty,
            "v15b_mode": bool(self.v15b_mode),
            "dr_episode_enabled": bool(self.dr_episode_enabled),
            "dr_strength": float(self.dr_strength),
            "goal_progress_window_m": float(self.last_window_progress_m),
        }
        if self.last_collision_details is not None:
            info["collision_details"] = dict(self.last_collision_details)
        if terminated or truncated:
            straight = float(np.linalg.norm(self.goal_xy - self.start_xy))
            goal_progress_m = float(self.initial_goal_distance_m - goal_dist)
            goal_progress = float(np.clip(
                goal_progress_m / max(self.initial_goal_distance_m, 1e-6), -1.0, 1.0
            ))
            # Path efficiency is meaningful only for completed traverses.  Failed
            # episodes are represented by NaN and compared using goal_progress instead.
            path_efficiency = float("nan")
            if self.success:
                path_efficiency = float(np.clip(
                    straight / max(self.distance_travelled, 1e-6), 0.0, 1.0
                ))
            info["episode_metrics"] = {
                "success": int(self.success),
                "collision": int(self.collision_count > 0),
                "rollover": int(self.rollover),
                "out_of_bounds": int(self.out_of_bounds),
                "terminal_reason": terminal_reason,
                "reward_profile": "v15b" if self.v15b_mode else "legacy",
                "dr_episode_enabled": int(self.dr_episode_enabled),
                "dr_strength": float(self.dr_strength),
                "goal_progress_window_m": float(self.last_window_progress_m),
                "success_efficiency_bonus": float(success_efficiency_bonus),
                "steps": int(self.episode_steps),
                "episode_step_limit": int(self.current_episode_step_limit),
                "sim_time_s": float(self.episode_steps * self.control_dt),
                "reward": float(self.total_reward),
                "distance_m": float(self.distance_travelled),
                "straight_line_m": straight,
                "initial_goal_distance_m": float(self.initial_goal_distance_m),
                "goal_progress_m": goal_progress_m,
                "goal_progress": goal_progress,
                "path_efficiency": path_efficiency,
                "reference_astar_m": float(self.reference_path_length_m),
                "energy_proxy": float(self.energy_proxy),
                "mean_action_change": float(self.action_change_sum / max(1, self.episode_steps)),
                "mean_abs_steer": float(self.abs_steer_sum / max(1, self.episode_steps)),
                "mean_abs_throttle": float(self.abs_throttle_sum / max(1, self.episode_steps)),
                "mean_signed_steer": float(self.signed_steer_sum / max(1, self.episode_steps)),
                "mean_signed_throttle": float(self.signed_throttle_sum / max(1, self.episode_steps)),
                "mean_abs_yaw_rate": float(self.abs_yaw_rate_sum / max(1, self.episode_steps)),
                "mean_heading_alignment": float(self.heading_alignment_sum / max(1, self.episode_steps)),
                "positive_progress_fraction": float(self.positive_progress_steps / max(1, self.episode_steps)),
                "mean_slip": float(self.slip_sum / max(1, self.episode_steps)),
                "mean_wheel_contacts": float(self.wheel_contact_sum / max(1, self.episode_steps)),
                "min_wheel_contacts": int(self.min_wheel_contacts),
                "low_contact_fraction": float(self.low_contact_steps / max(1, self.episode_steps)),
                "min_clearance_m": float(self.min_clearance_m),
                "reference_min_clearance_m": float(self.reference_min_clearance_m),
                "planner_footprint_radius_m": float(self.planner_footprint_radius_m),
                "planner_hard_clearance_m": float(self.planner_hard_clearance_m),
                "boundary_margin_m": float(self.boundary_margin_m),
                "max_roll_deg": float(self.max_roll_deg),
                "max_pitch_deg": float(self.max_pitch_deg),
                "final_goal_distance_m": goal_dist,
                "terrain": Path(self.current_terrain_path).name,
                "difficulty": self.current_difficulty,
                "rover_model": "six_wheel_rocker_bogie_surrogate",
                "collision_details": self.last_collision_details,
            }
        return obs, float(reward), terminated, truncated, info

    # --------------------------------------------------------------- GUI / video
    def _draw_scene(self) -> None:
        z0 = sample_height_world(self.height, self.res, float(self.start_xy[0]), float(self.start_xy[1]))
        zg = sample_height_world(self.height, self.res, float(self.goal_xy[0]), float(self.goal_xy[1]))
        p.addUserDebugText("START", [*self.start_xy, z0 + 0.8], [0.2, 0.75, 1], textSize=1.2, physicsClientId=self.cid)
        p.addUserDebugText("GOAL", [*self.goal_xy, zg + 0.8], [1, 0.35, 0.05], textSize=1.2, physicsClientId=self.cid)
        if self.route_observation:
            for a, b in zip(self.path_xy[:-1], self.path_xy[1:]):
                za = sample_height_world(self.height, self.res, float(a[0]), float(a[1])) + 0.12
                zb = sample_height_world(self.height, self.res, float(b[0]), float(b[1])) + 0.12
                p.addUserDebugLine([a[0], a[1], za], [b[0], b[1], zb], [0.05, 0.85, 1.0], lineWidth=4, physicsClientId=self.cid)

    def _handle_camera_keys(self) -> None:
        keys = p.getKeyboardEvents(physicsClientId=self.cid)
        triggered = p.KEY_WAS_TRIGGERED
        if keys.get(ord("1"), 0) & triggered:
            self.camera_mode = "free"
        if keys.get(ord("2"), 0) & triggered:
            self.camera_mode = "chase"
        if keys.get(ord("3"), 0) & triggered:
            self._camera_mode_before_top = self.camera_mode if self.camera_mode != "top" else "chase"
            self.camera_mode = "top"
        if keys.get(ord("4"), 0) & triggered:
            self.camera_mode = "overview"
        if (keys.get(ord("f"), 0) | keys.get(ord("F"), 0)) & triggered:
            self.camera_mode = "free" if self.camera_mode == "chase" else "chase"
        if (keys.get(ord("t"), 0) | keys.get(ord("T"), 0)) & triggered:
            if self.camera_mode == "top":
                self.camera_mode = "chase"
            else:
                self._camera_mode_before_top = self.camera_mode
                self.camera_mode = "top"

    def _update_debug_camera(self, pos: np.ndarray, yaw: float) -> None:
        if self.render_mode != "human" or self.camera_mode == "free":
            return
        span = max((self.height.shape[0] - 1) * self.res, (self.height.shape[1] - 1) * self.res)
        target = [float(pos[0]), float(pos[1]), float(pos[2] + 0.30)]
        if self.camera_mode == "chase":
            p.resetDebugVisualizerCamera(
                8.5, math.degrees(yaw) - 90.0, -27.0, target, physicsClientId=self.cid
            )
        elif self.camera_mode == "top":
            p.resetDebugVisualizerCamera(
                22.0, math.degrees(yaw) - 90.0, -89.0, target, physicsClientId=self.cid
            )
        elif self.camera_mode == "overview":
            p.resetDebugVisualizerCamera(
                min(72.0, max(28.0, span * 0.55)), 45.0, -58.0,
                [0.0, 0.0, float(np.mean(self.height))], physicsClientId=self.cid
            )

    def _camera_link_pose(self, pos: np.ndarray, yaw: float):
        idx = self.link_map.get("camera_head")
        if idx is None:
            fwd = np.array([math.cos(yaw), math.sin(yaw), 0.0])
            up = np.array([0.0, 0.0, 1.0])
            return pos + 0.55 * fwd + np.array([0, 0, 1.55]), fwd, up
        state = p.getLinkState(
            self.rover_id, idx, computeForwardKinematics=1, physicsClientId=self.cid
        )
        link_pos = np.asarray(state[4], dtype=float)
        link_orn = state[5]
        rot = np.asarray(p.getMatrixFromQuaternion(link_orn), dtype=float).reshape(3, 3)
        fwd = rot[:, 0]
        up = rot[:, 2]
        # Put the synthetic lens just in front of the camera head so the rover's own
        # mast/head cannot occlude the RGB/depth/segmentation view.
        eye = link_pos + 0.24 * fwd + 0.015 * up
        return eye, fwd, up

    def _update_sensor_preview(self, pos: np.ndarray, yaw: float) -> None:
        if not self.sensor_preview:
            return
        eye, fwd, up = self._camera_link_pose(pos, yaw)
        target = eye + 8.0 * fwd - 0.55 * up
        view = p.computeViewMatrix(eye.tolist(), target.tolist(), up.tolist())
        proj = p.computeProjectionMatrixFOV(62, 4 / 3, 0.08, 45.0)
        p.getCameraImage(
            320,
            240,
            viewMatrix=view,
            projectionMatrix=proj,
            renderer=p.ER_BULLET_HARDWARE_OPENGL,
            flags=p.ER_SEGMENTATION_MASK_OBJECT_AND_LINKINDEX,
            shadow=1,
            lightDirection=[-1, -1, -2],
            physicsClientId=self.cid,
        )

    def _live_debug(self, pos, roll, pitch, yaw, goal_dist, slip) -> None:
        if self._trace_last is not None and np.linalg.norm(pos[:2] - self._trace_last) > 0.20:
            z0 = sample_height_world(self.height, self.res, float(self._trace_last[0]), float(self._trace_last[1])) + 0.18
            z1 = sample_height_world(self.height, self.res, float(pos[0]), float(pos[1])) + 0.18
            p.addUserDebugLine(
                [self._trace_last[0], self._trace_last[1], z0],
                [pos[0], pos[1], z1],
                [1.0, 0.85, 0.05],
                lineWidth=2,
                lifeTime=0,
                physicsClientId=self.cid,
            )
            self._trace_last = pos[:2].copy()
        if self.episode_steps % 3 == 0:
            self._update_sensor_preview(pos, yaw)
        if self.episode_steps % 5 == 0:
            if self._hud_id >= 0:
                try:
                    p.removeUserDebugItem(self._hud_id, physicsClientId=self.cid)
                except Exception:
                    pass
            contacts = self._wheel_contact_count()
            clearance = self._clearance_to_obstacles(np.asarray(pos[:2], dtype=float))
            clearance_text = "inf" if not np.isfinite(clearance) else f"{clearance:.2f}"
            txt = (
                f"{self.current_difficulty.upper()} | 6W ROCKER-BOGIE | camera={self.camera_mode} | goal={goal_dist:.1f} m | "
                f"step={self.episode_steps} | contacts={contacts}/6 | clearance={clearance_text} m | "
                f"steer={self.prev_action[0]:+.2f} | throttle={self.prev_action[1]:+.2f} | "
                f"dGoal={self.last_progress_m:+.3f} m | slip={slip:.2f} | "
                f"roll={math.degrees(roll):.1f} deg | pitch={math.degrees(pitch):.1f} deg"
            )
            self._hud_id = p.addUserDebugText(
                txt,
                [float(pos[0]), float(pos[1]), float(pos[2] + 1.35)],
                [1, 1, 1],
                textSize=1.0,
                lifeTime=0.4,
                physicsClientId=self.cid,
            )

    def render_frame(self, width: int = 640, height: int = 480) -> np.ndarray:
        pos, _, _, yaw, _, _ = self._rover_pose()
        fwd = np.array([math.cos(yaw), math.sin(yaw), 0.0])
        eye = pos - 6.5 * fwd + np.array([0, 0, 3.3])
        target = pos + 2.0 * fwd + np.array([0, 0, 0.25])
        view = p.computeViewMatrix(eye.tolist(), target.tolist(), [0, 0, 1])
        proj = p.computeProjectionMatrixFOV(58, width / height, 0.08, 120.0)
        _, _, rgba, _, _ = p.getCameraImage(
            width,
            height,
            viewMatrix=view,
            projectionMatrix=proj,
            renderer=p.ER_TINY_RENDERER if self.render_mode != "human" else p.ER_BULLET_HARDWARE_OPENGL,
            shadow=1,
            physicsClientId=self.cid,
        )
        return np.asarray(rgba, dtype=np.uint8).reshape(height, width, 4)[..., :3]

    def close(self):
        if getattr(self, "cid", -1) >= 0:
            try:
                p.disconnect(self.cid)
            except Exception:
                pass
            self.cid = -1
