# Group 14 figures: actual Phase 1, source-derived

All original faculty **synthetic** results were replaced in the revised submission figures to prevent accidental reporting of invented human-trial numbers. The provided figures are illustrations/evidence-status, **not experimental measurements**.

| Filename (faculty filename retained) | Content | Type |
|---|---|---|
| `figure1_system_architecture.png` | Actual Phase 1 Python/Unity component relationships | Architecture schematic |
| `figure2_kinematic_telemetry.png` | Illustrative A* path/lookahead, rocks and six-wheel rover control geometry | Explanatory schematic, NOT measured trajectory |
| `figure3_comparative_performance.png` | Explicit marker that numerical comparisons require genuine benchmark CSV | Evidence status, NOT a performance chart |
| `video_poster.png` | Neutral proposed showcase title card; does NOT assert a video exists | Poster draft |

For a real comparison figure, run `telemetry/plot_real_phase1_benchmark.py --csv telemetry/phase1_evidence/benchmark_summary.csv`. It will refuse to run if a CSV is absent/has incompatible columns; the output is written to `figure3_measured_sac_comparison.png` without silently overwriting the faculty-slot placeholder.

A valid results figure must cite checkpoint, terrain split, number of episodes, random seed and regime. Do not use `SIMULATED_*` CSVs as experiment results.
