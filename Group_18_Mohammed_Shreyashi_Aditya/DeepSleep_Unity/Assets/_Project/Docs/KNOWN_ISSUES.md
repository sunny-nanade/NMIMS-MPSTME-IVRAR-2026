# KNOWN ISSUES

<!-- placeholders:start -->
## Placeholder audio
None. Every trigger ID has a real file.
<!-- placeholders:end -->

<!-- general:start -->
## Primitives in use (no asset exists)
- Teleport marker (flat cylinder) and comfort-vignette quad, both created at runtime by PlayerLocomotion.
- Phase 1 test area cubes/capsule were removed in Phase 2.

- Service door pull handle, coolant panel, port sockets and rack lever: capsule / cube primitives (no assets exist for them).

## Decisions and deviations
- Docs are in `Assets/_Project/Docs`, not a top-level `Docs/` (keeps them inside the Unity project).
- PF_PlayerRig is the whole Building Block camera rig root (OVRCameraRig + controller visuals), not only the child interaction rig, because the child needs the camera rig as parent. The Interaction SDK interactor web was removed from it; grab, poke and UI ray are implemented in `HandInteractor` on top of `InputService`.
- Unity 6000.5 marks `objectReferenceInstanceIDValue` obsolete; the validator uses `objectReferenceEntityIdValue`.
- SDK controller models leave renderer material slots empty on purpose; the validator skips those slots.
- In the Editor with no headset OpenXR still reports an active XR device; `InputService` therefore trusts only `OVRManager.isHmdPresent` in the Editor. `EditorFallbackRig` removes `OVRCameraRig` from the rig in that case so the simulated 1.70 m head height cannot override EYE_HEIGHT.
- Ambience ducking is done with a Normal/Ducked mixer snapshot pair (-8 dB on Ambience) instead of a sidechain Duck Volume effect.
- Medical Bay and Setup lighting are realtime (a few unshadowed point lights); baking is deferred. Smoke room will use fog and baked/cheap light.
- Wall slabs from the pack show glass windows with nothing behind them; accepted as ship-corridor glazing.
- Assets/_Project/Scripts/Editor/AssetGallery.cs is a throwaway debug helper and can be deleted.
- Unity crashes under play-mode load on the owner's machine: play checks are kept minimal and scenes saved after every edit.
- In the Editor the SDK controller meshes render magenta (their materials are assigned at runtime on device); gameplay is unaffected.
- Spatial (HRTF) rendering cannot be verified in the Editor: the checks assert the source settings (3D, spatialize on, Meta XR Audio selected in Audio settings) only.
- SCN_SmokeRoom contains an inactive PF_PlayerRig used only when the scene is opened on its own.
- Meta XR Simulator: entering play mode with XR initialised launched MetaXRSimulator.exe on this PC. Standalone XR init is now off (Assets/XR/XRGeneralSettingsPerBuildTarget.asset). A simulator process that was already running stays running until closed by its owner.
- The level match is an RMS measurement of the Editor audio output (Meta XR Audio, stereo output). Repeat it on the headphones or headset audio used in the study and confirm by ear (TEST_PROTOCOL.md section 4).
- Frame rate on Quest was not measured (no headset in this session). Editor profiler runs include multi-second scene-load hitches that do not occur on device.
- Bot telemetry (Analysis/data/bot_telemetry.csv) is simulated data that ignores the guidance condition; it must not be reported as study data.

