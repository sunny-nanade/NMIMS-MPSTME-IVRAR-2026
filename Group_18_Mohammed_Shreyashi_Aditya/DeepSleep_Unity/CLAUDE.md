You are building a single-player Unity VR escape room for Meta Quest, using the MCP for Unity tools. Re-read this file and Docs/PROGRESS.md at the start of every phase.

GAME: Protocol: Deep Sleep. The player wakes standing in the damaged Medical Bay of the research vessel Aethelgard after an electrical fire. The ship AI, HALCYON, is malfunctioning: it lies that everything is fine and tells the player to stay put. Rose, an engineer sealed on Deck 4, guides the player over comms. Stages in order: Setup, mission briefing screen (objective and the three tasks: beacons, maze, cables; about 20 s; shown once, before Rose speaks), Awakening (pick up the cup, teleport once), Beacon game (stage name EarCheck in code), Thermal Keypad, The Trail (leads to a hidden service door), two smoke-filled rooms (Block 1 and Block 2), Coolant Reroute, Escape, SUS and debrief. About 18 minutes. No fail state.
PROBLEM: Can spatial (HRTF) voice guidance cut evacuation time and wrong turns in smoke-filled, low-visibility egress? Stakeholder: Mumbai Fire Brigade.
STUDY: within-subject. In each smoke-room block Rose's voice comes from the exit door either spatial (HRTF) or stereo (identical in both ears); only that differs. Layouts A and B are counterbalanced with the condition (4 orders). Measures: evacuation time, wrong turns, dead-end entries, backtracks, path efficiency, head-turn latency, raw NASA-TLX, SUS, frame rate. (The Ear Check error measure was dropped when the Ear Check became the beacon game; the CSV columns earcheck_* stay empty, beacon_game_time_s and beacon_wrong_placements are logged instead.)
STACK (already installed, do not change packages): Unity 6000.5.2f1, URP 17.5.0, Meta XR All-in-One SDK 207.0.0 (Interaction SDK rig from Building Blocks in SCN_Setup), Input System 1.19.0, OpenXR 1.18.0, Meta XR Audio spatializer, TextMesh Pro. Namespace Fractured_Final. Log prefix [DeepSleep].

RULES
- No networking, no Photon, no XR Interaction Toolkit. Controllers only, no hand tracking.
- No hologram, no Emitter, no texture A/B. Rooms use their normal materials. Prefer plain materials; any shader is written as code (HLSL for URP), never Shader Graph.
- HUMAN SCALE AND CAMERA HEIGHT: the camera sits at a natural standing eye height of EYE_HEIGHT above the floor. Tracking origin type is Floor so the real headset height is used. Never offset the camera height in code. The player always starts standing. Everything is real-world size: doors 2.1 m high and 0.9 m wide, ceilings at least 2.6 m, tables and beds 0.9 m, corridors 2 m wide. If an asset feels wrong from that eye height, rescale the asset, never the camera.
- Use the asset names in Docs/ASSET_NAMES.md and the ASSET NAMES below. Use a primitive only when no asset exists, and list every primitive in Docs/KNOWN_ISSUES.md. Never invent or rename assets.
- No spatial audio outside the two smoke-room blocks: the beacon game is purely visual (poster, colours, table).
- Oxygen is cosmetic and never ends the session. A wrong plug causes a lockout only.
- Everything must be testable without a headset: all input goes through InputService (the only script that reads OVRInput or the Input System), with an Editor fallback rig and a scripted playthrough bot. Check ProjectSettings activeInputHandler and use the matching API; do not change that setting.
- Frame rate target is TARGET_FPS on Quest in the smoke room: fog instead of particles, capped decals, baked lighting where possible.
- Every tunable is a [SerializeField] with the DECISIONS value as its default.
- Do not install or remove packages. Do not run git push. Do not delete anything outside files you created, except unused assets when the owner asks for a cleanup (use AssetDatabase.MoveAssetToTrash, never rm -rf).
- Playtests: only at the very end or when essential. Never start the Meta XR Simulator: before a play run set Standalone XR init off (XRGeneralSettingsPerBuildTarget, m_InitManagerOnStart), and restore it to on afterwards. Prefer edit-mode renders (EditorTools.LookPreview, ViewShot); the scripted play check is DeepSleep > Run Check > Look.
- New [SerializeField] fields on components already saved in SCN_Main are overridden by the value saved in the scene: after changing a default, also set it in the scene (SerializedObject) and save.

WORKFLOW
- Use MCP for Unity for Editor actions and normal file tools for scripts. After writing or changing scripts, wait for compilation, read the Console through MCP and fix every error before continuing.
- Work through the phases of the MASTER prompt without stopping. When a phase check passes: git add -A, git commit -m 'Phase n done', append a short entry to Docs/PROGRESS.md (phase, status, files, checks, open issues).
- If a check still fails after 3 attempts, write it to Docs/KNOWN_ISSUES.md, commit, and continue unless a later phase depends on it.
- Stop only if the MCP connection drops, a package would have to be installed, or Unity crashes. Say which.

