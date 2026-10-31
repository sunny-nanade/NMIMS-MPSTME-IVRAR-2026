# Research Paper Manuscript Blueprint: Autonomous Mars Rover Navigation (Phase 1)

## Authorized Research Title (faculty-issued; unchanged)
> To what extent does a predictive ghost-avatar digital twin in Unity VR mitigate teleoperation path tracking error and collision frequency for planetary rover operators under simulated high-latency (1.5-second to 5-second) transmission delays?

**Actual Phase 1 implementation title (not an approved replacement):** Terrain-Aware Mars Rover Navigation Using A*-Guided SAC-CNN and Unity–Python Integration.

---

## Abstract — evidence-safe draft
This project investigates autonomous simulated planetary rover navigation using cost-aware graph planning and continuous-action reinforcement learning. A Python/PyBullet environment generates terrain and collision observations, and baseline algorithms include A*, D* Lite and Hybrid A*, alongside D3QN, PPO and SAC training scripts. The Phase 1 v1.5f integration routes a Unity six-wheel rover through a Python SAC model using a 5-channel spatial observation, a 14-value rover state and a moving 6 m A* path target. The Unity environment incorporates LiDAR-based occupancy, runtime rock spawning, local detours and safety-assisted action control. Source artifacts and trained checkpoints are present. **Final comparative numerical results cannot be asserted without the corresponding genuine CSV and test protocol.** High-latency ghost-avatar VR treatment and human-subject usability evaluation were not implemented in this phase.

---

## I. Introduction
- Terrain-aware rover autonomy and obstacles; risk of relying on incomplete route/pose observations.
- Difference between a planned path, an RL action policy and a safety intervention.
- Scope statement: this paper reports **implemented autonomous navigation**, not the faculty's unperformed ghost-avatar human latency study.
- Research questions: (RQ1) Can a trained SAC-CNN follow A*-guided routes in Unity? (RQ2) What happens under added rock obstacles and domain shift? (RQ3) How do policies compare under a fixed benchmark?

## II. Related Work
Use the seven primary citations in `LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md` (Hart; Koenig & Likhachev; Dolgov; Haarnoja; Schulman; Raffin; McEwen). Map each explicitly to an implemented module and separate future Phase 2 mechanisms.

## III. System Design and Methodology
### 3.1 World, terrain and rover model
- Python modules: `mars_rover/terrain.py`, `envs/rover_env.py`.
- Six-wheel Unity scene: `RoverDrive.cs`; LiDAR/occupancy `LidarGridSensor.cs`.
- Runtime rocks: `RuntimePathRockSpawner.cs`; local guidance `DynamicGuidanceAvoidance.cs`.

### 3.2 Planning and observation
- A* path cost: `f(n)=g(n)+h(n)`.
- Spatial `(5,32,32)` and rover state `(14,)` supplied by `unity_sac_bridge_v15f.py`.
- Route-target bearing is relative to a moving path lookahead of approximately 6 m.

### 3.3 RL controller
- SB3 SAC + CNN/state encoder in `mars_rover/rl/features.py`.
- Teacher demonstrations and replay protection in v1.5f training; trained checkpoints are supplied separately with Git LFS.
- Distinguish direct SAC mode from assisted hybrid/safety mode in Unity.

### 3.4 Unity/Python bridge
- Unity sensor sender -> Python UDP port 5005 -> state adapter/model -> action response -> Unity port 5006.
- Typical policy frequency ~8.333 Hz; explicitly report actual measured latency only if a real capture exists.

### 3.5 Baselines and ablations
- A*, D* Lite, Hybrid A*, D3QN, PPO, SAC where genuinely run.
- Unassisted vs safety-assisted SAC requires paired scenarios, with separated results.

## IV. Evaluation — not yet populated from verified raw data
### Protocol to execute before reporting results
1. Freeze model checkpoint IDs, source commit and terrain split identifiers.
2. Run reproducible fixed-seed evaluations on the genuine `data/phase2/test` from Phase 1.
3. Log per-run goal completion, collision count, route progress, elapsed steps/time, stuck and safety-assist interventions.
4. Use `scripts/37_benchmark_sac_v15f.py` to generate `results/sac_v15f_seed_71/benchmark_summary.csv`; copy genuine files into `telemetry/phase1_evidence/` using the provided PowerShell exporter.
5. Generate Figure 3 from that exported real CSV with the supplied gated plotting tool. Until then Figure 3 is explicitly an evidence-status image, not an effect-size plot.
6. Report `N`, mean, SD and intervals only after computation from source rows. Never describe the 50-row `SIMULATED_` faculty file as actual trials.

### Human usability component
**Not Implemented.** No human participant N, SUS, NASA-TLX, SSQ, participant consent, VR trial design or teleoperation latency randomization is claimed.

## V. Discussion and Limitations
- A PyBullet-trained policy is affected by sim-to-sim sensor/dynamics differences.
- Planner path existence is distinct from feasible rover traction/steering.
- Safety-assisted route completion cannot be claimed as pure learned SAC control.
- Single-run observations do not establish statistically robust superiority.
- Phase 2 UA/UR-SAC and recurrent GRU/attention are **not part of this Phase 1 paper**.
- Faculty-authorized research-title mismatch requires formal clarification.

## VI. Conclusion and Reproducibility
Phase 1 is a working code-level autonomous rover navigation implementation with source, Unity scene, selected model artifacts and scripts for measured evaluation. Final research claims depend on an evidence-backed benchmark table, release build verification, and accurate student/provenance declarations. See dossier, telemetry README, and Git LFS instructions in the root README.

## Proposed Figures and Tables
- **Figure 1:** source-derived architecture (not a measured result).
- **Figure 2:** navigation geometry schematic (not a measured result).
- **Figure 3:** measured comparison only when authentic benchmark CSV is provided.
- **Table 1:** source/config inventory and component mapping.
- **Table 2:** actual benchmark metrics and confidence intervals (pending raw CSV).
- **Table 3:** human evaluation (Not Implemented; do not fill fake numbers).

## Source References
See `LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md`. Target venue and title changes are subject to supervisor approval; no acceptance/publication claim is made.
