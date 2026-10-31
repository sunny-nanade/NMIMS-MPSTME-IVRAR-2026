# Upgrade: human casualties, controller-free hand tracking, reliable carry

Status: implemented and auto-tested in the Unity editor (2026-10-29). **Nothing here has been run on a physical Quest.** The hand-tracking tests drive the
same input code through a *simulated* hand source; they say nothing about how real tracking feels. Not committed or pushed.

## 1. Human casualty models
- **Asset:** Microsoft Rocketbox Avatar Library, <https://github.com/microsoft/Microsoft-Rocketbox>, **MIT licence** (free, redistributable, no account or purchase). Licence text kept at `Assets/_Project/Art/Characters/Rocketbox/LICENSE-Rocketbox-MIT.txt`.
- **Used (8 avatars, lowest-poly "hipoly_81" mesh each, 6.7k-10.3k triangles, one skinned mesh):** Male_Adult_01, Male_Adult_05, Male_Adult_10, Female_Adult_01, Female_Adult_04, Female_Adult_08, Construction_Male_01, Construction_Female_01.
- **Edits made by us:** colour textures converted from 2048 px TGA to 1024 px JPEG / 512 px PNG; materials regenerated as URP Lit; arms re-posed down at build time; colliders/animators stripped (the grab volume stays on the casualty root). Prefabs: `Assets/_Project/Prefabs/Characters/Human_<avatar>.prefab`.
- **Per-casualty variety:** `CasualtyAppearance` applies a stable height/width variation and a soot tint from the casualty name.
- **Mapping (IDs and gameplay unchanged):** Dana=Construction_Female_01, Marcus=Construction_Male_01, Priya=Female_Adult_08 (Factory); Amira=Female_Adult_01, Ben=Male_Adult_05 (Earthquake); Lena=Female_Adult_04, Omar=Male_Adult_10 (Flood); Triage C01-C10 cycle through the eight, standing/sitting/lying poses kept.
- **Rejected earlier:** the CesiumMan glTF sample (stylised, read as a protective suit). Moved to `_unused_assets/`, not shipped.
- Only the Rocketbox avatars were downloaded; nothing else was imported. Other Rocketbox avatars in the library were not used.

## 2. Hand tracking (controller-free)
- Packages: `com.unity.xr.hands` 1.5.0 added. OpenXR features enabled for Android by **Rescue Sim > Configure XR and Android**: Hand Tracking Subsystem and Meta Hand Tracking Aim. APK manifest contains `oculus.software.handtracking` and `com.oculus.permission.HAND_TRACKING`; `libUnityOpenXRHands.so` is packaged.
- `HandTrackingInput` attaches itself to the XR Origin at runtime (no scene edits). A tracked controller always wins; otherwise a tracked hand drives the same `NearFarInteractor` the controller would, so there is one owner and no duplicate events.
- Hand drawn as 26 joint spheres + pinch glow (Unlit; no skinned hand mesh).
- **Pinch** (thumb-index < 2.5 cm on, > 5 cm off, 0.12 s debounce, 0.35 s tracking-loss grace) = select / UI press.
- **Carry latch:** while carrying, relaxing the fingers does **not** drop the casualty; a deliberate **new pinch** (after the hand opened) puts them down.
- **Mode switching:** hand to controller (or tracking lost) keeps the casualty for a 2.5 s hand-over window, then releases normally (recoverable); picking up the other mode inside the window resumes the same carry (no second `CarryStarted`).
- Left hand: pinch-drag when not pointing at anything = walk (forward/back) and snap-turn; pinch while hovering = interact.
- Editor-only simulation: `SimulatedHandSource` (tests/recorder only; never active in a player build).

## 3. Reliable carry (all three rescue stages)
- **Root-cause fix:** casualties use a *trigger* collider, and XRI leaves trigger colliders out of an interactable's collider list and out of the near-grab sphere cast. Result: no interactor could hover a casualty. `Survivor.RegisterGrabCollider()` registers the collider and `HandTrackingInput` makes the near caster see triggers. This benefits **controllers too**.
- One owner per casualty (select filter), `Survivor.Carrying` one-carry guard, assess time 1.5 s -> 1.0 s.
- Released outside the safe zone = no credit and re-carryable; delivery credits exactly once; Rescued/Lost can not be re-selected; reset restores state.

## 4. Triage Study
Human models, IDs C01-C10 and START logic unchanged; no `Survivor` components, so nothing can be carried; a pinch/trigger only selects the casualty for assessment.

## 5. Files
- **New runtime:** `Scripts/Runtime/Hands/{HandSample,PinchDetector,CarryLatch,XRHandsSource,HandTrackingInput}.cs`, `Scripts/Runtime/Survivors/CasualtyAppearance.cs`.
- **Modified runtime:** `Survivors/Survivor.cs` (hand-over, collider registration, assess time), `RescueSim.Runtime.asmdef` (+Unity.XR.Hands, define `RESCUESIM_XR_HANDS`).
- **New editor:** `CasualtyModels.cs`, `CharacterDiagnostics.cs`, `ErrorLogToFile.cs`, `Recording/SimulatedDemoRecorder.cs`. **Modified editor:** `Prefabs.cs`, `SceneBuilder.cs`, `SceneBuilder.Triage.cs`, `XRProjectSetup.cs`, `CaptureViews.cs`, `AndroidBuild.cs` (output name `DisasterResponseVR_HumansHands.apk`), `RescueSim.Editor.asmdef`.
- **Scenes regenerated:** MainMenu, FactoryRescue (+NavMesh), EarthquakeRescue, FloodEvacuation, TriageSimulation. Prefab `Survivor.prefab`; materials in `Materials/Characters/`, `Mat_CasualtyHuman.mat`.
- **Config:** `Packages/manifest.json` + lock, `Assets/XR/Settings/OpenXR Package Settings.asset`, `ProjectSettings/*`. Unity also re-serialised the sample XR Origin prefab (`m_Priority` lines) and URP settings: noise, review before committing.
- **Tests:** `Tests/EditMode/HandAndCharacterTests.cs`, `Tests/PlayMode/HumanAndHandTests.cs` (+asmdef references).

