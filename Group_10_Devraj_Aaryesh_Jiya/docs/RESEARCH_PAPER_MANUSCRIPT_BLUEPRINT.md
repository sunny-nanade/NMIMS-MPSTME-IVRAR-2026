# Research Paper Manuscript Blueprint (4-Page IEEE Format)
## Controller-Free Architectural Room Authoring in VR Using Markerless MediaPipe Hand Tracking and Gesture-Driven Interaction

### Research Question
**How do first-time users experience building three-dimensional architectural structures (rooms) in VR using bare-hand gestures alone, in terms of perceived usability, workload and interaction breakdowns?**

> Rule for this document: every number in Sections IV-V must come from real data: the session videos, and any CSV or questionnaire that exists. Nothing from `SIMULATED_*` files or the dropped colour-marker topic may appear.
>
> **Data actually collected (27 Oct 2026, AR/VR Lab):** 10 first-time users on the Quest 2 standalone build, video-recorded. No trial CSV (logger needs a keyboard) and no SUS / NASA-TLX. Write Sections IV-V around what the videos support (completion, breakdowns) and state the missing measures as a limitation.

---

### Abstract (fill the brackets last)
Building architectural spaces in VR normally relies on handheld controllers, whose trigger gives an unambiguous confirmation signal. Without them, intent must be inferred from noisy hand-pose estimates, with no tactile click, no direct depth from a flat camera image, and a trade-off between jitter and lag. We built a controller-free room-authoring system: a webcam feeds the MediaPipe Hand Landmarker, 21 landmarks per hand are streamed to Unity over UDP, smoothed with a One Euro filter and classified by a geometric gesture recogniser that drives a pinch-based tool for placing floor corners, closing the room, raising walls, editing and colouring. The same interaction also runs on Meta Quest 2 built-in hand tracking. Ten first-time users built a structure of their own design on a standalone Quest 2 build while their sessions were video-recorded; [n]/10 completed a structure unaided, and the most frequent breakdowns were [list from video review]. Usability and workload questionnaires were not collected, so we report observed completion and failure modes only. We discuss design lessons and limits.

### Author Contribution Matrix
| Roll | Name | Specialisation | Manuscript ownership | Branch |
|---|---|---|---|---|
| R014 | Devraj Ghumare | Computer Vision Pipeline Lead | III.A Capture and detection; IV.C Latency and frame rate | `feat/r014-computer-vision-pipe` |
| R045 | Aaryesh Pathare | XR Systems Architect | III.C Unity integration and room-authoring interaction; IV Study design and logging | `feat/r045-xr-systems-architect` |
| R054 | Jiya Saxena | Gesture Recognition Specialist | III.B Filtering and gesture recognition; V Usability, workload and breakdowns | `feat/r054-gesture-recognition-` |

*Confirm the assignment against `git log` per file before the viva; each member must explain their own code.*

---

### Section I: Introduction
- **Problem:** controller-free construction removes the physical trigger; a pinch has no click; depth from a webcam is indirect; smoothing trades jitter for lag.
- **Gap:** hand-based sketching and hand tracking as a controller alternative are established. Evidence is limited on whether first-time users can complete an open-ended architectural construction workflow (footprint, walls, edit, colour) with a small gesture set, and where it breaks down.
- **Research question and sub-questions:** RQ1 usability (SUS), RQ2 workload (NASA-TLX), RQ3 interaction breakdowns, RQ4 feasibility (pipeline latency, fps, and whether outcomes hold on both tracking sources).
- **Hypotheses (exploratory):** H1 SUS above 68; H2 raw NASA-TLX at or below the midpoint; H3 users complete a self-designed structure unaided, reported as [n]/[N].
- **Contributions:** (1) a controller-free room-authoring workflow running on a webcam and on Quest 2 hand tracking; (2) a user-centred account of usability, workload and failures; (3) design lessons on thresholds, filtering and latency. No claim of being first.

### Section II: Related Work
- Marker and glove-based tracking as the older approach (`Wang2009`); surveys of vision-based gesture recognition (`Rautaray2015`).
- Hand tracking versus controllers and user experience in VR (`Steed2025`, `Pardo2026`); hand-tracking system design and usability (`Coox2025`); visual error feedback for gestures (`Fidalgo2025`).
- On-device landmark detection (`Zhang2020`); input filtering (`Casiez2012`).
- **Closest prior work to cite and distinguish:** hand-tracking sketching in VR for architectural design (TUM master's thesis) and Sketching with Hands (KAIST, UIST 2016). Retrieve full citations before writing.
- **Gap statement:** confirm with a fresh literature search before submission.

### Section III: System
- **A. Capture and detection:** webcam (MJPG, buffer 1), MediaPipe Hand Landmarker, two hands, timestamped JSON over UDP to port 5005.
- **B. Filtering and gestures:** One Euro filter (minCutoff 1.5 Hz, beta 25 after retuning). Palm-size-normalised geometry from the wrist to middle-MCP distance. Pinch with hysteresis (enter 0.35, exit 0.50) and 3-frame debounce. Gestures: pinch, point, fist, open palm, peace, thumbs-up; both palms open held 0.6 s switches mode (1.0 s cooldown).
- **C. Unity integration and interaction:** `HandProviderSelector` switches between `WebcamHandProvider` and `MetaXRHandProvider`. `RoomBuilderController` places grid-snapped corners (0.25 m), closes the room, extrudes walls (0.2-4.0 m) and paints; Edit / Color / View modes; undo and delete. `XRGuidePanel` shows in-headset instructions. `TrialLogger` records sessions.
- **Figure 1:** system diagram (redraw; the existing `figure1_system_architecture.png` shows the dropped topic).

### Section IV: Evaluation
- **Participants:** 10 first-time users, AR/VR Lab, 27 Oct 2026 (confirm consent forms are held).
- **Build:** Quest 2 standalone APK, Meta XR hand tracking.
- **Task:** open-ended; each participant builds a structure of their own design with their hands.
- **Measures actually available:** video of each session (completion, what was built, breakdowns observed). Not collected: SUS, NASA-TLX, logged time / corners / undo counts, webcam latency (the trial logger needs a keyboard and did not run in the headset build).
- **Analysis:** completion counts and a tally of breakdown types from the videos; thematic notes. All inference exploratory. H1, H2 and the SUS / TLX parts of RQ1 and RQ2 are reported as untested.
- **Figures:** breakdown frequencies from the video review; a still from a session. Development-time HUD observations (60-100 ms, about 15 fps in dim light, about 30 fps after the lighting fix) may be mentioned as informal observations, not as results.

### Section V: Usability, Workload and Breakdowns
- Report the completion count and what participants built. State clearly that SUS and NASA-TLX were not collected.
- Breakdown analysis: missed or double pinches, tracking loss, lighting and frame-rate effects, left/right label issues, users for whom pinch failed.
- **AI-assisted errors caught (dossier 5.1):** an unrealistic sub-15 ms latency claim; a filter beta far too low (hand felt late); frame-based pinch debounce; wrong Unity version in the old docs.

### Section VI: Conclusion and Future Work
- Answer RQ3 (and H3) from the videos, state that RQ1, RQ2 and RQ4 could not be answered with the data collected, and give the limits (small N, student participants, no questionnaires, no logged latency).
- Future work: collect SUS and NASA-TLX, log trials in the headset build (replace the keyboard trigger with an in-headset start gesture), depth estimation for the webcam route, per-user pinch calibration, larger and more varied participants, comparison with controller-based tools.

---

### References
See `docs/LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md`. Re-check every DOI, author list and venue on the publisher page before submission.
