# SCENE FLOW

Handled by `SceneFlow` (persistent system object created by `Bootstrap`). Every transition fades to black, loads, places the
player on the floor and fades in. The rig root is always on the floor, the tracking origin is Floor, so the head sits at the
real standing eye height (1.65 m in the Editor fallback rig).

| Stage entered | Scene | How loaded | Player placed at |
|---|---|---|---|
| Setup | SCN_Setup | first scene | `PlayerSpawn` |
| Awakening, EarCheck, KeypadSolved, TrailFollowed | SCN_MedicalBay | single load from Setup | `PlayerSpawn` |
| Block1, Block2 | SCN_SmokeRoom | **additive** over MedicalBay; MedicalBay root objects are hidden (rig and `KeepActiveWhenHidden` objects stay) | `SmokeEntrySpawn` (set by MazeBuilder per layout) |
| (after Block 2 TLX) CoolantRouted | SCN_MedicalBay | SmokeRoom unloaded, MedicalBay shown again | `CoreRoomSpawn` |
| Escaped, Questionnaire | SCN_HallwayDebrief | single load | `HallwaySpawn` |

Rules
- Stages advance one at a time (`SessionManager.Advance`); skipping is rejected with a warning. Editor and experimenter jumps use `JumpTo` / `StageJump`.
- Each scene contains its own `PF_PlayerRig`. The first rig wins; a duplicate rig in an additively loaded scene deletes itself.
- Locomotion: teleport + snap turn in every scene except SCN_SmokeRoom, where teleport is off and smooth move (max 1.4 m/s, comfort vignette) is on. `SceneFlow` calls `PlayerLocomotion.ConfigureForScene`.
- Oxygen starts at Awakening, is paused while in the smoke room and resumed on return.
- All systems (`InputService`, `SessionManager`, `SceneFlow`, `PauseManager`, `ConditionRandomizer`, `TelemetryLogger`, `ExperimenterPanel`, `SaveLoad`, `ProfilerCapture`, `OxygenSystem`, voice/SFX directors) are created once by `Bootstrap` in a `DontDestroyOnLoad` object, so each scene can also be played on its own in the Editor.
