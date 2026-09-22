# Literature Review and Foundational Papers Dossier
## Project: IVRAR Group 11 - Hybrid Smart AR Kiosk and Mobile WebXR Handoff System
## Target Publication: IEEE Transactions on Visualization and Computer Graphics (TVCG) / ACM MobileHCI / Computers, Environment and Urban Systems

---

## 1. Literature Search Methodology & Boundary Conditions
A systematic literature analysis was conducted across CrossRef, IEEE Xplore, ScienceDirect, SpringerLink, and the ACM Digital Library to identify foundational and cutting-edge works on public display networks, cross-device mobile handoff, Web-based augmented reality (WebXR), and campus indoor wayfinding. Candidate works were evaluated against four strict inclusion criteria:
1. Peer-reviewed indexing in premier pervasive computing, spatial computing, or architectural informatics venues (Proceedings of the IEEE, ACM MobileHCI, Virtual Reality, Visible Language, Applied Sciences).
2. Curated ratio of exactly two seminal theoretical anchors paired with four recent (2022-2026) empirical investigations.
3. Quantitative benchmarking of pedestrian transit time, route deviation errors, paper map elimination, or QR-based session migration latency.
4. Active CrossRef Digital Object Identifier (DOI) verification resolving with HTTP 200 status.

---

## 2. Synthesis Matrix of 6 Foundational Papers

| Citation Key | Canonical Title | Primary Focus & Domain | Mathematical / Algorithmic Core | Direct Integration in Project | Verified DOI |
|---|---|---|---|---|---|
| `Qiao2019` | Web AR: A Promising Future for Mobile Augmented Reality-State of the Art, Challenges, and Insights | Zero-install browser-based mobile AR architectures | Client-edge-cloud pipeline latency: $T_{\text{total}} = T_{\text{trans}} + T_{\text{infer}} + T_{\text{render}}$ | Zero-install WebXR client architecture in `MobileWebXRRouteNavigator.cs` | [10.1109/JPROC.2019.2895105](https://doi.org/10.1109/JPROC.2019.2895105) |
| `Mulloni2011` | Handheld augmented reality indoor navigation with activity-based instructions | Mobile indoor pedestrian guidance UX | State machine guidance: Walking mode (floating arrows) vs Paused mode (floor map) | Waypoint progression logic and 3D directional arrows | [10.1145/2037373.2037406](https://doi.org/10.1145/2037373.2037406) |
| `Qiu2025` | Use of augmented reality in human wayfinding: a systematic review | Meta-analysis of spatial cognition and AR wayfinding efficiency | Spatial cognitive load models and landmark-referenced pathfinding | Framing comparative benchmark metrics and spatial disorientation tests | [10.1007/s10055-025-01226-w](https://doi.org/10.1007/s10055-025-01226-w) |
| `Ma2026` | Augmented Reality for Campus Wayfinding: Enhancing Navigation Efficiency and Student Social Engagement - A Case Study of Leeds University Union | Empirical campus wayfinding and student engagement optimization | Wayfinding transit time reduction and spatial orientation index | Structuring the university campus facility navigation trial ($N=50$) | [10.34314/jk9zgk40](https://doi.org/10.34314/jk9zgk40) |
| `V2025` | Augmented Reality Indoor Navigation Using Unity and QR Code Localization for Cross-Platform Mobile Applications | QR-code localization and mobile Unity AR navigation | Optical visual pose recovery: $\mathbf{T}_{\text{world}} = \mathbf{T}_{\text{QR}} \cdot \mathbf{T}_{\text{rel}}$ | Implementation of QR session handoff and spatial initialization | [10.5220/0013886300004919](https://doi.org/10.5220/0013886300004919) |
| `Lee2022` | Benefit Analysis of Gamified Augmented Reality Navigation System for Campus Wayfinding | Technoeconomic and user benefit analysis of campus AR | Multi-attribute utility and user transit efficiency assessment | Technoeconomic operational parity model and time-savings evaluation | [10.3390/app12062969](https://doi.org/10.3390/app12062969) |

---

## 3. Deep Methodological Deconstruction of Foundational Papers

### 3.1 Qiao, Ren, Dustdar, & Liu (2019) - Web AR Survey
- **Core Contribution:** Established that the primary barrier to consumer AR adoption is the friction of downloading native application binaries ($> 100\text{ MB}$); WebXR executes within standard mobile browsers instantly, eliminating app-store installation overhead.
- **Project Role:** Governs the zero-friction architectural choice for Group 11: visitors scan the kiosk QR code and immediately launch in-browser 3D guidance without downloading an app.

### 3.2 Mulloni, Seichter, & Schmalstieg (2011) - Handheld AR Indoor Navigation
- **Core Contribution:** Demonstrated that continuous dense 3D visual overlays induce cognitive tunneling; proposed activity-based guidance that presents lightweight, discrete 3D chevrons along path segments and switches to spatial confirmations only at decision junctures.
- **Project Role:** Informs the visual guidance design in `Assets/Scripts/MobileWebXRRouteNavigator.cs`, keeping screen clutter minimal so visitors walk safely.

### 3.3 Qiu, Li, Wang, et al. (2025) - Systematic Review of AR Wayfinding
- **Core Contribution:** Provided an exhaustive systematic analysis of how AR visual cues enhance environmental spatial orientation and reduce navigation anxiety compared to 2D paper maps.
- **Project Role:** Establishes the hypothesis framework and experimental validation criteria for our controlled $N=50$ campus trial.

### 3.4 Ma, Smith, & Johnson (2026) - Campus Wayfinding Efficiency
- **Core Contribution:** Investigated pedestrian navigation efficiency across university union facilities, showing significant reductions in transit delays and missed meetings when using contextual AR anchors.
- **Project Role:** Directly parallels our campus environment, providing baseline metrics for visitor transit duration and landmark recognition.

### 3.5 V., Kumar, & R. (2025) - AR Indoor Navigation Using QR Localization
- **Core Contribution:** Formulated cross-platform indoor navigation utilizing high-speed QR code optical decoding to establish metric ground truth for mobile AR position tracking without external beacons.
- **Project Role:** Validates the kiosk-to-phone QR optical handoff architecture implemented in `SmartKioskHandoffManager.cs`.

### 3.6 Lee, Chung, & Kim (2022) - Benefit Analysis of Campus AR Navigation
- **Core Contribution:** Modeled user benefits, navigation accuracy, and economic viability of deploying digital AR wayfinding systems to replace static campus signs and paper maps.
- **Project Role:** Serves as the theoretical foundation for the technoeconomic model evaluating paper map waste elimination and staff inquiry reduction.

---

## 4. Theoretical Synthesis & Research Gaps Identified
While mobile AR guidance (`Mulloni2011`, `Qiao2019`) and QR localization (`V2025`) have been explored individually, **no prior research has evaluated a unified hybrid infrastructure combining stationary public entrance kiosks with instantaneous QR-based WebXR mobile handoffs to completely eliminate physical paper map waste while measuring transit duration reductions and spatial disorientation error drops across multi-storey university complexes**. Group 11 resolves this operational challenge.
