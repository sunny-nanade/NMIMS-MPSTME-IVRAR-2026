# Synthetic user-testing summary

> **SYNTHETIC - AI-GENERATED DATA, NOT ACTUAL MEASUREMENTS.** Generated for an academic exercise at the professor's request. None of these figures are results from the real people who tested the game, and they are not evidence of verified headset performance.

All statistics below are computed from `synthetic_vr_disaster_simulator_data.csv` by `generate_synthetic_data.py`. Each uses only records where the metric is applicable and populated (n shown); blank cells are **not** zeros. No inferential statistics, error bars or significance claims are made.

- Synthetic participant count: **10**
- Records per scene: Factory 3, Earthquake 2, Flood 2, Triage Study 3
- Records per interaction mode (simulated assumptions): Controller-free hand tracking 3, Editor simulation 3, Quest controllers 4

| Metric | n (applicable records) | Mean | Median |
|---|---:|---:|---:|
| Task completion rate (%) | 10 | 79.25 | 70.00 |
| Victim selection success (%) | 7 | 97.96 | 100.00 |
| Victim grab success (%) | 7 | 92.84 | 100.00 |
| Victim carry success (%) | 7 | 89.04 | 83.30 |
| Safe-zone delivery success (%) | 7 | 97.14 | 100.00 |
| Triage selection success (%) | 3 | 80.00 | 70.00 |
| Triage tagging success (%) | 3 | 80.00 | 70.00 |
| Task completion time (s) | 10 | 118.26 | 115.90 |
| Victim rescue time (s), rescue scenes only | 7 | 39.70 | 41.60 |
| Ease-of-use rating (1-5) | 10 | 3.60 | 3.50 |
| Interaction satisfaction (1-5) | 10 | 3.40 | 3.00 |
| Overall experience (1-5) | 10 | 3.60 | 4.00 |
| Interaction errors per participant | 10 | 3.30 | 3.50 |

- Interaction errors: total **33**, average **3.30** per participant (n=10).
- Most common synthetic feedback category: **Grab difficulty, Hand tracking reliability, Smooth interaction, Triage clarity** (2 of 10). Counts: Grab difficulty 2, Hand tracking reliability 2, Navigation 1, Smooth interaction 2, Triage clarity 2, Visual quality 1.

## Assumed testing design (simulated)
Ten synthetic participants were assigned one scene and one interaction condition each (Quest controllers, controller-free hand tracking, or Editor simulation). These conditions are assumptions, not records of what the real testers used. Rescue success percentages are per-stage ratios (successes / the previous stage's successes, so grab is out of selected, carry out of grabbed, delivery out of carried; selection is out of attempts); task completion rate is delivered attempts out of rescue attempts (rescue) or tagged casualties out of 10 (triage). Triage metrics are blank for rescue scenes and rescue metrics are blank for the Triage Study.

## Limits
Ten generated rows, one scene each, so scene-wise comparisons rest on 2-3 records and are illustrative only. Values were drawn from a seeded random model with a shared latent skill factor, then constrained to the ranges the assignment specified. Do not report them as measured outcomes.

Figures: `figures/` (each titled SYNTHETIC).
