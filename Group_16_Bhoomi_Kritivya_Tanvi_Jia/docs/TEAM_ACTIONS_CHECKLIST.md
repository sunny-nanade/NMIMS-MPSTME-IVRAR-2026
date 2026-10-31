# Remaining team actions (Group 16, deadline Monday 26 Oct 2026, 23:59 IST)

Legend: DONE = evidence exists in the repository. PARTIAL = built but needs your check. PENDING = only the team can do it. Nothing here has been marked complete without evidence.

## A. Software (done by Claude, needs your review)

| Item | Status | Evidence |
|---|---|---|
| Triage scene with 10 casualties, START workflow, 4 tags | DONE (editor-tested) | `TriageSimulation.unity`, `TriageSceneTests` pass |
| `STARTTriageSimulationManager.cs`, `TriageTelemetryLogger.cs`, CSV export | DONE | `Scripts/Runtime/Triage/` |
| Unit and scene tests | DONE | EditMode 109/109, PlayMode 31/31 |
| Android APK built with the triage scene | See `RELEASE_NOTES.md` | build report |
| Run on a Quest / Meta XR Simulator | **PENDING** | nobody has played it in a headset |
| Review code, decide authorship statements | **PENDING** | 13 of 22 commits are authored "Claude", 9 by Bhoomi Bhandari; no commits under the other three names; per-member effort cannot be derived from git |

## B. Git and release (needs your GitHub login)

1. Review the local commits (`git log --oneline -8`), then push without force (commands in the final report / `RELEASE_NOTES.md`).
2. Ask Dr. Nanade whether the course expects the code in the instructor's monorepo (`sunny-nanade/NMIMS-MPSTME-IVRAR-2026`, folder `Group_16_...`). The brief's rule is that each member commits from their own account.
3. Publish the GitHub Release `group-16-v1.0` with the APK **after** the team agrees. Verify the SHA-256 listed in `RELEASE_NOTES.md`.
4. Clone into a fresh folder and run `git lfs pull` to confirm a clean checkout.

## C. Dossier (`Docs/IVRAR_dossier_DRAFT/`)

| Section | Status | What you must do |
|---|---|---|
| 1.1, 1.2 claims and baseline | PARTIAL | Open each cited source yourselves and confirm the verdicts. Do not rely on the unattributed "30 seconds per patient" figure |
| 2 code, snippet, equations | PARTIAL | Confirm which member wrote which script; check the snippet and worked example match the final code |
| 3 repo, commit, LFS | PARTIAL | Fill the final commit hash and release link after you push; usernames and effort percentages must come from you |
| 4 results (N, SUS, NASA-TLX, plots, confusion matrix, provenance declaration) | **PENDING** | Needs a real study (`STUDY_PROTOCOL.md`). No human data exists |
| 5 LinkedIn video URL, length, AI-error audit, peer reviews (Groups 17 and 07) | **PENDING** | Make the video, log real AI mistakes you caught, watch and score the two groups |
| 6 individual learning record, sign-off, signatures, consent | **PENDING** | Each member completes this personally |

## D. Human study (shortest realistic path)

1. Today: pick a day, book a headset, print consent forms, rehearse once with `TEST`.
2. Run at least 10 volunteers (anonymous IDs `P01`...). Keep the SUS/NASA-TLX sheet filled as you go.
3. Copy the CSV files off the headset, compute the summary table and box-plot, then fill Section 4.
4. If you cannot run N >= 10 by the deadline, say so plainly in Section 4 (N and the shortfall). Do not simulate participants.

## E. Video (3:00-4:00, every member speaks)

0:00 problem and one verified number (Kahn 2009) - 0:20 the running build - 1:30 START flow and one equation - 2:15 real results (or an honest "pilot only" statement) - 3:00 reflections including one real AI error. Upload to LinkedIn natively; no participant faces or SAP IDs.

## F. Decisions only the team can make

1. How to describe the "AI-driven" part honestly (the software is scripted, with no machine-learning model).
2. Whether the instructor accepts START with capillary refill (default) or wants the radial-pulse variant. Switch via the manager's `perfusion` field and rebuild.
3. Whether to mirror the work into the course monorepo.
4. Unity 6 versus the dossier's "Unity 2022.3 LTS" statement: correct the dossier to the real version (6000.3.21f1).
