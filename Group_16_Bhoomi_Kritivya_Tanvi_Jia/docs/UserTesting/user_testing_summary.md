# User testing summary

## Total testers and source
- **10 people tested the game.** Source: the project owner's statement. No per-participant record (notes, forms, questionnaires, telemetry, screenshots, logs) exists in this repository or in the reference repository `sunny-nanade/NMIMS-MPSTME-IVRAR-2026`.
- Participants are anonymous (`P01`-`P10`). **No tester name is recorded.** The reference repository's `Group_16_*/docs/TEAM_ROSTER.json` lists the four *project team members* (Bhoomi Bhandari, Kritivya Mishra, Tanvi Paithankar, Jia Jadhav) with roles; nothing there says who the 10 testers were, so it is team metadata only and was not used as tester identity. Its `telemetry/SIMULATED_*.csv` files are labelled simulated and are not observations of anyone.

## Records with actual results
| Item | Value |
|---|---|
| Participant rows | 10 |
| Rows with any measured result | **0** |
| Rows with only the participant count | 10 |

## Task-level success rates (measured, human participants)
No outcome was recorded for any participant, so every denominator is 0 and **no rate is reported**: Factory / Earthquake / Flood / Triage completion; victim selection, grab, carry, safe-zone delivery; triage tagging.

## Time, errors, ratings, issues
Completion time: none recorded. Interaction/navigation error counts: none. Ease-of-use, satisfaction and overall ratings: none; no questionnaire or scale is on file, so none is assigned. Participants per issue type: none recorded. Feedback and recurring issues: none recorded. No SUS, NASA-TLX or SSQ was administered according to the available records.

## Evidence that does exist (automated, NOT human participants)
Editor tests on 2026-10-29 (EditMode 121/121, PlayMode 57/57; `Builds/TestSummary.txt`, `Docs/HUMANS_AND_HANDS_UPGRADE.md`) drive scripted carry, delivery, hand-input and triage flows. They show the software behaves as designed in the editor; they say nothing about how any of the 10 people performed or felt.

## Measured vs derived vs synthetic
- **Measured:** none for participants.
- **Derived:** none (nothing to derive from).
- **Synthetic:** `vr_disaster_simulator_synthetic_example.csv` holds invented numbers (IDs `SX01`-`SX10`, every row `SYNTHETIC_NOT_OBSERVED`) only to show the layout. Never cite it, never merge it into the real file. The validator rejects any synthetic marker in the real dataset.

## Limitations
Ten testers is a small sample; even with full data no statistical significance or population claim would be justified. Hardware, input mode and scenes per person are unknown, and the hand-tracking build (`DisasterResponseVR_HumansHands.apk`) has not been verified on a headset, so it must not be assumed to have been used.

## What is needed to compute real results
Per participant: date, device, input mode (controllers/hand tracking/simulator), scenes played, Yes/No/Not tested outcomes for each task, completion time, error counts, ratings with their original scale, comments, consent record. Enter them in `vr_disaster_simulator_user_testing.csv` (blank copy: `..._template.csv`), then run `python3 validate_user_testing.py`, which prints every rate with its denominator and the means/medians from valid values only, and update this file from its output.

## Dossier-ready findings
See `dossier_findings.md`.
