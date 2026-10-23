# Expected flow (reference for the timeline diff)

Time windows are relative to the trigger and measured in game time (voice and SFX sources are pitched to the test time scale so clip lengths match game time).
"Within 1 s" means: the audio_start row appears at most 1.0 s after the voice_request row, unless another voice line is still playing (lines never overlap, the queue waits).
Mixer groups: Rose = Voice_Rose, HALCYON = Voice_Halcyon, SFX = SFX, ambience = Ambience, UI = UI.
Standing: after every scene arrival the rig root is on the floor (floor distance below the root about 0) and head height = 1.65 m (+/- 0.05).

| # | Stage | Scene | Entry condition | Player action | Result on success | Voice lines (trigger, window) | SFX | Next | Logged |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Setup | SCN_Setup | app start | consent, calibration tones (4), comfort, volume | scene fade, load Medical Bay | none (S002 tones play, not R001-R003) | S002 x4 (tone per direction) | Awakening | participant id, order |
| 2 | Awakening | SCN_MedicalBay | Setup done; oxygen starts | go near tray, pick up cup, teleport once | stage EarCheck | R010 and H001 at start (R010 first, then H001, no overlap); H002 at 20 s (only if not yet advanced); R011 when near tray; R012 5 s after R011 if cup still on tray | S090 teleport, grab | EarCheck | awakening duration |
| 3 | EarCheck | SCN_MedicalBay | cup grabbed | stand on ring, click 6 beacons | keypad becomes ready | R030 (3.5 s after standing), R031 after each of 6 trials, R032 at the end | S001 x6 (spatial pings, group SFX) | (same stage) keypad | earcheck_mean_error_deg, earcheck_mean_rt_s |
| 4 | Thermal Keypad | SCN_MedicalBay | ear check completed | press 7, 3, 5 | stage KeypadSolved | R040 at keypad start, R041 after 30 s idle, H004 after a wrong sequence (max once per 30 s), R043 on solve | S040 per key, S041 wrong, S042 boot | KeypadSolved | keypad fails, time |
| 5 | The Trail | SCN_MedicalBay | keypad solved | follow decals to the wall panel, pull the handle | panel slides, service door opens; stage TrailFollowed | R050 when the trail starts, R052 when the door opens | S050 x2 | TrailFollowed | trail_time_s |
| 6 | Block 1 | SCN_SmokeRoom (additive) | enter vestibule zone | smooth-move to the exit door; Rose calls every 4 s from the door | exit reached, return to Medical Bay, TLX 1 | R201 every 4 s (first after 1.5 s), R202-R204 at junctions, R205 dead end, R207 20 s idle, R208 within 3 m of exit, R209 finished / R210 timeout; HALCYON silent; R140 when TLX 1 opens | S021 footsteps, S020 wall, S022 exit door | Block2 after TLX 1 | one telemetry row for block 1 |
| 7 | Block 2 | SCN_SmokeRoom (additive) | TLX 1 finished | same as Block 1 with the other layout/condition | exit reached, return, TLX 2 | as Block 1; R150 after TLX 2, R151 10 s later (stage Block2 still active) | same | CoolantRouted when the rack is solved | one telemetry row for block 2 |
| 8 | Coolant Reroute | SCN_MedicalBay | back from Block 2 | red to port 2, blue to 5, yellow to 3 | rack solved; lever unlocked | R153 on solve; wrong plug: S062, 3 s lockout, no voice | S060 plug, S062 wrong plug, S063 solved | CoolantRouted then lever | wrong plug count |
| 9 | Escape | SCN_MedicalBay -> SCN_HallwayDebrief | rack solved | grab and pull the lever | blast door opens, hallway load | H007 lever grabbed, H008 lever pulled then R160 (HALCYON and Rose never overlap) | S063, S080 blast door | Escaped | escape time |
| 10 | Questionnaire / debrief | SCN_HallwayDebrief | hallway loaded | SUS (10 items), preference, plaque | stage Questionnaire, files written | R161 at finish; HALCYON audible again in the hallway | ambience A001 + A005 | end | SUS_score, preference, plaque text |

## Always true
- Stages run in this order and never repeat: Awakening, EarCheck, KeypadSolved, TrailFollowed, Block1, Block2, CoolantRouted, Escaped, Questionnaire.
- Every R### and H### played at most once per run, except R031 (6 times), R201 (repeats), R202-R204 and R205 (once per event), R207 (once per idle event).
- No audio_start of an R/H line after the stage it belongs to has ended (lines for Questionnaire may still finish in the debrief).
- No two voice clips (R/H) overlap in time.
- HALCYON (Voice_Halcyon) has no audio_start while the active scene is SCN_SmokeRoom.
- Fog on only in SCN_SmokeRoom, off in the other scenes, on again for Block 2. Zero playing particle systems in the smoke room.
- Oxygen paused while the active scene is SCN_SmokeRoom and never depleted.
- Block calls R201-R210 play from group Voice_Rose; spatial (S): blend 1, spatialize on; stereo (M): blend 0, spatialize off.
- Condition order per participant (ConditionRandomizer index n mod 4): 0: S-A then M-B; 1: S-B then M-A; 2: M-A then S-B; 3: M-B then S-A.
- Telemetry: two rows per participant, all fields filled, no NaN, no negatives.