## Polish pass 2026-10-18
- Ceiling light panels in SCN_Setup, SCN_MedicalBay and SCN_HallwayDebrief are emissive cube primitives (`CeilingPanels` root, M_CeilingPanel); no asset exists for them. Teleport marker, comfort vignette, handle, panel, sockets and lever remain primitives as above.
- GuideHud panels (orientation hologram and goal card) are quads with the code-written HologramPanel shader (M_Hologram in Resources).
- The 303 MB texture skybox (Starfield Skybox) was replaced by the code-written StarSky shader (M_StarSky) and deleted. Reflections come from a dark cubemap (CUBE_DarkReflection) because mirrored stars showed as white speckle on glossy surfaces.
- Unused assets deleted on the owner's instruction: monitor, USB hub, Energy, extra Sci Fi Modular Pack, Barking_Dog kit, TextMesh Pro examples, tutorial files, _Recovery scenes, demo scenes.
## Rework 2026-10-19 (owner playtest feedback)
- Primitives added (no asset exists): beacon game tables, slot rings, beacon barrels and lenses, poster frames and discs (`EarCheckController`, `PosterBuilder`); cable plugs and boots (`CoolantCable`); exit door glow frame and handle (`MazeBuilder.BuildExitGlow`). The cup is a lathe mesh (`MeshFactory.Cup`), the cable wire a tube mesh (`MeshFactory.Tube`), both generated in code.
- The Ear Check is now a beacon-sorting game (colour order on a poster, no spatial audio). The telemetry columns `earcheck_mean_error_deg` / `earcheck_mean_rt_s` stay in the CSV header but are no longer filled; `beacon_game_time_s` and `beacon_wrong_placements` were added. The "Ear Check error" measure of the study no longer exists.
- Rose's lines R030 / R031 / R032 were recorded for the old pointing task. They are reused as start / after each correct beacon / end of the beacon game; check by ear that the wording still fits (re-record if it mentions sounds or pointing).
- HALCYON is pitched down by AudioSource.pitch 0.78, so his lines are also about 28 % slower. Rose in the smoke blocks is +4 dB (both conditions, so the level match is unchanged).
- Held objects switch their colliders off while in the hand (they used to block the teleport arc and push the player capsule); all grabbables use speculative continuous collision.
- Deleted as unused (reachability scan over scene, prefabs, Resources): large GLB models (pods, terminal, cable, tablet, lab machine, shelving, scanner), gurney, beacon pucks, cup and tray, server, Sci Fi Modular Pack Box3, monitor, Docs/Screens, PF_Beacon_01..06. The one-off scene builders (MedicalBayBuilder, Phase3Builder, Phase5Builder, SmokeRoomBuilder) still name some of these paths and can no longer rebuild the old scenes; SCN_Main is the source of truth.
<!-- general:end -->

## Final-final 2026-10-20
- Nothing was committed or tagged in this round (owner's instruction); the changes are in the working tree. The tag `v1.2-final` was not made.
- Not measured on a Quest: frame rate, thermal behaviour, Meta XR Audio HRTF rendering, controller haptics. See HEADSET_DAY_CHECKLIST.md.
- Lighting is realtime (a key light, one pulse light per section, light-pool quads); baked lighting is impossible while all sections share one scene.
- Lattice of bright dots on some smoke-room walls in free-camera Editor shots is not removed (pack mesh and material). Check it in the headset.
- The timeline timing diff (`Tools/flow_diff.py`) allows up to 2.0 s from the end of the previous line to the next start and R031 at least once: the x4 bot places beacons faster than Rose's R031 lines can play, so late R031 requests are dropped by design (VoiceRules). A human places a beacon every 10 s or more.
- `Assets/_Project/Scripts/Runtime/Testing` is compiled into builds (all Editor-only parts are inside `#if UNITY_EDITOR`); it is not used at run time.
- R030, R031 and R032 were replaced by the owner; their import setting "preload audio data" was switched on to match the other clips (the validator requires it).
- The `Recorder` MP4 is made in real time from the Game view in the Editor, so it shows the Editor fallback rig, not the headset view.
- The Android development build (Builds/Quest.apk) and the Meta Project Setup Tool list were skipped on the owner's request; nothing about Quest build or install was tested.
- The v3 recording (Builds/Recordings) was not made, on the owner's request; the partial file from the stopped run was deleted. Earlier recordings (v1, v2) are unchanged. Standalone XR init is back ON.
