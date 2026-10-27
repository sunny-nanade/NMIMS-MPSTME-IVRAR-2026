import argparse
import math
import os
import time
import numpy as np
from scipy.ndimage import map_coordinates
import pybullet as p
import pybullet_data

from mars_rover.config import TerrainConfig, RoverConfig
from mars_rover.terrain import (
    load_terrain,
    build_cost_map,
    nearest_safe,
    grid_to_world,
    sample_height_world,
    obstacle_components,
)
from mars_rover.planners import astar, hybrid_astar


parser = argparse.ArgumentParser()
parser.add_argument("--terrain", default="data/processed/procedural_mars.npz")
parser.add_argument("--planner", choices=["astar", "hybrid"], default="hybrid")
parser.add_argument("--physics-upsample", type=int, default=2, help="2 gives a smoother 257x257 physics mesh from a 129x129 DEM")
parser.add_argument("--no-sensors", action="store_true", help="disable rover RGB/depth/segmentation camera updates")
args = parser.parse_args()

TC = TerrainConfig()
RC = RoverConfig()
t = load_terrain(args.terrain)
height, obstacle, res = t["height"], t["obstacle"], t["meters_per_cell"]
cost, slope, _ = build_cost_map(height, obstacle, res, TC.max_slope_deg, TC.slope_weight, TC.roughness_weight)
rows, cols = cost.shape

# Pick spawn/goal sites that are not merely traversable, but relatively flat and clear.
start = nearest_safe(cost, slope, obstacle, (int(rows * .12), int(cols * .12)), max_spawn_slope_deg=10.0, clearance_cells=2)
goal = nearest_safe(cost, slope, obstacle, (int(rows * .86), int(cols * .86)), max_spawn_slope_deg=10.0, clearance_cells=2)
plan = astar(cost, start, goal) if args.planner == "astar" else hybrid_astar(cost, start, goal)
if not plan.success:
    raise RuntimeError("Planner could not find a path")
world_path = grid_to_world(plan.path, height.shape, res)


def upsample_heightfield(src, factor):
    factor = max(1, int(factor))
    if factor == 1:
        return src.astype(np.float32), res
    out_rows = (src.shape[0] - 1) * factor + 1
    out_cols = (src.shape[1] - 1) * factor + 1
    rr = np.linspace(0, src.shape[0] - 1, out_rows)
    cc = np.linspace(0, src.shape[1] - 1, out_cols)
    R, C = np.meshgrid(rr, cc, indexing="ij")
    out = map_coordinates(src, [R, C], order=3, mode="nearest").astype(np.float32)
    out_res = res * (src.shape[0] - 1) / (out_rows - 1)
    return out, float(out_res)


physics_height, physics_res = upsample_heightfield(height, args.physics_upsample)
ph_rows, ph_cols = physics_height.shape

cid = p.connect(p.GUI)
p.setAdditionalSearchPath(pybullet_data.getDataPath())
p.setGravity(0, 0, -RC.mars_gravity)
p.setTimeStep(1 / 240)
p.setPhysicsEngineParameter(numSolverIterations=120, enableConeFriction=1)
p.configureDebugVisualizer(p.COV_ENABLE_SHADOWS, 1)
p.configureDebugVisualizer(p.COV_ENABLE_WIREFRAME, 0)
p.configureDebugVisualizer(p.COV_ENABLE_RGB_BUFFER_PREVIEW, 0 if args.no_sensors else 1)
p.configureDebugVisualizer(p.COV_ENABLE_DEPTH_BUFFER_PREVIEW, 0 if args.no_sensors else 1)
p.configureDebugVisualizer(p.COV_ENABLE_SEGMENTATION_MARK_PREVIEW, 0 if args.no_sensors else 1)