## 6. Test results (macOS editor target, 2026-10-29)
| Suite | Result |
|---|---|
| EditMode | **121 / 121** pass (109 existing + 12 new: pinch detector, carry latch, simulated source, avatar prefab checks, licence present, appearance variants, OpenXR hand features enabled for Android) |
| PlayMode | **57 / 57** pass (43 existing + 14 new) |

The 15 requested checks and where they are covered:
| Requested check | Test |
|---|---|
| Scenes use the intended human models; IDs valid | `Stages_UseHumanModels_AndKeepCasualtyNames` (x3), `Triage_UsesHumanModels_KeepsIds_AndHasNoCarrying` |
| Selection for assessment; triage select and START tagging | `Triage_HandPinch_SelectsACasualty_WithoutMovingIt`, existing triage suites |
| Grab path; follows the carry anchor | `Hand_Pinch_GrabsAndCarries_ThroughTheRigsInteractor`, `Carried_FollowsAnchor_*` |
| Release outside gives no credit; dropped casualty recoverable | `Hand_NewPinch_ReleasesOutsideZone_NoCredit_AndCasualtyCanBeRecovered` |
| Delivery credited once; rescued can't be re-scored | `Hand_Delivery_InsideSafeZone_CreditsExactlyOnce` |
| Hand and controller paths don't duplicate; mode switch doesn't detach | `SwitchingToControllers_*`, `SwitchingBackToHands_*`, `BothHandsPinching_StillOneOwner_AndOneCarry` |
| Tracking loss | `Hand_TrackingLoss_ShorterThanGrace_DoesNotDrop` |
| Latch | `Hand_Carry_StaysLatched_WhenFingersRelax` |
| Reset restores states | `Hand_Restart_ResetsCasualtyAndInputState`, `Reset_RestoresInitialState_AndClearsCarry` |
| Extinguisher and debris still work | `Factory_ExtinguisherAndDebris_StillGrabbableAndWired`, campaign playthroughs |
| Scenes load without missing refs | `CampaignSceneTests`, `TriageSceneTests` |

Caveats: the first PlayMode run after an editor crash/restart showed three timing-sensitive failures (a one-frame carry-follow check and the Factory and Earthquake playthroughs) that passed on the next run; the editor itself crashed twice mid-run (mono crash dumps are in the project root, not committed).

## 7. Build
`Builds/Android/DisasterResponseVR_HumansHands.apk`, **249,530,021 bytes**, SHA-256 `819fcc430dcdff846acafd00ee5d9b49e0e312ab5df97213533b7afa2c103ca5`, built 2026-10-29 with Unity 6000.3.21f1 (full build 8 min 55 s). Result Succeeded, 359 warnings, 1 error line: the same `NullReferenceException` in the OpenXR Meta Quest validation callback as the previous build (unresolved, build still completes). The previous known-good `DisasterResponseVR.apk` (242,635,956 bytes, SHA `a533c6d0...`) is untouched. Debug-signed, IL2CPP ARM64.

## 8. Control guide
| Action | Hands (no controllers) | Quest controllers |
|---|---|---|
| Point / press UI | point at panel, **pinch** | ray + trigger |
| Hold-to-assess a casualty | reach to the casualty (hand inside the highlighted body) and **keep pinching ~1 s** | near/ray + grip, hold ~1 s |
| Lift and carry | keep pinching until the casualty lifts; then **relax your hand, they stay held** | keep holding grip |
| Put down | open the hand, then **pinch again** inside the green safe zone to deliver (anywhere else = put down, no credit, pick up again) | release grip |
| Walk | left hand: pinch and drag forward/back when not pointing at anything | teleport / thumbstick |
| Turn | left-hand drag sideways | right thumbstick snap turn |
| Triage select | pinch a casualty to select, then use the panel | trigger |
| Switch input | put controllers down / pick them up; casualty is held through a 2.5 s hand-over | |
| Editor keys (no headset) | Meta XR Simulator keyboard profile: see README | |

## 9. Needs physical Quest testing (all unverified)
1. Real hand-tracking quality: pinch thresholds (2.5/5 cm), debounce, loss grace, and false pinches in smoke/low light.
2. The shoulder-based aim for the hand ray and pointing at UI panels with a pinch.
3. Whether pinching reliably hovers the casualty's grab volume (box volume sized for controllers) at arm's length.
4. Left-hand walk/turn comfort and accidental locomotion while interacting.
5. The hand-over window when switching hands/controllers on real hardware.
6. Frame rate on Quest 2 with up to 10 casualties of 7-10k triangles each (Triage), plus the 26-sphere hand drawing. No LODs were made; reduce the mesh if the frame rate drops.
7. The Unlit hand material (found by `Shader.Find`, falls back to Sprites/Default): confirm hands are drawn in colour on device.
8. APK install, the manifest hand-tracking entries/permission prompt, comfort and lighting of the human models (lying pose under real lighting).
9. The one OpenXR Meta Quest validation `NullReferenceException` at build time.
