# Disaster Response VR — Three-Stage Rescue Game

A short VR disaster-response game built in Unity 6 (6000.3.21f1, URP) for Meta Quest 3/3S, tested in Meta XR Simulator.
Pick a stage from the main menu, read the briefing, complete 3–6 objectives, get a score out of 100 and 1–3 stars, and unlock the next stage.

| # | Stage | Scene | Theme |
|---|---|---|---|
| 1 | Factory Rescue | `FactoryRescue` | Cut power, shut the gas valve, extinguish fires, clear the collapse, rescue 3 workers |
| 2 | Earthquake Rescue | `EarthquakeRescue` | Check zones, clear debris, find survivors and guide them out of a damaged building |
| 3 | Flood Evacuation | `FloodEvacuation` | Rising water: collect supplies, reach high ground, escort survivors to the evacuation point |

> **Status honesty**: see [`Docs/AUDIT.md`](Docs/AUDIT.md). Everything here is *implemented* and *auto-tested* (EditMode + PlayMode). The PlayMode
> playthroughs drive real XR Interaction Toolkit select events, not a headset; manual simulator use was only partial. **Nothing is verified on a
> physical headset** — comfort and performance on a real Quest are unknown.
> This is a game, not a safety-certified training tool.

## Controls (same in every stage)
| Action | Quest controllers | Meta XR Simulator (keyboard profile) |
|---|---|---|
| Point / press UI | ray + **trigger** | aim with arrows, **T** |
| Select / hold to assess a survivor, then lift and carry | near or ray + **grip** held ~1 s; keep holding to carry; release in the green safe zone | T / U held |
| Move | teleport and snap turn (thumbsticks) | W A S D / arrows |
| Pause | left menu button | Esc |

**Controller-free (Quest hand tracking):** point and **pinch** to press UI or hold a casualty; keep pinching until they lift, then you may relax your hand (they stay held); open the hand and **pinch again** to put them down (inside the green zone = delivered). Left-hand pinch-drag away from any object walks/turns. Details, limits and what is still untested on a headset: [`Docs/HUMANS_AND_HANDS_UPGRADE.md`](Docs/HUMANS_AND_HANDS_UPGRADE.md).

Help is also in the main menu (Controls). No hazard is an instant fail, and a waypoint guides you when you are lost.
Stage 1 specifics: select the breaker lever to cut power, hold the gas valve wheel to close it, grab the extinguisher and squeeze the trigger to put out fires, carry debris out of the marked gap.

## Requirements
- Unity **6000.3.21f1** (Unity 6.3 LTS) with **Android Build Support** (OpenJDK, SDK & NDK) for Quest builds.
- Packages (already in `Packages/manifest.json`): URP 17.3, XR Hands 1.5.0, XR Interaction Toolkit 3.6.1, OpenXR 1.18, Unity OpenXR: Meta 2.6.1, Input System 1.20, AI Navigation 2.0, TextMeshPro.
- **Git LFS** (the repo uses LFS for textures, models, audio). Run `git lfs install && git lfs pull` after cloning.
- Optional for desktop testing: **Meta XR Simulator** (macOS app) — no Meta XR SDK package is required.

## Quick start (editor + Meta XR Simulator)
1. Open the project in Unity 6000.3.21f1. Let packages resolve and scripts compile (console should show 0 errors).
2. (Re)generate scenes if needed: **Rescue Sim ▸ Build ▸ Everything**. This builds `MainMenu`, `FactoryRescue`, `EarthquakeRescue`, `FloodEvacuation`, prefabs, materials, the NavMesh and Build Settings.
3. Install/enable Meta XR Simulator and turn on "XR Simulator runtime enabled" (toggle top-left of the simulator app).
4. Open `Assets/_Project/Scenes/MainMenu.unity` (or any stage scene) and press **Play**. The simulator window launches automatically.
5. Click once inside the simulator's headset view to give it keyboard focus.

### Meta XR Simulator keyboard controls (Keyboard and mouse profile, "same input for both sides")
| Action | Key |
|---|---|
| Move | W A S D (R/F up/down) |
| Look | Arrow keys |
| Tilt head | Q / E |
| Index trigger | T |
| Grip | U |
| A/X, B/Y | B / N |
| Thumbstick press | I |
| Pause (editor shortcut) | Esc |

Tips: aim the ray at a button by turning/pitching with the arrows (≈70°/s) or stepping back with S; clicking on a controller model in the simulator view selects that controller for manual posing, so click an empty area instead.

## Campaign
- **Main menu**: three stage cards (locked/unlocked, best score and stars), Controls, Settings (volume, comfort: snap/smooth turn, vignette), overall progress, and a **Demo: unlock all** option for assessors. Progress persists in PlayerPrefs.
- **Each stage**: briefing panel → *Begin Mission* → consistent HUD (timer, objective checklist) → results screen with score /100, 1–3 stars and **Next / Retry / Menu**. Finishing stage 3 shows a campaign-complete screen.
- **Stars** (`StageScoring`): ≥85 → 3, ≥60 → 2, otherwise 1; 0 if not completed. Completing stage N unlocks stage N+1.

## START triage study (IVRAR capstone)
A stand-alone scene, **Triage Study** on the main menu (`Assets/_Project/Scenes/TriageSimulation.unity`), is a ten-casualty mass-casualty exercise in a burning factory yard. It is separate from the three campaign stages.