# Bullet recenters heightfields around the midpoint of min/max height. Placing the
# terrain body at that midpoint restores world Z ~= source DEM Z.
terrain_mid_z = 0.5 * (float(physics_height.min()) + float(physics_height.max()))
flags = getattr(p, "GEOM_CONCAVE_INTERNAL_EDGE", 0)
terrain_shape = p.createCollisionShape(
    p.GEOM_HEIGHTFIELD,
    flags=flags,
    meshScale=[physics_res, physics_res, 1.0],
    heightfieldTextureScaling=(ph_rows - 1) / 2,
    heightfieldData=physics_height.ravel(order="C").tolist(),
    numHeightfieldRows=ph_rows,
    numHeightfieldColumns=ph_cols,
)
terrain_id = p.createMultiBody(baseMass=0, baseCollisionShapeIndex=terrain_shape, basePosition=[0, 0, terrain_mid_z])
p.changeVisualShape(terrain_id, -1, rgbaColor=[0.52, 0.22, 0.09, 1])
p.changeDynamics(terrain_id, -1, lateralFriction=1.05, restitution=0.0)

# Verify that the source DEM and Bullet heightfield agree at a handful of locations.
def physics_ground_z(x, y):
    ztop = float(height.max()) + 10.0
    zbot = float(height.min()) - 10.0
    hit = p.rayTest([x, y, ztop], [x, y, zbot])[0]
    if hit[0] < 0:
        return None
    return float(hit[3][2])

sample_errors = []
for frac in (0.2, 0.4, 0.6, 0.8):
    x = (frac - 0.5) * (cols - 1) * res
    y = ((1.0 - frac) - 0.5) * (rows - 1) * res
    pz = physics_ground_z(x, y)
    if pz is not None:
        sample_errors.append(abs(pz - sample_height_world(height, res, x, y)))
if sample_errors:
    print(f"Heightfield alignment check: mean |physics-source| = {np.mean(sample_errors):.3f} m, max = {np.max(sample_errors):.3f} m")

# Create one physical boulder per connected obstacle blob, rather than one sphere per obstacle cell.
rng = np.random.default_rng(4)
rock_mesh = os.path.join(pybullet_data.getDataPath(), "sphere_smooth.obj")
rocks = sorted(obstacle_components(obstacle, res), key=lambda d: d["cells"], reverse=True)[:32]
for rock in rocks:
    rc = np.array([[rock["row"], rock["col"]]], dtype=float)
    xy = grid_to_world(rc, height.shape, res)[0]
    x, y = float(xy[0]), float(xy[1])
    gz = sample_height_world(height, res, x, y)
    r = float(np.clip(rock["radius_m"], 0.45, 1.45))
    sx, sy, sz = r * rng.uniform(0.78, 1.12), r * rng.uniform(0.72, 1.05), r * rng.uniform(0.42, 0.72)
    yaw = rng.uniform(-math.pi, math.pi)
    orn = p.getQuaternionFromEuler([0, 0, yaw])
    color = [rng.uniform(0.24, 0.34), rng.uniform(0.10, 0.16), rng.uniform(0.05, 0.09), 1]

    if os.path.exists(rock_mesh):
        col = p.createCollisionShape(p.GEOM_MESH, fileName=rock_mesh, meshScale=[sx, sy, sz])
        vis = p.createVisualShape(p.GEOM_MESH, fileName=rock_mesh, meshScale=[sx, sy, sz], rgbaColor=color)
    else:
        col = p.createCollisionShape(p.GEOM_BOX, halfExtents=[sx, sy, sz])
        vis = p.createVisualShape(p.GEOM_BOX, halfExtents=[sx, sy, sz], rgbaColor=color)

    body = p.createMultiBody(0, col, vis, [x, y, gz + sz + 0.5], orn)
    # Drop the static mesh exactly onto the local terrain using its actual AABB.
    aabb_min, _ = p.getAABB(body, -1)
    pos, q = p.getBasePositionAndOrientation(body)
    p.resetBasePositionAndOrientation(body, [pos[0], pos[1], pos[2] + (gz + 0.015 - aabb_min[2])], q)

