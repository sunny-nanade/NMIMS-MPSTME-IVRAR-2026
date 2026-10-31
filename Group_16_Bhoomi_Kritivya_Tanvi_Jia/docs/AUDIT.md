# Final audit — what is real, what is tested, what is not

Date: 2026-10-25 · Unity 6000.3.21f1 · macOS (Apple Silicon M3) · Meta XR Simulator · branch `feature/factory-rescue-vr` (local commits only, **nothing pushed**).

Legend — **Implemented**: code/assets exist. **Auto-tested**: Unity Test Runner (EditMode/PlayMode) with real results. **Simulator-tested**: exercised by hand in Meta XR Simulator. **Headset-verified**: run on a physical Quest — **nothing in this project has been headset-verified.**

## 1. Verified facts (with how)
| Claim | Evidence |
|---|---|
| EditMode tests: **57 / 57 passed** | Unity Test Runner via `Rescue Sim ▸ Tests`, `Builds/TestSummary.txt` |
| PlayMode tests: **23 / 23 passed** (run in batches of 4 + 10 + 9) | same |
| All three stage scenes load with one rig, one EventSystem, one mission, no missing scripts, 3–6 objectives | `CampaignSceneTests` |
| Full playthroughs of Factory → Earthquake → Flood reach results, record score/stars, unlock the next stage; campaign-complete after stage 3; Retry and Menu work | `CampaignPlaythroughTests` (drives real `XRInteractionManager` select/deselect on survivors, pickups, valves, zones — **not** a headset and not manual simulator play) |
| Meta XR Simulator provides HMD + controllers; VR ray-click on UI works (from the earlier milestone) | `[XRProbe]` console logs |
| Manual simulator session: menu → stage load and briefing panel observed | screenshots; button clicking by keyboard aiming was too imprecise to complete a stage by hand |
| Android build | see section 5 |

## 2. Feature matrix
| Feature | Implemented | Auto-tested | Simulator-tested (manual) | Headset |
|---|---|---|---|---|
| Main menu: stage cards, locks, progress, controls, settings, demo unlock-all | ✔ | ✔ (locks, unlock-all, load) | partial (menu → stage load) | ✘ |
| Persistent unlocks/scores/stars (PlayerPrefs) | ✔ | ✔ (in-memory store) | ✘ | ✘ |
| Briefing, HUD, results (score /100, stars, Next/Retry/Menu), campaign-complete | ✔ | ✔ (via playthroughs) | partial (briefing) | ✘ |
| Stage 1 Factory (power, gas, fire, debris, 3 survivors) | ✔ | ✔ | ✘ | ✘ |
| Stage 2 Earthquake | ✔ | ✔ | ✘ | ✘ |
| Stage 3 Flood (rising water visual, supplies, evacuation) | ✔ | ✔ | ✘ | ✘ |
| Survivor assess → follow → evacuate | ✔ | ✔ | ✘ | ✘ |
| Teleport/snap-turn locomotion, comfort vignette, waypoint | ✔ | partly | ✘ | ✘ |
| Procedural audio/VFX | ✔ | — | ✘ | ✘ |

"Auto-tested" playthroughs call the real runtime and XRI selection API but **do not prove** that grabbing, aiming and locomotion feel right with real controllers.

## 3. Bugs found and fixed this milestone
1. Stages 2/3 started with a null `MissionDefinition`: `NewScene(Single)` unloaded the asset → builder reloads it after creating the scene (found by tests).
2. Briefing panel at floor/ceiling height because head tracking reads (0,0,0) for the first frames → `VRPanel` waits for tracking, falls back to rig height + 1.6 m.
3. Hazard-free stages scored 0 on the hazard share → now awarded on completion (test updated deliberately).
4. Retry test raced two scene loads (test bug) → fixed.
5. Earlier milestone fixes: see git history.

## 4. Known issues / risks
- **Never run on a headset.** Frame rate, thermals, comfort, controller ergonomics are unknown. Light counts are modest and no post-processing is used, but this was not profiled.
- Unity (editor) crashed several times during PlayMode runs, mostly the first run after launch; workaround in README. Cause not investigated.
- An `XRI BaseAsyncAffordanceStateReceiver.HandleTween` NullReferenceException flood appeared in the console after a manual simulator session; not investigated or reproduced in tests. May be a pre-existing XRI sample issue.
- Manual simulator aiming with the keyboard profile is laggy and imprecise; Quest controllers should be easier.
- Platform switches rewrite some pack `.meta` files and settings assets; these incidental changes are deliberately not committed.
- LFS binaries from the XRI sample and TextMesh Pro `EmojiOne.png` are untracked (authoring VM has no Git LFS). Add them from a machine with Git LFS before pushing, otherwise a fresh clone may miss some sprites/models.
- APK size and first-build time are large (previous build ≈ 231 MB, ≈ 1 h) because of pack textures and extra packages already in the project.

## 5. Android build
- Active target Android, IL2CPP, ARM64, Vulkan, min SDK 29, OpenXR with Meta Quest support + Touch profiles enabled, 4 scenes (MainMenu, FactoryRescue, EarthquakeRescue, FloodEvacuation).
- Result **Succeeded**, 354 warnings on the full build. APK: `Builds/Android/DisasterResponseVR.apk`, 242,528,206 bytes (≈ 231 MiB), 816 entries, SHA-256 `7775384dbee3a500e735f2079d1917684dbcad52b6a6dde542f360aac781efe1`. The report's "Size: 1604 MB" is Unity's total build-output size (incl. symbols), not the APK. The report timestamp is local time mislabelled `Z`.
- Inspected the APK manifest: package `com.rescuesim.*`, Oculus supported devices (quest/quest2/quest3s), head-tracking and `android.hardware.vr` entries; `libopenxr_loader.so`, `libunity.so`, `libil2cpp.so` (arm64-v8a) present. It is debug-signed (no keystore). **Never installed or run on a device.**
- Build-time console error (non-blocking, 1 per build): `NullReferenceException` inside the OpenXR package's own Project Validation rule (`MetaQuestFeature.CreateValidationRules`, MetaQuestFeature.cs:539 via MockRuntime). Build result is still Succeeded and the APK is produced; not caused by project code. A first rebuild during the audit had shown the Quest OpenXR features disabled in `OpenXR Package Settings.asset` (a side effect of earlier platform switching); re-running *Rescue Sim ▸ Configure XR and Android* restored them and the APK was rebuilt afterwards.

## 6. Recommended next steps (needs you)
1. Install the APK on a Quest (`adb install -r Builds/Android/DisasterResponseVR.apk`), play all three stages, and log fps with OVR Metrics.
2. Tune timings/score weights in the three `Mission_*.asset` files after headset play.
3. Add LFS binaries, review `git status`, then approve a push (origin: `queenjj11/VR-Disaster-Simulator`). No push has been made.
