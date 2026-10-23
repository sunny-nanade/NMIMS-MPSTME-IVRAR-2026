# PROGRESS

Docs live in `Assets/_Project/Docs`. Re-read CLAUDE.md and this file at the start of every phase.

## Phase 1: Foundation. DONE
- Folders, tags (8), layers (2), scenes moved/created and in Build Settings in order (SCN_Setup keeps its GUID).
- Audit: `ASSET_AUDIT.md`. Audio indexed by ID into `Data/voice_manifest.csv` and `Resources/DeepSleepAssets.asset`; MIX_Main built (groups + Normal/Ducked snapshots).
- `PF_PlayerRig` from the Building Block rig. Distance grab and the ISDK interactor web were removed (they threw errors and are not allowed); interaction is own code driven by `InputService` (grab, poke, UI ray). HoverOutline + ComfortFade shaders in HLSL.
- Scripts: InputService, EditorFallbackRig, PlayerRig, PlayerLocomotion, Grabbable, HandInteractor, PokeTarget, RayTarget, SessionManager, SceneFlow, PauseManager, ConditionRandomizer, TelemetryLogger, ExperimenterPanel, OxygenSystem, SaveLoad, ProfilerCapture (stub), Bootstrap, StageJump, ScriptedPlayer, PlayCheckRunner, editor tools (ProjectValidator, DeepSleepMenu, builders).
- Checks: `Reports/phase1_check.md` 13/13 pass (camera 1.650 m, table top 0.75 m below camera, grab/release/hover outline, teleport, 45 degree snap turn, stages advance in order and skipping is rejected). Validate Project passes. No script outside InputService reads input (validator rule).
- Open: temporary test area still in SCN_Setup (removed in Phase 2). ProfilerCapture is a stub until Phase 7.

## Phase 2: Setup scene, Medical Bay, lighting. DONE
- Runtime: SetupController (consent text from DECISIONS, height notice, S002 four-tone facing check, volume slider that locks, comfort vignette toggle, StudyConfig warning, advances to Awakening and loads SCN_MedicalBay), AwakeningController (R010/H001 on arrival, H002 at 20 s, R011 near tray, R012 5 s later, completes on cup grabbed + one teleport), VoiceBus + ComfortSettings, EmergencyLighting (2 s pulse, flicker, sparks off in smoke room), OxygenDisplay (wall + wrist, cosmetic).
- Editor: SceneBuilderUtil (bounds-based placement, shells with 0.9 x 2.1 m openings), MedicalBayBuilder (menu DeepSleep/Build: Medical Bay, Setup Alcove, Wrist Oxygen On Rig). Scenes are rebuilt idempotently by these menus.
- Fix found by the check: HandInteractor poke tip moved to Ignore Raycast so it no longer blocks the UI ray.
- SCN_Setup: 6 x 6 x 3 m alcove with cryo pods, test area removed. SCN_MedicalBay: 8 x 6 x 3 m, bed, side table + tray + grabbable cup, terminal, keypad pedestal + Keypad model, server rack, wall panel in front of the service door, main (welded) door, red pulse lights, wall oxygen readout, PlayerSpawn, CoreRoomSpawn. PF_PlayerRig has the wrist oxygen readout.
- Checks: `Reports/phase2_check.md` 28/28 pass (bot on the Editor fallback rig; ray click, four tones, slider, scene load, scale table, oxygen, light pulse, awakening completion). Validate Project passes (25/25).
- Open: H002 timing not waited in the bot run; lighting in the Medical Bay is realtime (small room, no baking yet); wall slabs have glass windows from the pack. Trail decals, keypad logic and wall panel opening come in Phase 3.