# Densify the planner path and place it close to the DEM surface.
path_color = [0.1, 0.65, 1.0] if args.planner == "astar" else [0.15, 1.0, 0.25]
for a, b in zip(world_path[:-1], world_path[1:]):
    seg_len = max(0.001, float(np.linalg.norm(b - a)))
    n = max(2, int(math.ceil(seg_len / 0.30)))
    pts = []
    for k in range(n + 1):
        u = k / n
        x, y = (1 - u) * a + u * b
        z = sample_height_world(height, res, float(x), float(y)) + 0.055
        pts.append([float(x), float(y), z])
    for q0, q1 in zip(pts[:-1], pts[1:]):
        p.addUserDebugLine(q0, q1, path_color, lineWidth=2.2, lifeTime=0)

# Start/goal markers are visual-only (no collision).
def marker(x, y, color, radius=0.23):
    z = sample_height_world(height, res, x, y)
    vis = p.createVisualShape(p.GEOM_SPHERE, radius=radius, rgbaColor=color)
    return p.createMultiBody(baseMass=0, baseCollisionShapeIndex=-1, baseVisualShapeIndex=vis, basePosition=[x, y, z + radius + 0.08])

sx, sy = map(float, world_path[0])
gx, gy = map(float, world_path[-1])
marker(sx, sy, [0.1, 0.65, 1.0, 1.0])
marker(gx, gy, [1.0, 0.25, 0.05, 1.0], radius=0.30)
p.addUserDebugText("START", [sx, sy, sample_height_world(height, res, sx, sy) + 0.7], [0.2, 0.75, 1], textSize=1.2)
p.addUserDebugText("GOAL", [gx, gy, sample_height_world(height, res, gx, gy) + 0.8], [1, 0.35, 0.05], textSize=1.2)

# Align rover yaw with the first path segment and spawn it just a few cm above contact.
if len(world_path) > 1:
    d0 = world_path[1] - world_path[0]
    spawn_yaw = math.atan2(float(d0[1]), float(d0[0]))
else:
    spawn_yaw = 0.0
spawn_ground = sample_height_world(height, res, sx, sy)
spawn_high = spawn_ground + 2.0
husky = p.loadURDF(
    "husky/husky.urdf",
    [sx, sy, spawn_high],
    p.getQuaternionFromEuler([0, 0, spawn_yaw]),
    flags=p.URDF_USE_INERTIA_FROM_FILE,
)

# Determine the true lowest point across every link, then place the wheels just above terrain.
def aggregate_bottom_z(body):
    mins = [p.getAABB(body, -1)[0][2]]
    for j in range(p.getNumJoints(body)):
        mins.append(p.getAABB(body, j)[0][2])
    return min(mins)

base_pos, base_orn = p.getBasePositionAndOrientation(husky)
bottom = aggregate_bottom_z(husky)
bottom_offset = base_pos[2] - bottom
p.resetBasePositionAndOrientation(husky, [sx, sy, spawn_ground + bottom_offset + 0.035], base_orn)
p.resetBaseVelocity(husky, [0, 0, 0], [0, 0, 0])

left = [2, 4]
right = [3, 5]
for j in left + right:
    p.changeDynamics(
        husky, j,
        lateralFriction=1.35,
        rollingFriction=0.02,
        spinningFriction=0.015,
        restitution=0.0,
        contactStiffness=35000,
        contactDamping=1200,
    )
p.changeDynamics(husky, -1, linearDamping=0.06, angularDamping=0.22, restitution=0.0)

MAX_FORCE = max(160.0, RC.max_force * 2.0)
BASE_SPEED = min(0.80, RC.base_speed_mps)
TRACK_WIDTH = RC.track_width
WHEEL_RADIUS = RC.wheel_radius


