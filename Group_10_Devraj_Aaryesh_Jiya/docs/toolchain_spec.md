# Toolchain & Hardware Specification
**Group ID:** IVRAR_GROUP_10  
**Project:** Controller-Free Architectural Room Authoring in VR Using Markerless MediaPipe Hand Tracking and Gesture-Driven Interaction.  

## Software Stack (as installed in `ARVR_Project`)
* **Game Engine:** Unity 6000.5.8f1
* **Render Pipeline:** Universal Render Pipeline 17.5.0
* **XR:** Meta XR SDK (`com.meta.xr.sdk.all` 207.0.1), OpenXR Plugin 1.17.1, XR Plug-in Management 4.7.0
* **Input:** Input System 1.20.0
* **Vision side:** Python 3 with OpenCV and MediaPipe (Hand Landmarker task model `hand_landmarker.task`); packages listed in `ARVR_Project/Group10Gestures~/tools/requirements.txt`
* **Transport:** JSON over UDP to `127.0.0.1:5005`, one packet per frame with timestamp
* **Filtering:** One Euro filter per joint (minCutoff 1.5 Hz, beta 25 in `HandFilter` defaults)
* **Code editor:** Visual Studio / VS Code

## Hardware
* Meta Quest 2 headset
* Windows laptop with built-in or USB webcam (webcam condition and Quest Link)
* Participants' view cast to the laptop for recording (Meta Quest Developer Hub)

## Study Task
* Open-ended: participants build a structure of their own design with their hands (no fixed target size).
* Run on 27 Oct 2026 at the AR/VR Lab with 10 first-time users on the Quest 2 standalone build; sessions were video-recorded. SUS and NASA-TLX were not collected.

## Builds
* **Quest 2 standalone:** `Group10_HandRoomBuilder_v1.0.apk` (Meta XR hand tracking only; the webcam route needs the PC and Python sender)
* **PC-VR / Editor:** scene `Group10_Main` (desktop) and `Group10_XR` (headset); source switch with F1
* **Release:** GitHub Release tag `group-10-v1.0` (https://github.com/Aaryupat10/NMIMS-MPSTME-IVRAR-2026/releases/tag/group-10-v1.0) carrying the Quest 2 APK; final code commit `03c7d14b492c8c971ec65694eda705cf333b0931`

## Telemetry & Data Collection Protocol
* Output: `TrialData/g10_trials.csv`, one row per trial (Editor / PC) or the Unity persistent data path (build)
* Status: no trial CSV was recorded in the user sessions, because recording starts with the F2 keyboard key and the Quest standalone build has no keyboard.
* Columns: `participant_id, trial, condition, start_utc, duration_s, completed, corners_placed, undo_count, rooms_deleted, room_area_m2, area_error_pct, room_height_m, height_error_pct, tracking_lost_s, mean_latency_ms, p95_latency_ms, mean_fps, min_fps`
* Columns `area_error_pct` and `height_error_pct` are not used for the open-ended task.
* Keys: F2 start recorded trial, F3 end and save as incomplete, F4 next participant, F1 switch tracking source (not during a trial)
* Latency: webcam route only, Python timestamp to Unity receive on one PC clock
