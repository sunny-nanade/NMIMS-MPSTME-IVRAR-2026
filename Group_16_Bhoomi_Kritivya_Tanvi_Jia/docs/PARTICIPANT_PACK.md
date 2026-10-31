# Participant pack: consent, briefing, SUS, NASA-TLX, SSQ (printable blanks)

**Status:** blank forms. No participant has used them. Fill one set per real adult volunteer. Never write a name on the questionnaire sheet or in any CSV; use the anonymous ID (P01, P02, ...). Keep the name-to-ID key on paper, outside the repository.

## A. Consent form (one signed copy per participant; stored by the team, never in Git)
*Instructor/ethics approval of this wording: `PENDING — TEAM INPUT REQUIRED` (ask Dr. Nanade whether a course template exists; use it if so).*

**Study:** Group 16, IVRAR (SVKM's NMIMS MPSTME). VR training exercise: tagging ten simulated casualties with the START triage method, then short questionnaires. About 15 minutes.

- I am 18 or older and I take part voluntarily. I may stop at any time, for any reason, without giving a reason and without any effect on my marks or standing.
- I understand this is a student prototype, not a medical tool, and it does not assess my clinical ability.
- The app records only my anonymous ID, my triage choices and timings. It does not record my name, face, voice or video.
- VR can cause dizziness or nausea. I can stop if I feel unwell.
- My anonymous results may be reported in a student dossier and video. Data are kept until: `____________` (team to fill, e.g. end of course assessment) and then deleted.
- Optional: I agree to the anonymous data being used for course research.  ☐ Yes  ☐ No

Participant ID (assigned by team): `P__`      Date: `__ / __ / ____`
Participant signature: ______________________   Team member who explained the study: ______________________

## B. Briefing script (read the same words to everyone)
"You will see a factory yard on fire with ten injured people. Use the START method to decide a colour for each: Green (can walk), Yellow (delayed), Red (immediate), Black (deceased/expectant). Walk or teleport to a person, point and press the trigger to select, use the six check buttons as you need them, then press a colour. Colours are final. Work at your own pace; there is no right pace. Please do not ask us for hints during the run. Tell us at once if you feel unwell. After the last person, a results panel appears, then I will give you three short questionnaires."
Do **not** explain START beyond this unless every participant gets the same explanation. Record anything you changed in `notes`.

## C. Fixed settings for the whole study (decide once, write on the sheet)
Build commit: `________`  APK SHA-256: `________`  Perfusion variant: `CapillaryRefill` / `RadialPulse`  Feedback toggle: `ON` / `OFF`  Device: `________`

## D. SUS (System Usability Scale), score 1 = strongly disagree to 5 = strongly agree
1. I think that I would like to use this system frequently.
2. I found the system unnecessarily complex.
3. I thought the system was easy to use.
4. I think that I would need the support of a technical person to be able to use this system.
5. I found the various functions in this system were well integrated.
6. I thought there was too much inconsistency in this system.
7. I would imagine that most people would learn to use this system very quickly.
8. I found the system very cumbersome to use.
9. I felt very confident using the system.
10. I needed to learn a lot of things before I could get going with this system.

Enter answers as `sus_q1..sus_q10` (integers 1-5). SUS = 2.5 x (sum of (item-1) for odd items + sum of (5-item) for even items). Write `sus_score` only if you computed it by hand; the analysis script recomputes it and flags any mismatch. Source of the scale: Brooke, J. (1996), "SUS: a quick and dirty usability scale". The team should cite it from the original.

## E. NASA-TLX (raw, six scales, mark 0 to 100; each scale has 21 ticks of 5)
Mental demand: how mentally demanding was the task? (Very low 0 ... Very high 100)
Physical demand: how physically demanding?
Temporal demand: how hurried or rushed was the pace?
Performance: how successful were you in accomplishing what you were asked to do? (Perfect 0 ... Failure 100)
Effort: how hard did you have to work to accomplish your level of performance?
Frustration: how insecure, discouraged, irritated, stressed and annoyed were you?
Enter as `nasa_tlx_*`. The analysis reports the raw (unweighted) mean of the six scales, per participant.

## F. SSQ-style symptom check (optional, simplified)
Enter a count or a 0-3 severity per cluster as agreed in advance: `ssq_nausea`, `ssq_oculomotor`, `ssq_disorientation`. State your scoring rule in the dossier. This is not the full Kennedy SSQ weighting unless you apply it.

## G. Where each number goes
Telemetry: three CSVs per session copied unedited from the headset into `telemetry/` (see `STUDY_PROTOCOL.md` section 5). Questionnaires: one row per participant in `telemetry/questionnaires.csv` (header from `templates/participant_questionnaires_template.csv`). Then run `python3 Docs/tools/analyze_study.py --records telemetry --questionnaire telemetry/questionnaires.csv --out analysis`.
