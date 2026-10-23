# ASSET NAMES (owner's sheet)

Use these names before anything else. Never invent or rename assets.

- Root folder `Assets/_Project`. Scenes `SCN_Setup`, `SCN_MedicalBay`, `SCN_SmokeRoom`, `SCN_HallwayDebrief` in that build order.
- Player rig: the Building Block rig `[BuildingBlock] OVRComprehensiveInteractionRig`, saved as prefab `PF_PlayerRig`.
- Keypad: model `Keypad` (prefab `PF_Keypad`). Rack: `server_2` (`PF_ServerRack`). Cables: `usb_type_c_cable` for all three (`PF_Cable_01..03`). Bed: `hospital bed` (gurney folder). Cup: `cup and tray`. Terminal: `scifi_terminal_2` or `monitor`. Cryo pods: `sci-fi_pod_collection`. Other props: `Assets/_Project/Models` and `Modular_SciFi_Pack`.
- Beacons `PF_Beacon_01..06` use `Sci-Fi Barrel_Grey` (inactive), `Sci-Fi Barrel_Blue` (correct), `Sci-Fi Barrel_Red` (incorrect). If the barrel assets are not found by that name, use the nearest barrel or `beacon pucks` and say so.
- Smoke room: no vent; a plain room built from `Modular_SciFi_Pack` pieces. Layout files `Data/MazeLayoutA.json` and `Data/MazeLayoutB.json`.
- Shader `HoverOutline`. Mixer `MIX_Main` with groups `Voice_Rose`, `Voice_Halcyon`, `SFX`, `Ambience`, `UI`.
- Audio: files in `Assets/_Project/Audio` named by ID (`R###`, `H###`, `S###`, `A###`; extension may be wav or mp3). Resolve by ID, never by extension.
- Tags: `Grabbable`, `Beacon`, `Port`, `Wall`, `Junction`, `DeadEndCap`, `EntryZone`, `ExitZone`. Layers: `Interactable`, `Wall`.
- Data: `StudyConfig.json`, `deepsleep_telemetry.csv`, `profiler_log.csv`, `voice_manifest.csv` (ID, file, stage, voice, source_anchor).
- Scripts (exact names): InputService, EditorFallbackRig, SessionManager, SceneFlow, PauseManager, ConditionRandomizer, TelemetryLogger, ExperimenterPanel, OxygenSystem, EmergencyLighting, EarCheckController, ThermalKeypad, BreachTrail, CoolantRack, MazeBuilder, SmokeVisibility, EgressBlockController, PathTracker, MentorDirector, AudioConditionSwitch, NarrativeVoiceDirector, HalcyonDirector, SfxDirector, QuestionnaireUI, SaveLoad, ProfilerCapture, PlaythroughBot, ProjectValidator. Python: MazeLayoutVerifier.py, analysis.py, generate_figures.py.

## Where things actually live

- Docs: `Assets/_Project/Docs` (CLAUDE.md says `Docs/`; this is that folder inside the Unity project so it is versioned with the assets).
- Runtime scripts `Assets/_Project/Scripts/Runtime` (assembly `Fractured_Final.Runtime`), editor tools `Scripts/Editor`, tests `Tests/PlayMode`.
- Asset lookup at runtime: `Assets/_Project/Resources/DeepSleepAssets.asset` (audio by ID, mixer, shared materials, data text assets).
