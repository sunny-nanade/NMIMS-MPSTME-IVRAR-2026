# Planetary Rover Research Pipeline — Phase 1

This is the first runnable block of the 8-model benchmark:

**A* → D* Lite → Hybrid A* → A*+D3QN → PPO-CNN → SAC-CNN → UA-RSAC → UA-RSAC+Hybrid A***

Phase 1 freezes the terrain format, classical baselines, metrics and 3D physics visualization before RL is added.

## Windows setup

```powershell
cd path\to\mars_rover_phase1
py -3.11 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements_phase1.txt
```

Install a pinned CUDA-enabled PyTorch build that is comfortably compatible with SB3 2.9.0:

```powershell
pip install torch==2.11.0 torchvision==0.26.0 --index-url https://download.pytorch.org/whl/cu128
```

Then install the RL stack used in later phases:

```powershell
pip install "gymnasium==1.3.0" "stable-baselines3[extra]==2.9.0" "sb3-contrib==2.9.0"
```

If the CUDA command ever fails because of a driver mismatch, use the current Windows/Pip/CUDA command from the official selector at https://pytorch.org/get-started/locally/.

## Run Phase 1

```powershell
python scripts/00_check_system.py
python scripts/01_make_procedural_terrain.py
python scripts/03_run_classical_benchmarks.py
python scripts/04_dstar_dynamic_demo.py
python scripts/05_pybullet_live_demo.py --planner hybrid
```

The classical benchmark writes:
- `outputs/metrics/classical_metrics.csv`
- `outputs/figures/classical_paths_2d.png`
- `outputs/figures/classical_paths_3d.png`

## Add real HiRISE terrain

Start with the Jezero DTM:
https://hirise.lpl.arizona.edu/dtm/ESP_045994_1985

Download `DTEEC_045994_1985_046060_1985_U01` (PDS IMG). On Windows, the simplest conversion is QGIS:
https://qgis.org/

In QGIS:
1. Add the `.IMG` as a raster layer.
2. Right-click → Export → Save As.
3. Choose GeoTIFF.
4. Keep floating-point elevation values.
5. Save as `data/raw/jezero_dtm.tif`.

Then:

```powershell
python scripts/02_prepare_hirise_geotiff.py data/raw/jezero_dtm.tif --out data/processed/jezero.npz --size 257 --crop 512
python scripts/03_run_classical_benchmarks.py --terrain data/processed/jezero.npz
python scripts/05_pybullet_live_demo.py --terrain data/processed/jezero.npz --planner hybrid
```

## Why the live rover is Husky in Phase 1

The bundled PyBullet Husky URDF is a reliable wheeled dynamics surrogate and requires no ROS/Xacro conversion. The ExoMy model is useful later, but its public model repository is oriented to ROS2/Gazebo rather than drop-in PyBullet. First prove the environment/metrics/RL; then swap the body.

## Later phases

**Phase 2:** Gymnasium environment, A*+D3QN, PPO-CNN, SAC-CNN, TensorBoard, checkpoints and videos.

**Phase 3:** probabilistic traversability, recurrent SAC, UA-RSAC, UA-RSAC+Hybrid A*, safety shield.

**Phase 4:** train/validation/test terrain splits, 3–5 seeds/model, uncertainty U0–U7 tests, aggregate metrics, confidence intervals, plots/videos, Unity export.

## Phase 2
See `PHASE2_GUIDE.md` for the complete Gymnasium/PyBullet RL setup and training sequence.
