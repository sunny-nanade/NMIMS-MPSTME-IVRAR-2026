# IVRAR Group 10: Controller-Free Architectural Room Authoring in VR Using Markerless MediaPipe Hand Tracking and Gesture-Driven Interaction

**Course:** Immersive Virtual Reality & Augmented Reality (IVRAR, 702TG0C003) | AY 2026-27, Semester V  
**Institution:** SVKM's NMIMS, Mukesh Patel School of Technology Management & Engineering (MPSTME), Mumbai  
**Batch:** B2

---

## Research Question
> **How do first-time users experience building three-dimensional architectural structures (rooms) in VR using bare-hand gestures alone, in terms of perceived usability, workload and interaction breakdowns?**

*The earlier colour-marker / fiducial title was dropped after the course faculty approved a change of topic.*

---

## Problem Statement
Constructing and editing three-dimensional structures in immersive VR conventionally depends on handheld controllers, which give the user a physical trigger for selecting and confirming points in space. Removing the controller removes that discrete confirmation signal. A bare-hand system must instead infer intent from continuous, noisy hand-pose estimates: a pinch has no tactile click, depth has to be recovered from a two-dimensional camera image or inferred from headset tracking, and the filtering that suppresses jitter introduces perceptible lag.

Prior work has shown hand-based sketching in VR and hand tracking as a controller alternative. Evidence is still limited on whether first-time users can complete an open-ended architectural construction workflow (defining a floor footprint, raising walls, editing and colouring the result) using only a small set of hand gestures, and on where such interaction breaks down in practice. This project addresses that gap by building a controller-free room-authoring system for VR and evaluating it with first-time users.

### Sub-questions
- **RQ1 (Usability):** What System Usability Scale (SUS) score do novice users give the system?
- **RQ2 (Workload):** What perceived workload (NASA-TLX) does bare-hand construction impose?
- **RQ3 (Breakdowns):** Which interaction failures (missed or double-fired pinches, tracking loss, lighting and frame-rate effects) occur most often, and how do users describe them?
- **RQ4 (Feasibility):** What pipeline latency and frame rate does the webcam hand-tracking route sustain, and do the main outcomes hold on both tracking sources (webcam and Meta Quest 2 built-in hand tracking)?

### Hypotheses (exploratory)
- **H1:** Novice users rate usability above the commonly cited SUS benchmark of 68.
- **H2:** Perceived workload (raw NASA-TLX) is at or below the scale midpoint.
- **H3:** Users complete a structure of their own design without assistance; the completion rate is reported as [n]/[N].

> **Status after the user sessions:** SUS and NASA-TLX were not collected, so **RQ1, RQ2, H1 and H2 were not tested**. The 10 recorded sessions support **RQ3 and H3** (completion and breakdowns, counted from the videos). RQ4 rests on development-time HUD observations only (see Results).

### Why it is harder than it looks
- **2D to 3D.** A webcam gives a flat image, but placing a wall corner needs a position in 3D room space, and the camera frame has to be aligned with the headset's frame.
- **A pinch has no click.** Thumb-index distance is noisy, so one pinch can fire twice or not at all. We normalise by palm size, use hysteresis (enter 0.35, exit 0.50) and a 3-frame debounce.
- **Smoothing versus lag.** Raw landmarks jitter; a One Euro filter removes it, but with wrong parameters the hand felt about half a second late. Our first settings did exactly that and were retuned.
- **Latency and frame rate.** Early builds showed 60-100 ms on the HUD, and dim yellow room light dropped the camera to about 15 fps.
- **Differences between people.** Pinch detection that works for one user can fail for another.

### What is and is not claimed
Hand tracking, controller-free VR and MediaPipe are established, and hand-based sketching in VR has been studied before. We do **not** claim to be first. The contribution is a complete bare-hand footprint-to-walls-to-paint workflow built from open components, evaluated with first-time users, with an honest account of where it breaks down.

### Contributions
1. A controller-free architectural room-authoring workflow for VR that runs on a standard webcam as well as on Meta Quest 2 hand tracking.
2. A user-centred account of its usability, workload and failure modes with first-time users.
3. Design lessons on thresholds, filtering and latency for controller-free selection.

---

## What Was Built
```
Webcam -> Python MediaPipe Hand Landmarker -> JSON over UDP (127.0.0.1:5005) -> Unity WebcamHandProvider
                                                                                      |
Meta Quest 2 hand tracking (Meta XR SDK) -> MetaXRHandProvider ------> HandProviderSelector (F1 switches source)
                                                                                      |
                                       One Euro HandFilter -> GestureRecognizer -> RoomBuilderController -> TrialLogger (CSV)
```
| Module | Location | Role |
|---|---|---|
| MediaPipe sender | `ARVR_Project/Group10Gestures~/tools/mediapipe_sender.py` | Webcam capture, 21 landmarks per hand, timestamped UDP packets |
| Hand providers | `Assets/Group10Gestures/Providers/` | `WebcamHandProvider`, `MetaXRHandProvider`, `HandProviderSelector` |
| Filtering | `Assets/Group10Gestures/Filtering/` | One Euro filter on every joint |
| Gesture recognition | `Assets/Group10Gestures/Recognition/` | Geometric classifier: Pinch, Point, Fist, Open palm, Peace, Thumbs-up; two-hand open-palm mode switch |
| Room builder | `Assets/Group10Gestures/Interaction/` | Pinch to place corners on a floor grid, close the room, raise walls, paint; Edit / Color / View modes; undo and delete |
| XR guide panel | `Interaction/XRGuidePanel.cs` | In-headset instruction panel |
| Trial logger | `Interaction/TrialLogger.cs` | One CSV row per trial |

