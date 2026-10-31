# IVRAR Group 16 — Submission audit (verified facts, pending items, blockers)

Date: 2026-10-25 (updated after the triage implementation) · Project: Disaster Response VR (Unity 6000.3.21f1) · Local branch `feature/factory-rescue-vr`

## 1. Verdict
The repository now contains both the three-stage rescue game and a **START mass-casualty triage exercise** that matches the dossier's software requirements (scene with 10 casualties, `STARTTriageSimulationManager.cs`, `TriageTelemetryLogger.cs`, CSV telemetry, accuracy / under-triage / latency / confusion matrix). It is tested in the editor and an APK builds, but it has **not been run on a headset** and **no human study has been done**. Dossier Sections 4, 5 and 6 and the individual-contribution table are therefore still empty, on purpose.

## 2. Verified facts
| Fact | Evidence |
|---|---|
| Triage code commits `b2b6d84` and `f054410` (fix: findings no longer revealed on selection; protocol labelled in UI) on `feature/factory-rescue-vr` (parent `ade9e21`); docs commit follows it. Neither is pushed | `git log` |
| 249 files tracked by Git LFS, `git lfs fsck` OK; the triage commit added no binaries | `git lfs ls-files` |
| No APK, Library, Temp or Builds in any commit | `git log --all --name-only` finds 0 `.apk`; `.gitignore` covers `Builds/`, `Library/` |
| EditMode 109/109 pass (57 existing + 52 triage cases); PlayMode 31/31 pass (23 existing + 8 triage) | Unity Test Runner, `Builds/TestSummary.txt` |
| Triage scene builds from `Rescue Sim > Build > Triage Scene`; editor capture views reviewed; no console errors after compile | `Builds/Shots/TriageSimulation_*.png` (not committed) |
| Android build Succeeded (1 min incremental, 2026-10-26), 1 non-blocking OpenXR validation `NullReferenceException`, 354 warnings, 5 scenes | `Builds/android-build-report.txt` |
| APK (rebuilt 2026-10-26 with fix f054410; see BUILD_REPORT.md) `Builds/Android/DisasterResponseVR.apk`: 242,635,956 bytes (231.4 MiB), SHA-256 `a533c6d08427da2d5268c421d9d6cb5ea64f807248abdce518c7476798ecd6f4`, IL2CPP ARM64, Quest manifest entries, `level0`-`level4` | build report, `unzip -l`, manifest strings |
| C# in `Assets/_Project`: 84 files, 10,012 lines | `find … | wc -l` |
| Real participant telemetry files: **0** | no `telemetry/` folder |
| APK **not** installed or run on any device | no headset, no `adb` |

## 3. Requirement-by-requirement
| Requirement | Status | Where |
|---|---|---|
| Dedicated industrial triage scene, 10 identifiable casualties | DONE | `TriageSimulation.unity`, `TriageScenario.cs` |
| XR workflow: approach, assess, assign; four categories | DONE (editor/simulator-level) | `STARTTriageSimulationManager.cs`, `TriageUI.cs` |
| Begin / complete / restart, feedback, desktop controls | DONE | `TriageUI.cs`, `TriageDesktopInput.cs` |
| One START variant, source named, no silent mixing | DONE | technical report section 2 |
| Telemetry CSV: ids, ground truth, assigned, timestamps, latency, inputs, correctness, under-triage, provenance, no PII | DONE | `TriageCsv.cs` (26 + 17 columns) |
| Accuracy, under-triage, latency, zero-denominator handling, raw/derived separation | DONE | `TriageMetrics.cs`, `NA` values |
| Tests for branches, categories, metrics, CSV, reset, campaign compatibility | DONE | `TriageTests.cs`, `TriageSceneTests.cs` |
| Unity 2022.3 LTS in the title sheet | **DISCREPANCY** | project is 6000.3.21f1; flagged in the dossier note, team must confirm with the instructor |
| "Virtual triage belt with 4 colour tags" | **DISCREPANCY** | implemented as colour tags on casualties plus panel buttons, not a belt |
| "AI-driven" | **GAP** | scenario is scripted; no machine-learning component. Decide how to word this (see checklist) |
| Headset interaction, comfort, frame rate | **UNVERIFIED** | never run on a physical device |
| Human study, SUS, NASA-TLX, confusion matrix from people | **PENDING — TEAM INPUT REQUIRED** | plan in `STUDY_PROTOCOL.md`, blank templates in `Docs/templates/` |

