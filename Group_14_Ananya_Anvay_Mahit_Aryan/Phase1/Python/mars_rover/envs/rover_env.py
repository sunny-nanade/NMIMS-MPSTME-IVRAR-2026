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
        v15c_mode: bool = False,
        v15d_mode: bool = False,
        v15e_mode: bool = False,
        v15f_mode: bool = False,
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

        # V1.5b..V1.5e are opt-in so older PPO/D3QN scripts keep legacy behaviour.
        # V1.5e aligns the SAC observation with the same moving-A* guidance used by
        # the reward: final-goal distance remains state[0], while state[1:3] carry
        # bearing to a local A* lookahead point. The full route is never exposed.
        self.v15b_mode = bool(v15b_mode)
        self.v15c_mode = bool(v15c_mode)
        self.v15d_mode = bool(v15d_mode)
        self.v15f_mode = bool(v15f_mode)
        self.v15e_mode = bool(v15e_mode or self.v15f_mode)
        self.anti_loophole_mode = bool(
            self.v15b_mode or self.v15c_mode or self.v15d_mode or self.v15e_mode
        )
        self.v15d_stage = 0
        self.v15f_stage = 0
        self.dr_force_episode = dr_force_episode
        self.dr_fixed_strength = (
            None if dr_fixed_strength is None
            else float(np.clip(dr_fixed_strength, 0.0, 1.0))
        )

        # Backward compatibility with the earlier V1.5 DR patch.
        legacy_dr_requested = (
            os.environ.get("MARS_ROVER_UNITY_DR", "0").strip().lower()
            in {"1", "true", "yes", "on"}
        )
        self.unity_dr_enabled = bool(
            self.randomize_physics
            and (
                self.v15b_mode or self.v15c_mode or self.v15d_mode
                or self.v15e_mode or legacy_dr_requested
            )
        )
        self.dr_episode_enabled = False
        self._set_nominal_domain_params()

        # V1.5b/V1.5c use goal-distance no-progress. V1.5d switches the same
        # 14.4-s detector to HIDDEN A* feasible-route progress so legitimate
        # sideways/curved obstacle detours are not misclassified as "stuck".
        self.no_progress_window_steps = 120
        self.no_progress_required_m = 0.40
        self.goal_progress_window = deque(maxlen=self.no_progress_window_steps + 1)
        self.route_progress_window = deque(maxlen=self.no_progress_window_steps + 1)
        self.route_deviation_window = deque(maxlen=self.no_progress_window_steps + 1)
        self.last_window_progress_m = float("inf")
        self.last_route_window_progress_m = float("inf")
        self.last_route_window_cross_track_improvement_m = float("inf")

        # Hidden A* reward state. These values are NEVER inserted into standalone
        # SAC observations: route_observation remains False and state[12:14] stay zero.
        self.path_cumulative_m = np.empty(0, dtype=np.float64)
        self.path_remaining_m = np.empty(0, dtype=np.float64)
        self.reward_path_index = 0
        self.prev_route_remaining_m = 0.0
        self.initial_route_remaining_m = 0.0
        self.last_route_progress_m = 0.0
        self.last_route_deviation_m = 0.0
        self.last_route_heading_error_rad = 0.0
        self.last_route_target_steer = 0.0
        self.last_route_s_m = 0.0
        self.prev_route_s_m = 0.0
        self.last_route_forward_speed_mps = 0.0
        self.prev_route_deviation_m = 0.0
        self.route_positive_progress_steps = 0
        self.route_deviation_sum = 0.0
        self.route_steer_bonus_sum = 0.0
        self.route_velocity_bonus_sum = 0.0
        self.route_throttle_bonus_sum = 0.0  # legacy V1.5d metric compatibility
        self.route_forward_speed_sum = 0.0
        self.v15e_guidance_lookahead_m = 6.0

        # V1.5e Hotfix 3: two-stage local recovery logic.
        #
        # 120 controls = 14.4 s -> stall WARNING / recovery opportunity.
        # 200 controls = 24.0 s -> only then classify as terminal stuck.
        #
        # The original 120-step route windows are kept for backward compatibility
        # and stall-warning diagnostics.  Separate long windows are used only by
        # V1.5e so older reward profiles are not silently changed.
        self.v15e_stall_warning_steps = 120
        self.v15e_stuck_total_steps = 200
        self.v15e_stall_route_required_m = 0.35
        self.v15e_stall_cross_track_required_m = 0.30
        self.v15e_stuck_route_required_m = 0.55
        self.v15e_stuck_cross_track_required_m = 0.40
        self.v15e_long_route_s_window = deque(maxlen=self.v15e_stuck_total_steps + 1)
        self.v15e_long_route_deviation_window = deque(maxlen=self.v15e_stuck_total_steps + 1)

        self.v15e_stall_warning_active = False
        self.v15e_stall_warning_count = 0
        self.v15e_recovery_phase_steps = 0
        self.v15e_first_stall_warning_step = -1
        self.v15e_last_short_route_progress_m = float("inf")
        self.v15e_last_short_cross_track_improvement_m = float("inf")
        self.v15e_last_long_route_progress_m = float("inf")
        self.v15e_last_long_cross_track_improvement_m = float("inf")

        # Tiny potential-based heading recovery shaping.  This rewards actual
        # reduction in A*-lookahead heading error, not the steering command itself.
        # Oscillation largely cancels because it is a potential difference.
        self.v15e_heading_potential_scale = 0.008
        self.prev_abs_route_heading_error_rad = 0.0
        self.route_heading_potential_sum = 0.0

        # Snapshot captured only when an episode truly terminates as stuck.
        self.last_stuck_diagnostics = None

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



    def set_v15f_stage(self, stage: int) -> None:
        self.v15f_stage = int(np.clip(int(stage), 0, 3))

    def v15f_stage_snapshot(self) -> dict:
        stage = int(np.clip(self.v15f_stage, 0, 3))
        table = [
            dict(stage=0,name="S0_SHORT_20_30M",mission_min_m=20.0,mission_max_m=30.0,easy_weight=0.85,medium_weight=0.15,hard_weight=0.00,dr_probability=0.10,dr_strength_min=0.05,dr_strength_max=0.20,demo_fraction=0.25),
            dict(stage=1,name="S1_SHORT_30_50M",mission_min_m=30.0,mission_max_m=50.0,easy_weight=0.70,medium_weight=0.25,hard_weight=0.05,dr_probability=0.20,dr_strength_min=0.10,dr_strength_max=0.30,demo_fraction=0.25),
            dict(stage=2,name="S2_MEDIUM_50_75M",mission_min_m=50.0,mission_max_m=75.0,easy_weight=0.50,medium_weight=0.40,hard_weight=0.10,dr_probability=0.35,dr_strength_min=0.15,dr_strength_max=0.45,demo_fraction=0.20),
            dict(stage=3,name="S3_FULL_75_110M",mission_min_m=75.0,mission_max_m=110.0,easy_weight=0.35,medium_weight=0.45,hard_weight=0.20,dr_probability=0.50,dr_strength_min=0.20,dr_strength_max=0.60,demo_fraction=0.20),
        ]
        return table[stage]

    def set_v15d_stage(self, stage: int) -> None:
        """Set competence-gated V1.5d training stage: 0=A, 1=B, 2=C."""
        self.v15d_stage = int(np.clip(int(stage), 0, 2))

    @property
    def v15e_stage(self) -> int:
        """Compatibility alias: V1.5e reuses the V1.5d competence-stage machinery."""
        return int(self.v15d_stage)

    def v15d_stage_snapshot(self) -> dict:
        """Return the fixed competence-stage distribution (V1.5d/V1.5e)."""
        stage = int(np.clip(self.v15d_stage, 0, 2))
        if stage == 0:
            if self.v15e_mode:
                return {
                    "stage": 0,
                    "name": "A_BOOTSTRAP",
                    "easy_weight": 0.70,
                    "medium_weight": 0.25,
                    "hard_weight": 0.05,
                    "dr_probability": 0.20,
                    "dr_strength_min": 0.10,
                    "dr_strength_max": 0.30,
                }
            return {
                "stage": 0,
                "name": "A_BOOTSTRAP",
                "easy_weight": 0.60,
                "medium_weight": 0.30,
                "hard_weight": 0.10,
                "dr_probability": 0.30,
                "dr_strength_min": 0.10,
                "dr_strength_max": 0.35,
            }
        if stage == 1:
            return {
                "stage": 1,
                "name": "B_ROBUSTNESS",
                "easy_weight": 0.45,
                "medium_weight": 0.40,
                "hard_weight": 0.15,
                "dr_probability": 0.45,
                "dr_strength_min": 0.15,
                "dr_strength_max": 0.50,
            }
        return {
            "stage": 2,
            "name": "C_DEPLOYMENT",
            "easy_weight": 0.35,
            "medium_weight": 0.45,
            "hard_weight": 0.20,
            "dr_probability": 0.60,
            "dr_strength_min": 0.20,
            "dr_strength_max": 0.65,
        }

    def v15c_schedule_snapshot(self) -> dict:
        """Return the smooth V1.5c terrain/DR schedule at current progress.

        The schedule ramps only during the first 40% of a 1M run, then stays
        permanently anchored.  There is no abrupt 100k distribution jump.
        """
        if not self.v15c_mode:
            return {}

        # progress=0.40 corresponds to 400k in the intended 1M run.
        t = float(np.clip(self.curriculum_progress / 0.40, 0.0, 1.0))

        def lerp(a: float, b: float) -> float:
            return float(a + (b - a) * t)

        return {
            "ramp_fraction": t,
            "easy_weight": lerp(0.60, 0.35),
            "medium_weight": lerp(0.30, 0.45),
            "hard_weight": lerp(0.10, 0.20),
            "dr_probability": lerp(0.40, 0.60),
            # Mild early sim variation; moderate final range.
            "dr_strength_min": lerp(0.10, 0.20),
            "dr_strength_max": lerp(0.35, 0.65),
        }

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

        if self.v15f_mode:
            sched = self.v15f_stage_snapshot()
            if self.dr_force_episode is not None:
                use_dr = bool(self.dr_force_episode)
            else:
                use_dr = bool(self.np_random.random() < float(sched["dr_probability"]))
            if not use_dr:
                return
            self.dr_episode_enabled = True
            if self.dr_fixed_strength is not None:
                self.dr_strength = self.dr_fixed_strength
            else:
                self.dr_strength = float(self.np_random.uniform(float(sched["dr_strength_min"]), float(sched["dr_strength_max"])))
        elif self.v15d_mode or self.v15e_mode:
            sched = self.v15d_stage_snapshot()
            if self.dr_force_episode is not None:
                use_dr = bool(self.dr_force_episode)
            else:
                use_dr = bool(
                    self.np_random.random() < float(sched["dr_probability"])
                )

            if not use_dr:
                return

            self.dr_episode_enabled = True
            if self.dr_fixed_strength is not None:
                self.dr_strength = self.dr_fixed_strength
            else:
                self.dr_strength = float(
                    self.np_random.uniform(
                        float(sched["dr_strength_min"]),
                        float(sched["dr_strength_max"]),
                    )
                )

        elif self.v15c_mode:
            sched = self.v15c_schedule_snapshot()
            if self.dr_force_episode is not None:
                use_dr = bool(self.dr_force_episode)
            else:
                use_dr = bool(
                    self.np_random.random() < float(sched["dr_probability"])
                )

            if not use_dr:
                return

            self.dr_episode_enabled = True
            if self.dr_fixed_strength is not None:
                self.dr_strength = self.dr_fixed_strength
            else:
                self.dr_strength = float(
                    self.np_random.uniform(
                        float(sched["dr_strength_min"]),
                        float(sched["dr_strength_max"]),
                    )
                )

        elif self.v15b_mode:
            if self.dr_force_episode is not None:
                use_dr = bool(self.dr_force_episode)
            else:
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
            "v15c_mode": bool(self.v15c_mode),
            "v15d_mode": bool(self.v15d_mode),
            "v15e_mode": bool(self.v15e_mode),
            "v15d_stage": int(self.v15d_stage),
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

        if self.v15f_mode:
            sched = self.v15f_stage_snapshot()
            weights = {"easy": float(sched["easy_weight"]), "medium": float(sched["medium_weight"]), "hard": float(sched["hard_weight"])}
            available = [(d,w) for d,w in weights.items() if w > 0.0 and self.terrain_groups.get(d)]
            if not available:
                return self.terrain_paths[int(self.np_random.integers(len(self.terrain_paths)))]
            probs = np.asarray([w for _,w in available], dtype=np.float64); probs /= probs.sum()
            preferred = available[int(self.np_random.choice(len(available), p=probs))][0]
            pool = self.terrain_groups[preferred]
            return pool[int(self.np_random.integers(len(pool)))]

        if self.v15d_mode or self.v15e_mode:
            sched = self.v15d_stage_snapshot()
            weights = {
                "easy": float(sched["easy_weight"]),
                "medium": float(sched["medium_weight"]),
                "hard": float(sched["hard_weight"]),
            }

            available = [(d, w) for d, w in weights.items() if self.terrain_groups.get(d)]
            if not available:
                return self.terrain_paths[int(self.np_random.integers(len(self.terrain_paths)))]
            probs = np.asarray([w for _, w in available], dtype=np.float64)
            probs /= probs.sum()
            idx = int(self.np_random.choice(len(available), p=probs))
            preferred = available[idx][0]
            pool = self.terrain_groups[preferred]
            return pool[int(self.np_random.integers(len(pool)))]

        if self.v15c_mode:
            sched = self.v15c_schedule_snapshot()
            weights = {
                "easy": float(sched["easy_weight"]),
                "medium": float(sched["medium_weight"]),
                "hard": float(sched["hard_weight"]),
            }

            available = [(d, w) for d, w in weights.items() if self.terrain_groups.get(d)]
            if not available:
                return self.terrain_paths[int(self.np_random.integers(len(self.terrain_paths)))]
            probs = np.asarray([w for _, w in available], dtype=np.float64)
            probs /= probs.sum()
            idx = int(self.np_random.choice(len(available), p=probs))
            preferred = available[idx][0]
            pool = self.terrain_groups[preferred]
            return pool[int(self.np_random.integers(len(pool)))]

        if self.v15b_mode:
            # V1.5b legacy anchored distribution retained for reproducibility.
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
        if self.v15f_mode:
            snap=self.v15f_stage_snapshot(); lo=float(snap["mission_min_m"]); hi=float(snap["mission_max_m"]); target=0.5*(lo+hi)
            best=None; best_xy=None; best_err=float("inf")
            for _ in range(220):
                a=pts[int(self.np_random.integers(len(pts)))]; b=pts[int(self.np_random.integers(len(pts)))]
                e=float(np.linalg.norm(a-b)*self.res)
                if e<0.55*lo or e>1.35*hi: continue
                cand=astar(self.cost,tuple(map(int,a)),tuple(map(int,b)))
                if not cand.success or len(cand.path)<8: continue
                xy=grid_to_world(cand.path,self.height.shape,self.res); L=_path_length_xy(xy); err=abs(L-target)
                if err<best_err: best,best_xy,best_err=cand,xy,err
                if lo<=L<=hi: best,best_xy=cand,xy; break
            if best is None:
                for _ in range(120):
                    a=pts[int(self.np_random.integers(len(pts)))]; b=pts[int(self.np_random.integers(len(pts)))]
                    cand=astar(self.cost,tuple(map(int,a)),tuple(map(int,b)))
                    if not cand.success or len(cand.path)<8: continue
                    xy=grid_to_world(cand.path,self.height.shape,self.res); L=_path_length_xy(xy); err=abs(L-target)
                    if err<best_err: best,best_xy,best_err=cand,xy,err
            if best is None: raise RuntimeError(f"V1.5f could not generate reachable mission on {self.current_terrain_path}")
            plan=best; self.path_xy=np.asarray(best_xy,dtype=np.float64)
        else:
            min_sep=0.30*math.hypot(rows,cols); plan=None
            for _ in range(60):
                a=pts[int(self.np_random.integers(len(pts)))]; b=pts[int(self.np_random.integers(len(pts)))]
                if np.linalg.norm(a-b)<min_sep: continue
                cand=astar(self.cost,tuple(map(int,a)),tuple(map(int,b)))
                if cand.success and len(cand.path)>=12: plan=cand; break
            if plan is None:
                a=pts[0]; d=np.linalg.norm(pts-a,axis=1); b=pts[int(np.argmax(d))]; plan=astar(self.cost,tuple(map(int,a)),tuple(map(int,b)))
                if not plan.success: raise RuntimeError(f"Could not generate reachable start/goal on {self.current_terrain_path}")
            self.path_xy=grid_to_world(plan.path,self.height.shape,self.res)
        self.start_rc=tuple(map(int,plan.path[0])); self.goal_rc=tuple(map(int,plan.path[-1])); self.path_rc=plan.path
        self.start_xy=self.path_xy[0].astype(np.float64); self.goal_xy=self.path_xy[-1].astype(np.float64); self.reference_path_length_m=_path_length_xy(self.path_xy)
        if len(self.path_xy)>=2:
            seg=np.linalg.norm(np.diff(self.path_xy,axis=0),axis=1); self.path_cumulative_m=np.concatenate([np.array([0.0],dtype=np.float64),np.cumsum(seg,dtype=np.float64)]); total=float(self.path_cumulative_m[-1]); self.path_remaining_m=(total-self.path_cumulative_m).astype(np.float64)
        elif len(self.path_xy)==1:
            self.path_cumulative_m=np.array([0.0],dtype=np.float64); self.path_remaining_m=np.array([0.0],dtype=np.float64)
        else:
            self.path_cumulative_m=np.empty(0,dtype=np.float64); self.path_remaining_m=np.empty(0,dtype=np.float64)
        if self.clearance_from_footprint_m is not None and len(plan.path):
            vals=self.clearance_from_footprint_m[plan.path[:,0],plan.path[:,1]]; self.reference_min_clearance_m=float(np.min(vals)) if np.isfinite(vals).any() else float("inf")
        else: self.reference_min_clearance_m=float("inf")
        mask=np.zeros(self.height.shape,dtype=bool)
        if len(self.path_rc): mask[self.path_rc[:,0],self.path_rc[:,1]]=True
        self.path_mask=binary_dilation(mask,iterations=1).astype(np.float32)

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


    def _route_guidance_features(
        self,
        pos_xy: np.ndarray,
        yaw: float,
        lookahead_m: float | None = None,
        update_index: bool = True,
    ) -> tuple[float, float, float, float, float, np.ndarray, int]:
        """Continuous local A* projection and moving-lookahead guidance.

        Returns:
          route_s_m            : continuous arc-length coordinate along A* route
          route_remaining_m    : A* arc length still remaining
          cross_track_m        : distance from rover to projected route segment
          heading_error_rad    : bearing to moving lookahead point minus rover yaw
          target_steer         : normalized desired steer [-1,1], positive-left
          tangent_xy           : unit local A* tangent at projection
          segment_index        : projected route segment index

        V1.5e uses the same moving lookahead bearing in state[1:3] AND in reward
        shaping. This removes the V1.5d hidden-target contradiction while keeping
        the full global route hidden: route image channel and state[12:14] stay zero.
        """
        if lookahead_m is None:
            lookahead_m = float(self.v15e_guidance_lookahead_m)

        if len(self.path_xy) < 2 or len(self.path_cumulative_m) != len(self.path_xy):
            goal_vec = self.goal_xy - pos_xy
            dist = float(np.linalg.norm(goal_vec))
            tangent = goal_vec / max(dist, 1e-9)
            desired = math.atan2(float(tangent[1]), float(tangent[0])) if dist > 1e-9 else yaw
            err = _wrap(desired - yaw)
            target = float(np.clip(err / max(self.max_steer_rad, 1e-6), -1.0, 1.0))
            return 0.0, dist, 0.0, err, target, tangent.astype(np.float64), 0

        nseg = len(self.path_xy) - 1
        center = int(np.clip(self.reward_path_index, 0, nseg - 1))
        lo = max(0, center - 10)
        hi = min(nseg, center + 100)
        if hi <= lo:
            lo, hi = 0, nseg

        best = None
        for i in range(lo, hi):
            a = self.path_xy[i]
            b = self.path_xy[i + 1]
            ab = b - a
            seg2 = float(np.dot(ab, ab))
            if seg2 <= 1e-12:
                continue
            t = float(np.clip(np.dot(pos_xy - a, ab) / seg2, 0.0, 1.0))
            q = a + t * ab
            d = float(np.linalg.norm(pos_xy - q))
            seg_len = math.sqrt(seg2)
            s = float(self.path_cumulative_m[i] + t * seg_len)
            # Prefer the farther-along projection for near-ties, which avoids
            # oscillating between adjacent segments at corners.
            key = (d, -s)
            if best is None or key < best[0]:
                best = (key, i, t, q, seg_len, s, ab / seg_len)

        if best is None:
            goal_vec = self.goal_xy - pos_xy
            dist = float(np.linalg.norm(goal_vec))
            tangent = goal_vec / max(dist, 1e-9)
            desired = math.atan2(float(tangent[1]), float(tangent[0])) if dist > 1e-9 else yaw
            err = _wrap(desired - yaw)
            target = float(np.clip(err / max(self.max_steer_rad, 1e-6), -1.0, 1.0))
            return 0.0, dist, 0.0, err, target, tangent.astype(np.float64), 0

        _, i, t, q, seg_len, route_s, tangent = best
        if update_index:
            self.reward_path_index = int(i)

        total = float(self.path_cumulative_m[-1])
        route_remaining = max(0.0, total - route_s)
        cross_track = float(np.linalg.norm(pos_xy - q))

        target_s = min(total, route_s + max(1.0, float(lookahead_m)))
        j = int(np.searchsorted(self.path_cumulative_m, target_s, side="right") - 1)
        j = int(np.clip(j, 0, nseg - 1))
        a2 = self.path_xy[j]
        b2 = self.path_xy[j + 1]
        seg2b = float(np.linalg.norm(b2 - a2))
        if seg2b <= 1e-9:
            lookahead_xy = b2
        else:
            frac = float(np.clip(
                (target_s - float(self.path_cumulative_m[j])) / seg2b, 0.0, 1.0
            ))
            lookahead_xy = a2 + frac * (b2 - a2)

        look_vec = lookahead_xy - pos_xy
        if np.linalg.norm(look_vec) > 1e-6:
            desired = math.atan2(float(look_vec[1]), float(look_vec[0]))
        else:
            desired = math.atan2(float(tangent[1]), float(tangent[0]))
        heading_error = _wrap(desired - yaw)
        target_steer = float(
            np.clip(heading_error / max(self.max_steer_rad, 1e-6), -1.0, 1.0)
        )
        return (
            float(route_s), float(route_remaining), cross_track,
            float(heading_error), target_steer,
            np.asarray(tangent, dtype=np.float64), int(i),
        )

    # Backward-compatible alias used by V1.5d metrics/code paths.
    def _route_reward_features(
        self,
        pos_xy: np.ndarray,
        yaw: float,
        lookahead_m: float = 4.0,
    ) -> tuple[float, float, float, float, int]:
        s, remaining, dev, err, target, _tangent, idx = self._route_guidance_features(
            pos_xy, yaw, lookahead_m=lookahead_m, update_index=True
        )
        # Preserve V1.5d's old signature: remaining, deviation, error, steer, idx.
        return remaining, dev, err, target, idx

    def v15f_teacher_action(self) -> np.ndarray:
        if not self.v15f_mode: raise RuntimeError("v15f_teacher_action requires v15f_mode=True")
        pos,_r,_p,yaw,lin,_ang=self._rover_pose()
        _s,_rem,cross,_err,target,tangent,_idx=self._route_guidance_features(pos[:2],yaw,lookahead_m=self.v15e_guidance_lookahead_m,update_index=True)
        steer=float(np.clip(1.05*target,-1.0,1.0)); route_speed=float(np.dot(lin[:2],tangent)); clearance=self._clearance_to_obstacles(pos[:2])
        frac=0.80-0.43*abs(steer)-0.10*float(np.clip(cross/2.0,0.0,1.0))
        if np.isfinite(clearance) and clearance<1.25: frac*=float(np.clip(0.45+0.55*clearance/1.25,0.40,1.0))
        frac=float(np.clip(frac,0.30,0.82)); desired=frac*self.max_speed_mps
        throttle=float(np.clip(0.40+0.70*((desired-route_speed)/max(self.max_speed_mps,1e-6)),0.22,0.88))
        if self.v15e_stall_warning_active: throttle=min(throttle,0.30); steer=float(np.clip(1.20*target,-1.0,1.0))
        return np.asarray([steer,throttle],dtype=np.float32)

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

        # V1.5e mirrors the working Unity moving-goal semantics:
        # state[0] = FINAL mission distance, while state[1:3] = bearing to a
        # local A* moving lookahead point. The entire route itself remains hidden.
        if self.v15e_mode and self.action_mode == "continuous":
            _s, _rem, _dev, guidance_bearing, _steer, _tan, _idx = (
                self._route_guidance_features(
                    pos[:2], yaw, lookahead_m=self.v15e_guidance_lookahead_m,
                    update_index=False,
                )
            )
            rel_policy_target = float(guidance_bearing)
        else:
            rel_policy_target = float(rel_goal)

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
                math.sin(rel_policy_target),
                math.cos(rel_policy_target),
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

        if self.v15e_mode and len(self.path_xy) >= 2:
            # V1.5e is explicitly a moving-A*-waypoint-guided SAC architecture,
            # matching Unity. Start approximately tangent to the planned traverse
            # instead of giving observation/reward contradictory initial headings.
            target_s = min(
                float(self.path_cumulative_m[-1]),
                float(self.v15e_guidance_lookahead_m),
            )
            j = int(np.searchsorted(self.path_cumulative_m, target_s, side="right") - 1)
            j = int(np.clip(j, 0, len(self.path_xy) - 2))
            a = self.path_xy[j]
            b = self.path_xy[j + 1]
            seg_len = max(float(np.linalg.norm(b - a)), 1e-9)
            frac = float(np.clip(
                (target_s - float(self.path_cumulative_m[j])) / seg_len, 0.0, 1.0
            ))
            target_xy = a + frac * (b - a)
            initial = target_xy - self.start_xy
        else:
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

        # Reset A* guidance/reward state.
        self.reward_path_index = 0
        if self.v15e_mode:
            route_s, route_remaining, route_dev, route_err, route_target_steer, _tan, _ = (
                self._route_guidance_features(
                    self.last_pos_xy, yaw, lookahead_m=self.v15e_guidance_lookahead_m,
                    update_index=True,
                )
            )
            self.prev_route_s_m = float(route_s)
            self.last_route_s_m = float(route_s)
        else:
            route_remaining, route_dev, route_err, route_target_steer, _ = self._route_reward_features(
                self.last_pos_xy, yaw
            )
            self.prev_route_s_m = 0.0
            self.last_route_s_m = 0.0
        self.prev_route_remaining_m = float(route_remaining)
        self.initial_route_remaining_m = max(float(route_remaining), 1e-6)
        self.last_route_progress_m = 0.0
        self.last_route_deviation_m = float(route_dev)
        self.prev_route_deviation_m = float(route_dev)
        self.last_route_heading_error_rad = float(route_err)
        self.last_route_target_steer = float(route_target_steer)
        self.last_route_forward_speed_mps = 0.0
        self.route_progress_window.clear()
        self.route_progress_window.append(self.prev_route_s_m if self.v15e_mode else self.prev_route_remaining_m)
        self.route_deviation_window.clear()
        self.route_deviation_window.append(float(route_dev))
        self.last_route_window_progress_m = float("inf")
        self.last_route_window_cross_track_improvement_m = float("inf")
        self.route_positive_progress_steps = 0
        self.route_deviation_sum = 0.0
        self.route_steer_bonus_sum = 0.0
        self.route_velocity_bonus_sum = 0.0
        self.route_throttle_bonus_sum = 0.0
        self.route_forward_speed_sum = 0.0
        self.route_heading_potential_sum = 0.0
        self.prev_abs_route_heading_error_rad = abs(float(route_err))

        self.v15e_long_route_s_window.clear()
        self.v15e_long_route_s_window.append(float(self.prev_route_s_m))
        self.v15e_long_route_deviation_window.clear()
        self.v15e_long_route_deviation_window.append(float(route_dev))
        self.v15e_stall_warning_active = False
        self.v15e_stall_warning_count = 0
        self.v15e_recovery_phase_steps = 0
        self.v15e_first_stall_warning_step = -1
        self.v15e_last_short_route_progress_m = float("inf")
        self.v15e_last_short_cross_track_improvement_m = float("inf")
        self.v15e_last_long_route_progress_m = float("inf")
        self.v15e_last_long_cross_track_improvement_m = float("inf")
        self.last_stuck_diagnostics = None

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
            "v15d_stage": int(self.v15d_stage),
            "v15d_stage_info": self.v15d_stage_snapshot() if (self.v15d_mode or self.v15e_mode) else {},
            "v15e_mode": bool(self.v15e_mode),
            "v15f_mode": bool(self.v15f_mode),
            "v15f_stage_info": self.v15f_stage_snapshot() if self.v15f_mode else {},
            "initial_route_remaining_m": float(self.initial_route_remaining_m),
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

        if self.anti_loophole_mode and self.action_mode == "continuous":
            if self.v15e_mode:
                # -------------------------------------------------------------
                # V1.5e: OBSERVABLE moving-A* guidance + continuous arc progress.
                # state[1:3] already tells SAC the same local lookahead direction
                # used here, so reward and observation are no longer contradictory.
                # -------------------------------------------------------------
                (
                    route_s,
                    route_remaining,
                    route_deviation,
                    route_heading_error,
                    route_target_steer,
                    route_tangent,
                    _,
                ) = self._route_guidance_features(
                    pos_xy, yaw, lookahead_m=self.v15e_guidance_lookahead_m,
                    update_index=True,
                )

                route_progress = float(route_s - self.prev_route_s_m)
                self.prev_route_s_m = float(route_s)
                self.last_route_s_m = float(route_s)
                self.prev_route_remaining_m = float(route_remaining)
                self.last_route_progress_m = route_progress
                previous_deviation = float(self.prev_route_deviation_m)
                cross_track_improvement = previous_deviation - float(route_deviation)
                self.prev_route_deviation_m = float(route_deviation)
                self.last_route_deviation_m = float(route_deviation)
                self.last_route_heading_error_rad = float(route_heading_error)
                self.last_route_target_steer = float(route_target_steer)
                self.route_deviation_sum += float(route_deviation)
                if route_progress > 0.002:
                    self.route_positive_progress_steps += 1

                route_denom = max(float(self.reference_path_length_m), 1e-6)

                # Dominant potential: a complete feasible A* traverse ~= +400.
                reward = 400.0 * (route_progress / route_denom)

                # Tiny cross-track potential. Leaving the route costs and returning
                # repays it; oscillation cannot manufacture net reward.
                reward += 0.10 * float(np.clip(cross_track_improvement, -0.50, 0.50))

                # Tiny heading-error potential for LOCAL RECOVERY.
                # This rewards actual rover re-orientation toward the moving A*
                # waypoint rather than merely commanding a steering angle.
                # The term is deliberately tiny relative to the +400 route-progress
                # potential and approximately cancels under left/right oscillation.
                abs_heading_error = abs(float(route_heading_error))
                heading_improvement = (
                    float(self.prev_abs_route_heading_error_rad) - abs_heading_error
                )
                self.prev_abs_route_heading_error_rad = abs_heading_error
                heading_potential = self.v15e_heading_potential_scale * float(
                    np.clip(
                        heading_improvement / max(self.max_steer_rad, 1e-6),
                        -0.50,
                        0.50,
                    )
                )
                reward += heading_potential
                self.route_heading_potential_sum += heading_potential

                # Small secondary costs only.
                reward -= 0.004  # time
                reward -= 0.012 * slip
                reward -= 0.003 * float(np.dot(action_delta, action_delta))

                # Actual velocity along the A* tangent: this is the safe throttle
                # teaching signal. Merely commanding throttle against a rock earns 0.
                route_forward_speed = float(np.dot(lin[:2], route_tangent))
                self.last_route_forward_speed_mps = route_forward_speed
                self.route_forward_speed_sum += route_forward_speed
                speed_norm = float(np.clip(
                    route_forward_speed / max(self.max_speed_mps, 1e-6), 0.0, 1.0
                ))
                velocity_bonus = 0.015 * speed_norm
                reward += velocity_bonus
                self.route_velocity_bonus_sum += velocity_bonus

                # Correct steering gets only a small hint, and only while the rover
                # is truly advancing along the route. This prevents stationary wheel
                # turning from becoming an alternate objective.
                movement_gate = float(np.clip(route_forward_speed / 0.25, 0.0, 1.0))
                steer_error = abs(float(steer) - float(route_target_steer))
                steer_alignment = float(np.clip(1.0 - steer_error / 0.80, 0.0, 1.0))
                steer_bonus = 0.010 * movement_gate * steer_alignment
                reward += steer_bonus
                self.route_steer_bonus_sum += steer_bonus

                # Mild footprint danger cost only when genuinely close.
                if np.isfinite(clearance_m) and clearance_m < 0.50:
                    q = float(np.clip((0.50 - clearance_m) / 0.50, 0.0, 1.0))
                    reward -= 0.03 * q * q

                # -------------------------------------------------------------
                # V1.5e Hotfix 3 two-stage stall/recovery detector.
                #
                # 14.4 s:
                #   detect a stall and enter a RECOVERY GRACE period, but DO NOT
                #   terminate. SAC can reverse, re-orient or rejoin the route.
                #
                # ~24 s:
                #   terminate as stuck only if BOTH A* arc progress AND cross-track
                #   recovery are still insufficient over the longer interval.
                # -------------------------------------------------------------
                self.route_progress_window.append(float(route_s))
                self.route_deviation_window.append(float(route_deviation))
                self.v15e_long_route_s_window.append(float(route_s))
                self.v15e_long_route_deviation_window.append(float(route_deviation))

                no_progress_failure = False
                short_stall = False

                if (
                    len(self.route_progress_window) >= self.v15e_stall_warning_steps + 1
                    and len(self.route_deviation_window) >= self.v15e_stall_warning_steps + 1
                ):
                    self.last_route_window_progress_m = float(
                        self.route_progress_window[-1] - self.route_progress_window[0]
                    )
                    self.last_route_window_cross_track_improvement_m = float(
                        self.route_deviation_window[0] - self.route_deviation_window[-1]
                    )
                    self.v15e_last_short_route_progress_m = (
                        self.last_route_window_progress_m
                    )
                    self.v15e_last_short_cross_track_improvement_m = (
                        self.last_route_window_cross_track_improvement_m
                    )
                    short_stall = bool(
                        route_remaining > 3.0
                        and self.last_route_window_progress_m
                            < self.v15e_stall_route_required_m
                        and self.last_route_window_cross_track_improvement_m
                            < self.v15e_stall_cross_track_required_m
                    )

                # Count transitions INTO a stall warning, not every stalled step.
                if short_stall:
                    if not self.v15e_stall_warning_active:
                        self.v15e_stall_warning_count += 1
                        if self.v15e_first_stall_warning_step < 0:
                            self.v15e_first_stall_warning_step = int(self.episode_steps)
                    self.v15e_stall_warning_active = True
                    self.v15e_recovery_phase_steps += 1
                else:
                    # Successful movement/rejoining exits recovery naturally.
                    self.v15e_stall_warning_active = False

                if (
                    len(self.v15e_long_route_s_window)
                        >= self.v15e_stuck_total_steps + 1
                    and len(self.v15e_long_route_deviation_window)
                        >= self.v15e_stuck_total_steps + 1
                ):
                    self.v15e_last_long_route_progress_m = float(
                        self.v15e_long_route_s_window[-1]
                        - self.v15e_long_route_s_window[0]
                    )
                    self.v15e_last_long_cross_track_improvement_m = float(
                        self.v15e_long_route_deviation_window[0]
                        - self.v15e_long_route_deviation_window[-1]
                    )
                    no_progress_failure = bool(
                        route_remaining > 3.0
                        and self.v15e_last_long_route_progress_m
                            < self.v15e_stuck_route_required_m
                        and self.v15e_last_long_cross_track_improvement_m
                            < self.v15e_stuck_cross_track_required_m
                    )

                collision = self._collision_with_rock()
                if collision:
                    self.collision_count += 1
                    reward -= 250.0
                    terminated = True
                    terminal_reason = "collision"

                if not terminated and (
                    abs(math.degrees(roll)) > 58.0
                    or abs(math.degrees(pitch)) > 58.0
                ):
                    self.rollover = True
                    reward -= 300.0
                    terminated = True
                    terminal_reason = "rollover"

                if not terminated and self._outside(pos_xy):
                    self.out_of_bounds = True
                    reward -= 275.0
                    terminated = True
                    terminal_reason = "out_of_bounds"

                if not terminated and goal_dist <= 2.2:
                    self.success = True
                    ref = max(float(self.reference_path_length_m), 1e-6)
                    efficiency = float(
                        np.clip(ref / max(self.distance_travelled, ref), 0.0, 1.0)
                    )
                    success_efficiency_bonus = 40.0 * efficiency
                    reward += 300.0 + success_efficiency_bonus
                    terminated = True
                    terminal_reason = "goal"

                if not terminated and no_progress_failure:
                    self.last_stuck_diagnostics = {
                        "episode_step": int(self.episode_steps),
                        "stall_warning_count": int(self.v15e_stall_warning_count),
                        "first_stall_warning_step": int(self.v15e_first_stall_warning_step),
                        "recovery_phase_steps": int(self.v15e_recovery_phase_steps),
                        "short_route_progress_m": float(
                            self.v15e_last_short_route_progress_m
                        ),
                        "short_cross_track_improvement_m": float(
                            self.v15e_last_short_cross_track_improvement_m
                        ),
                        "long_route_progress_m": float(
                            self.v15e_last_long_route_progress_m
                        ),
                        "long_cross_track_improvement_m": float(
                            self.v15e_last_long_cross_track_improvement_m
                        ),
                        "route_heading_error_deg": float(
                            math.degrees(route_heading_error)
                        ),
                        "route_forward_speed_mps": float(route_forward_speed),
                        "throttle": float(throttle),
                        "steer": float(steer),
                        "slip": float(slip),
                        "wheel_contacts": int(wheel_contacts),
                        "clearance_m": float(clearance_m)
                            if np.isfinite(clearance_m) else float("nan"),
                        "route_deviation_m": float(route_deviation),
                        "route_remaining_m": float(route_remaining),
                    }
                    reward -= 225.0
                    terminated = True
                    terminal_reason = "stuck"

                if not terminated and self.episode_steps >= self.current_episode_step_limit:
                    reward -= 225.0
                    truncated = True
                    terminal_reason = "time_limit"

            elif self.v15d_mode:
                # -------------------------------------------------------------
                # V1.5d reward: HIDDEN A* feasible-route potential.
                #
                # The policy still receives NO route input. A* is used only by
                # the training reward and no-progress detector so proper curved
                # obstacle detours can earn progress even when straight-line
                # goal distance temporarily fails to improve.
                # -------------------------------------------------------------
                (
                    route_remaining,
                    route_deviation,
                    route_heading_error,
                    route_target_steer,
                    _,
                ) = self._route_reward_features(pos_xy, yaw)

                route_progress = float(self.prev_route_remaining_m - route_remaining)
                self.prev_route_remaining_m = float(route_remaining)
                self.last_route_progress_m = route_progress
                self.last_route_deviation_m = float(route_deviation)
                self.last_route_heading_error_rad = float(route_heading_error)
                self.last_route_target_steer = float(route_target_steer)
                self.route_deviation_sum += float(route_deviation)
                if route_progress > 0.002:
                    self.route_positive_progress_steps += 1

                route_denom = max(self.initial_route_remaining_m, 1e-6)

                # Dominant signal: completing the feasible A* route is worth
                # approximately +400 regardless of absolute mission length.
                reward = 400.0 * (route_progress / route_denom)

                # Tiny secondary costs only; none should compete with route progress.
                reward -= 0.005  # time
                wasted_motion = max(0.0, step_dist - max(route_progress, 0.0))
                reward -= 0.030 * wasted_motion
                reward -= 0.015 * slip
                reward -= 0.004 * float(np.dot(action_delta, action_delta))

                # Very small action-alignment bonuses. They are GATED by actual
                # positive route progress, so the rover cannot earn them by
                # steering correctly while stationary or holding throttle against
                # an obstacle.
                progress_gate = float(np.clip(route_progress / 0.030, 0.0, 1.0))

                steer_error = abs(float(steer) - float(route_target_steer))
                steer_alignment = float(np.clip(1.0 - steer_error / 0.75, 0.0, 1.0))
                steer_bonus = 0.012 * progress_gate * steer_alignment
                reward += steer_bonus
                self.route_steer_bonus_sum += steer_bonus

                heading_alignment = float(max(0.0, math.cos(route_heading_error)))
                throttle_bonus = (
                    0.010
                    * progress_gate
                    * max(float(throttle), 0.0)
                    * heading_alignment
                )
                reward += throttle_bonus
                self.route_throttle_bonus_sum += throttle_bonus

                # Mild footprint-danger cost only when genuinely close.
                if np.isfinite(clearance_m) and clearance_m < 0.50:
                    q = float(np.clip((0.50 - clearance_m) / 0.50, 0.0, 1.0))
                    reward -= 0.03 * q * q

                # No-progress is now defined by feasible ROUTE progress rather than
                # Euclidean goal progress. Sideways/curved A* detours therefore do
                # not look like "stuck" behavior.
                self.route_progress_window.append(float(route_remaining))
                no_progress_failure = False
                if len(self.route_progress_window) >= self.no_progress_window_steps + 1:
                    self.last_route_window_progress_m = float(
                        self.route_progress_window[0] - self.route_progress_window[-1]
                    )
                    no_progress_failure = bool(
                        route_remaining > 3.0
                        and self.last_route_window_progress_m < self.no_progress_required_m
                    )

                collision = self._collision_with_rock()
                if collision:
                    self.collision_count += 1
                    reward -= 250.0
                    terminated = True
                    terminal_reason = "collision"

                if not terminated and (
                    abs(math.degrees(roll)) > 58.0
                    or abs(math.degrees(pitch)) > 58.0
                ):
                    self.rollover = True
                    reward -= 300.0
                    terminated = True
                    terminal_reason = "rollover"

                if not terminated and self._outside(pos_xy):
                    self.out_of_bounds = True
                    reward -= 275.0
                    terminated = True
                    terminal_reason = "out_of_bounds"

                if not terminated and goal_dist <= 2.2:
                    self.success = True
                    ref = max(float(self.reference_path_length_m), 1e-6)
                    efficiency = float(
                        np.clip(ref / max(self.distance_travelled, ref), 0.0, 1.0)
                    )
                    success_efficiency_bonus = 40.0 * efficiency
                    reward += 300.0 + success_efficiency_bonus
                    terminated = True
                    terminal_reason = "goal"

                if not terminated and no_progress_failure:
                    reward -= 225.0
                    terminated = True
                    terminal_reason = "stuck"

                if not terminated and self.episode_steps >= self.current_episode_step_limit:
                    reward -= 225.0
                    truncated = True
                    terminal_reason = "time_limit"

            else:
                # -------------------------------------------------------------
                # V1.5b/V1.5c reward retained for backward compatibility.
                # -------------------------------------------------------------
                denom = max(self.initial_goal_distance_m, 1e-6)
                progress_scale = 400.0 if self.v15c_mode else 300.0
                reward = progress_scale * (progress / denom)

                reward -= 0.005
                wasted_motion = max(0.0, step_dist - max(progress, 0.0))
                reward -= 0.20 * wasted_motion
                reward -= 0.015 * slip
                reward -= 0.005 * float(np.dot(action_delta, action_delta))

                if np.isfinite(clearance_m) and clearance_m < 0.50:
                    q = float(np.clip((0.50 - clearance_m) / 0.50, 0.0, 1.0))
                    reward -= 0.03 * q * q

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

                collision = self._collision_with_rock()
                if collision:
                    self.collision_count += 1
                    reward -= 250.0 if self.v15c_mode else (
                        220.0 + 120.0 * float(np.clip(goal_dist / denom, 0.0, 1.0))
                    )
                    terminated = True
                    terminal_reason = "collision"

                if not terminated and (
                    abs(math.degrees(roll)) > 58.0
                    or abs(math.degrees(pitch)) > 58.0
                ):
                    self.rollover = True
                    reward -= 300.0 if self.v15c_mode else (
                        280.0 + 120.0 * float(np.clip(goal_dist / denom, 0.0, 1.0))
                    )
                    terminated = True
                    terminal_reason = "rollover"

                if not terminated and self._outside(pos_xy):
                    self.out_of_bounds = True
                    reward -= 275.0 if self.v15c_mode else (
                        250.0 + 120.0 * float(np.clip(goal_dist / denom, 0.0, 1.0))
                    )
                    terminated = True
                    terminal_reason = "out_of_bounds"

                if not terminated and goal_dist <= 2.2:
                    self.success = True
                    ref = max(float(self.reference_path_length_m), 1e-6)
                    efficiency = float(
                        np.clip(ref / max(self.distance_travelled, ref), 0.0, 1.0)
                    )
                    success_efficiency_bonus = 40.0 * efficiency
                    reward += (300.0 if self.v15c_mode else 250.0) + success_efficiency_bonus
                    terminated = True
                    terminal_reason = "goal"

                if not terminated and no_progress_failure:
                    reward -= 225.0 if self.v15c_mode else (
                        180.0 + 120.0 * float(np.clip(goal_dist / denom, 0.0, 1.0))
                    )
                    terminated = True
                    terminal_reason = "stuck"

                if not terminated and self.episode_steps >= self.current_episode_step_limit:
                    reward -= 225.0 if self.v15c_mode else (
                        180.0 + 120.0 * float(np.clip(goal_dist / denom, 0.0, 1.0))
                    )
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
            "v15c_mode": bool(self.v15c_mode),
            "v15d_mode": bool(self.v15d_mode),
            "v15e_mode": bool(self.v15e_mode),
            "v15d_stage": int(self.v15d_stage),
            "dr_episode_enabled": bool(self.dr_episode_enabled),
            "dr_strength": float(self.dr_strength),
            "goal_progress_window_m": float(self.last_window_progress_m),
            "route_progress_window_m": float(self.last_route_window_progress_m),
            "route_cross_track_improvement_window_m": float(
                self.last_route_window_cross_track_improvement_m
            ),
            "route_progress_m": float(self.last_route_progress_m),
            "route_forward_speed_mps": float(self.last_route_forward_speed_mps),
            "route_deviation_m": float(self.last_route_deviation_m),
            "route_heading_error_deg": float(math.degrees(self.last_route_heading_error_rad)),
            "route_target_steer": float(self.last_route_target_steer),
            "route_remaining_m": float(self.prev_route_remaining_m),
            "v15e_stall_warning_active": bool(self.v15e_stall_warning_active),
            "v15e_stall_warning_count": int(self.v15e_stall_warning_count),
            "v15e_recovery_phase_steps": int(self.v15e_recovery_phase_steps),
            "v15e_short_route_progress_m": float(self.v15e_last_short_route_progress_m),
            "v15e_long_route_progress_m": float(self.v15e_last_long_route_progress_m),
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
                "reward_profile": (
                    "v15e" if self.v15e_mode else
                    "v15d" if self.v15d_mode else
                    ("v15c" if self.v15c_mode else
                     ("v15b" if self.v15b_mode else "legacy"))
                ),
                "v15d_stage": int(self.v15d_stage),
                "dr_episode_enabled": int(self.dr_episode_enabled),
                "dr_strength": float(self.dr_strength),
                "goal_progress_window_m": float(self.last_window_progress_m),
                "route_progress_window_m": float(self.last_route_window_progress_m),
                "success_efficiency_bonus": float(success_efficiency_bonus),
                "initial_route_remaining_m": float(self.initial_route_remaining_m),
                "final_route_remaining_m": float(self.prev_route_remaining_m),
                "route_progress_m": float(
                    self.initial_route_remaining_m - self.prev_route_remaining_m
                ),
                "route_progress": float(np.clip(
                    (self.initial_route_remaining_m - self.prev_route_remaining_m)
                    / max(self.initial_route_remaining_m, 1e-6),
                    -1.0, 1.0
                )),
                "mean_route_deviation_m": float(
                    self.route_deviation_sum / max(1, self.episode_steps)
                ),
                "mean_route_steer_bonus": float(
                    self.route_steer_bonus_sum / max(1, self.episode_steps)
                ),
                "mean_route_throttle_bonus": float(
                    self.route_throttle_bonus_sum / max(1, self.episode_steps)
                ),
                "positive_route_progress_fraction": float(
                    self.route_positive_progress_steps / max(1, self.episode_steps)
                ),
                "last_route_forward_speed_mps": float(self.last_route_forward_speed_mps),
                "mean_route_forward_speed_mps": float(
                    self.route_forward_speed_sum / max(1, self.episode_steps)
                ),
                "route_velocity_bonus_total": float(self.route_velocity_bonus_sum),
                "route_heading_potential_total": float(self.route_heading_potential_sum),
                "route_cross_track_improvement_window_m": float(
                    self.last_route_window_cross_track_improvement_m
                ),
                "stall_warning_count": int(self.v15e_stall_warning_count),
                "recovery_phase_steps": int(self.v15e_recovery_phase_steps),
                "first_stall_warning_step": int(self.v15e_first_stall_warning_step),
                "stuck_short_route_progress_m": float(
                    self.v15e_last_short_route_progress_m
                ),
                "stuck_short_cross_track_improvement_m": float(
                    self.v15e_last_short_cross_track_improvement_m
                ),
                "stuck_long_route_progress_m": float(
                    self.v15e_last_long_route_progress_m
                ),
                "stuck_long_cross_track_improvement_m": float(
                    self.v15e_last_long_cross_track_improvement_m
                ),
                "stuck_route_heading_error_deg": float(
                    self.last_stuck_diagnostics.get("route_heading_error_deg", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
                "stuck_route_forward_speed_mps": float(
                    self.last_stuck_diagnostics.get("route_forward_speed_mps", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
                "stuck_throttle": float(
                    self.last_stuck_diagnostics.get("throttle", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
                "stuck_steer": float(
                    self.last_stuck_diagnostics.get("steer", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
                "stuck_slip": float(
                    self.last_stuck_diagnostics.get("slip", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
                "stuck_wheel_contacts": float(
                    self.last_stuck_diagnostics.get("wheel_contacts", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
                "stuck_clearance_m": float(
                    self.last_stuck_diagnostics.get("clearance_m", float("nan"))
                    if isinstance(self.last_stuck_diagnostics, dict)
                    else float("nan")
                ),
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