ASSET NAMES (the owner's sheet; use these before anything else)
- Root folder Assets/_Project. One scene, SCN_Main, with sections SEC_Setup, SEC_MedicalBay, SEC_Hallway; the smoke room is the Resources prefab PF_SmokeSection (SceneFlow.Section switches sections). The old four scene names remain as section names in code.
- Player rig: the Building Block rig [BuildingBlock] OVRComprehensiveInteractionRig, saved as prefab PF_PlayerRig.
- Keypad: model 'Keypad' (prefab PF_Keypad, original mesh and textures, plus code-built number labels and heat plates). Rack PF_ServerRack and cables PF_Cable_01..03 are prefabs whose look is built in code (CoolantCable builds a bent tube with plugs). Most imported GLB models were removed as unused; scene props are primitives or code-generated meshes (MeshFactory: cup, tube), listed in Docs/KNOWN_ISSUES.md. Other props: Modular_SciFi_Pack.
- Beacons: six coloured barrels (red, green, blue, yellow, purple, white) built by EarCheckController from primitives; the PF_Beacon_01..06 prefabs and barrel assets were removed. Posters (beacon order, coolant ports) are built from primitives by PosterBuilder.
- Smoke room: no vent; a plain room built from Modular_SciFi_Pack pieces. Layout files Data/MazeLayoutA.json and MazeLayoutB.json.
- Shader HoverOutline. Mixer MIX_Main with groups Voice_Rose, Voice_Halcyon, SFX, Ambience, UI.
- Audio: files already in Assets/_Project/Audio named by ID (R###, H###, S###, A###; extension may be wav or mp3). Resolve by ID, never by extension.
- Tags: Grabbable, Beacon, Port, Wall, Junction, DeadEndCap, EntryZone, ExitZone. Layers: Interactable, Wall.
- Data: StudyConfig.json, deepsleep_telemetry.csv, profiler_log.csv, voice_manifest.csv (ID, file, stage, voice, source_anchor).
- Scripts (exact names): ObjectiveScreen (briefing), PosterBuilder, MeshFactory, InputService, EditorFallbackRig, SessionManager, SceneFlow, PauseManager, ConditionRandomizer, TelemetryLogger, ExperimenterPanel, OxygenSystem, EmergencyLighting, EarCheckController, ThermalKeypad, BreachTrail, CoolantRack, MazeBuilder, SmokeVisibility, EgressBlockController, PathTracker, MentorDirector, AudioConditionSwitch, NarrativeVoiceDirector, HalcyonDirector, SfxDirector, QuestionnaireUI, SaveLoad, ProfilerCapture, PlaythroughBot, ProjectValidator; Python: MazeLayoutVerifier.py, analysis.py, generate_figures.py.

DECISIONS (all values are final; do not ask)
- EYE_HEIGHT 1.65 m. TARGET_FPS 72. EDITOR_FRAME_BUDGET 11 ms.
- Teleport: right thumbstick pushed forward and released (Editor key E). Snap turn 45 degrees. Smooth move only in SCN_SmokeRoom, max 1.4 m/s, comfort vignette, teleport off there.
- ConditionRandomizer: participant_number mod 4 gives orders S-A then M-B; S-B then M-A; M-A then S-B; M-B then S-A (S spatial, M stereo).
- Consent text: 'This is a research study about guidance audio in a VR evacuation. You will wear a headset for about 18 minutes. You can stop at any time by taking the headset off. Only an anonymous participant number is stored. If you feel dizzy or unwell, tell the experimenter at once.'
- Medical Bay about 8 x 6 x 3 m. Oxygen 20 min. Emergency light pulse every 2 s.
- Beacon game: 6 grabbable coloured beacons start on a supply table; a wall poster shows the colour order left to right; the player puts each on its numbered slot on a second table (0.9 m high). Correct locks in and the slot turns green; wrong is sent back after 0.5 s, no lockout. Rose R030 at start, R031 per correct beacon, R032 at the end. Order is seeded from StudyConfig seed.
- Briefing: ObjectiveScreen, 20 s, once per session; Rose R010 and HALCYON H001 start after it.
- HALCYON voice: AudioSource pitch 0.78 (deeper than Rose). Rose +4 dB in the smoke blocks, same in both conditions.
- Held objects have their colliders off while in the hand (so teleport and movement still work); grabbables use speculative continuous collision.
- Keypad: 9 keys labelled 1 to 9 at about 1.1 m. Heat-glow keys 7, 3, 5 shown as flat bright plates: red (least hot), white, blue (hottest); every key shows its number; press order coolest to hottest = 7, 3, 5. Wrong sequence lockout 2 s; after 3 fails the glowing keys pulse for 4 s; idle event after 30 s.
- Trail: one route, no forks, from beside the terminal to a wall panel on the wall opposite the main door; at most 24 emissive decals. Service door behind that panel, pull handle at about 1.0 m. The agent picks exact waypoints and records them in Docs/DECISIONS_APPLIED.md.
- Coolant Reroute: red cable to port 2, blue to port 5, yellow to port 3. A wall poster shows each port number in its cable's colour. Wrong plug lockout 3 s. Lever at about 1.1 m.
- Smoke room grid 6 x 6 cells of 2 m; shortest path about 36 m; 6 junctions on it; 3 dead ends; at least 2 misleading junctions. The agent uses the verifier's top-ranked pair as Layout A and B, builds the room from Modular_SciFi_Pack floor, wall (2.6 m high) and door pieces, and records the choice in Docs/DECISIONS_APPLIED.md.
- Visibility about 2 m (exponential-squared fog), helmet lamp range 1.5 m. The exit door has a glowing green frame and handle (still fogged). Rose beacon call every 4 s. Idle nudge at 20 s. Block timeout 4 min.
- Ambience ducks under voices by -8 dB. M gain trim is computed by the level-match tool.
- Experimenter menu: hold both grips and press the menu button (Editor key F10).
- Plaque text: 'MISSION LOG. Aethelgard, Deck 4 evacuation. Participant {id}. Block 1 {time}. Block 2 {time}.'
- Bot: wrong-turn rate 0.15 per junction (also the chance of a wrong beacon slip), 8 participants.