## Phase 3: Ear Check, Thermal Keypad, The Trail. DONE
- Runtime: BeaconVisual + EarCheckController (ring to stand on, R030, six spatial S001 pings in seeded shuffled order, correct if ray hits beacon or error within 15 degrees, blue/red feedback, R031 per trial, R032, logs earcheck_mean_error_deg / earcheck_mean_rt_s, then starts the keypad), ThermalKeypad (7, 3, 5 coolest to hottest with dull red / orange / yellow-white glow, 2 s lockout, hint pulse for 4 s after every 3rd failure, R041 idle 30 s, H004 at most once per 30 s, 0.25 s press debounce, poke and ray both work, solves to KeypadSolved with R043 and telemetry), BreachTrail (11 emissive arrow decals fading in along one route, panel slides away on arrival, pull handle at 1.0 m opens the service door, R050 / R052, trail_time_s, advances to TrailFollowed).
- Prefabs: PF_Beacon_01..06 (Sci-Fi Barrel Grey / Blue / Red, 0.4 m), PF_Keypad (asset Keypad prefab with its own input scripts removed). Menu: DeepSleep/Build/Phase 3 Prefabs; MedicalBayBuilder adds the Phase 3 objects and a dark vestibule behind the service door.
- Fixes found by the run: key colliders overlapped (shrunk), poke + ray double press (debounce), WorldUI.FontScale tuned to 0.45 (TMP size 1.0 is about 11 cm), Setup panel and oxygen readout enlarged.
- Checks: `Reports/phase3_check.md` 61/61 pass (includes the Phase 2 checks; one chained bot run from SCN_Setup). Validate Project passes. Screenshots of each step were reviewed.
- Open: Editor-only magenta controller meshes (SDK controller materials are assigned at runtime on device); H002 20 s timing not waited; beacon azimuth is measured from the head forward when the trials start.

## Phase 4: Smoke room and guidance audio. DONE
- Tools/MazeLayoutVerifier.py (generate / verify, stdlib only): 120 valid layouts from 445k trials, 7140 pairs ranked, top pair written to Data/MazeLayoutA.json and B.json; report in `Reports/maze_layout_report.md`. Both layouts: 6 x 6 cells of 2 m, unique shortest path 18 steps = 36 m, 6 junctions on it, 3 dead ends, 2 misleading junctions, 2 loops. The C# `MazeLayout.Verify()` reproduces the same numbers.
- Runtime: MazeLayout (+ data classes), MazeBuilder (activates baked layout, exposes exit anchor / zone / door), SmokeVisibility (exp-squared fog for 2 m visibility, 1.5 m helmet lamp), AudioConditionSwitch (Spatial = 3D source through the Meta XR Audio spatializer; Stereo = 2D centred; same mono sample data, same code-side distance gain, stereo gets the level-match trim), MentorDirector (R201 every 4 s, junction R202 to R204, dead end R205, idle R207 at 20 s, near exit R208, R209 / R210; head-turn latency per call), PathTracker (wrong turns, dead-end entries, backtracks, path length, efficiency, idle), EgressBlockController (plan from ConditionRandomizer, timer, 4 min timeout, telemetry, return to CoreRoomSpawn, R150 / R151 after block 2), BlockLauncher (entry zone in the vestibule).
- Editor: SmokeRoomBuilder (DeepSleep/Build/Smoke Room: both layouts from Metal Wall2_3 / Floor2_2 / Ceiling2_2 / Door2 pieces, walls 2.6 m, door gaps 0.9 x 2.1, Junction / DeadEndCap / EntryZone / ExitZone tagged triggers, static batching flags); MedicalBayBuilder adds the EntryZone + BlockLauncher in the vestibule.
- Fix: the rig stored in SCN_SmokeRoom is kept inactive and only enabled when that scene runs on its own (a duplicate OVR rig in an additive load is the likely cause of one Editor crash during the first run).
- Checks: `Reports/phase4_check.md` 45/45 pass (both blocks through the real entry zone: S-A then M-B, wrong turn / dead end / backtrack counting, telemetry rows, spatial vs stereo source settings, idle nudge, timeout, R150). Validate Project passes.
- Open: the HRTF effect itself cannot be heard or measured in the Editor (needs a headset or the level-match tool in Phase 7); Quest frame rate of the maze is unmeasured; R201 has one file although the manifest says 3 variants.

