# IVRAR Group 01: Real-Time Acoustic Raycasting, VR Spatial Audio & CEDIA/CTA-RP22 Standards
**Course:** Introduction to Virtual Reality & Augmented Reality (IVRAR - Course Code: 702COI002)  
**Academic Term:** Academic Year 2026–2027 | Semester V  
**Institution:** SVKM's NMIMS MPSTME, Mumbai  


---

## 1. Authorized Research Title & Problem Statement

> "To what extent can real-time acoustic raycasting and spatial audio simulation in Unity VR enable residential AV integrators to optimize reverberation time (RT60) and sightline clearance according to CEDIA/CTA-RP22 standards?"

### Core Engineering Focus
* **Interactive VR Environment:** Unity OpenXR immersive environment for residential home theater pre-visualization (`Assets/index.html` and `Assets/Scripts/`).
* **Geometrical Acoustic Raycasting:** Real-time Monte Carlo acoustic raycasting simulating specular reflections and frequency-dependent boundary absorption (`Assets/Scripts/AcousticRaycaster.cs`).
* **Reverberation & Sightline Telemetry:** Automated Schroeder backward integration logging octave-band RT60 ($T_{20}, T_{30}$) and vertical/horizontal sightline clearance (`Assets/Scripts/RT60TelemetryLogger.cs`).
* **Techno-Managerial Systems Evaluation:** Dimensionless operational rework elimination model and contractor payback amortization analysis (`telemetry/acoustic_av_integration_roi.py`).
* **Human Factors & Usability Benchmarking:** Standardized evaluation using Kennedy SSQ, NASA-TLX cognitive workload, and System Usability Scale (SUS) calculators (`telemetry/`).

---

> [!NOTE]
> ### The Devil's Advocate: Reality Check & Theoretical Roast
> *"Calculating Schroeder acoustic impulse decay tails to within 0.04 seconds according to CEDIA RP22 standards, completely oblivious to the fact that the client will buy a giant faux-leather sectional, let their dog bark at 80 dBA, and still watch Christopher Nolan movies with the dialogue volume turned all the way down."*

---

## 2. Student Engineering Team Matrix

| Roll No | SAP ID | Student Name | Degree Programme | Technical Role | Assigned Git Branch | Core Viva Defense Area |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `I066` | `70412500015` | **Meahaan Sharma** | MBA (Tech) (Info. Tech) | Lead XR Systems Architect | `feat/i066-xr-systems-architect` | Unity OpenXR rig configuration, room mesh collision bounds, interactive UI controls, and sightline clearance raycasting. |
| `C034` | `70322200003` | **Amogh Gupta** | B.Tech (Comp. Engg.) (Integrated) | Spatial Acoustics & Audio Specialist | `feat/c034-spatial-acoustics-au` | Geometrical acoustic raycasting, frequency-dependent absorption modeling (Sabine/Eyring formulas), Schroeder backward integration for RT60, and CEDIA/CTA-RP22 curve matching. |
| `N083` | `70472400056` | **Harshit Rai** | MBA (Tech) (Computer) | Human Factors & Usability Engineer | `feat/n083-human-factors-usabil` | Ergonomic sightline clearance calculations (vertical elevation < 15 deg, horizontal FOV 36-40 deg), NASA-TLX cognitive workload reduction, Kennedy SSQ cybersickness minimization, and SUS score evaluation. |
| `N087` | `70472400098` | **Rannbir Sachdeva** | MBA (Tech) (Computer) | Techno-Managerial Product Manager | `feat/n087-technoeconomic-produ` | AV integration workflow modeling, physical rework reduction (from 29.2% to 4.2%), acoustic panel material amortization, and dimensionless payback horizon (5.1 months). |

---


### Student Engineering Commendation & Acknowledgments
SVKM's NMIMS MPSTME conveys sincere appreciation and heartfelt gratitude to Meahaan Sharma (I066), Amogh Gupta (C034), Harshit Rai (N083), Rannbir Sachdeva (N087) for their disciplined commitment, late-night debugging, and technical craftsmanship throughout Semester V. Your rigorous engineering inquiry and dedication to immersive XR environments exemplify the highest standards of undergraduate technical research.

> *"Scientists discover the world that exists; engineers create the world that never was."*  
> — **Theodore von Kármán**

