# EXPERIMENTER GUIDE

## Opening the menu
- Headset: hold **both grips** and press the **menu button**.
- Editor: **F10**.
- The game pauses while the menu is open. Menu button alone (Editor key **F**) is the normal pause.

## Menu items
| Item | What it does |
|---|---|
| Participant ID (-10 / -1 / +1 / +10) | Changes `participant_id` in `StudyConfig.json`. The shown order (e.g. `S-A then M-B`) follows `participant_number mod 4`. |
| Restart | Resets session, telemetry and oxygen, returns to SCN_Setup. |
| Export | Writes the session JSON and appends unwritten telemetry rows to `deepsleep_telemetry.csv` in `Application.persistentDataPath`. |
| Profiler ON/OFF | Toggles the on-device frame statistics capture. |
| Jump to stage (Editor only) | Pick a stage with the arrows and press Go. Not available on a headset. |
| Debrief note | Free text for the debrief. On a PC keyboard just type and press Enter; on the headset press "Type on device keyboard". |

## Before a session
1. Check `StudyConfig.json` in persistentDataPath. If it was missing, a template is written and SCN_Setup shows a warning panel. Edit it or use the menu to set the participant ID, then press Restart.
2. The order for participant N is by `N mod 4`: 0 = S-A then M-B, 1 = S-B then M-A, 2 = M-A then S-B, 3 = M-B then S-A (S = spatial HRTF, M = stereo).
3. Run the spatializer check and level-match steps in `TEST_PROTOCOL.md`.

## Files written (persistentDataPath)
`StudyConfig.json`, `deepsleep_telemetry.csv` (one row per block), `deepsleep_session_<id>_<session>.json`, `profiler_log.csv` when profiling.