## Phase 5: Coolant Reroute, Escape, questionnaires. DONE
- Runtime: CoolantCable / CoolantPort / CoolantRack (red to port 2, blue to port 5, yellow to port 3; wrong plug = 3 s lockout and the cable returns to the table; lever at 1.1 m after all three are seated; R153, H007, H008, R160, S063 / S080 events; stage CoolantRouted then Escaped; main door slides open as the blast door, then SCN_HallwayDebrief), QuestionnaireUI (raw NASA-TLX six sliders after each block with R140 / R150 / R151; SUS 10 items; preference question; mission-log plaque with the exact text; session JSON + CSV export; R161), EgressBlockController now opens the TLX at the rack after each return and `MarkCompleted` for stage jumps.
- Editor: Phase5Builder (PF_Cable_01..03, cable table, coolant panel with 6 labelled ports, lever, rack logic, Questionnaire object, SCN_HallwayDebrief 2 x 9 m with emergency pulse lights, plaque and HallwaySpawn). CoreRoomSpawn moved to z = 0.4 so the rack and panel have room.
- Bugs found and fixed by the run: UI button colliders were 0.75 m deep (an angled ray hit the neighbouring button; now 5 cm), grip-edge polling for the lever and door handle moved to LateUpdate, Grabbable.ResetToStart now syncs the rigidbody (interpolation overwrote the reset).
- Checks: `Reports/phase5_check.md` 41/41 pass (both TLX passes with ray clicks, rack with a wrong plug and lockout, lever, hallway, SUS 100 for the all-best answers, preference, plaque text, telemetry CSV and session JSON). Phase 4 test updated for the TLX-after-block behaviour (re-run in the final regression). Validate Project passes.
- Open: PF_Cable models are the usb_type_c_cable glb scaled to 0.5 m (wire tinted red / blue / yellow); panel, sockets and lever are primitives; Hallway has no ambience yet (Phase 6).

## Phase 6: Voice, SFX and ambience integration. DONE
- Runtime: SfxDirector (SfxEvent to clip map with pooled 3D / 2D sources, 9 key pitches, ambience per scene with crossfade: bay A001, smoke A004 + A006 as mono 2D identical in both ears, hallway A001 + A005, and the -8 dB Ducked / Normal snapshot switch), NarrativeVoiceDirector (Rose comms, 2D, queued, block-only R201 to R210 ignored), HalcyonDirector (ceiling speaker, waits for Rose, H004 at most once per 30 s), AudioUtil (shared mono down-mix). PlayerLocomotion now raises Teleport, Footstep and WallContact events; MentorDirector ducks through SfxDirector. HalcyonSpeaker objects added to the bay and hallway.
- Docs: `AUDIO_TRIGGERS.md` lists every manifest ID with its trigger.
- Checks: `Reports/phase6_check.md` 18/18 pass (65 manifest IDs resolve to non-empty clips, R010 then H001 without overlap, ducking, queue order, cancel, H004 limit, SFX clip and pitch map, ambience per scene, mono smoke ambience). The audible result (levels, HRTF) still needs a human ear or the Phase 7 level-match tool.
- Open: S062 is a placeholder clip (see KNOWN_ISSUES); R031 and R201 have one file each although the manifest says 3 variants.

