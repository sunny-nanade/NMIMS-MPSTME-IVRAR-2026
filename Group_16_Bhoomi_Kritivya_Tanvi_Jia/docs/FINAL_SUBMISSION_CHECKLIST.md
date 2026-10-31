# Final submission checklist — IVRAR Group 16 (deadline Mon 26 Oct 2026, 23:59 IST)

Status key: **Complete** = verified with evidence · **Partial** = built, needs team check · **Pending** = only the team can do it · **Blocked** = cannot proceed until something else happens.
Updated 2026-10-26 after the professor confirmed our own repositories are evaluated and AI-assisted dossier preparation is permitted (relayed by the team; not independently verified by the author of this file).

| # | Requirement | Status | Evidence / what is missing |
|---|---|---|---|
| 1 | Triage scene, 10 casualties, menu launch, full session | Complete (editor-level) | `TriageSimulation.unity`; `TriageSceneTests` |
| 2 | EditMode tests | Complete | 109/109 (rerun 2026-10-26) |
| 3 | PlayMode tests | Complete | 31/31 (rerun 2026-10-26) |
| 4 | Findings not revealed before assessment; protocol labelled | Complete | `f054410`; APK metadata contains the neutral prompt |
| 5 | Telemetry, CSV, zero-denominator handling | Complete (software) | `TriageCsv.cs`, `TriageMetrics.cs`, tests |
| 6 | Android APK current | Complete (built) | `BUILD_REPORT.md`: 242,635,956 B, SHA-256 `a533c6d0…ecd6f4`, arm64 IL2CPP, 5 scenes |
| 7 | Tested on a physical Quest | **Planned, not done** | team plans a headset test in class; dossier 2.4 has a blank `[PENDING — COMPLETE AFTER HEADSET TESTING]` record; `MANUAL_TEST_CHECKLIST.md` section B |
| 8 | OpenXR validation exception | Partial | 1 `NullReferenceException` in package validation callback each build; build succeeds; unproven on device |
| 9 | Final-candidate DOCX | Partial | `Docs/IVRAR_dossier_DRAFT/IVRAR_Group_16_Technical_Dossier_FINAL_CANDIDATE.docx` (6 pages; Sections 1-3 and 2.4 from evidence; 4-6 and headset record pending; open items in `DOSSIER_OPEN_ITEMS.md`; previous version in `backup/`) |
| 10 | Final-candidate PDF | Partial | same folder, `.pdf`; LibreOffice preview; re-export from Word after completion |
| 11 | START source confirmed | Partial | CHEMM + Benson 1996 support both variants; team confirms the 2 s threshold in the original |
| 12 | GitHub branch and commit | Complete for pushed work | local = `bhoomi/feature/factory-rescue-vr` at `5f7ac5e` before this session's docs; later commits need a push (command below) |
| 13 | Git LFS | Complete | 249 files, `git lfs fsck` OK, retrievable |
| 14 | GitHub release `group-16-v1.0` | Pending approval | command prepared, NOT run; APK current |
| 15 | Own repository evaluated | Complete per professor | no longer a blocker; keep the repo URL in dossier Section 3 |
| 16 | Human study, N ≥ 10, consent | **Pending (grade-affecting)** | pack, script and checklist ready (`PARTICIPANT_PACK.md`, `tools/analyze_study.py`); no participant exists |
| 17 | SUS, NASA-TLX, plots, confusion matrix | Blocked by 16 | the script produces them from real files only |
| 18 | Data provenance declaration | Blocked by 16 | team answers truthfully |
| 19 | LinkedIn video | Pending (grade-affecting) | script ready: `VIDEO_SCRIPT_STORYBOARD.md`; not recorded |
| 20 | AI interaction audit (3 cases) | Partial | three real candidate cases in `MEMBER_CHECKLISTS.md`; members confirm and select dropdowns |
| 21 | Peer reviews, Groups 17 and 07 | Pending | blank forms in `PEER_REVIEW_FORMS.md`; videos not watched |
| 22 | Per-member contribution (usernames, commits, %) | Pending | git: 13 "Claude", 9 Bhoomi Bhandari (+3 on feature/bhoomi-work), none for the other three; each member supplies own data (dossier 3.2 note) |
| 23 | Learning record, signatures, sign-off | Pending | each member personally (`MEMBER_CHECKLISTS.md`) |
| 24 | "AI-driven" wording | Pending decision | scenario scripted; no ML |
| 25 | Unity 2022.3 (title sheet) vs 6000.3.21f1 (project) | Pending decision | flagged in the dossier |

## Dossier dropdowns left unchanged
Script authorship, release-runs-on-clean-device, dossier-committed, study design, provenance declaration, video-speaker, AI-audit dropdowns, peer-review scores, learning record, sign-off.

## Commands (not run by Claude)
```bash
# push everything committed since 5f7ac5e (no force)
cd ~/VR-Disaster-Simulator && git status -sb && git push bhoomi feature/factory-rescue-vr

# release: ONLY after the final APK and notes are approved
shasum -a 256 Builds/Android/DisasterResponseVR.apk   # must equal a533c6d08427da2d5268c421d9d6cb5ea64f807248abdce518c7476798ecd6f4
gh release create group-16-v1.0 Builds/Android/DisasterResponseVR.apk \
  --repo bhoomibhandari88/VR-Disaster-Simulator --target feature/factory-rescue-vr \
  --title "Disaster Response VR v1.0 (Quest APK)" --notes-file Docs/RELEASE_NOTES.md
# afterwards: download the asset and compare its SHA-256; clone fresh and run `git lfs pull`
```