def wrap(a):
    return (a + math.pi) % (2 * math.pi) - math.pi


def wheels(vl, vr, force=MAX_FORCE):
    lv = float(np.clip(vl / WHEEL_RADIUS, -RC.max_wheel_speed, RC.max_wheel_speed))
    rv = float(np.clip(vr / WHEEL_RADIUS, -RC.max_wheel_speed, RC.max_wheel_speed))
    for j in left:
        p.setJointMotorControl2(husky, j, p.VELOCITY_CONTROL, targetVelocity=lv, force=force)
    for j in right:
        p.setJointMotorControl2(husky, j, p.VELOCITY_CONTROL, targetVelocity=rv, force=force)


# Let suspension/contact settle gently before driving.
wheels(0.0, 0.0, force=MAX_FORCE)
for _ in range(360):
    p.stepSimulation()
    time.sleep(1 / 720)

pos, orn = p.getBasePositionAndOrientation(husky)
roll0, pitch0, _ = p.getEulerFromQuaternion(orn)
if abs(math.degrees(roll0)) > 40 or abs(math.degrees(pitch0)) > 40:
    print("Rover settled badly; resetting to the safe start pose.")
    p.resetBasePositionAndOrientation(husky, [sx, sy, spawn_ground + bottom_offset + 0.04], p.getQuaternionFromEuler([0, 0, spawn_yaw]))
    p.resetBaseVelocity(husky, [0, 0, 0], [0, 0, 0])
    for _ in range(240):
        wheels(0.0, 0.0)
        p.stepSimulation()

# Initial overview camera only. We deliberately do NOT reset it every frame in free mode.
terrain_span = max((rows - 1) * res, (cols - 1) * res)
p.resetDebugVisualizerCamera(min(55.0, max(18.0, terrain_span * 0.38)), 45, -50, [sx, sy, spawn_ground])

camera_mode = "free"  # free, chase, top, overview
sensor_counter = 0
status_text_id = -1


def update_sensor_camera():
    pos, orn = p.getBasePositionAndOrientation(husky)
    yaw = p.getEulerFromQuaternion(orn)[2]
    fwd = np.array([math.cos(yaw), math.sin(yaw), 0.0])
    eye = np.array(pos) + 0.20 * fwd + np.array([0, 0, 0.55])
    target = eye + 6.0 * fwd + np.array([0, 0, -0.35])
    view = p.computeViewMatrix(eye.tolist(), target.tolist(), [0, 0, 1])
    proj = p.computeProjectionMatrixFOV(fov=62, aspect=4 / 3, nearVal=0.08, farVal=40.0)
    p.getCameraImage(
        320, 240,
        viewMatrix=view,
        projectionMatrix=proj,
        renderer=p.ER_BULLET_HARDWARE_OPENGL,
        flags=p.ER_SEGMENTATION_MASK_OBJECT_AND_LINKINDEX,
        shadow=1,
        lightDirection=[-1, -1, -2],
    )


def update_view(mode, pos, yaw):
    if mode == "free":
        return
    if mode == "chase":
        p.resetDebugVisualizerCamera(8.5, math.degrees(yaw) - 90.0, -27, pos)
    elif mode == "top":
        p.resetDebugVisualizerCamera(22.0, 0, -89.0, pos)
    elif mode == "overview":
        p.resetDebugVisualizerCamera(min(70.0, max(25.0, terrain_span * 0.55)), 45, -58, [0, 0, float(np.mean(height))])


def choose_lookahead(pos_xy, last_idx, lookahead_m=2.3):
    # Re-lock to a nearby path point if the rover drifted away, then target a point ahead.
    hi = min(len(world_path), last_idx + 24)
    local = world_path[last_idx:hi]
    if len(local):
        nearest_rel = int(np.argmin(np.linalg.norm(local - pos_xy, axis=1)))
        last_idx += nearest_rel
    target_idx = last_idx
    accum = 0.0
    while target_idx < len(world_path) - 1 and accum < lookahead_m:
        accum += float(np.linalg.norm(world_path[target_idx + 1] - world_path[target_idx]))
        target_idx += 1
    return last_idx, target_idx


