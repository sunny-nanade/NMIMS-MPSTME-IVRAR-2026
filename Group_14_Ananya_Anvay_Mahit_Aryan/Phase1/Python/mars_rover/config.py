from dataclasses import dataclass

@dataclass(frozen=True)
class TerrainConfig:
    size: int = 129
    meters_per_cell: float = 1.0
    max_slope_deg: float = 30.0
    slope_weight: float = 0.08
    roughness_weight: float = 2.0
    obstacle_cost: float = 1e9

@dataclass(frozen=True)
class RoverConfig:
    mars_gravity: float = 3.71
    wheel_radius: float = 0.17775
    track_width: float = 0.57
    max_wheel_speed: float = 12.0
    max_force: float = 80.0
    base_speed_mps: float = 1.1
