# Study protocol (DRAFT, not yet approved or run)

**Status:** `PENDING — TEAM INPUT REQUIRED`. No participant has been recruited, consented or tested. No human data exists in this repository. Everything below is a plan the team can follow; it is not a result.

**Research question (approved title, unchanged):** How can an AI-driven VR mass-casualty triage simulation improve START protocol categorization accuracy and reduce assessment latency for emergency medical trainees under dynamic industrial hazard conditions?

> Honest scope note. The software implements a fixed, scripted START exercise with cosmetic hazards that escalate over time. It does **not** contain a machine-learning model, and the hazards do not change the correct answer. If the course expects an "AI-driven" component to be literally present, the team must decide how to describe this (see `TEAM_ACTIONS_CHECKLIST.md`). Do not describe the scripted scenario as machine learning in the dossier or video.

## 1. Design

| Item | Plan |
|---|---|
| Design | Single-session, within-subject exploratory study (one VR session per participant). No control group is built. A pre/post or control comparison needs extra work the team has not done. |
| Participants | Adult volunteers, N >= 10 (20 recommended). Record whether each has prior triage training. The software tags every row with a provenance label; use `HumanParticipant` only for real consented participants. |
| Build | The exact release build (APK, commit hash noted in the questionnaire sheet). |
| Task | Tag all 10 casualties (C01-C10) as Green, Yellow, Red or Black using START. Fixed scenario, so every participant sees the same casualties. |
| Protocol variant | START adult with capillary refill > 2 s (default). Record `protocol_perfusion` (it is in the CSV). |
| Feedback | The toggle on the start panel shows correct/incorrect after each tag. Decide in advance and keep it the same for every participant (recommended: OFF for measurement sessions, ON for training). |
| Duration | About 10-15 minutes including briefing and questionnaires. |

## 2. Before any session

1. Get approval from the instructor/ethics contact the course requires. Prepare the consent form (what is logged, voluntary, may stop at any time, no personal data stored in the software, how long data are kept).
2. Give each participant an anonymous ID `P01`, `P02`, ... Keep the key (name to ID) separate and off this repository. The software rejects any other ID format, but nothing stops someone typing a personal name into a notes field, so keep names out of every file.
3. Rehearse with `TEST` and data type `SoftwareTest` first. Delete rehearsal files or keep them in a folder named `rehearsal`.
4. Charge the headset, check the guardian boundary, have water and a seat ready. Stop immediately if a participant reports nausea or discomfort. Record simulator-sickness symptoms (SSQ) afterwards.

## 3. Running a session (in the headset)

1. Participant stands in the yard. On the start panel: press **Participant** until the intended ID shows (`P01`...), press **Data type** until it shows `HumanParticipant`, set the feedback toggle, press **Begin Exercise**.
2. Participant walks or teleports to each casualty, points and presses the trigger to select, uses the six check buttons, then presses a colour button. Tags are final.
3. When all ten are tagged, the results panel appears and three CSV files are written automatically (see section 5).
4. Immediately afterwards: SUS (10 items), NASA-TLX (6 scales), optional SSQ. Record in `templates/participant_questionnaires_template.csv` (a blank copy per study).
5. If a session is interrupted, press **Restart** (or quit): a partial file is written with `session_complete = false`. Exclude or report partial sessions explicitly; never fill gaps by hand.

## 4. Measures and equations

All are computed per session by `TriageMetrics.Compute` and written to the `_summary.csv`. Raw per-casualty rows are in `_records.csv`.

| Measure | Definition | CSV column(s) |
|---|---|---|
| Accuracy | correct / classified | `accuracy`, `correct`, `casualties_classified` |
| Under-triage rate | true Red tagged Yellow or Green / true Red | `under_triage_rate`, `true_red_under_triaged`, `true_red_total` |
| Assessment latency | time from first selecting a casualty to tagging it (s) | `assessment_latency_s` per casualty; `mean_latency_s`, `median_latency_s`, `min_latency_s`, `max_latency_s` |
| Confusion matrix | 4 x 4, rows = ground truth, columns = assigned | `_confusion.csv` |
| SUS | 2.5 x (sum of (odd item - 1) + sum of (5 - even item)) | questionnaire sheet |
| NASA-TLX | six 0-100 scales | questionnaire sheet |

Denominators that are zero are written as `NA`, never as 0. Latency starts on the first selection of a casualty; switching away and back does not restart it. A negative latency (clock error) is flagged `latency_valid = false` and excluded from the latency statistics.

Analysis suggestions (not yet performed): report N, mean, SD, median per measure; one box-plot of latency by true category; the pooled 4 x 4 confusion matrix; accuracy and under-triage overall. With N around 10-20, report descriptives and avoid claims of statistical significance.

## 5. Where the data are written

`Application.persistentDataPath/TriageTelemetry/` on the headset. For a Quest this is on the device: `/sdcard/Android/data/<package id>/files/TriageTelemetry/` (check the package id in Player Settings). Copy the files off with `adb pull` or Android File Transfer. In the editor the folder is under the Unity user data path; the results panel prints the exact path after each session.

Files per session (`<session_id>` looks like `S20261025T153000-01`):

- `<session_id>_records.csv` - one row per casualty tagged (26 columns).
- `<session_id>_summary.csv` - one row of derived metrics (17 columns).
- `<session_id>_confusion.csv` - the 4 x 4 matrix.

Copy participant files into `telemetry/P01.csv`... as the dossier asks, or keep the original names and say so. A blank header-only copy of the records format is in `templates/participant_telemetry_template.csv`.

## 6. Data handling and honesty rules

- `data_provenance` is one of `SoftwareTest`, `Pilot`, `HumanParticipant`. Only the last may be used as human-study evidence. Software test runs and rehearsals must stay labelled and must not be mixed into the human results.
- The unit tests in this repository use synthetic data written to a temporary folder. None of it is saved as a result.
- Do not edit CSV values by hand. If a file is wrong, record why and exclude it with a note.
- Do not report a result that was not produced by a real session. Anything unmeasured stays `PENDING`.

## 7. Known limitations that must appear in the write-up

- START protocol variant and thresholds come from published START descriptions (see `TECHNICAL_IMPLEMENTATION_REPORT.md`). The exercise is a training aid, not a clinically validated triage tool, and was not reviewed by a clinician.
- The scenario has only ten fixed casualties, so memorisation across repeat sessions is possible and findings are specific to this set.
- Hazard escalation is cosmetic (smoke, lighting) and does not change a casualty's findings, so "dynamic hazard conditions" is represented only as time-varying atmosphere and elapsed-time display.
- Single device type (Meta Quest), small sample, no control arm, self-selected volunteers.
- The headset build has not been installed or run on a physical device by the author of this document.
