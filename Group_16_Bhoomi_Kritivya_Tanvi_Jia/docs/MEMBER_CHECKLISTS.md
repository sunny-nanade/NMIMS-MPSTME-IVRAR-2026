# Individual member checklists (each member confirms their own statements)

AI drafted this wording. Nothing here is a statement by a member until that member edits and ticks it. Do not sign, fill or tick on someone else's behalf. Git shows 13 of 22 commits on this branch authored "Claude" (AI-assisted sessions) and 9 by Bhoomi Bhandari (plus 3 on `feature/bhoomi-work`), and none under Kritivya, Tanvi or Jia, so git history cannot show who did what: describe your real work yourself.

Common items for everyone
- [ ] Read dossier Sections 1-3 and be able to explain one equation and the code snippet in 30 seconds without notes.
- [ ] Open at least the START source (CHEMM page) and Kahn 2009 yourself.
- [ ] Watch Groups 17 and 07 videos and complete `PEER_REVIEW_FORMS.md`.
- [ ] Speak in the LinkedIn video.
- [ ] Complete your own row in dossier 3.2, Section 6 (learning record, two one-line answers) and the sign-off table in Word (dropdowns, signature).

## Bhoomi Bhandari (I004) — declared role: Triage Clinical Protocol Lead
- [ ] Confirm role and what you personally did: ____________________________
- [ ] Verify START order and the 2 s capillary-refill threshold against the original sources; confirm or change the perfusion variant.
- [ ] GitHub username (yours, not guessed): ________  Commits you made: ____  Agreed effort %: ____
- [ ] Hours spent: ____  AI share of your drafting: ____  AI errors you personally caught: ____________
- [ ] Learning record (Section 6) done · Sign-off (3 statements, signature, consent choice) done

## Kritivya Mishra (I037) — declared role: XR Systems Architect
- [ ] Confirm role and what you personally did: ____________________________
- [ ] Install the rebuilt APK on a Quest and run `MANUAL_TEST_CHECKLIST.md` (headset section); record real results.
- [ ] GitHub username: ________  Commits: ____  Agreed effort %: ____
- [ ] Hours spent: ____  AI share: ____  AI errors you caught: ____________
- [ ] Learning record done · Sign-off done

## Tanvi Paithankar (I044) — declared role: Spatial Telemetry & Confusion Matrix Specialist
- [ ] Confirm role and what you personally did: ____________________________
- [ ] Run `analyze_study.py --self-test`; after the study run it on real files and check the numbers by hand for one participant.
- [ ] GitHub username: ________  Commits: ____  Agreed effort %: ____
- [ ] Hours spent: ____  AI share: ____  AI errors you caught: ____________
- [ ] Learning record done · Sign-off done

## Jia Jadhav (I069) — declared role: Human Factors & Usability Engineer
- [ ] Confirm role and what you personally did: ____________________________
- [ ] Run the human study with the team (`STUDY_OPERATIONS_CHECKLIST.md`): consent, briefing, SUS, NASA-TLX.
- [ ] GitHub username: ________  Commits: ____  Agreed effort %: ____
- [ ] Hours spent: ____  AI share: ____  AI errors you caught: ____________
- [ ] Learning record done · Sign-off done

## AI errors already on record from this project (candidates for dossier 5.1; confirm each yourself)
1. The AI-built triage scene showed casualty findings in the text on selection, before the trainee ran any check. Found in QA, fixed in commit `f054410`, with tests.
2. Test counts and APK hash in the README and release notes went stale after that fix (107/29 vs 109/31; APK predates the fix). Caught and corrected on 2026-10-25/26.
3. The 2-second capillary-refill threshold could not be confirmed on the CHEMM text page the AI could read (the PDF was blocked); it is marked "confirm". Team must check the original.
