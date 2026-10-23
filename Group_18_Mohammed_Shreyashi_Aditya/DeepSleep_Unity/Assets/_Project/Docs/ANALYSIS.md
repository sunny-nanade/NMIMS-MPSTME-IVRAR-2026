# ANALYSIS

Question: can spatial (HRTF) voice guidance cut evacuation time and wrong turns in smoke-filled, low-visibility egress? (Stakeholder: Mumbai Fire Brigade.)

Design: within-subject, one smoke-room block per condition (S = spatial HRTF from the exit door, M = stereo identical in both ears). Layouts A and B are counterbalanced with the condition (four orders, `participant mod 4`). Only the spatialization differs: same mono sample data, same distance gain, level-matched (see TEST_PROTOCOL).

## Data

`deepsleep_telemetry.csv` has one row per block with: evacuation time, wrong turns, dead-end entries, backtracks, path length and efficiency (36 m shortest / travelled), wall contacts, idle time, timeout flag, head-turn latency, the six raw NASA-TLX scales and their mean, SUS (one per participant, written to both rows), Ear Check mean error and reaction time, frame rate (mean, min, 1 % low), plus keypad, trail and coolant stage times and errors.

Definitions (also in `DECISIONS_APPLIED.md`): a wrong turn is stepping from a cell on the shortest path onto one that is not; a backtrack is stepping back into the cell just left; head-turn latency runs from a beacon call that starts with the head more than 45 degrees off the door until the head is within 30 degrees of it (capped at 4 s).

## Pipeline

```
python Tools/analysis.py --csv deepsleep_telemetry.csv --out Analysis/results --label "text"
python Tools/generate_figures.py --csv deepsleep_telemetry.csv --out Analysis/figures
python Tools/analysis.py --selftest        # planted effect must be found
```

`analysis_report.md` and `analysis_summary.csv` contain, per outcome: paired difference S - M with 95 % CI, paired t, Wilcoxon, Cohen's dz, Holm correction over the six primary outcomes (time, wrong turns, dead-end entries, backtracks, efficiency, head-turn latency), a mixed model (condition + layout + block order, random intercept per participant), the same test excluding participants with a timed-out block, a carry-over check and a counterbalance check. Secondary: TLX scales, wall contacts, idle time, path length, frame rate. SUS is descriptive only.

## Caveats

- The bot data in `Analysis/data/bot_telemetry.csv` come from scripted participants that ignore the guidance condition. They test the pipeline (all fields present, counterbalance correct, analysis runs); they say nothing about the hypothesis.
- Eight human participants give low power for between-block differences; report effect sizes and intervals, not only p values.
- The RMS level match is a first-order match; confirm by ear and re-run after audio changes.
- Frame rate from Editor runs is not a Quest measurement.
