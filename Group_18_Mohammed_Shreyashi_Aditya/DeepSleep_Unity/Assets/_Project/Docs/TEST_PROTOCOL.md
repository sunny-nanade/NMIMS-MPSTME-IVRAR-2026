# TEST PROTOCOL

Everything below except section 3 and 4 runs on a PC with no headset (Editor fallback rig + scripted player). Use **DeepSleep** menu items in the Unity Editor. Do not use the Meta XR Simulator: Standalone "Initialize XR on Start" is switched off so play mode never starts it.

## 1. Software checks (no headset)

| Step | Menu / command | Pass condition |
|---|---|---|
| Static validation | DeepSleep > Validate Project | PASS (tags, layers, build order, no missing scripts or references, manifest IDs, input only in InputService, rig height, scripts, layouts, scene contents, prefab names, documents) |
| Logic tests | DeepSleep > Run Logic Tests | all pass (`Reports/logic_tests.md`) |
| Layouts | `python Tools/MazeLayoutVerifier.py verify Assets/_Project/Data/MazeLayoutA.json Assets/_Project/Data/MazeLayoutB.json` | PASS PASS |
| Phase scenarios | DeepSleep > Run Check > Phase 3 (includes Phase 2), Phase 4, Phase 5, Phase 6 | 0 failed in each `Reports/phaseN_check.md` |
| Full playthrough | DeepSleep > Run Playthrough > Participants 1 to 4, then 5 to 8 | 0 failed; telemetry rows complete; every triggered manifest ID reached |
| Analysis pipeline | `python Tools/analysis.py --selftest` then `python Tools/analysis.py` and `python Tools/generate_figures.py` | selftest PASS; reports in `Analysis/` |

Unity can crash under play-mode load. Save scenes first, run one scenario at a time, and re-open the project if needed: the scenarios start from saved scene files.

## 2. Frame rate (Editor and device)

- Target: 72 fps on Quest (13.9 ms). The Editor budget is 11 ms (`DS.EDITOR_FRAME_BUDGET_MS`).
- Turn on the profiler (experimenter menu > Profiler, or `ProfilerCapture.Enabled`). One row per second goes to `profiler_log.csv` in `Application.persistentDataPath`: fps, mean and worst frame time, share of frames over budget, managed and allocated memory, GC count.
- Per-block `mean_fps`, `min_fps` and `p1_low_fps` are also written to `deepsleep_telemetry.csv`.
- On the headset: run Block 1 and Block 2 once with the profiler on. Pass when at least 95 % of seconds are within budget in the smoke room and nothing drops below 60 fps. If not: lower the lamp range cost first, then reduce wall piece count (static batching is already on).

## 3. Spatializer check (headphones, a person listening)

1. Project Settings > Audio: Spatializer Plugin = **Meta XR Audio** (the validator checks the audio manifest, this setting is in `ProjectSettings/AudioManager.asset`).
2. In SCN_Setup the audio check plays four tones (front, left, behind, right) through the spatializer. The listener must turn toward each and pull the trigger. If the experimenter hears them all in the centre, the spatializer is not active: stop and fix before any session.
3. In a block, Rose calls from the exit door. Spatial: turning the head changes where the voice appears. Stereo: the voice stays in the middle of the head whatever the head does. Swap the headphone sides to confirm.
4. Ambience in the smoke room (A004, A006) is mono and must sound identical in both ears in both conditions.

## 4. Level match (spatial vs stereo)

The conditions must be equally loud so that only spatialization differs.

1. Open the project, wear or place headphones on the PC (an audio device must be active).
2. DeepSleep > Run Check > **Level match (spatial vs stereo)**. It plays R201 from 1, 3, 6, 10, 15 and 20 m at four azimuths in both conditions and measures the RMS of the listener output.
3. DeepSleep > **Apply Level Match** stores the trim curve in `DeepSleepAssets` (`stereoGainTrimDb` and the distance curve used by `AudioConditionSwitch`).
4. Result recorded in `Analysis/data/level_match.json` and `Reports/level_match.md`. Latest run: the stereo condition needs about **-8.1 dB** (flat within 0.4 dB from 1 m to 20 m; spatial level varies 1.8 dB across azimuths).
5. Repeat after any change to R201, the mixer, the spatializer settings or the headset/audio chain. RMS is a first-order match: a final check by ear (alternate S and M on the same call) is recommended.

## 5. Running a participant

1. Set the participant ID in `StudyConfig.json` (or the experimenter menu); the order follows `participant mod 4`: 0 = S-A then M-B, 1 = S-B then M-A, 2 = M-A then S-B, 3 = M-B then S-A.
2. Headset on, standing, floor tracking. The setup scene shows consent, a height notice, the four-tone audio check, volume and the comfort vignette option.
3. The session takes about 18 minutes: Awakening, Ear Check, Thermal Keypad, Trail, Block 1, TLX, Block 2, TLX, Coolant Reroute, Escape, SUS and debrief. There is no fail state; a 4-minute block timeout is logged (`timeout_flag`).
4. Experimenter menu: hold both grips and press the menu button. Use Export after the debrief (also automatic at the end).
5. Pull `deepsleep_telemetry.csv`, `deepsleep_session_*.json` and `profiler_log.csv` from `Application.persistentDataPath` (on Quest: `/sdcard/Android/data/<package>/files/`) with adb.
6. Stop at once if the participant feels unwell; the headset coming off ends the session and only the participant number is stored.

## 6. After data collection

`python Tools/analysis.py --csv <your telemetry csv> --label "Study N = ..."` then `python Tools/generate_figures.py --csv <csv>`. See `ANALYSIS.md`.