- **Flow:** Begin Exercise -> approach a casualty, point and press the trigger to select it -> run the six START checks (walking, breathing, airway, respiratory rate, perfusion, mental status) -> tag Green / Yellow / Red / Black (final) -> results panel -> Restart or Main Menu.
- **Protocol:** START adult with capillary refill > 2 s (default) or radial pulse, chosen explicitly and written to every CSV row. A training exercise, **not clinically validated**. Details and sources: `Docs/TECHNICAL_IMPLEMENTATION_REPORT.md`.
- **Editor keys:** Enter begin, Tab next casualty, 1-6 checks, 7/8/9/0 (or F1-F4) tag Green/Yellow/Red/Black, F5 restart.
- **Telemetry:** three CSV files per session in `persistentDataPath/TriageTelemetry/` (`*_records.csv` raw, `*_summary.csv` derived, `*_confusion.csv`). Participant IDs are anonymous (`P01`...), each row carries a provenance label (`SoftwareTest`, `Pilot`, `HumanParticipant`). Columns and equations: technical report. Blank templates and the human-study plan: `Docs/STUDY_PROTOCOL.md`, `Docs/templates/`.
- **Status:** editor-tested only. No physical headset testing has been done. No human-study data exists.

## Project layout
```
Assets/_Project/
  Scripts/Runtime/{Core,Hazards,Interaction,Survivors,UI,Audio,Player,World,Triage}   # gameplay (assembly RescueSim.Runtime)
  Scripts/Editor/                                                       # scene/prefab builder, XR+Android setup, Android build
  Data/            one MissionDefinition ScriptableObject per stage
  Scenes/          MainMenu, FactoryRescue, EarthquakeRescue, FloodEvacuation, TriageSimulation (+ baked NavMeshes)
  Prefabs/ Materials/ Shaders/                                           # generated + authored
  Tests/{EditMode,PlayMode}                                              # NUnit/Unity Test Framework suites
Assets/_Project/Art/Characters/Rocketbox/   8 Microsoft Rocketbox avatars (MIT) used for casualties
Assets/TirgamesAssets/   original factory pack (materials upgraded to URP Lit)
Docs/                    TROUBLESHOOTING.md, CHANGES.md, AUDIT.md
```
Architecture: a data-driven shared framework. Each stage is a `MissionDefinition` asset (objectives, time, score weights) listed in `StageCatalog`; `MissionController` runs any of them; `CampaignProgress` (backed by `ICampaignStore`: PlayerPrefs, or in-memory for tests) owns unlocks, scores and stars. Stage scenes are generated by `SceneBuilder` (partials `.Campaign/.Earthquake/.Flood`) with one rig, one EventSystem and one mission per scene. No singletons — the scene builder wires references through serialized fields. `MissionController` owns state/objectives/score;
`Hazard` subclasses (`FireHazard`, `GasLeakHazard`, `ElectricalHazard`) raise `Neutralized`; `PassageMonitor` polls debris; `Survivor` is a small state machine;
`ScoreCalculator`/`ObjectiveTracker` are pure C# (unit-tested). VFX and audio are procedural (no extra audio/texture assets were added).

## Tests
Menu **Rescue Sim ▸ Tests** (or Window ▸ General ▸ Test Runner); results are written to `Builds/TestSummary.txt`.
- **Latest run (2026-10-29, after the human-model / hand-tracking upgrade): EditMode 121/121, PlayMode 57/57.** The breakdown below is the earlier baseline.
- **EditMode — 109 passing (earlier baseline)**: scoring, objectives, vitals, settings, mission data, campaign progress/unlocks/stars, stage catalog, Android config, plus 52 triage cases (START branches and boundaries, session, metrics, CSV, export, scenario).
- **PlayMode — 31 passing (earlier baseline)** (23 campaign + 8 triage scene tests): all three scenes load clean (one rig/EventSystem/mission, no missing scripts, 3–6 objectives); menu lock behaviour; XRI-select-driven playthroughs of stage 1 → 2 → 3 with results, unlocks and campaign-complete; retry and return to menu.
- Tip: do a Play/Stop warm-up after launching the editor before running PlayMode tests (the first run after launch crashed the editor here), and run the groups (Playthroughs / Scenes and menu / Mission flow) separately.

## Building for Quest
See `Docs/TROUBLESHOOTING.md` and `Docs/AUDIT.md` (Android section). Menu: **Rescue Sim ▸ Configure XR and Android** (idempotent), switch platform to Android in Build Profiles, then **Rescue Sim ▸ Build ▸ Android APK (Quest)**. Output goes to `Builds/Android/DisasterResponseVR_HumansHands.apk` (the earlier `DisasterResponseVR.apk` is the previous build and is not overwritten) with a report in `Builds/android-build-report.txt`.


---

## Project Demonstration & Academic Showcase (LinkedIn)

* **LinkedIn Video Demonstration:** [Watch Disaster Rescue Simulation Showcase on LinkedIn](https://lnkd.in/p/dm4vBKsW)
* **Student Presenters:** **Bhoomi Bhandari** (I004), **Kritivya Mishra** (I037), **Tanvi Paithankar** (I044), **Jia Jadhav** (I069)
* **Academic Institutional Tags:** SVKM's NMIMS MPSTME | Academic Directorate | Immersive VR/AR Technologies
