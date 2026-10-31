# telemetry/: Group 14 Phase 1 evidence and original faculty tools

## Provenance categories — never mix

1. **REAL PYTHON RUN OUTPUTS**: `Phase1/Python/checkpoints/sac_v15f/seed_71/{eval_history.csv,stage_history.csv}` from the user's actual training session. Copy these into `telemetry/phase1_evidence/` with `export_phase1_evidence.ps1`; retain original headers, provenance and file bytes.
2. **REAL BENCHMARK SUMMARY**: `Phase1/Python/results/sac_v15f_seed_71/benchmark_summary.csv` (only if a genuine `scripts/37_benchmark_sac_v15f.py` run generated it). Do not create a blank, sample or invented substitute.
3. **FACULTY EXAMPLES (SIMULATED)**: `SIMULATED_rover_teleoperation_benchmark.csv` and `SIMULATED_sample_experiment_data.csv`. **Not actual rover experiments and not actual participant tests.** These were supplied with the original scaffold.
4. **FACULTY UTILITIES (retained unchanged)**: `generate_paper_figures.py`, `rover_teleoperation_eval.py`, `nasa_tlx_calculator.py`, `sus_calculator.py`, `kennedy_ssq_calculator.py`, `test_evaluation_tools.py`. These are coursework examples based on teleoperation/usability, not evidence that the relevant tests were conducted.

## How to export actual data
From repo root in Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Group_14_Ananya_Anvay_Mahit_Aryan\telemetry\export_phase1_evidence.ps1
```

The exporter checks for sources, prints which real files it copied, and never creates fake numbers. Any missing benchmark is marked **NOT AVAILABLE** in output—not counted as completed.

## How to make a REAL Figure 3

```powershell
python .\Group_14_Ananya_Anvay_Mahit_Aryan\telemetry\plot_real_phase1_benchmark.py --csv .\Group_14_Ananya_Anvay_Mahit_Aryan\telemetry\phase1_evidence\benchmark_summary.csv
```

The script checks required columns, excludes synthetic filenames and writes `docs/figures/figure3_measured_sac_comparison.png`. The `success` and `collision` rates are read from the genuine CSV. Labels note when only one checkpoint exists. **Interpret metrics as sampled episodes, not universal reliability.**

## Human research protocol (faculty dossier Section 4)
Real human participants, consent forms, SUS/NASA-TLX and 1.5-5 s ghost avatar teleoperation conditions were **Not Implemented**. Report `N=0` (not tested), leave participant fields/peer scores for human entry and do not substitute algorithm training episodes for people.
