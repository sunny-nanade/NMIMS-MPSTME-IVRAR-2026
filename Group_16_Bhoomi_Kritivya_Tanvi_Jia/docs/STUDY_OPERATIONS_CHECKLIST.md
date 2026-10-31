# Human-study operations checklist (needs at least 10 real adult volunteers)

Companion to `STUDY_PROTOCOL.md` and `PARTICIPANT_PACK.md`. Status of every item: **PENDING — TEAM INPUT REQUIRED** until a person ticks it. No participant, consent form or human telemetry exists today.

## Before
- [ ] Ask Dr. Nanade whether faculty/ethics approval or a specific consent template is required (the professor has confirmed our own repository and AI-assisted writing are acceptable; the study consent itself is a separate question).
- [ ] Print `PARTICIPANT_PACK.md` sections A, D, E (and F if used). Fill section C once.
- [ ] Private key sheet (name to P01...), kept off the repository.
- [ ] Install the **rebuilt** release APK on a Quest; record the commit and APK SHA-256 (see `BUILD_REPORT.md`).
- [ ] Rehearse once with TEST + SoftwareTest; keep that output in a folder named `rehearsal/`, never in `telemetry/`.
- [ ] Assign who runs the headset, who reads the briefing, who watches for discomfort, who copies files.

## Each participant (about 15 minutes)
- [ ] Consent signed first. Adult. Voluntary.
- [ ] Briefing script read verbatim. Fit headset, check boundary.
- [ ] Start panel: Participant = ID, Data type = **HumanParticipant**, same feedback setting as everyone, Begin.
- [ ] No coaching. Stop on discomfort and mark the session partial.
- [ ] Questionnaires on paper immediately after the results panel; enter the numbers as written.
- [ ] Copy the three CSV files from the headset (`adb pull`, path in `STUDY_PROTOCOL.md`), do not rename or edit, set read-only.

## After
- [ ] Put files in `telemetry/` (records, summary, confusion) and the filled sheet in `telemetry/questionnaires.csv`.
- [ ] Run `python3 Docs/tools/analyze_study.py --self-test`, then the real command from `PARTICIPANT_PACK.md` section G.
- [ ] Read `analysis/validation_report.txt`. Anything excluded needs a one-line reason in the dossier. Do not fix values by hand.
- [ ] Copy N, mean, SD, median from `analysis/results_summary.csv`, the matrix from `pooled_confusion.csv`, plots from `analysis/*.png` into Section 4.
- [ ] Answer the provenance declaration honestly (all four statements).

## What the script checks
Header match; provenance exactly `HumanParticipant`; ID format `P01`-`P999` (TEST is rejected); ten distinct casualties; categories valid; `is_correct` and under-triage flags consistent with ground truth; one perfusion variant per session; latency valid; consent = yes; SUS items integers 1-5 and recorded score = recomputed; NASA-TLX 0-100; every telemetry session has a questionnaire row and vice versa. Invalid sessions are excluded and listed, never repaired.

## If fewer than 10 participants by the deadline
Report the real N and the shortfall in Section 4 and the video. Section 4 asks for 10 or more real people; fewer costs marks. Simulated or invented data scores zero and is misconduct.
