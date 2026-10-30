# Group 06 — SmartCampus Wayfinding XR
### Immersive Virtual Reality & Augmented Reality (IVRAR - Course Code: 702TG0C003 / 702COI002)
**SVKM's NMIMS Mukesh Patel School of Technology Management & Engineering, Mumbai**  
*Academic Year 2026–2027 (Semester V) · Problem-Based Learning (PBL) Research Cohort*

---

## 👥 Student Engineering Team (Group 06 · Batch B2)

| Student Name | Roll No. | SAP ID | Programme | Studio Role | Assigned Branch |
| :--- | :---: | :---: | :--- | :--- | :--- |
| **Mishika Shah** | C136 | 70322200040 | B.Tech Computer Engineering (Integrated) | Spatial Vision & AR Lead | `feat/c136-spatial-vision-ar-lead` |
| **Parva Gaglani** | C172 | 70322200086 | B.Tech Computer Engineering (Integrated) | XR Systems Architect | `feat/c172-xr-systems-architect` |
| **Vansh Panchal** | C139 | 70322200104 | B.Tech Computer Engineering (Integrated) | Graph Algorithms & Navigation Specialist | `feat/c139-graph-algorithms-nav` |
| **Triesha Shah** | C174 | 70322200143 | B.Tech Computer Engineering (Integrated) | Human Factors & Usability Engineer | `feat/c174-human-factors-usability` |

---

## 🧭 Authorized Research Title
> *"How can an AR visual-marker navigation system using ArUco and QR anchors optimize transit time and route-finding errors across multi-storey university buildings for first-year students?"*

---

## 🏛️ Executive Summary & System Architecture

**SmartCampus Wayfinding XR** is an interactive spatial computing wayfinding system engineered in **Unity 6 (`6000.5.7f1`)** targeting the **Meta Quest platform** via OpenXR. The system empowers incoming university students and campus visitors to seamlessly navigate unfamiliar multi-wing facilities.

Users approach an interactive World-Space Navigation Kiosk at the campus arrival lobby, select any destination across academic wings, laboratories, the central library, dining commons, or upper terrace, and receive dynamic turn-by-turn route guidance. Guidance is visualized through glowing animated 3D floor chevrons, spatial path ribbons, and real-time HUD telemetry.

### Core Architectural Pillars:
1. **Mathematical A* Search Engine ($f = g + h$):**
   - Evaluates accumulated walking distance $g(n)$ plus simulated crowd density penalties, combined with an admissible 3D Euclidean distance heuristic $h(n) = \lVert\mathbf{p}_n - \mathbf{p}_{\text{target}}\rVert$.
   - **Step-Free Accessibility Filter:** Dynamically redirects wheelchair and stroller users through accessible ramps ($1:12$ slope) instead of staircases when `requireStepFree = true`.
   - **Dynamic Rerouting:** Instantly recalculates alternative paths if a corridor is blocked or when user deviation exceeds $3.5\text{m}$.
2. **Procedural Multi-Wing 3D Campus:**
   - Synthesizes 19 topological landmarks and 20 bidirectional corridors with verified non-clipping physical bounds and URP forward+ materials.
3. **Interactive World-Space Kiosk UI:**
   - Proximity-triggered World-Space Canvas providing real-time destination metrics (distance, estimated transit time at $1.35\text{ m/s}$, and dynamic instruction banners).
4. **Mandatory New Input System Compliance:**
   - Built **strictly for the New Input System ONLY** (`activeInputHandler: 1`). ZERO legacy `UnityEngine.Input` API calls exist across the codebase.
5. **Meta XR Simulator 2.0 Integration:**
   - Native Unity Play Mode support using the installed Meta XR Simulator runtime for rapid iterative verification without physical headset tethering.

---

## 📂 Submission Directory Layout

