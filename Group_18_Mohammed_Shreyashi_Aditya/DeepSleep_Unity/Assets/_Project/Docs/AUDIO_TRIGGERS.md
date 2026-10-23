# AUDIO TRIGGERS

Generated from `Data/voice_manifest.csv`. Voices are resolved by ID (never by extension). Rose block calls come from the exit door through AudioConditionSwitch (spatial HRTF or stereo); all other Rose lines are 2D comms (NarrativeVoiceDirector); HALCYON plays from the ceiling speaker (HalcyonDirector). Ambience ducks by -8 dB under every voice line (SfxDirector, Normal / Ducked snapshots of MIX_Main).

| ID | Voice | Stage | Trigger |
|---|---|---|---|
| R001 | Rose | Setup | (Setup calibration; not used: the setup panel plays the S002 tones instead) |
| R002 | Rose | Setup | (same) |
| R003 | Rose | Setup | (same) |
| R010 | Rose | Awakening | AwakeningController.Start |
| R011 | Rose | Awakening | AwakeningController (player near the tray) |
| R012 | Rose | Awakening | AwakeningController (5 s after R011, cup still on the tray) |
| R030 | Rose | EarCheck | EarCheckController (trials start) |
| R031 | Rose | EarCheck | EarCheckController (after each trial) |
| R032 | Rose | EarCheck | EarCheckController (end) |
| R040 | Rose | EarCheck | ThermalKeypad.Begin |
| R041 | Rose | EarCheck | ThermalKeypad (30 s idle) |
| R043 | Rose | KeypadSolved | ThermalKeypad.Solve |
| R050 | Rose | KeypadSolved | BreachTrail.StartTrail |
| R052 | Rose | TrailFollowed | BreachTrail.OpenDoor |
| R150 | Rose | Block2 | QuestionnaireUI (after the block 2 TLX) |
| R151 | Rose | Block2 | QuestionnaireUI (10 s after R150) |
| R153 | Rose | CoolantRouted | CoolantRack.Solve |
| R160 | Rose | CoolantRouted | CoolantRack.PullLever |
| R140 | Rose | Block1 | QuestionnaireUI.StartTlx(block 1) |
| R161 | Rose | Questionnaire | QuestionnaireUI.FinishDebrief |
| R201 | Rose | Block2 | MentorDirector (beacon call every 4 s, from the exit door) |
| R202 | Rose | Block2 | MentorDirector (junction) |
| R203 | Rose | Block2 | MentorDirector (junction) |
| R204 | Rose | Block2 | MentorDirector (junction) |
| R205 | Rose | Block2 | MentorDirector (dead end) |
| R207 | Rose | Block2 | MentorDirector (20 s idle) |
| R208 | Rose | Block2 | MentorDirector (within 3 m of the exit) |
| R209 | Rose | Block2 | MentorDirector.EndBlock (finished) |
| R210 | Rose | Block2 | MentorDirector.EndBlock (timeout) |
| H001 | HALCYON | Awakening | AwakeningController.Start |
| H002 | HALCYON | Awakening | AwakeningController (20 s) |
| H004 | HALCYON | EarCheck | ThermalKeypad.Fail (once per 30 s) |
| H007 | HALCYON | CoolantRouted | CoolantRack (lever grabbed) |
| H008 | HALCYON | CoolantRouted | CoolantRack (lever pulled) |
| S001 | SFX | EarCheck | EarCheckController (spatial ping at the beacon) |
| S002 | SFX | Setup | SetupController (calibration tones) |
| S020 | SFX | Block2 | SfxDirector: WallContact |
| S021 | SFX | Block2 | SfxDirector: Footstep |
| S022 | SFX | Block2 | SfxDirector: ExitDoor |
| S040 | SFX | EarCheck | SfxDirector: KeyPress (pitch by key) |
| S041 | SFX | EarCheck | SfxDirector: WrongSequence |
| S042 | SFX | KeypadSolved | SfxDirector: TerminalBoot |
| S050 | SFX | TrailFollowed | SfxDirector: ServiceDoor |
| S060 | SFX | CoolantRouted | SfxDirector: Plug / Unplug |
| S062 | SFX | CoolantRouted | SfxDirector: WrongPlug |
| S063 | SFX | CoolantRouted | SfxDirector: RackSolved / Lever |
| S080 | SFX | Escaped | SfxDirector: BlastDoor |
| S090 | SFX | Questionnaire | SfxDirector: Teleport |
| S091 | SFX | Questionnaire | SfxDirector: UI |
| A001 | Ambience | Awakening | SfxDirector ambience: SCN_MedicalBay and SCN_HallwayDebrief |
| A004 | Ambience | Block2 | SfxDirector ambience: SCN_SmokeRoom (mono) |
| A006 | Ambience | Block2 | SfxDirector ambience: SCN_SmokeRoom (mono) |
| A005 | Ambience | Questionnaire | SfxDirector ambience: SCN_HallwayDebrief |
| R060 | Rose | none | unused (no trigger) |
