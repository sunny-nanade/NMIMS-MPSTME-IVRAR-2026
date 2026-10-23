# Final check, 2026-10-16

Scope: whole-game flow check (Part 1) and one recorded real-time run (Part 2). Expected flow: `flow_expected.md`. Timelines: `timeline/`. Diff: `timeline_diff.md` (`Tools/flow_diff.py`). Edge cases: `final_edge.md`. Validate: `validate_latest.md`.

## Result
- Flow diff: 0 unresolved mismatches. All four condition orders ran (P1 to P4 at 4x, human-listening pace; timelines kept for P1, P3, P4; P2 passed with 0 flow mismatches but its file was removed, see below) and the recorded 1x run (participant 4, S-A then M-B).
- Edge cases (pause mid-line, restart mid-line, jumps forward and back x2, keypad idle 30 s, wrong plug during a voice line, scene reload x2, real 240 s block timeout, telemetry export twice): 45/45.
- Audits: audio levels 19/19, smoke visibility 19/19, flow/puzzles/telemetry/leaks 53/53. Validate Project 48/48. Logic tests not re-run (no logic changed).
- Console: zero errors and zero warnings from our code in every final run (see notes on package warnings).
- Skipped on request: a repeat of the 4x flow runs after the last small changes (the last change set touched only the setup tone volume, the test tools and the launcher).

## Discrepancies found, fixes, commits
| # | Finding | Fix | Commit |
|---|---|---|---|
| 1 | Audio library stale after the owner's audio edits: R150 resolved to null ("no clip for R150" warning, line never played); S062 still the placeholder | Rebuilt the library (audio files untouched) | Final: rebuild audio library after audio file changes |
| 2 | H001 starved: HALCYON waited for the whole Rose queue and was dropped after 30 s | HALCYON waits only for the line Rose is saying; Rose lets an older HALCYON request go first | Final: voice queue fixes |
| 3 | Stale lines: R012, R031, R040, R043, R050, R052, R151, H007 played after their stage had ended | `VoiceRules` drops queued lines whose stage has passed | same |
| 4 | R031 collapsed to one play; trials started while R030 was still talking | Ear Check waits for Rose before the first ping and before each next ping | same |
| 5 | Comms line (R052 etc.) overlapped the smoke-room beacon call R201 | MentorDirector waits while a comms line is speaking | same |
| 6 | Stage jump forward/back left the old voice playing (R010 during Escaped) and left oxygen paused after leaving the smoke room; jump to Setup did not reset oxygen | `StageJump` cancels all voices, resumes or resets oxygen | Final: stage jumps cancel voices and restore oxygen |
| 7 | "Coroutine couldn't be started ... 'EarCheck' is inactive" error when jumping while the Medical Bay is hidden | `isActiveAndEnabled` guard in EarCheckController and BreachTrail stage handlers | same |
| 8 | Setup tone S002 (rms -5.9 dBFS) repeated and stacked: listener mix peaked at 1.94 (clipping, 468 blocks in the first recording) | Tone volume 0.35, restart instead of stacking | Final: setup tone no longer clips |
| 9 | Launcher left a stale `.done.meta` (Unity "meta data file exists but asset can't be found" warning) | Launcher removes it | same |
| 10 | Validate failed: manifest listed 11 files the owner removed | Manifest rebuilt from the audio folder, AUDIO_TRIGGERS and KNOWN_ISSUES updated | Final: manifest rebuilt |
| 11 | Warning when the exit-door source is gone during unload (`AudioConditionSwitch.Play`) | Guard | Final: manifest rebuilt / audits fixed |
| 12 | 3 compiler warnings in RigPrefabBuilder (obsolete find APIs) | Replaced | Final: compile fixes |
| 13 | Test-side issues, not game bugs: smoke audit 1 m transmittance limit (lamp lights the wall), flow audit teleported beyond range, audio audit asked for out-of-stage lines | Test thresholds / placement / `VoiceRules.BypassForTests` | Final: manifest rebuilt / audits fixed |

## Verified in the timelines (no mismatch)
Stages in order and never repeated; every transition leaves the rig on the floor at 1.650 m; every R### and H### plays at most once (R031 x6, R201 repeating by design); request-to-start at most 0.03 s in the 1x run (0.9 s worst in the 4x runs, only behind another line); no overlap between voice clips; no line after its stage; HALCYON never plays in SCN_SmokeRoom; fog on only in the smoke room (density 0.989, 2 m) and again for Block 2; oxygen paused in the smoke room and never at 0; no playing particle systems; spatial (blend 1, spatialize on) vs stereo (blend 0, spatialize off) follows the participant order; 0.6 s into every voice the ambience is ducked 8 dB and SFX 10 dB. Voice margins over the bed: bay Rose 22.9 dB, HALCYON 21.9 dB; smoke room worst 11.6 dB (M R209 at 15 m); hallway HALCYON 24.1 dB, Rose 24.8 dB (all above 6 dB). Telemetry: 8 bot rows (P1 to P4), one per block, none empty, no NaN, no negatives; exported twice gives the same 2 rows.

## Not changed / limits
- No scripted HALCYON line plays in the hallway (the manifest has none); the hallway speaker was measured by requesting H001/H004/H007/H008 there (audible, 24 dB over the bed).
- Participant 2's 4x timeline was removed after its two warnings were traced to an Account API package warning and a double menu launch of mine; it was not re-run (skipped on request). Its earlier version had 0 flow mismatches.
- Package warnings (not ours): "Account API did not become accessible within 30 seconds" (com.unity.ai.assistant), a Windows signature warning from the Unity MCP bridge, and a ModelSelector "NoSubscription" log. They appear only with the AI Assistant package and have no effect on the game.
- Recorder installs its own dependency `com.unity.bindings.openimageio 1.0.2` (automatic). The Recorder's Quick Recording hotkey is F10, the same key as the experimenter menu in the Editor.
- The first `pip download` of imageio-ffmpeg that I tried for probing was deleted and never installed.
- Quest frame rate and the audible HRTF effect remain unmeasured (no headset), as before.

## Recording
- `Builds/Recordings/DeepSleep_FinalRun_20261016.mp4` (copied to `C:\Users\shrey\Downloads`): 13 min 31 s (811.7 s), 1920 x 1080, 30 fps constant, H.264 (avc1) video + AAC (mp4a, 48 kHz) audio, 344.7 MB. Probe: `Tools/mp4_probe.py` (no ffmpeg on this PC). Audio not silent: listener mix mean -28.5 dBFS, peak 0.53, 34456 of 38044 blocks above -80 dBFS, no clipping. Participant 4, S-A then M-B, real time (timeScale 1), caption with stage and voice line (speaker and ID).
- No ffmpeg, so no highlight clip. Highlight timestamps (video time): Ear Check 2:07 to 3:00; keypad 3:03 to 3:41; trail 3:46 to 4:30; smoke room Block 1 with Rose (spatial) 4:38 to 6:38; Block 2 (stereo) 7:29 to 10:21; coolant rack 11:20 to 11:39 (R153 at 11:39); lever and blast doors 11:54 to 12:10; hallway debrief 13:17 to 13:27.
