# Technical implementation report: START triage exercise

Project: Disaster Response VR, Unity 6000.3.21f1, branch `feature/factory-rescue-vr`. Date: 2026-10-25.
This report describes what exists in the repository and what was run. Items not run are marked as such.

## 1. What was added

A stand-alone scene, `Assets/_Project/Scenes/TriageSimulation.unity`, reached from a new **Triage Study** button on the main menu. It is not a campaign stage: no stars, unlocks or progress, and the three campaign scenes are unchanged.

Ten fixed casualties (C01-C10) lie or stand in a factory yard with fires, smoke and alarm lights. The trainee approaches a casualty, points and presses the trigger to select it, runs START checks with six buttons (walking, breathing, airway, respiratory rate, perfusion, mental status), then assigns Green, Yellow, Red or Black. Tags are final. After the tenth tag the results panel shows accuracy, under-triage and latency and the CSV files are written. **Restart** resets everything (partial data are exported first) and **Main Menu** returns to the campaign menu.

### Files (all under `Assets/_Project`)

| File | Role |
|---|---|
| `Scripts/Runtime/Triage/TriageTypes.cs` | Enums (category, branch, exam kind, provenance, perfusion criterion) and `VitalSigns` |
| `Scripts/Runtime/Triage/STARTEvaluator.cs` | The START decision tree and the text shown for each check |
| `Scripts/Runtime/Triage/TriageSession.cs` | Pure-C# session: state, timing, tagging, records. Takes an injectable clock, so it is fully unit-testable |
| `Scripts/Runtime/Triage/TriageMetrics.cs` | Accuracy, under-triage, latency statistics, confusion matrix, zero-denominator handling |
| `Scripts/Runtime/Triage/TriageCsv.cs` | CSV columns, escaping, record/summary/confusion text |
| `Scripts/Runtime/Triage/TriageTelemetryLogger.cs` | Writes the three files under `persistentDataPath/TriageTelemetry` |
| `Scripts/Runtime/Triage/TriageScenario.cs` | The fixed ten-casualty set |
| `Scripts/Runtime/Triage/STARTTriageSimulationManager.cs` | MonoBehaviour tying session, scene objects, hazards, logger and UI together |
| `Scripts/Runtime/Triage/CasualtyProfile.cs`, `CasualtySelectable.cs` | Per-casualty data and visuals; XR select hook |
| `Scripts/Runtime/Triage/TriageUI.cs` | Start, assessment and results panels |
| `Scripts/Runtime/Triage/TriageHazardEscalation.cs` | Cosmetic hazard that grows every 60 s (smoke and firelight) |
| `Scripts/Runtime/Triage/TriageDesktopInput.cs` | Keyboard controls for the editor |
| `Scripts/Editor/SceneBuilder.Triage.cs` | Menu `Rescue Sim > Build > Triage Scene` builds the scene |
| `Tests/EditMode/TriageTests.cs`, `Tests/PlayMode/TriageSceneTests.cs` | Tests (section 5) |

Changed existing files: `MainMenuController.cs` (new optional button), `CampaignProgress.cs` (one constant, `StageCatalog.TriageScene`), `SceneBuilder.cs`/`SceneBuilder.Campaign.cs` (menu button, Build Settings entry), `CaptureViews.cs`, `OpenScenes.cs` (editor helpers), `MainMenu.unity`.

Origin note: the decision-tree idea and the `VitalSigns` fields follow the foundation Bhoomi committed on `feature/bhoomi-work` (commit `ea174c8`). That code uses a different namespace and folder and was not merged; the new files are original to this branch. Authorship of each file for the dossier must be confirmed by the team.

## 2. START variant and source

- **Protocol:** START adult (Simple Triage and Rapid Treatment), four tags: Green (minor), Yellow (delayed), Red (immediate), Black (deceased/expectant).
- **Order of checks implemented:** walks -> Green. Not breathing: reposition airway; still not breathing -> Black, breathing returns -> Red. Breathing: respiratory rate above 30/min -> Red. Perfusion fails -> Red. Cannot follow simple commands -> Red. Otherwise Yellow. (`STARTEvaluator.Evaluate`)
- **Perfusion check (one variant at a time, never mixed):** default `CapillaryRefill` = refill longer than 2 s fails (the original 1983 Newport Beach START formulation). Alternative `RadialPulse` = absent radial pulse fails (the 1996 Benson revision). Chosen by the serialized `perfusion` field on the manager; the choice is shown on the start panel, on the check button label and in the results, and is written to every CSV row (`protocol_perfusion`). Only the observation relevant to the chosen variant is recorded (`obs_cap_refill_s` or `obs_radial_pulse`).
- **Sources (read 2026-10-25; the team must open them before submission):** US HHS CHEMM, START Adult Triage Algorithm (chemm.hhs.gov/startadult.htm and the text version chemm.hhs.gov/startalgotext.htm). It confirms the order walking, breathing, respiratory rate (30/min), perfusion by radial pulse **or** capillary refill, obeying commands, and the four tags, and states that the original START used capillary refill while Benson et al. (1996) replaced it with radial pulse. The CHEMM text page gives **no seconds threshold** for capillary refill; the "> 2 s" figure is the commonly taught value (for example the Wikipedia START article, a tertiary source), so cite a primary or course source for it. Benson M, Koenig KL, Schultz CH. "Disaster triage: START, then SAVE - a new method of dynamic triage for victims of a catastrophic earthquake", Prehospital and Disaster Medicine, 1996 (DOI 10.1017/S1049023X0004276X; volume and pages not verified).
- **Limitations:** the exercise is a training aid built from a published algorithm. It is **not clinically validated**, has not been reviewed by a clinician, uses invented casualty findings, and covers adults only (no JumpSTART for children). The "about 30 seconds per patient" figure that appears in the dossier template comes from a secondary document and is not used or relied on by the software.