**Controls, left hand:** fist = undo, peace = delete room, thumbs-up = close room, point (hold 1 s) = next mode.  
**Controls, right hand:** pinch thumb + index = place / confirm.

**Stack:** Unity 6000.5.8f1, URP 17.5.0, Meta XR SDK 207.0.1, OpenXR 1.17.1, XR Management 4.7.0, Input System 1.20.0, Python with OpenCV and MediaPipe. Targets: Meta Quest 2 (standalone APK, and PC-VR via Quest Link).

---

## Build and Release
| Item | Value |
|---|---|
| Release | [`group-10-v1.0`](https://github.com/Aaryupat10/NMIMS-MPSTME-IVRAR-2026/releases/tag/group-10-v1.0) |
| Quest 2 build | `Group10_HandRoomBuilder_v1.0.apk` (standalone, Meta XR hand tracking; install with Meta Quest Developer Hub) |
| Final code commit | `03c7d14b492c8c971ec65694eda705cf333b0931` |
| Source | `ARVR_Project/` (Unity 6000.5.8f1). No build is committed to Git history; builds ship only through the Release. |

The webcam route needs the PC, the Python sender and Unity or a PC build; it is not part of the Quest APK.

---

## Evaluation Protocol
### As carried out (27 Oct 2026, AR/VR Lab)
- **Participants:** 10 first-time users.
- **Build:** the standalone Quest 2 build (`Group10_HandRoomBuilder_v1.0.apk`, Meta XR hand tracking).
- **Task:** open-ended. Each participant built a structure of their own design in VR with their hands; there was no fixed target size.
- **Evidence kept:** video recordings of the 10 sessions.
- **Not collected:** the trial logger (`TrialData/g10_trials.csv`) starts only from the F2 key, and there is no keyboard in the headset build, so no per-trial CSV exists. SUS and NASA-TLX were not collected during the sessions.
- **What the videos can support:** how many participants completed a structure unaided (H3) and which interaction breakdowns occurred (RQ3), reviewed and counted by the group.

### Planned protocol (not fully executed)
- SUS and NASA-TLX after each session; `g10_trials.csv` with time, completion, corners placed, undo count, rooms deleted, tracking-lost time, fps and webcam latency; short comments; consent forms (confirm they are held).
- Analysis: descriptive statistics (mean, SD, median, IQR); SUS and TLX against the H1 and H2 benchmarks (one-sample Wilcoxon signed-rank); completion counts; themes from comments. With N of about 10 all inference would be exploratory.
- `area_error_pct` and `height_error_pct` columns do not apply to the open-ended task.
- Latency (Python timestamp to Unity receive, same PC clock) is webcam pipeline latency only, not motion-to-photon.

## Results
> **To be filled from the real data only.** The earlier figures (12.1 ms latency, 94.6 percent accuracy, 265.4 s vs 218.6 s, N = 50) belonged to the dropped colour-marker topic and came from generated sample data. They are not results of this project and must not be quoted.

| Measure | Value |
|---|---|
| Participants (N) | 10 first-time users (27 Oct 2026, AR/VR Lab) |
| Completed a structure unaided | [n] / 10, to be counted from the session videos |
| SUS (mean, SD, median) | Not collected |
| NASA-TLX raw (mean, SD) | Not collected |
| Webcam pipeline latency and fps (logged) | Not logged. Development observations only (HUD, not a CSV): 60-100 ms and about 15 fps in dim light before fixes, about 30 fps after the lighting fix |
| Most common breakdowns | [fill from the video review] |

## Known Limitations
- Webcam tracking depends on lighting and camera frame rate.
- Pinch reliability differed between users during early testing; the cause is still under investigation.
- Latency reported is pipeline latency only.
- Small N and student participants limit generalisation.
- The 10 user sessions were video-recorded only. No SUS, NASA-TLX or logged trial data exist, so usability and workload claims cannot be made.
- The trial logger needs a keyboard (F2) and did not run in the Quest standalone build.
- Left/right hand labels from the sender need re-checking after the final script edits.

---

## Literature
See `docs/LITERATURE_REVIEW_AND_FOUNDATIONAL_PAPERS.md`. **Each DOI and author list must be re-checked on the publisher page before submission.** Closest prior work to cite and distinguish: hand-tracking sketching in VR for architectural design (TUM master's thesis) and Sketching with Hands (KAIST, UIST 2016).

## Team
| Roll No | Name | Role | GitHub | Agreed effort | Branch |
|---|---|---|---|---|---|
| R014 | Devraj Ghumare | Computer Vision Pipeline Lead | `devraj2712` | 33 % | `feat/r014-computer-vision-pipe` |
| R045 | Aaryesh Pathare | XR Systems Architect | `Aaryupat10` | 34 % | `feat/r045-xr-systems-architect` (final work merged to `main` via `feat/r045-final-submission`) |
| R054 | Jiya Saxena | Gesture Recognition Specialist | `JiyaSaxena` | 33 % | `feat/r054-gesture-recognition-` |

Devraj's webcam pipeline was developed and tested on his own laptop; the files reached Git through R045's commits.

## Project Demonstration (LinkedIn)
Video: <https://lnkd.in/p/dtxyJCs7> (3 min 26 s, all three members appear). The URL is also in `docs/TEAM_ROSTER.json` (`linkedin_url`) and dossier Section 5.
