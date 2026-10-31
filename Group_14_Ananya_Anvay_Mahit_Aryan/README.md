# IVRAR Group 14: Autonomous Mars Rover Navigation — Phase 1 (Unity + Python)
**Course:** Immersive Virtual Reality & Augmented Reality (IVRAR) — see official course dossier for code  
**Academic Term:** AY 2026–27, Odd Semester  
**Institution:** SVKM's NMIMS MPSTME, Mumbai  
**Group:** 14 · Batch B2 · I003, I006, I010, I041

## Authorized Research Title (faculty record retained)
> To what extent does a predictive ghost-avatar digital twin in Unity VR mitigate teleoperation path tracking error and collision frequency for planetary rover operators under simulated high-latency (1.5-second to 5-second) transmission delays?

**Research-scope disclosure:** The title above is the original faculty-issued assignment. The code submitted for **Phase 1** implements **autonomous** rover navigation, not a completed experimental test of that teleoperation hypothesis. A formal replacement of the authorized title requires faculty approval. Starter scripts in `Assets/Scripts` are not proof that a full VR/teleoperation study is implemented.

---

## Executive Abstract & Problem Scope
We implemented a terrain-aware autonomous planetary rover simulator integrating classical A*/D* Lite/Hybrid-A* path-planning benchmarks, deep reinforcement learning (D3QN, PPO and SAC) in Python/PyBullet, and a Unity rover demonstration. The final v1.5f inference path constructs a `(5,32,32)` terrain/occupancy observation and a 14-element rover state, uses a moving ~6 m A* lookahead, sends observations from Unity over UDP to a trained SAC-CNN policy in Python, and returns steering/throttle actions. The Unity scene adds six-wheel rover motion, LiDAR occupancy, runtime rocks, local detour guidance, safety checks and a HUD. The preserved Python artifacts include the seed-71 best SAC checkpoint and a final 1,000,000-step checkpoint. **Source presence does not establish a measured 100% success rate.**

Human participation, SUS/TLX/SSQ scoring, 1.5–5 s delayed-operator trials and high-latency predictive VR treatment: **Not Implemented**. Faculty `SIMULATED_*` CSVs and original synthetic figure files are not empirical results.

## Student Engineering Team & Task Matrix

| Roll | Name | Proposed project role (verify against actual work) | GitHub |
|---|---|---|---|
| I003 | Ananya Baweja | Terrain Assets & Simulation QA Coordinator | To be completed |
| I006 | **Anvay Borade** | **Lead AI/RL Engineer, System Architect & Unity-Python Integration** | `anvayborade` |
| I010 | Mahit Naresh Daswani Chanchlani | LiDAR, Planning & Physics Integration Support | To be completed |
| I041 | Aryan Oberoi | Evaluation, Results & Research Documentation Coordinator | To be completed |

The roles above are proposed task allocations, not independent verification of authorship or hours. Each member must confirm contributions in the official dossier.

---

## Core System Architecture & Egress Telemetry

1. **Terrain and classical planners:** `Phase1/Python/mars_rover/terrain.py`, `planners.py`, `scripts/03_run_classical_benchmarks.py`, `04_dstar_dynamic_demo.py`.
2. **Python RL simulator/training:** `mars_rover/envs/rover_env.py`, `mars_rover/rl/features.py`, `scripts/09_train_d3qn.py`, `10_train_ppo.py`, `36_train_sac_v15f.py`, `37_benchmark_sac_v15f.py`.
3. **Unity simulation:** `Phase1/Unity/Assets/Scenes/SampleScene.unity`, `Assets/Scripts/RoverDrive.cs`, `LidarGridSensor.cs`, `DynamicGuidanceAvoidance.cs`, `RuntimePathRockSpawner.cs`.
4. **UDP live inference:** `Phase1/Unity/Assets/Scripts/UnityUdpBridge.cs`, `Phase1/Python/unity_sac_bridge_v15f.py`. Unity sends sensor/goal state to local Python port **5005** and receives actions on Unity port **5006** at a nominal **8.333 Hz**.
5. **Model input:** image-like spatial data `(5,32,32)` and state vector `(14,)`; this is **Phase 1** and must not be confused with the Phase 2 recurrent SAC observation contract.

### Tested project version and dependency scope
- **Unity editor specified in actual file:** `6000.3.23f1` (not 2022.3 LTS).
- Python project files: `Phase1/Python/requirements_phase1.txt` and `pyproject.toml`.
- Project is Unity **desktop simulation**. Standalone PCVR or Quest build, XRI interaction and measured human teleoperation trial: **Not Implemented / Not Supplied**.

### Starting Phase 1 (Windows PowerShell)

From the repository root:

```powershell
Set-Location .\Group_14_Ananya_Anvay_Mahit_Aryan\Phase1\Python
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements_phase1.txt
.\.venv\Scripts\python.exe unity_sac_bridge_v15f.py --model checkpoints\sac_v15f\seed_71\best\best_model.zip --height-npy unity_hirise_export\height_m.npy --meta unity_hirise_export\heightmap_meta.json --device cpu --self-test
```

If that self-test passes, rerun without `--self-test` to open the bridge. Open `Phase1/Unity` in Unity `6000.3.23f1` and run `Assets/Scenes/SampleScene.unity`. Confirm the scene's actual Inspector references and `StreamingAssets/global_path.csv`, then press Play. CPU inference may be slower than GPU; use `--device cuda` when CUDA-compatible PyTorch is available. See source bridge's `--help` for additional flags.

**Important:** `unity_hirise_export/height_m.npy`, model files and Unity meshes are LFS assets. Install Git LFS and run `git lfs pull` for Group 14 before launching; don't treat pointer text as model binaries.

## Figures, Evaluation and Provenance

- `docs/figures/figure1_system_architecture.png` — source-derived architecture schematic; **not a performance result**.
- `docs/figures/figure2_kinematic_telemetry.png` — illustrative path/obstacle geometry; **not measured telemetry**.
- `docs/figures/figure3_comparative_performance.png` — evidence-status panel; real performance chart requires actual benchmark CSV.
- `telemetry/README.md` and `telemetry/export_phase1_evidence.ps1` explain how to copy real result CSVs, preserving the faculty's original tools and `SIMULATED_*` examples as non-evidence.
- `scripts/37_benchmark_sac_v15f.py` writes `results/sac_v15f_seed_71/benchmark_summary.csv` only when run on real local checkpoints and terrain data. Do not claim the CSV already exists unless verified.

## Foundational Literature
See `docs/LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md` for primary sources (Hart et al.; Koenig & Likhachev; Dolgov et al.; Haarnoja et al.; Schulman et al.; McEwen et al.) and project-specific method mapping.

## Submission and Reproducibility
Keep `docs/IVRAR_Group_14_Technical_Dossier.docx` and its PDF export with the same six-section faculty form. Complete real Git telemetry, authorship, individual learning, AI audit, peer scores, LinkedIn video URL and signatures yourself. Do not claim human study statistics or completed releases without evidence. The active contribution branch is `feat/group14-phase1`; final review/merge targets faculty `main`.
