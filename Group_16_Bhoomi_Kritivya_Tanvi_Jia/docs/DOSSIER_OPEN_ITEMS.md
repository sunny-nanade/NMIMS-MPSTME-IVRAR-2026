# Dossier open items, owners and claims still needing verification

Applies to `Docs/IVRAR_dossier_DRAFT/IVRAR_Group_16_Technical_Dossier_FINAL_CANDIDATE.docx` (6 pages). Previous version kept in `Docs/IVRAR_dossier_DRAFT/backup/`. Evidence base: repository at `74e9279`, `Docs/BUILD_REPORT.md`, `Builds/TestSummary.txt`.

## Completed from evidence
| Section | Evidence |
|---|---|
| Identification, repo URL, branch | git remote, `git rev-parse` |
| 1.1 claim 1 (START) / claim 2 (Kahn 2009) | CHEMM text pages, Benson 1996, Kahn 2009 as recorded in the table; read 2026-10-25 |
| 2 scripts, state machine, toolchain (Unity 6000.3.21f1) | `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `STARTEvaluator.cs` |
| 2.2 snippet (21 lines, 39-64) | matches current `STARTEvaluator.cs` |
| 2.3 equations, illustrative example | `TriageMetrics.cs`, unit test `Metrics_AccuracyUnderTriageAndLatency_AreCorrect` |
| 2.4 build config, test methodology, results | `ProjectSettings.asset`, `TestSummary.txt` (109/109, 31/31), `BUILD_REPORT.md` |
| 3 repo metrics | `git rev-list --count HEAD` (22), `git branch -a` (8), line count 10,087, 249 LFS files |

## Placeholders and who confirms them
| Placeholder | Where | Owner |
|---|---|---|
| Whole headset-test record table | 2.4 | Kritivya (run) + whoever tests; fill after the session |
| Release build installs/runs on clean device | Section 3 dropdown | after headset test |
| GitHub usernames, effort %, personal contribution | 3.2 | each member for themselves |
| Script authorship dropdowns (4 scripts) | 2.1 | each member; Bhoomi for any file derived from `VitalSigns.cs`/`VictimData.cs` |
| Release URL / tag | Section 3 | team, after approval |
| N, study design, dates, SUS, NASA-TLX, plots, raw CSV path | Section 4 | Jia + team, after a real study |
| Provenance declaration (4 dropdowns) | Section 4 | team, truthfully |
| LinkedIn URL, video length, all-speak dropdown | Section 5 | team, after recording |
| AI-audit dropdowns and confirmation of the 3 drafted cases | 5.1 | each member |
| Peer reviews Groups 17 and 07 | 5.2 | all members, after watching |
| Learning record, signatures, sign-off, consent choice | Section 6 | each member personally |

## Claims and citations still needing verification
1. **Capillary refill > 2 s.** Not stated on the CHEMM text pages the author could read; CHEMM PDF was blocked. Check the original START description or CHEMM PDF.
2. **"About 30 s (max 1 min) per patient"** comes from an IAVOAD guide (secondary). Find the primary or drop it.
3. **Kahn 2009 figures** (148 patients, 3 under-, 79 over-triaged) were read through the DOI/ASPR TRACIE/eScholarship summaries, not the PubMed page (blocked). Open the paper.
4. **Benson, Koenig, Schultz 1996** volume/pages not verified; DOI recorded as 10.1017/S1049023X0004276X.
5. **Template claim "under-triage leads to preventable mortality"**: Kahn 2009 reports no mortality data; the dossier states this and marks the claim Corrected.
6. **Authorised title says "AI-driven"** while the software is scripted; the dossier says so plainly. The team should agree wording with the instructor.
7. **OpenXR `NullReferenceException`**: harmlessness is unproven on a device.

## Not changed on purpose
Template guidance sentences (italic instructions), footer, the "Monorepo folder"/"Repository (monorepo)" labels (values now name the team repository), all content controls and dropdowns.

## Added 2026-10-29 (human models + hand tracking upgrade)
- The dossier PDFs/DOCX still quote EditMode 109 / PlayMode 31 and show placeholder-shape casualties. Current numbers: EditMode 121/121, PlayMode 57/57; casualties are Rocketbox humans (MIT); carry + hand tracking added. Update the test table, the casualty screenshots and the "not verified on headset" list (hand tracking, human-model performance) before submission. See `HUMANS_AND_HANDS_UPGRADE.md`.
- Credit Microsoft Rocketbox (MIT) in the asset/licence section.
- User testing: 10 people tested the game (owner's statement), but no per-participant results are in the repository. `UserTesting/vr_disaster_simulator_user_testing.csv` has 10 anonymous rows (P01-P10) with all measurements blank; fill from the original notes (`UserTesting/data_dictionary.md`), then update `UserTesting/user_testing_summary.md`. Until then the dossier may say only that 10 people tested the game: no success rates, SUS, NASA-TLX or ethics-approved-study claims. `STUDY_PROTOCOL.md` still records no consent forms on file.

- User testing (updated 2026-10-29): dossier text is in `UserTesting/dossier_findings.md`; reference-repo check found only the team roster (no tester identities) and SIMULATED sample telemetry. Real participant measurements are still missing.