## 3. Telemetry (what is logged, what is derived)

**Raw, per casualty (`_records.csv`, 26 columns):** `session_id, data_provenance, participant_id, protocol_perfusion, casualty_id, ground_truth, assigned, is_correct, true_red_under_triaged, expected_branch, assessment_start_utc, assessment_end_utc, assessment_start_s, assessment_end_s, assessment_latency_s, latency_valid, hazard_level, inputs_examined, airway_repositioned, obs_can_walk, obs_breathing, obs_breathing_after_airway, obs_resp_rate, obs_cap_refill_s, obs_radial_pulse, obs_follows_commands`. The `obs_*` fields are empty unless the trainee actually used that check, which is how "decision inputs" are recorded.

**Derived, per session (`_summary.csv`, 17 columns):** `session_id, data_provenance, participant_id, protocol_perfusion, casualties_total, casualties_classified, session_complete, correct, accuracy, true_red_total, true_red_under_triaged, under_triage_rate, mean_latency_s, median_latency_s, min_latency_s, max_latency_s, invalid_latency_count`. A zero denominator is written as `NA`.

**Matrix (`_confusion.csv`):** header `ground_truth\assigned,Green,Yellow,Red,Black`, one row per true category.

No personal data: the participant is an ID (`P01`..`P999` or `TEST`); free text is not collected. Cells are escaped per RFC 4180 and a leading `= + - @` is neutralised.

Equations: `accuracy = correct / classified`; `under_triage_rate = true_red_under_triaged / true_red_total`; `latency = t_tag - t_first_select` per casualty; mean and median over valid latencies.

Worked example (illustrative only, not a result): a session tags five casualties, two of the four true-Red as Yellow and Green. `true_red_under_triaged = 2`, `true_red_total = 4`, `under_triage_rate = 0.5`. With latencies 2, 4, 6, 8, 10 s, `mean_latency_s = 6` and `median_latency_s = 6`. This is exactly the case in the unit test `Metrics_AccuracyUnderTriageAndLatency_AreCorrect`.

## 4. Controls

- **Headset / Meta XR Simulator:** point and trigger select a casualty and press UI buttons; locomotion as in the campaign (left stick walk, right stick turn, teleport).
- **Editor keyboard (`TriageDesktopInput`):** Enter begin, Tab next casualty, 1-6 run the six checks, 7/8/9/0 or F1-F4 tag Green/Yellow/Red/Black, F5 restart. The keys avoid the Meta XR Simulator's own bindings.
- **Distance rule:** a casualty must be within 8 m of the head to be selected. The player starts in the middle of the yard, so all ten are in range at the start.

## 5. Verification actually performed (2026-10-25, macOS editor target)

| Check | Result |
|---|---|
| Unity compile | 0 errors, console empty after refresh |
| EditMode tests | 109 passed, 0 failed (57 existing + 52 triage cases) |
| PlayMode tests | 31 passed, 0 failed (23 existing + 8 triage scene tests, incl. findings-not-revealed and hazard escalation) |
| Scene build | `TriageSimulation.unity` generated by the builder; `MainMenu.unity` regenerated with the new button |
| Visual check | Editor captures of the yard (aerial, close, panel views) reviewed; casualties, tags, yard lines and signs render. Smoke/flame particles and the UI panels appear only at runtime and were not captured |
| Android APK | See `RELEASE_NOTES.md` for the result of the build run |
| Physical headset | **Not tested.** No device, no `adb` install, no comfort or performance measurement |

The six PlayMode triage tests load the real scene and drive the real manager, UI buttons and logger (full ten-casualty run with CSV files written to a temporary folder, partial export on restart, UI begin button, wrong-tag feedback, retag refusal, menu button present). The EditMode tests cover every START branch and boundary (RR 30/31, refill 2.0/2.01 s, both perfusion variants), correct/incorrect and under-triage cases, zero-true-Red and zero-record denominators, latency (first-selection start, switching, negative step), partial and invalid sessions, reset and repeat runs, CSV headers, escaping, row completeness, file export and failure paths, scenario shape, and campaign compatibility.

## 6. Known defects and risks

1. **Not run on a headset.** Interaction ergonomics (ray select distance, panel placement and size, comfort) are unverified.
2. **Observation text (fixed 2026-10-25).** An earlier build showed a scenario sentence on selection (for example "limping but walking") that leaked findings. The panel now shows only a neutral prompt; every finding appears only after its check button is pressed (PlayMode test `Selecting_ARedCasualty_DoesNotRevealFindings`).
3. **Hazards are cosmetic.** There is no AI or adaptive difficulty; "dynamic hazard" means smoke and light intensity growth plus an elapsed-time display.
4. **Fixed scenario.** Ten casualties, always identical, so repeat participants can memorise it.
5. **Editor-only particle/UI check.** Smoke, flames and panels were checked through automated tests and capture views, not by playing in a headset.
6. **OpenXR build exception (diagnosed, harmless to runtime).** Every Android build logs one `NullReferenceException` in `MetaQuestFeature.CreateValidationRules` (Unity OpenXR package, editor-only build validation, lambda 16 of the rule list). By counting the rules, lambda 16 is the "All Passes mode with Dynamic Resolution" check, which calls `GetFeature` on `OpenXRSettings.GetSettingsForBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup)` without a null check; it is a non-error advisory rule and does not apply to this project. The build succeeds and the APK contains `libopenxr_loader.so`, `libUnityOpenXR.so` and the Quest manifest entries. Not fixed (package code).
7. **Course-repo requirement.** The brief asks for submission in the instructor's monorepo; this work lives in the team's own repository.
