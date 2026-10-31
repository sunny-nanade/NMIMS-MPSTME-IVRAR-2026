# Literature Review and Foundational Papers
## Project: IVRAR Group 10 - Controller-Free Architectural Room Authoring in VR Using Markerless MediaPipe Hand Tracking and Gesture-Driven Interaction

> **Verification status.** The six papers below were carried over from the earlier title sheet. The dossier requires each source to be checked by the group (author, year, venue, DOI). Until that is done, treat the author lists and article numbers as unverified. The summary column states only what each paper is about; replace it with what you read yourself.

## 1. How to Verify Each Source
1. Open `https://doi.org/<DOI>` and confirm it resolves to the stated title.
2. Compare authors, year and venue with the publisher page.
3. Record the check in dossier Section 1.1 and set `verified_by_group` to `true` in `TEAM_ROSTER.json`.
4. Delete any entry that does not resolve or does not match.

## 2. Sources and Their Role in This Project

| Key | Title | Relevance to this project | DOI / source |
|---|---|---|---|
| `Wang2009` | Real-time hand-tracking with a color glove | Marker/glove-based tracking: the older approach our markerless pipeline moves away from | 10.1145/1531326.1531369 |
| `Rautaray2015` | Vision based hand gesture recognition for human computer interaction: a survey | Background on vision-based gesture recognition; framing for our geometric classifier | 10.1007/s10462-012-9356-9 |
| `Steed2025` | Comparison of hand tracking-based and controller-based interaction in a consumer virtual reality game | Hand tracking as a controller alternative; user-experience framing | 10.1007/s10055-025-01190-5 |
| `Pardo2026` | Analyzing the effectiveness and satisfaction of hand tracking vs. controllers among VR-experienced users | Satisfaction and effectiveness measures; usability framing | 10.1007/s10055-026-01333-2 |
| `Coox2025` | Virtual reality rehabilitation using hand tracking: interaction system design and usability tests | Hand-tracking system design and usability testing in VR | 10.1007/s10055-025-01253-7 |
| `Fidalgo2025` | Exploring AR hand augmentations as error feedback mechanisms for enhancing gesture-based tutorials | Visual feedback for gesture errors; relevant to our in-headset guide panel | 10.3389/frvir.2025.1574965 |

### Closest prior work (find full citations and cite)
| Work | Why it matters |
|---|---|
| TUM master's thesis, "Sketching via hand tracking in virtual reality for architectural design process" (arc.ed.tum.de) | Hand-tracking sketching for architectural design: the nearest prior work; state clearly how this project differs |
| Sketching with Hands, KAIST, UIST 2016 (sketch.kaist.ac.kr) | Bare-hand sketching in 3D; background for gesture-based creation |

### Method references (add to the paper)
| Key | Reference | Used for |
|---|---|---|
| `Zhang2020` | F. Zhang et al., "MediaPipe Hands: On-device Real-time Hand Tracking", arXiv:2006.10214, 2020 | Landmark detection in our webcam route |
| `Casiez2012` | G. Casiez, N. Roussel, D. Vogel, "1 Euro Filter: A Simple Speed-based Low-pass Filter for Noisy Input in Interactive Systems", CHI 2012 | Joint smoothing (`OneEuroFilter.cs`, `HandFilter.cs`) |
| `Brooke1996` | J. Brooke, "SUS: A quick and dirty usability scale", in Usability Evaluation in Industry, 1996 | Usability questionnaire |
| `Hart1988` | S. G. Hart, L. E. Staveland, "Development of NASA-TLX: Results of empirical and theoretical research", Advances in Psychology 52, 1988 | Workload questionnaire |

## 3. Claims in the Old Documents That Were Removed
- "Sub-15 ms end-to-end latency" and "12.1 ms" latency budget: not measured on this system; measured webcam pipeline latency is reported only from `g10_trials.csv`.
- "N = 50", "94.6 percent accuracy", "265.4 s vs 218.6 s", NASA-TLX and technoeconomic figures: generated sample data for the dropped colour-marker topic.
- "None of the papers solved ..." gap statement: it described the old topic and was not backed by a search. Write a new gap statement only after a fresh search.

## 4. Open Items
- Run a fresh search (Google Scholar, ACM DL, IEEE Xplore, Springer) for: controller-free 3D authoring in VR, hand-tracking architectural design tools, MediaPipe in VR, pinch detection and hysteresis, usability evaluation of hand interaction.
- Add at least five verified sources with DOIs to dossier Section 1.1.
