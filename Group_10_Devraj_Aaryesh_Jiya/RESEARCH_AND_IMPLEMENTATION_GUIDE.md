# Research and Implementation Guide
## IVRAR Group 10 - Controller-Free Architectural Room Authoring in VR Using Markerless MediaPipe Hand Tracking and Gesture-Driven Interaction

---

## 1. Technical Formulation

### 1.1 Landmarks and palm normalisation
MediaPipe returns 21 landmarks per hand. All gesture thresholds are scaled by palm size so they hold for different hand sizes and distances from the camera:

    palm = || p[0] - p[9] ||            (wrist to middle-finger MCP)
    pinch = || p[4] - p[8] || / palm    (thumb tip to index tip)

A finger counts as extended when its tip-to-wrist distance exceeds 1.15 times its MCP-to-wrist distance, and as curled when it is below 0.85 times.

### 1.2 Pinch with hysteresis and debounce
- Enter pinch when `pinch < 0.35`; leave it only when `pinch > 0.50`. The gap between the two thresholds stops flicker.
- A new gesture label is committed only after 3 consecutive frames (`debounceFrames`).
- Two open palms held for 0.6 s switch mode, with a 1.0 s cooldown.

### 1.3 One Euro filter
For each joint coordinate the cutoff adapts to speed:

    fc = minCutoff + beta * |dx/dt|

With positions in metres, `beta = 0.005` never raised the cutoff, so the hand felt about 0.5 s late. The retuned defaults are minCutoff 1.5 Hz and beta 25 (about a 9 Hz cutoff at 0.3 m/s). *Check that `GestureConfig.cs` (which still lists 1.0 and 0.005) and `HandFilter.cs` agree, because the config asset overrides the code defaults.*

### 1.4 Webcam pipeline latency
Measured per packet as Unity receive time minus the Python capture timestamp, on the same PC clock:

    latency_pipeline = t_unity_receive - t_python_stamp

This excludes camera exposure and Unity render time, so it is **not** motion-to-photon. Report it as "pipeline latency" with mean and 95th percentile (`mean_latency_ms`, `p95_latency_ms`).

### 1.5 Room geometry
Corners are snapped to a 0.25 m grid on the floor, closed into a polygon, extruded from 0.2 m to 4.0 m, and painted. Participants build a structure of their own design, so there is no target size; the `area_error_pct` and `height_error_pct` columns are not used.

---

## 2. Individual Responsibilities
| Roll | Name | Role | Main modules (confirm against `git log`) |
|---|---|---|---|
| R014 | Devraj Ghumare | Computer Vision Pipeline Lead | `mediapipe_sender.py`, webcam capture settings, `WebcamHandProvider` |
| R045 | Aaryesh Pathare | XR Systems Architect | `Interaction/` (room builder, mesh builder, trial logger, XR guide panel), Meta XR integration |
| R054 | Jiya Saxena | Gesture Recognition Specialist | `GestureRecognizer`, `GestureConfig`, filtering thresholds |

Aaryesh also edited two teammates' files during integration (`HandFilter.cs` filter parameters and the sender's capture settings); these edits are to be listed as "Modified" in dossier Section 2.1.

---

## 3. Running the Project
1. **Python side:** install the packages in `ARVR_Project/Group10Gestures~/tools/requirements.txt` once (`pip install -r requirements.txt`), then run `run_mediapipe_sender.bat` (uses `hand_landmarker.task`; press q in the preview window to stop).
2. **Unity:** open `ARVR_Project` in Unity 6000.5.8f1, load scene `Group10_Main`. Turn the Meta XR Simulator window off for webcam mode.
3. **Source switch:** F1 toggles Webcam / Meta XR.
4. **Sessions:** F2 start a recorded trial, F3 end and save as incomplete, F4 next participant. A recorded trial ends automatically when the finished room is painted. Ask each participant to build a structure of their own design; note what they built and where they struggled, then give SUS and NASA-TLX.
   *As run on 27 Oct 2026 (AR/VR Lab, 10 users, Quest 2 standalone APK): the headset build has no keyboard, so F2 could not start the logger and no trial CSV exists; sessions were video-recorded and SUS / NASA-TLX were not collected. To record trials with the logger, run the PC / Editor build with the keyboard attached.*
5. **Headset:** Quest 2 via Quest Link for PC-VR, or install `Group10_HandRoomBuilder_v1.0.apk` for standalone Meta XR hand tracking. Cast the headset view to the laptop with Meta Quest Developer Hub for recording.

## 4. Analysis Workflow
1. Collect `TrialData/g10_trials.csv`, the SUS / NASA-TLX sheets, comments and observer notes from real participants. *For the 27 Oct sessions only the 10 videos exist: review them, count unaided completions and tally the breakdowns (missed or double pinches, tracking loss, mode confusion).*
2. If a CSV is produced later, copy it into the repo `telemetry/` folder.
3. Update `telemetry/generate_paper_figures.py` and the calculators to read this data (they currently read the `SIMULATED_*` files). Report SUS and TLX against the H1 and H2 benchmarks, plus completion counts, breakdown themes and latency / fps.
4. Re-plot, then regenerate `docs/figures/`.

## 5. Verification Checklist
- [ ] Every number in the dossier, README and paper comes from the real CSV or questionnaires.
- [ ] No `SIMULATED_*` file is cited as evidence.
- [x] 10 real participants tested on 27 Oct 2026 (videos kept); [ ] consent forms held; [ ] SUS / NASA-TLX not collected, so no usability or workload claim.
- [ ] Six (plus method) references re-checked against publisher pages.
- [ ] Figures 1-3 redrawn for the new pipeline.
- [x] `TEAM_ROSTER.json` has all three GitHub usernames and the LinkedIn URL.
- [x] Release `group-10-v1.0` contains the Quest 2 APK; no build is committed to Git history.
- [ ] Each member can explain their own code on a whiteboard in 30 seconds.