## 4. Dossier (`Docs/IVRAR_dossier_DRAFT/`)
Files: `IVRAR_Group_16_Technical_Dossier_v2_DRAFT.docx` and `.pdf` (5 pages; the earlier 4-page draft `IVRAR_Group_16_Technical_Dossier.docx/.pdf` is kept as it was, and the instructor's original file was never touched). All 155 content controls, 85 drop-downs and 25 tables are preserved. Edited at XML level; the PDF is a LibreOffice preview only. **Not for submission as-is.**

| Section | Status |
|---|---|
| 1.1 claims, 1.2 baseline | PARTIAL: written from sources I could read (CHEMM, Benson 1996, Kahn 2009 via DOI/ASPR TRACIE/eScholarship). PubMed and one Cambridge page blocked me. The team must open every source and confirm the verdicts. The "about 30 s per patient" figure comes from a secondary IAVOAD document and is attributed as such; the "preventable mortality" wording is not supported by Kahn 2009 |
| 2 script list, snippet, equations | PARTIAL: real file names, a 21-line line-numbered snippet of `STARTEvaluator.Evaluate` (lines 39-64), equations mapped to CSV columns and a worked example labelled illustrative. Authorship dropdowns left unselected; AI-use disclosed in the table and note |
| 3 repo facts | PARTIAL: commit hash, LFS and metrics filled from the team repo (not the monorepo); release URL, clean-device and dossier-committed ticks pending; 3.2 member table PENDING |
| 4 results, plots, provenance declaration | PENDING — TEAM INPUT REQUIRED |
| 5 LinkedIn URL, length, AI-error audit, peer reviews (Groups 17 and 07) | PENDING — TEAM INPUT REQUIRED |
| 6 learning record, sign-off, signatures, consent | PENDING — TEAM INPUT REQUIRED |

## 5. Push and release (needs your GitHub credentials; none were pushed)
```bash
cd ~/VR-Disaster-Simulator
git lfs install
git remote -v                                  # bhoomi -> bhoomibhandari88/VR-Disaster-Simulator; origin is queenjj11 (NOT the target)
git status && git log --oneline -4
git lfs ls-files | wc -l                       # expect 249
git push -u bhoomi feature/factory-rescue-vr   # no force; LFS objects upload through the pre-push hook
# only after the team agrees:
gh release create group-16-v1.0 Builds/Android/DisasterResponseVR.apk \
  --repo bhoomibhandari88/VR-Disaster-Simulator --target feature/factory-rescue-vr \
  --title "Disaster Response VR v1.0 (Quest APK)" --notes-file Docs/RELEASE_NOTES.md
```
Then compare the asset's SHA-256 with section 2, clone into a fresh folder and run `git lfs pull`.

## 6. Risks
1. **Instructor's rule:** the brief says to push to the course monorepo (`sunny-nanade/NMIMS-MPSTME-IVRAR-2026`, folder `Group_16_Bhoomi_Kritivya_Tanvi_Jia/`) with each member committing from their own account. Confirm that your own repo is acceptable.
2. **Contribution evidence:** git shows 13 of 22 commits on this branch authored "Claude" and 9 by Bhoomi Bhandari (plus 3 more on `feature/bhoomi-work`); nothing under Kritivya, Tanvi or Jia. Git cannot support per-member effort in Section 3.2; each member must state their own contribution.
3. **No human data:** Section 4 and the video score nothing without a real study. The deadline is Monday 26 Oct 2026, 23:59 IST.
4. **Viva:** authorship of the triage code must be agreed and understood; the code was drafted with an AI assistant and must be disclosed as such.
5. **Unverified on hardware:** comfort, frame rate and controller interaction.
6. **Unresolved build error:** one OpenXR validation `NullReferenceException` on every Android build (non-blocking).
7. **Clinical claim:** START is implemented from published descriptions only and is not clinically validated.
8. **Uncommitted Unity noise:** six Unity-regenerated project files and `ProjectSettings/BuildProfileUtilityOpenXR.asset` were deliberately left out of the commits.