> *"There is no substitute for hard work. Genius is one percent inspiration and ninety-nine percent perspiration."*  
> — **Thomas A. Edison**

---

## 3. Foundational Literature Benchmarks (Strict 2:4 Ratio)

The research project is theoretically anchored on six foundational peer-reviewed publications validated against global bibliographic databases (IEEE, ASA, Springer, JAES, MDPI).

| # | Author (Year) | Type | Paper Title | Venue / Indexing | Active DOI Link | Primary Extracted Formulation | Student Lead |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | **Schroeder (1965)** | Seminal | *New Method of Measuring Reverberation Time* | The Journal of the Acoustical Society of America | [10.1121/1.1909343](https://doi.org/10.1121/1.1909343) | `E(t) = \int_t^\infty [h(\tau)]^2 d\tau` | Amogh Gupta (C034) |
| 2 | **Savioja & Svensson (2015)** | Seminal | *Overview of geometrical room acoustic modeling techniques* | The Journal of the Acoustical Society of America | [10.1121/1.4926438](https://doi.org/10.1121/1.4926438) | `E_refl = E_inc * (1 - alpha_mat) * (1 - s)` | Meahaan Sharma (I066) |
| 3 | **Hold & Mckenzie (2022)** | Recent | *Resynthesis of Spatial Room Impulse Response Tails With Anisotropic Multi-Slope Decays* | Journal of the Audio Engineering Society | [10.17743/jaes.2022.0017](https://doi.org/10.17743/jaes.2022.0017) | `E(t, \Omega) = E_0 \exp(-2\delta(\Omega) t)` | Amogh Gupta (C034) & Harshit Rai (N083) |
| 4 | **Mi & Kearney (2022)** | Recent | *Impact Thresholds of Parameters of Binaural Room Impulse Responses (BRIRs) on Perceptual Reverberation* | Applied Sciences | [10.3390/app12062823](https://doi.org/10.3390/app12062823) | `JND_RT \approx 5%` | Harshit Rai (N083) |
| 5 | **Deppisch & Gari (2023)** | Recent | *Direct and Residual Subspace Decomposition of Spatial Room Impulse Responses* | IEEE/ACM Transactions on Audio, Speech, and Language Processing | [10.1109/taslp.2023.3240657](https://doi.org/10.1109/taslp.2023.3240657) | `H(f) = U_s \Lambda_s V_s^H + U_r \Lambda_r V_r^H` | Meahaan Sharma (I066) & Rannbir Sachdeva (N087) |
| 6 | **Ratnarajah & Manocha (2024)** | Recent | *Listen2Scene: Interactive material-aware binaural sound propagation for reconstructed 3D scenes* | IEEE Conference on Virtual Reality and 3D User Interfaces (VR) | [10.1109/VR58804.2024.00048](https://doi.org/10.1109/VR58804.2024.00048) | `L_p = 10 \log_{10}(\sum E_k / E_{ref})` | Rannbir Sachdeva (N087) & Amogh Gupta (C034) |

For the exhaustive literature analysis, mathematical derivations, and viva defense questions, refer to:
* [`docs/LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md`](docs/LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md)
* [`docs/RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md`](docs/RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md)

---

---

## 4. Project Demonstration & Academic Showcase (LinkedIn)

[![Watch Video Demonstration on LinkedIn](docs/figures/video_poster.png)](https://www.linkedin.com/posts/amogh-gupta-4245001b7_virtualreality-metaquest-openxr-ugcPost-7520530878360645632-acu5/?utm_source=social_share_send&utm_medium=member_desktop_web&rcm=ACoAADJxLlwB3McXXW2dtI7T_jSKEvwQ8kASEXw)

* **Video Demonstration:** [Watch 60-Second Walkthrough on LinkedIn](https://www.linkedin.com/posts/amogh-gupta-4245001b7_virtualreality-metaquest-openxr-ugcPost-7520530878360645632-acu5/?utm_source=social_share_send&utm_medium=member_desktop_web&rcm=ACoAADJxLlwB3McXXW2dtI7T_jSKEvwQ8kASEXw) *(Click thumbnail above to open LinkedIn post)*
* **Student Presenters:** **Meahaan Sharma** (I066), **Amogh Gupta** (C034), **Harshit Rai** (N083), **Rannbir Sachdeva** (N087)
* **Academic Institutional Tags:** SVKM's NMIMS MPSTME | Academic Directorate | Immersive VR/AR Technologies
* **Submission Protocol:** Record a 60–90 second demonstration of your VR/AR interactive environment and telemetry. Publish on LinkedIn tagging MPSTME, Dean, and Course Faculty. Insert your live post URL in `docs/TEAM_ROSTER.json` under `"linkedin_url"`, and submit a pull request to update this project dossier and the cohort dashboard.

---

## 5. Repository Directory Architecture

```
.
|-- README.md                                          <- Front-page research charter, student roster & literature
|-- RESEARCH_AND_IMPLEMENTATION_GUIDE.md               <- Comprehensive technical engineering guide
|-- Assets/
|   |-- .gitkeep
|   |-- index.html                                     <- WebXR browser-based interactive acoustic visualizer
|   `-- Scripts/
|       |-- AcousticRaycaster.cs                       <- Real-time Monte Carlo acoustic raycasting engine
|       `-- RT60TelemetryLogger.cs                     <- Schroeder backward integration RT60 logger
|-- docs/
|   |-- TEAM_ROSTER.json                               <- Machine-readable team configuration & verified papers
|   |-- LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md   <- Exhaustive literature dossier (6 verified papers)
|   |-- RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md         <- 4-page IEEE conference manuscript blueprint
|   |-- toolchain_spec.md                              <- Unity/WebXR environment configuration guide
|   `-- figures/
|       |-- figure1_system_architecture.png            <- 300 DPI system architecture diagram
|       |-- figure2_kinematic_telemetry.png            <- 300 DPI raycasting and RT60 telemetry
|       `-- figure3_comparative_performance.png        <- 300 DPI comparative benchmark visualization
`-- telemetry/
    |-- .gitkeep
    |-- acoustic_real_trials.csv                    <- Empirical benchmark trial dataset (N = 50 runs)
    |-- acoustic_av_integration_roi.py                 <- Techno-managerial dimensionless operational model
    |-- generate_paper_figures.py                      <- Standalone 300 DPI publication figure generator
    |-- kennedy_ssq_calculator.py                      <- Simulator sickness questionnaire evaluation tool
    |-- nasa_tlx_calculator.py                         <- Cognitive workload assessment tool
    |-- sus_calculator.py                              <- System usability scale evaluator
    `-- test_evaluation_tools.py                       <- Unit tests for evaluation framework
```

---

## 6. Pedagogical Boundaries: Guidance vs Student Ownership

To ensure academic rigor and authentic student learning, this repository enforces strict boundaries between scaffolding and student deliverables:

* **Provided by Course Scaffolding:**
  * Interactive 3D scene architecture and Raycast hit detection framework (`Assets/`).
  * Telemetry toolchain and usability evaluation calculators (`telemetry/`).
  * Literature foundation, mathematical formulations, and conference paper blueprint (`docs/`).
  * Dimensionless techno-managerial economic framework (`telemetry/acoustic_av_integration_roi.py`).
* **Student Technical Deliverables (Required for Evaluation):**
  * Implement and calibrate the frequency-dependent absorption and Schroeder integration in `Assets/Scripts/AcousticRaycaster.cs` and `Assets/Scripts/RT60TelemetryLogger.cs` (completing all `# TODO [Student Roll / Name]` blocks).
  * Execute simulation trials across untreated, partially treated, and fully optimized home cinema configurations to collect empirical data in `telemetry/acoustic_real_trials.csv`.
  * Re-run `telemetry/acoustic_av_integration_roi.py` with live experimental data to regenerate publication figures.
  * Author the final 4-page IEEE conference paper in `docs/RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md` and defend individual Git commits during the oral viva.

---

## 7. Sprint 0 Onboarding & Toolchain Verification

Verify your local Python, Unity/WebXR, and telemetry toolchain:

```powershell
# Step 1: Clone repository and navigate to group directory
git clone <repository_url>
cd <repository_root>/Group_01_Meahaan_Amogh_Harshit_Rannbir

# Step 2: Checkout your individual feature branch
# Example for lead student:
git checkout -b feat/i066-xr-systems-architect

# Step 3: Run telemetry and evaluation unit tests
python telemetry/test_evaluation_tools.py

# Step 4: Verify 300 DPI publication figures and techno-managerial ROI model
python telemetry/acoustic_av_integration_roi.py
```