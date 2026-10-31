# Literature Review and Foundational Papers Dossier
## Project: IVRAR Group 14 — Autonomous Mars Rover Navigation (implemented Phase 1)
## Target Publication: A future planetary robotics/navigation methods paper, pending completed empirical evaluation

---

## 1. Literature Search Methodology & Boundary Conditions
This source-backed overview prioritizes primary journal/conference papers and official publications covering heuristic path planning, dynamic replanning, vehicle motion constraints, continuous-control reinforcement learning, and HiRISE terrain data. Citations are checked against publisher, conference, USGS or PMLR pages; the original faculty teleoperation references are not presented as implemented dependencies. This is a focused literature overview, **not a completed systematic review** and not evidence of a human user study.

Four criteria guide inclusion: (1) primary source, (2) relevance to an actually implemented Phase 1 module, (3) usable official DOI or stable publisher link, and (4) clear separation of implemented algorithm from a cited algorithm's broader claims.

---

## 2. Synthesis Matrix of Foundational Papers

| Citation key | Canonical paper | Domain | Method and application | Direct Phase 1 implementation relevance | Primary record |
|---|---|---|---|---|---|
| `Hart1968` | Hart, Nilsson & Raphael (1968), *A Formal Basis for the Heuristic Determination of Minimum Cost Paths* | Graph search | A* evaluation `f=g+h` | `mars_rover/planners.py::astar`; route guidance in Unity | [DOI 10.1109/TSSC.1968.300136](https://doi.org/10.1109/TSSC.1968.300136) |
| `Koenig2002` | Koenig & Likhachev (2002), *D* Lite* | Incremental replanning | Re-use search information after map changes | `mars_rover/planners.py::DStarLite`; `04_dstar_dynamic_demo.py` | [AAAI primary archive](https://ocs.aaai.org/Library/AAAI/2002/aaai02-072.php) |
| `Dolgov2010` | Dolgov, Thrun, Montemerlo & Diebel (2010), *Path Planning for Autonomous Vehicles in Unknown Semi-structured Environments* | Kinematic planning | Hybrid-A* concepts | Phase 1 baseline `planners.py::hybrid_astar`; do not confuse with Phase 2 recurrent/Hybrid A* fusion | [DOI 10.1177/0278364909359210](https://doi.org/10.1177/0278364909359210) |
| `Haarnoja2018` | Haarnoja et al. (2018), *Soft Actor-Critic: Off-Policy Maximum Entropy Deep Reinforcement Learning with a Stochastic Actor* | Continuous RL | Entropy-regularized actor-critic | `36_train_sac_v15f.py` using SB3 SAC | [ICML/PMLR paper](https://proceedings.mlr.press/v80/haarnoja18b.html) |
| `Schulman2017` | Schulman et al. (2017), *Proximal Policy Optimization Algorithms* | RL baseline | Clipped/on-policy objective | `scripts/10_train_ppo.py` | [arXiv primary preprint](https://arxiv.org/abs/1707.06347) |
| `McEwen2007` | McEwen et al. (2007), *Mars Reconnaissance Orbiter's High Resolution Imaging Science Experiment (HiRISE)* | Planetary sensing | HiRISE mission/instrument | `scripts/02_prepare_hirise_geotiff.py` and terrain export pipeline | [DOI 10.1029/2005JE002605](https://doi.org/10.1029/2005JE002605) |
| `Raffin2021` | Raffin et al. (2021), *Stable-Baselines3: Reliable Reinforcement Learning Implementations* | Software implementation | SB3 reference algorithms | SB3 `SAC`, `PPO` training and model saving/loading | [JMLR paper](https://www.jmlr.org/papers/volume22/20-1364/20-1364.pdf) |

---

## 3. Deep Methodological Deconstruction of Foundational Papers

### 3.1 Hart et al. (1968) — Heuristic Search
A* minimizes accumulated grid traversal cost while estimating remaining cost. The project uses weighted terrain/obstacle cost maps and route computation rather than straight-line driving through rocks. A route found by a point-grid planner is not proof that a wide six-wheel rover can physically follow it.

### 3.2 Koenig & Likhachev (2002) — D* Lite
D* Lite can update route solutions efficiently as obstacles change, supporting the project's classical planning comparisons. The Unity `DynamicGuidanceAvoidance.cs` uses its own runtime local search; **do not claim Unity executes the exact Python D* Lite implementation** without verified instrumentation.

### 3.3 Dolgov et al. (2010) — Kinematic Planning
The hybrid-A* baseline in `mars_rover/planners.py` discretizes heading and steering choices. Its existence in Phase 1's Python comparison does not prove the deployed Unity controller runs the later Phase 2 Hybrid A* system.

### 3.4 Haarnoja et al. (2018) — Soft Actor-Critic
SAC learns continuous steering and throttle with critic-based updates and entropy regularization. Phase 1 uses SB3 `SAC` and a multimodal CNN/state extractor. V1.5f adds teacher demonstrations and replay protection; those project-specific choices are not features asserted by the original 2018 paper.

### 3.5 Schulman et al. (2017) and Raffin et al. (2021)
PPO is a useful RL comparison. SB3 provides implementation infrastructure. Comparing algorithms fairly still requires identical terrain splits, compute constraints, random seeds, and outcome metrics.

### 3.6 McEwen et al. (2007) — HiRISE
HiRISE is the source mission for high-resolution Martian imagery. Our actual work combines terrain processing and procedural obstacles. **HiRISE data origin and exact DEM processing provenance should be checked against the archived source file manifest before publication**.

---

## 4. Theoretical Synthesis & Research Gaps Identified
The engineering question actually explored in Phase 1 is whether route-informed RL plus perception and safety-assist can navigate rock-strewn terrain in a Unity simulation without instability when transferring from PyBullet. This is an **engineering motivation, not a proven novel state-of-the-art advance**. Required next comparisons: fixed episodes; paired checkpoints; collision, success, time and route progress; with/without route guidance and safety assist; and reproducible test terrain identifiers. The original faculty predictive ghost-avatar human teleoperation hypothesis was **Not Implemented** and would need a separate approved study.

## 5. Citations and Claims Policy
Do not reuse synthetic faculty claims of `N=50`, an `88.5%` collision reduction, `p<0.001`, NASA-TLX improvement or cost-payback data as results from this project. Where DOI is absent, the official publisher/conference URL is supplied rather than inventing one. Verify exact authors, titles and permissions prior to final research submission.
