# Check report: record

Date: 2026-10-17 01:01:21
Result: 14 passed, 1 failed. Console during run: 1 errors, 0 warnings.

- [PASS] The Unity Recorder started (Movie, MP4 H.264, 1920 x 1080, 30 fps, constant frame rate, audio on)
- [PASS] Game view audio is not muted (mute False)
- Bot: participants 4 to 4, ear error 10 deg, wrong-turn rate 0.15, time scale 1
- --- participant 4 (S-A then M-B) ---
- [PASS] P4: stages ran in order (Awakening>EarCheck>KeypadSolved>TrailFollowed>Block1>Block2>CoolantRouted>Escaped>Questionnaire)
- [PASS] P4: two telemetry rows (one per block)
- [PASS] P4 block 1: every telemetry field is filled
- [PASS] P4 block 1: condition S layout A match the order S-A then M-B
- [PASS] P4 block 2: every telemetry field is filled
- [PASS] P4 block 2: condition M layout B match the order S-A then M-B
- [PASS] P4: session JSON and bot CSV were written
- P4: wrong turns made 2; block times 121.8 s / 173.6333 s; SUS 80
- P4: timeline written, console errors 1, warnings 0; first error: connection.state_change
- [FAIL] P4: zero console errors during the run
- [PASS] P4: zero console warnings during the run
- [PASS] No clipping at the listener (0 blocks above full scale, first at game time -1.0 s)
- Recorded game time 808.7 s (13.5 min)
- [PASS] The mix reaching the AudioListener is not silent (mean -24.5 dBFS, peak 0.71)
- [PASS] The recorded run is longer than 12 minutes (13.5 min)
- [PASS] The recorder stopped and finalised the file

## Errors seen
- connection.state_change oldState=Running newState=Failed error=Process exited unexpectedly. code=-1
