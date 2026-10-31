# LinkedIn video: script and storyboard (3:30 target, limit 3:00-4:00)

**Status:** script only. The video does not exist yet. Nobody may be described as "appearing" until recorded. Every member must speak on camera (assignment rule); no participant faces, no SAP IDs on screen. Upload natively to LinkedIn, then put the URL and the real length in dossier Section 5.

Speaking split (about 52 s each): Bhoomi (problem + START) · Kritivya (build walk-through) · Tanvi (telemetry + equations) · Jia (testing, usability, honesty). All four close together.
Words below are a draft. Each speaker may reword but must keep every factual statement true. Items in [brackets] are choices for the team.

| Time | Speaker | Screen (real footage/screenshots from the build or repo) | Script |
|---|---|---|---|
| 0:00-0:12 | Bhoomi | Title card: "Group 16: VR START Triage Trainer" with 4 names. Cut to factory yard fire (headset capture) | "After an industrial explosion, responders must sort many casualties fast. Mistakes cost lives." |
| 0:12-0:35 | Bhoomi | Dossier Section 1.1 table, then the START decision tree | "A study of a 148-patient train crash found START under-triaged 3 patients and over-triaged 79 (Kahn et al., 2009). We built a VR exercise to practise START and measure accuracy, under-triage and assessment time." |
| 0:35-0:55 | Bhoomi | Algorithm diagram: walk → breathing → rate >30 → perfusion → commands | "START asks: can they walk, are they breathing, is the rate above thirty, is perfusion adequate, can they follow commands. Our default perfusion check is capillary refill over two seconds; radial pulse is a selectable, separately logged variant." |
| 0:55-1:10 | Kritivya | Main menu, press **Triage Study**, start panel | "From the main menu, Triage Study opens the yard. Ten fixed casualties, C01 to C10, with fires, smoke and alarm lights." |
| 1:10-1:40 | Kritivya | Point, select casualty, press the six check buttons, tag colour, show a tag appear | "You walk or teleport to a casualty, point and press the trigger, run the checks you need, then tag Green, Yellow, Red or Black. Findings appear only after you run a check, and tags are final." |
| 1:40-1:50 | Kritivya | Results panel | "After the tenth tag you see accuracy, under-triage and timing, and the data files are written." |
| 1:50-2:10 | Tanvi | CSV opened; highlight columns | "Each tag is one CSV row: ground truth, your tag, timestamps, latency and which checks you used. A second file holds the summary and a third the confusion matrix." |
| 2:10-2:40 | Tanvi | Equations on screen (from dossier 2.3), STARTEvaluator snippet 2 s | "Accuracy is correct over classified. Under-triage rate is true-Red casualties tagged Yellow or Green over all true Red. Latency runs from first selecting a casualty to tagging it. If a denominator is zero we write NA, never zero." |
| 2:40-2:55 | Jia | Test runner screenshot (109 EditMode, 31 PlayMode) | "The logic is covered by 109 EditMode and 31 PlayMode tests, which all passed in the editor." |
| 2:55-3:15 | Jia | Limitations slide | "Limits: the scenario is scripted, not machine learning. Hazards are cosmetic. It is a training aid, not clinically validated. [Choose ONE true line:] We ran it on a Quest and found ___ / We have not yet run it on a physical headset." |
| 3:15-3:30 | Jia | Results slide | [If the study is done:] "N = __ real participants; mean SUS __; accuracy __; under-triage __." [If not:] "The human study is not complete, so we report no results." |
| 3:30-3:45 | All four, one line each | Four faces on screen | Bhoomi: "I learned ___." Kritivya: "I learned ___." Tanvi: "I learned ___." Jia: "One AI mistake we caught: ___ (see dossier 5.1)." |

## Recording checklist
- [ ] Record the running build (Quest capture or editor Game view). Say which it is on screen.
- [ ] Re-check every number against the dossier on the day (test counts, N, any result).
- [ ] No participant faces or names; no SAP IDs; no personal account pages.
- [ ] Length between 3:00 and 4:00; subtitles recommended.
- [ ] Post natively on LinkedIn; copy URL and mm:ss into dossier Section 5; tick "every member speaks" only if true.
- [ ] AI disclosure line in the post: script drafted with AI assistance (Claude), reviewed by the team.

## Screenshot sources in the repo
Editor captures live in `Builds/Shots/TriageSimulation_*.png` on the author's Mac (not committed). Retake them from the final build before using them.