print("\nLive PyBullet demo running.")
print("Controls: 1=FREE camera, 2=CHASE, 3=TOP, 4=OVERVIEW, F=toggle FREE/CHASE, R=reset rover, ESC/close window=exit")
print("In FREE mode PyBullet's normal mouse camera controls are no longer overwritten by the script.")
print("RGB/depth/segmentation previews update from the rover's forward synthetic camera unless --no-sensors is used.\n")

path_idx = 0
frame = 0
while p.isConnected():
    keys = p.getKeyboardEvents()
    if keys.get(ord("1"), 0) & p.KEY_WAS_TRIGGERED:
        camera_mode = "free"
    if keys.get(ord("2"), 0) & p.KEY_WAS_TRIGGERED:
        camera_mode = "chase"
    if keys.get(ord("3"), 0) & p.KEY_WAS_TRIGGERED:
        camera_mode = "top"
    if keys.get(ord("4"), 0) & p.KEY_WAS_TRIGGERED:
        camera_mode = "overview"
    if keys.get(ord("f"), 0) & p.KEY_WAS_TRIGGERED:
        camera_mode = "free" if camera_mode != "free" else "chase"
    if keys.get(ord("r"), 0) & p.KEY_WAS_TRIGGERED:
        p.resetBasePositionAndOrientation(husky, [sx, sy, spawn_ground + bottom_offset + 0.04], p.getQuaternionFromEuler([0, 0, spawn_yaw]))
        p.resetBaseVelocity(husky, [0, 0, 0], [0, 0, 0])
        path_idx = 0

    pos, orn = p.getBasePositionAndOrientation(husky)
    roll, pitch, yaw = p.getEulerFromQuaternion(orn)
    pos_xy = np.asarray(pos[:2], dtype=float)

    path_idx, target_idx = choose_lookahead(pos_xy, path_idx)
    target = world_path[target_idx]
    dx, dy = float(target[0] - pos[0]), float(target[1] - pos[1])
    dist_target = math.hypot(dx, dy)
    err = wrap(math.atan2(dy, dx) - yaw)

    # Stable pure-pursuit-like skid steering. Turn nearly in place for large heading errors.
    if abs(err) > math.radians(75):
        v = 0.03
    else:
        v = BASE_SPEED * max(0.28, math.cos(err))
    omega = float(np.clip(1.55 * err, -1.35, 1.35))
    vl = v - omega * TRACK_WIDTH / 2
    vr = v + omega * TRACK_WIDTH / 2

    goal_dist = float(np.linalg.norm(pos_xy - world_path[-1]))
    if goal_dist < 1.1:
        vl = vr = 0.0
    wheels(vl, vr)

    update_view(camera_mode, pos, yaw)

    # Real synthetic sensor call: this is what updates the three preview panes on the left.
    if not args.no_sensors and frame % 6 == 0:
        update_sensor_camera()

    # Compact on-screen telemetry.
    if frame % 12 == 0:
        if status_text_id >= 0:
            p.removeUserDebugItem(status_text_id)
        speed = math.hypot(*p.getBaseVelocity(husky)[0][:2])
        txt = (
            f"{args.planner.upper()} | camera={camera_mode} | speed={speed:.2f} m/s | "
            f"goal={goal_dist:.1f} m | roll={math.degrees(roll):.1f}° | pitch={math.degrees(pitch):.1f}°"
        )
        status_text_id = p.addUserDebugText(txt, [pos[0], pos[1], pos[2] + 1.35], [1, 1, 1], textSize=1.0, lifeTime=0.2)

    for _ in range(4):
        p.stepSimulation()
    frame += 1
    time.sleep(1 / 60)