```
Group_06 Parva_Mishika_Vansh_Triesha/
├── README.md                                          # Master group documentation (this file)
├── submission_manifest.md                             # Comprehensive deliverable manifest
├── docs/                                              # Academic dossiers & blueprints
│   ├── IVRAR_Group_06_Technical_Dossier.docx          # Primary completed technical dossier
│   ├── IVRAR_Group_06_Technical_Dossier.pdf           # Natively exported 6-page submission PDF
│   ├── LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md   # Literature review with verified CrossRef DOIs
│   ├── RESEARCH_PAPER_MANUSCRIPT_BLUEPRINT.md         # 4-page IEEE research blueprint
│   ├── TEAM_ROSTER.json                               # Cohort roster metadata
│   ├── toolchain_spec.md                              # Hardware, SDK, and package spec
│   └── figures/                                       # High-resolution architectural figures
├── source/                                            # Authoritative clean Unity 6 project
│   ├── Assets/                                        # Scripts, Scenes, Materials, Prefabs
│   ├── Packages/                                      # manifest.json and packages-lock.json
│   └── ProjectSettings/                               # Complete project settings & Input Actions
├── builds/                                            # Standalone executables & verification
│   ├── smartcampus.apk                                # Standalone Android / Meta Quest APK (83.2 MB)
│   └── BUILD_VERIFICATION_REPORT.md                   # Build settings & test verification report
└── member_contributions/                              # Distinct individual work attribution
    ├── Parva/                                         # Core Architecture, Procedural Builder, VR Rig
    ├── Mishika/                                       # Spatial Vision, ArUco Anchor Scaffold, Literature
    ├── Vansh/                                         # A* Algorithm, Graph Engine, Floor Arrows, NUnit Tests
    └── Triesha/                                       # World-Space Kiosk UI, Ray Pointer, Location Tracker
```

---

## 🧪 Verification & Automated Testing Results

### Automated NUnit EditMode Test Suite
The test suite was executed in Unity batchmode CLI:
```xml
<test-run id="2" testcasecount="5" result="Passed" total="5" passed="5" failed="0">
  <test-case name="Graph_ContainsRequiredCampusLandmarks" result="Passed" />
  <test-case name="Pathfinding_EntranceToLab204_Succeeds" result="Passed" />
  <test-case name="Pathfinding_StepFreeRouting_ChoosesRampInsteadOfStairs" result="Passed" />
  <test-case name="Pathfinding_CorridorClosure_ReroutesThroughBypass" result="Passed" />
  <test-case name="ProjectOwnedScripts_ContainNoLegacyInputCalls" result="Passed" />
</test-run>
```
- **100% Pass Rate (5/5 tests passed).**
- **Legacy Input Audit:** 21 project-owned C# scripts audited; **0 legacy calls detected**.

---

## 🚀 Running the Project

### Option A: Unity Editor Play Mode (Meta XR Simulator)
1. Open Unity Hub and add `source/` (or the project root).
2. Open in **Unity 6 (`6000.5.7f1`)**.
3. Open scene `Assets/Scenes/SampleScene.unity` or `Assets/SmartCampus/Scenes/SmartCampusScene.unity`.
4. Press **Play** — the Meta XR Simulator window initializes automatically.
5. **Locomotion:** Use W / A / S / D keys to walk, Right Mouse Button drag to look around, and Left Mouse Button to click buttons. In VR, use controller thumbsticks and trigger.

### Option B: Standalone Meta Quest Installation
1. Connect Meta Quest headset with Developer Mode enabled.
2. Install the standalone package via ADB:
   ```bash
   adb install -r builds/smartcampus.apk
   ```

---

## 📦 Review Workspace Archives (Root Workspace)
For convenient faculty review and individual contribution assessment, four self-contained archives are generated at the workspace root:
- `Parva.zip` (25.2 KB)
- `Mishika.zip` (5.8 KB)
- `Vansh.zip` (20.0 KB)
- `Triesha.zip` (29.6 KB)

---

## Project Demonstration & Academic Showcase (LinkedIn)

[![Watch Video Demonstration on LinkedIn](docs/figures/video_poster.png)](https://lnkd.in/p/d-QEAKRh)

* **Project Demonstration:** [Watch the SmartCampus Wayfinding XR Showcase on LinkedIn](https://lnkd.in/p/d-QEAKRh) *(Click thumbnail above to open LinkedIn post)*
* **Student Presenters:** **Mishika Shah** (C136), **Parva Gaglani** (C172), **Vansh Panchal** (C139), **Triesha Shah** (C174)
* **Academic Institutional Tags:** SVKM's NMIMS MPSTME | Academic Directorate | Immersive VR/AR Technologies
* **Submission Protocol:** Record a 60–90 second demonstration of your VR/AR interactive environment and telemetry. Publish on LinkedIn tagging MPSTME, Dean, and Course Faculty. Insert your live post URL in docs/TEAM_ROSTER.json under "linkedin_url", and submit a pull request to update this project dossier and the cohort dashboard.