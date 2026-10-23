# FINAL REPORT: Protocol: Deep Sleep

Single-player Unity VR escape room for Meta Quest, built to test whether spatial (HRTF) voice guidance cuts evacuation time and wrong turns in smoke-filled, low-visibility egress (stakeholder: Mumbai Fire Brigade). Unity 6000.5.2f1, URP, Meta XR SDK, OpenXR, namespace `Fractured_Final`, log prefix `[DeepSleep]`. Nothing was pushed; all work is in local git commits (`Phase 1 done` to `Phase 7 done`, plus WIP commits).

## What exists

| Part | Content |
|---|---|
| Scenes (build order) | SCN_Setup (6 x 6 x 3 m alcove), SCN_MedicalBay (8 x 6 x 3 m, plus a dark vestibule), SCN_SmokeRoom (layouts A and B baked, loaded additively), SCN_HallwayDebrief (2 x 9 m) |
| Stages | Setup, Awakening (cup + one teleport), Ear Check (six beacons), Thermal Keypad (7, 3, 5), The Trail (11 decals, panel, handle at 1.0 m, service door), Block 1, TLX, Block 2, TLX, Coolant Reroute (red 2, blue 5, yellow 3, lever 1.1 m), Escape (blast doors), SUS, preference, plaque, export. No fail state; oxygen is cosmetic |
| The manipulation | Rose's block calls come from the exit door through one source. Spatial = 3D through Meta XR Audio. Stereo = 2D, identical in both ears. Same mono sample data, same code-side distance gain, stereo level-matched by the measured trim (-8.1 dB) |
| Measures | evacuation time, wrong turns, dead-end entries, backtracks, path length and efficiency, wall contacts, idle time, timeout flag, head-turn latency, raw TLX (six scales + mean), SUS, Ear Check error and reaction time, frame rate (mean, min, 1 % low), stage times and errors (`deepsleep_telemetry.csv`, one row per block) |
| Maze | Python verifier ranked 7,140 pairs from 120 valid layouts. A and B: 6 x 6 cells of 2 m, unique 36 m shortest path, 6 junctions on it, 3 dead ends, 2 misleading junctions, 2 loops |
| Input | only `InputService` reads input; `EditorFallbackRig` + `ScriptedPlayer` drive everything without a headset |
| Audio | NarrativeVoiceDirector (Rose comms), HalcyonDirector (ceiling speaker), MentorDirector (block calls), SfxDirector (SFX map, ambience per scene, -8 dB duck), all 65 manifest IDs resolved by ID |
| Tools | `Tools/MazeLayoutVerifier.py`, `analysis.py`, `generate_figures.py`; Editor menus under DeepSleep (Validate, Run Logic Tests, Run Check, Run Playthrough, Build, Level match, Jump to Stage) |
| Docs | PROGRESS, KNOWN_ISSUES, DECISIONS_APPLIED, SCENE_FLOW, EXPERIMENTER, TEST_PROTOCOL, ANALYSIS, AUDIO_TRIGGERS, ASSET_AUDIT, this report |

## Evidence (final state of the project)

| Check | Result |
|---|---|
| Validate Project | PASS, 48 checks |
| Edit-mode logic tests (NUnit) | 22 / 22 |
| Phase 3 scenario (includes Phase 2: setup, medical bay, awakening) | 61 / 61 |
| Phase 4 scenario (both blocks through the real entry, spatial then stereo, timeout, telemetry) | 46 / 46 |
| Phase 5 scenario (TLX x2, rack, wrong plug lockout, lever, hallway, SUS, debrief, exports) | 41 / 41 |
| Phase 6 scenario (voices, queue, ducking, SFX map, ambience per scene, 65 manifest IDs) | 18 / 18 |
| Playthrough bot, 8 participants | every stage in order, 16 complete telemetry rows, condition and layout match the order, 50 / 50 triggered manifest IDs reached |
| Level match | Meta XR Audio active; stereo trim -8.1 dB, flat from 1 to 20 m |
| Analysis | `--selftest` finds the planted effect; bot data analysed and plotted |
| Camera height | 1.650 m in every scene, never offset in code |

## Deviations and judgement calls (details in DECISIONS_APPLIED.md and KNOWN_ISSUES.md)

- Docs live in `Assets/_Project/Docs`; Python tools in `Tools/`; analysis outputs in `Analysis/`.
- The wall pieces have glass windows in the bay and hallway; the smoke room uses plain metal slabs. Lighting in the bay is realtime (a few unshadowed lights), not baked.
- Primitives: teleport marker, comfort vignette quad, service-door handle, coolant panel, port sockets, rack lever. The cable is the usb_type_c_cable model scaled to 0.5 m.
- Ambience ducks through a Normal / Ducked snapshot pair instead of a sidechain.
- `R001` to `R003` (setup calibration lines) are not used: the setup panel plays the S002 tones. `R031` and `R201` have one file each although the manifest says three variants. `S062` is a placeholder clip.
- Standalone "Initialize XR on Start" was turned off (Assets/XR/XRGeneralSettingsPerBuildTarget.asset) because play mode launched `MetaXRSimulator.exe`. Device settings are unchanged.
- One Editor crash happened during Phase 4 play checks; the likely cause (a duplicate OVR rig in the additive smoke scene) was removed and no crash followed.
- Real bugs found by the scripted runs and fixed: poke tip blocked the UI ray; UI colliders 0.75 m deep; poke + ray double press on keys; overlapping key colliders; grip edges polled too early; interpolation overrode cable resets; idle nudge starved behind beacon calls; HandInteractor and text scale tuning.

## What still needs a person or a headset

1. Listen to the four-tone check, spatial vs stereo and the level match on the study headphones (TEST_PROTOCOL.md sections 3 and 4). The measurement is an RMS of the Editor output, not a perceptual loudness match.
2. Run on a Quest: frame rate (72 fps target, profiler on), comfort of smooth move with the vignette, controller visuals (they render magenta in the Editor), real hand reach to the keypad, ports, handle and lever.
3. Pilot with a few people before the study: timing (about 18 minutes), clarity of Rose's lines, difficulty of the two layouts.
4. Close `MetaXRSimulator.exe` if it is still running (it was started by play mode before the XR setting was changed).
5. Treat `Analysis/data/bot_telemetry.csv` as simulated: it ignores the condition and says nothing about the hypothesis.

## Run it

```
Unity: DeepSleep > Validate Project | Run Logic Tests | Run Check > Phase 3 / 4 / 5 / 6 | Run Playthrough > ... | Run Check > Level match
python Tools/MazeLayoutVerifier.py verify Assets/_Project/Data/MazeLayoutA.json Assets/_Project/Data/MazeLayoutB.json
python Tools/analysis.py --selftest ; python Tools/analysis.py ; python Tools/generate_figures.py
```
