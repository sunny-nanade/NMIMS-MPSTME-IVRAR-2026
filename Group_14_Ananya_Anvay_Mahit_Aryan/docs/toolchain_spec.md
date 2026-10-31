# Group 14 — Toolchain Specification (actual Phase 1)

## Unity Environment
- **Editor version:** Unity **6000.3.23f1** (`Phase1/Unity/ProjectSettings/ProjectVersion.txt`). This supersedes the 2022.3 LTS assumption in the faculty starter.
- **Project layout:** `Phase1/Unity/Assets/`, `Packages/`, `ProjectSettings/`.
- **Main scene:** `Assets/Scenes/SampleScene.unity`.
- **Runtime scripts:** `UnityUdpBridge.cs`, `RoverDrive.cs`, `LidarGridSensor.cs`, `DynamicGuidanceAvoidance.cs`, `RuntimePathRockSpawner.cs`, `V01HUD.cs`, `GlobalPathRenderer.cs`.
- **Terrain/model:** included asset scene, rover `.glb`, PolyOne rock assets and Unity metadata; verify third-party redistribution licences before sharing publicly.
- **Simulation:** six-wheel WheelCollider rover, raycast LiDAR, occupancy grid, global A* path and optional local safety/detour guidance.
- **VR/XRI/Quest build:** **Not Implemented** in reviewed Phase 1 source; `XRSettings.asset` alone is not proof of a working VR headset application.

## Python & ML Environment
- Python 3.11 is the development baseline; use project `requirements_phase1.txt` and `pyproject.toml` as dependency authority.
- PyBullet, Gymnasium, Stable-Baselines3 and PyTorch for training/inference; NumPy, SciPy and terrain processing for maps.
- `mars_rover/planners.py` implements classical A*, D* Lite and a Hybrid-A* comparison.
- `scripts/09_train_d3qn.py`, `10_train_ppo.py`, `11_train_sac.py`, and `36_train_sac_v15f.py` define RL experiments.
- Frozen best policy: `checkpoints/sac_v15f/seed_71/best/best_model.zip`; final 1M-step snapshot: `sac_1000000_steps_final.zip`. These are **different artifacts**, not proof that the final one outperforms the best one.
- Python model contract: spatial `(5,32,32)` `uint8`, state `(14,)` `float32`, action 2 continuous values.

## Interoperability & Timing
- Unity listens on UDP port **5006**; Python listens on **5005** (`UnityUdpBridge.cs` / `unity_sac_bridge_v15f.py`).
- Nominal command frequency **8.333333 Hz** (0.12 s), subject to actual runtime performance.
- World/array coordinate calibration may require `--flip-x` / `--flip-z`; verify physically before changing signs.
- A* route lookahead in v1.5f ~6 m; positive learned steer means rover-left under the project contract.

## Evaluation Data & Git LFS
- Data: phase1 Python's `data/phase2/` split (name historically reflects dataset stage; not the separate active Phase 2 project).
- Genuine training histories: `checkpoints/sac_v15f/seed_71/eval_history.csv`, `stage_history.csv`; additional result CSVs must be copied from actual generated runs.
- `telemetry/SIMULATED_*.csv`: **faculty teaching examples, not measured rover/participant evidence**.
- `.gitattributes` tracks model/data and Unity binaries via Git LFS; run `git lfs pull` before testing.

## Release, Usability and Hardware
- A release `.exe/.apk` link and a clean install verification: **Not yet supplied / to be completed**.
- Headset studies, voluntary participants, SUS, NASA-TLX and SSQ: **Not Implemented**.
- Hardware used for training is a Windows laptop with NVIDIA GPU; local performance depends on installed drivers and compatible PyTorch build.
