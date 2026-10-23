# Headset day checklist

Everything below was only tested in the Editor (bot, edge cases, Game view). None of it has run on a Quest yet. Work top to bottom; stop at the first item that fails and write it to KNOWN_ISSUES.md.

## Before the participant arrives
- [ ] Quest charged (at least 80 %), controllers have fresh batteries, Guardian / boundary set, floor level calibrated.
- [ ] Build the Android APK first (File > Build Profiles > Android, scene SCN_Main; it was NOT built in this round, by request), run Meta's Project Setup Tool and fix its list, then install it (`adb install -r Builds/Quest.apk`) and start it once to accept permissions.
- [ ] Headphones plugged in or the Quest speakers set to a fixed volume. Same device for every participant.
- [ ] Participant number written on the sheet. `participant_number mod 4` gives the order: 1 = S-B then M-A, 2 = M-A then S-B, 3 = M-B then S-A, 0 (4, 8) = S-A then M-B.
- [ ] Experimenter menu opens (hold both grips and press the menu button).
- [ ] Room has at least 2 x 2 m free floor, no cables, chair out of the way.

## First 5 minutes in the headset (smoke test)
- [ ] Consent page readable, buttons respond to the controller ray, volume slider moves.
- [ ] Height page: eye height looks natural (about 1.65 m for a standing adult). No camera offset is applied in code.
- [ ] One beep plays, then the comfort page.
- [ ] Briefing screen shows the objective and three tasks (about 20 s), then Rose speaks.
- [ ] Cup can be grabbed. Teleport works (right thumbstick forward and release). Held objects do not block teleport.
- [ ] Beacon game: six coloured beacons, poster legible, a wrong beacon is sent back after 0.5 s, a correct one locks and turns the slot green.
- [ ] Keypad: numbers and the red / white / blue heat plates readable; pressing 7, 3, 5 solves it.
- [ ] Trail: glowing marks lead to the wall panel; panel opens; service door handle can be pulled.

## Smoke room (the study manipulation)
- [ ] Visibility about 2 m, no pink or black surfaces, walls readable inside the helmet-lamp circle.
- [ ] Smooth move works (max 1.4 m/s), comfort vignette appears, teleport is off.
- [ ] Rose is clearly audible and comes from the exit door. Spatial block: the voice moves when you turn. Stereo block: the voice stays in both ears. Check both on the experimenter side before the first participant.
- [ ] Exit door has the green frame and handle; opening it ends the block.
- [ ] Frame rate holds 72 FPS (use the OVR Metrics overlay or `profiler_log.csv`). Note any dips in the smoke room.
- [ ] Dizzy participants: tell them they can stop at any time; no data is lost for the rows already written.

## After the session
- [ ] Both telemetry rows present (one per block) with condition and layout matching the order sheet.
- [ ] SUS, TLX and preference answered; plaque shows participant id and both block times.
- [ ] Copy `deepsleep_telemetry.csv`, `profiler_log.csv` and the session JSON from the headset (`adb pull /sdcard/Android/data/<package>/files/`).
- [ ] Run `analysis.py` on the pulled files.

## Known limits (see KNOWN_ISSUES.md)
- The smoke room can show a faint lattice of bright dots on some walls from the pack material; check on the headset whether it is visible.
- Baked lighting is not possible (all sections share one scene); lights are realtime and few. Watch the frame rate in the bay.
- No Android build or Meta Project Setup Tool run was done in this round (skipped on request). Expect to fix a few Setup Tool items on the first build.
