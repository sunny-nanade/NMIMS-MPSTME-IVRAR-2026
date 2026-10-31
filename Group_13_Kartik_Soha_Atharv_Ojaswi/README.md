# IVRAR Group 13: BoardroomXR, Continuous Behavioural Biometric Authentication in Collaborative VR

**Course:** Immersive Virtual Reality & Augmented Reality (IVRAR, 702TG0C003), AY 2026–27, Semester V
**Institution:** SVKM's NMIMS MPSTME, Mumbai

## Authorised research title
> "How can continuous behavioral biometric authentication leveraging head and hand kinematic telemetry achieve equal error rates (EER) below 5% against avatar identity-spoofing in collaborative VR enterprise environments?"

## Short answer (from our own data)
Not with free-use head and hand kinematics alone. In 11 headset sessions recorded by the four of us on a Meta Quest 2, the window-level EER between different people was **17.2 %** (interleaved test windows) to **30.3 %** (test windows from the end of the session). Our earlier idealised simulation had predicted 4.7 %; we keep it only as a clearly labelled simulation.

## What we built
A Unity 6 (6000.5.4f1) VR boardroom for Meta Quest (XR Interaction Toolkit, Netcode for GameObjects, the headset runs as host):
1. **Entry gate (server-authoritative MFA):** PIN (salted SHA-256), 3D gesture signature (3-stroke enrolment, threshold 0.65) and a 6-digit TOTP from a virtual token.
2. **Continuous behavioural verification:** head + both controllers sampled at 60 Hz, 3 s windows every 0.5 s, 16 kinematic features, per-user template (mean, SD), score S = exp(-0.5 · mean z²), smoothed, and a lockout after k consecutive low windows. Three presets: Strict (0.25, k=4), Balanced (0.15, k=6, default), Relaxed (0.10, k=8). An adaptive template is updated only while the user is trusted.
3. **Consequence of a lockout:** the host revokes verification, the confidential whiteboard is hidden, security doors close and the MFA restarts.
4. **Explainability and demo tooling:** live trust panel with plain-language reasons, employee badges so several people can enrol on one headset, a laptop dashboard and a one-click expo script with automatic log capture (`EXPO/`).

## Testing on the headset (dossier Section 4)
- **Who:** the four team members (P01–P04), 11 sessions on 27-Oct-2026 (S01–S11), after a 14-session pilot on 26-Oct-2026. In each session the member deliberately moved differently to stand in for a different user, enrolled from scratch (templates cleared), and at the end deliberately changed their movement to test the lockout. This is a team study, not a study of independent users: N = 4. A study with external users is planned at the expo on 31-Oct-2026.
- **Entry gate:** 11/11 owner gesture verifications passed at the first attempt (mean 0.896). All 15 attempts by a member on another member's gesture were rejected (max 0.574). Median login was 31.4 s (15.0–88.9 s).
- **Continuous verifier:** 11/11 enrolments completed. Deliberate behaviour changes were locked out in 9/9 sessions where they were made, 7 of them within 2.5 s of the first warning.
- **Error rates (offline, on-device logs):** own-session vs other members, pooled EER 30.3 % (held-out end of session) and 17.2 % (interleaved). Per-session EER: mean 25.4 %, SD 17.4 %, median 20.0 %. Body geometry alone gives 20.7 %; motion dynamics alone give 48.0 %.
- **Posture matters more than who you are, in our data:** a member's other, deliberately different session scored like a different person (mean 0.168 vs 0.171). Most of this is posture: three of us recorded both seated (eye height 1.06–1.22 m) and standing (1.36–1.56 m) sessions, and standing, three of us were within 1 cm of each other. At matched posture, the same person scored slightly higher (0.36 vs 0.29, only 6 pairs). Identification by body size needs users of clearly different heights, which we will test at the expo.
- **Legacy speed-threshold detector:** it would have revoked 11/11 genuine sessions within about 10 s, so in our build it only logs.
- **Data:** `telemetry/real_participants/` (per-session window CSVs, `sessions_summary.csv`, `make_section4_plots.py`).

## Simulation (clearly labelled, not human data)
- The course starter's simulated files (`SIMULATED_*.csv`, synthetic participants, figure generator) have been removed from this folder.
- `docs/paper/` contains two simulators: an idealised one (40 synthetic users) and one calibrated to our field data. Every simulated number in the paper is labelled as simulated.

## Research paper
`docs/paper/BoardroomXR_paper.pdf` (IEEE two-column, 7 pages) with LaTeX source, figures and analysis scripts. The per-session window CSVs are in `telemetry/real_participants/`.

## Verified literature (DOIs checked at source)
| # | Reference | Venue | DOI |
|---|---|---|---|
| 1 | Pfeuffer et al., "Behavioural Biometrics in VR: Identifying People from Body Motion and Relations in Virtual Reality" | ACM CHI 2019 | 10.1145/3290605.3300340 |
| 2 | Miller et al., "Personal identifiability of user tracking data during observation of 360-degree VR video" | Scientific Reports 2020 | 10.1038/s41598-020-74486-y |
| 3 | Miller, Banerjee, Banerjee, "Temporal Effects in Motion Behavior for VR Biometrics" | IEEE VR 2022 | 10.1109/VR51125.2022.00076 |
| 4 | Chen, Xin, Phoha, "SSPRA: A Robust Approach to Continuous Authentication Amidst Real-World Adversarial Challenges" | IEEE TBIOM 2024 | 10.1109/TBIOM.2024.3369590 |
| 5 | Li, Banerjee, Banerjee, "Real or fake motion: protecting VR behavioral authentication systems against motion forecasting attacks" | Frontiers in VR 2026 | 10.3389/frvir.2026.1766672 |
| 6 | Gyreyiri, Shukla, "Head Movement Biometrics for Continuous Authentication in Virtual Reality" | ACM VRST 2025 | 10.1145/3756884.3768408 |
| Std | ISO/IEC 19795-1:2021, Biometric performance testing and reporting, Part 1: Principles and framework | ISO/IEC | iso.org/standard/73515.html |

## Project Demonstration & Academic Showcase (LinkedIn)
* **LinkedIn Video Demonstration:** [Watch BoardroomXR Demonstration on LinkedIn](https://lnkd.in/p/d_G32T8Q)
* **Student Presenters:** **Kartik Agrawal** (I001), **Soha Chand** (I007), **Atharv Dixit** (I013), **Ojaswi Gondalia** (I019)
* **Academic Institutional Tags:** SVKM's NMIMS MPSTME | Academic Directorate | Immersive VR/AR Technologies

## Team
| Roll No | Name | Role |
|---|---|---|
| I001 | Kartik Agrawal | Biometric Authentication Lead |
| I007 | Soha Chand | XR Systems Architect |
| I013 | Atharv Dixit | Kinematic Telemetry Specialist |
| I019 | Ojaswi Gondalia | Security QA & Threat Analyst |

## Build
The Quest APK is attached to the GitHub Release **group-13-v1.0** (builds are never committed to Git).

## AI use
AI tools (Anthropic Claude) were used for implementation, analysis and drafting. Errors we caught are logged in dossier Section 5.1. Among them: the original README's claims of an "empirical N = 50 study, EER 4.12 %, SUS 88.4" were not real data and have been removed.