## Phase 7: Tests, bot, analysis, profiler, docs. DONE
- ProfilerCapture (profiler_log.csv, one row per second: fps, frame ms mean / max, share over budget, memory, GC; 11 ms Editor budget, 1 / 72 s on a device; experimenter toggle). TelemetryLogger / ProfilerCapture file-name overrides keep bot rows apart from real data.
- PlaythroughBot (ear error 10 deg, wrong-turn rate 0.15 per junction, 8 participants, time scale 3): full game from Setup to debrief through ScriptedPlayer. Extras so that every manifest ID is reached (idle at the tray, a long wait at the keypad, a wrong keypad press, a wall bump, a forced dead end, a wrong plug, one block left to time out). Menu DeepSleep > Run Playthrough (all 8, 1 to 4, 5 to 8, participant 4 + coverage).
- Results: participants 1 to 4: 28/28; 5 to 8: 28/29 (R207 never reached because a beacon call blocked it, fixed: the idle nudge now interrupts); participant 4 re-run + coverage: 8/8, **50/50 triggered manifest IDs reached**; 16 telemetry rows, none with an empty field, order and layout match participant mod 4 every time.
- Tools: MazeLayoutVerifier.py (Phase 4), analysis.py (paired tests, Holm, mixed model, timeouts, order and balance checks, `--selftest` finds a planted effect), generate_figures.py (layouts, paired outcomes, effect sizes, load and usability, frame rate). Outputs in `Analysis/` (data, results, figures).
- Level match: DeepSleep > Run Check > Level match measured the listener output for R201 from 1 to 20 m at four azimuths. Meta XR Audio is the active spatializer; the stereo condition needs **-8.1 dB** (flat within 0.4 dB over distance), stored in DeepSleepAssets (curve + mean) and applied by AudioConditionSwitch.
- Validator extended to 48 checks (required scripts, Python tools, layouts verify in C#, scene components and markers, 12 named prefabs incl. PF_ServerRack, documents). EditMode NUnit tests (22, DeepSleep > Run Logic Tests): orders, balance, constants, layouts, SUS scoring, plaque text, beacon spec, audio maps, trim curve, text scale.
- Docs: TEST_PROTOCOL.md, ANALYSIS.md, Data/StudyConfig.json template.
- Safety fix: Standalone "Initialize XR on Start" is now off. In play mode it started the OpenXR runtime and with it MetaXRSimulator.exe, which must not be used. Device (Android) XR settings are unchanged.
- Final regression on the final project state: Phase 3 incl. Phase 2 61/61, Phase 4 46/46, Phase 5 41/41, Phase 6 18/18, Validate Project 48/48, logic tests 22/22.

## Phase 8: Final report. DONE
- `FINAL_REPORT.md`: what was built, evidence table, deviations, what still needs a person or a headset, how to run everything.
- Project state: all phases done and committed; nothing pushed.

### Remaining work

None in the build plan. Open items are listed in FINAL_REPORT.md (headset checks, listening checks, pilot).

### How to work in this project (lessons learned)
- Unity MCP: use `Unity_RunCommand` (class `CommandScript : IRunCommand`, no `System.Reflection`, no types from the OVR assembly such as `OVRCameraRig`); call static editor tools from `Fractured_Final.EditorTools`. Unity does not recompile on its own when unfocused: send `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` through RunCommand after editing scripts, and do not edit scripts while in play mode.
- Bash tool heredocs with `'` inside often fail ("unexpected EOF"): write files with the Write tool, patch them with small python files (`C:\Users\shrey\scratch\patchN.py`) run from PowerShell.
- Scripted play checks: `DeepSleep/Run Check/Phase 1 rig check` pattern: `PlayCheckLauncher.Launch(scenario, scenePath, reportPath)` then wait for `<report>.done`. `PlayCheckRunner` (Runtime/Testing, Editor only) hosts scenarios and uses `ScriptedPlayer` (drives only `InputService.Scripted*`). An unfocused Editor throttles play mode; `PlayCheckLauncher.KeepTicking` queues player-loop updates. Runs take 1 to 3 minutes. Stop play mode (`EditorApplication.isPlaying=false`) before running Validate Project.
- Unity restarted twice during the first session (MCP pipe dropped once). If the MCP calls hang, check `tasklist` for Unity.exe and `Logs/Editor.log`.
- Each MonoBehaviour that is saved into a scene or prefab must be in its own file named after the class (the Phase 1 splitter script is in `C:\Users\shrey\scratch\split.py`).
- One Meta SDK gotcha: removing parts of the Interaction SDK rig piecemeal breaks it; the rig was slimmed to the OVR camera rig only (see KNOWN_ISSUES).

## Final check and recording. DONE (2026-10-16)
- Flow check with timelines (FlowTimeline, Tools/flow_diff.py), 13 fixes (voice queue, stage jumps, setup tone clipping, library, manifest), edge cases 45/45, audits and Validate Project 48/48, recorded run 13.5 min. Details: `Docs/Reports/final_check_20261016.md`.
- Added com.unity.recorder 5.1.7 (plus its automatic dependency com.unity.bindings.openimageio). Menu DeepSleep > Record Final Run; DeepSleep > Final > Edge cases / flow checks.

## Cleanup and polish (2026-10-18)
- Deleted ~1.3 GB of unused assets (unused models, Barking_Dog kit, 303 MB Starfield skybox textures, TMP examples, tutorial and _Recovery files).
- New: GuideHud (10 s orientation hologram on first waking, CURRENT GOAL card for every stage), HologramPanel and StarSky shaders (code), VoiceGate (Rose/HALCYON lines wait out the scene-load hitch, fixing the crackle at scene start), LookPolish (dark reflection cubemap, calmer pack materials, brighter ambient, ceiling light panels), ViewShot tool, Guide HUD play check (10/10).
- Checks: Validate Project 51/51; Guide HUD 10/10. The full bot playthrough was not re-run after the cleanup (stopped on request); the earlier bot run collided with a logic-test run started at the same time.

## One scene, lighter (2026-10-19)
- All four scenes are merged into `SCN_Main` (sections SEC_Setup, SEC_MedicalBay, SEC_Hallway; the smoke room is the Resources prefab PF_SmokeSection, created per block). SceneFlow now switches sections with the same fade (`SceneFlow.Section` replaces the active scene name); jumps and restart reload SCN_Main. Old scene files removed; build settings list only SCN_Main. Merge tool: DeepSleep > Merge Into One Scene (one-off).
- Lighter: every imported model mesh in the scene and prefabs is a generated box primitive (Assets/_Project/Meshes/Prim), model materials are flat colours (Materials/Prim), model prefab instances unpacked so the build no longer pulls in the GLBs, light shadows off. The model files themselves are kept in the project.
- Setup: the four-direction tone check is gone; one 2D beep (S002) confirms the audio is audible, then volume and comfort. R002 and R003 (tone-check lines) are no longer played.
- Not tested on request. Test scripts under Runtime/Testing and the old scene builders still assume separate scenes and are stale.

## Rework after owner playtest (2026-10-19)
- Mission briefing (`ObjectiveScreen`): shown once per session after Setup, before R010 / H001; objective plus the three tasks (beacons, maze, cables) without solutions, 20 s, then Rose and HALCYON start. GuideHud's controls hologram and goal card wait for it.
- Ear Check replaced by a beacon-sorting game (`EarCheckController`, `BeaconVisual`): six coloured beacons, poster of the colour order (primitives), numbered slots on a table, wrong beacon is sent back, no spatial audio.
- Keypad: number labels on all nine keys, heat keys are flat unlit plates, 7 red (least hot), 3 white, 5 blue (hottest).
- HALCYON pitched down (0.78). Rose +4 dB in the smoke blocks (both conditions). Exit door of the maze has a glowing green frame and handle.
- Cables rebuilt (bent tube, plugs, capsule collider, start on the table, speculative CCD); cable poster with port numbers in the cable colours; cup is a real mug in cyan.
- Held objects no longer block teleport or movement (colliders off while held).
- Checks: edit-mode renders of every new prop (Analysis/screens/review/p_*.png, tool `LookPreview`). No play-mode run: the Meta XR Simulator must stay off and Standalone XR init is on, so the scripted scenario `DeepSleep > Run Check > Look` (PlayCheckLook) was written but not executed.
- Cleanup: unreferenced models and screenshots removed (see KNOWN_ISSUES).

## Final-final (2026-10-20)
- Status: done except where listed in KNOWN_ISSUES.md (no commit or tag, by instruction).
- Part 1 polish: lighting per section (`SectionLook`), decor, post volumes, Quest URP settings, shader scan clean; see Reports/polish_summary.md and polish_stats_*.md; screenshots in Screens/polish.
- Part 2 bug hunt: bot flow for participants 1 to 4 (x4 time scale): 36 passed, 0 failed, 0 console errors or warnings. Edge cases (pause mid-line, restart mid-line, stage jumps both ways twice, keypad idle, wrong plug during a line, reload twice, 4-minute timeout, telemetry flushed twice): 45 passed, 0 failed. Audio levels audit: 19 passed, 0 failed; timeline timing diff: 0 mismatches over 4 participants.
- Fixes found by the hunt: trail bot teleport blocked by the terminal (bot only), bot clicked a same-named button in another section (bot only), `EarCheckController` subscribed to `StageChanged` twice (fixed: unsubscribe before subscribe), audit and edge tests still assumed four scenes (updated), validator expected four scenes (now SCN_Main plus PF_SmokeSection) and warned about a second OVRCameraRig when loading prefab contents (now reads the asset), R030 to R032 import settings (preload on).
- Part 3: DeepSleep > Validate Project: PASS, 39 checks, 0 failed, no warnings.
- Docs added: HEADSET_DAY_CHECKLIST.md, Reports/polish_summary.md.
- Android build and Project Setup Tool list: skipped on request.
- Recording, ffprobe verification and copy to Downloads: skipped on request. Standalone XR init restored to ON. Smoke fog colour (0.24, 0.20, 0.16, warm dark brown) checked: set on purpose in SmokeVisibility, unchanged.
