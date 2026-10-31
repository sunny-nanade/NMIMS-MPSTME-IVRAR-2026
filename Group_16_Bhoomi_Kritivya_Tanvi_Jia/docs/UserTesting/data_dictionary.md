# Data dictionary: `vr_disaster_simulator_user_testing.csv`

One row per anonymous participant. **Empty = not recorded / unknown** (never a failure, never a success, never zero). Where known use `Not tested` for a task the person did not do. Yes/No columns: `Yes`, `No` (observed failure), `Not tested`, or empty. Observed, self-report and evaluator-note columns are kept separate.

| Column | Meaning / expected values |
|---|---|
| `participant_id` | Anonymous key P01-P10, unique |
| `participant_name` | Left blank: no tester name is verifiable. Only add with a source and the person's consent |
| `test_date` | YYYY-MM-DD, observed |
| `device_used` | e.g. Meta Quest 3, Meta XR Simulator |
| `interaction_mode` | Controllers / Hand Tracking / Both / Other |
| `scenes_tested` | Factory, Earthquake, Flood, Triage; ';'-separated |
| `factory_task_completed` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `earthquake_task_completed` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `flood_task_completed` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `triage_task_completed` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `victim_selection_success` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `victim_grab_success` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `victim_carry_success` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `safe_zone_delivery_success` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `triage_tagging_success` | `Yes` / `No` / `Not tested` / empty (observed outcome) |
| `task_completion_time_seconds` | Numeric seconds, only when measured; 0 is a real zero, blank is unknown |
| `interaction_errors` | Count of observed grab/select/carry/UI errors (integer >= 0) |
| `navigation_errors` | Count of observed locomotion/orientation errors (integer >= 0) |
| `ease_of_use_rating` | Self-report on the scale named in rating_scale |
| `interaction_satisfaction_rating` | Self-report, same scale |
| `overall_experience_rating` | Self-report, same scale |
| `rating_scale` | Original questionnaire scale, e.g. '1-5 Likert'. Never assign retrospectively |
| `participant_feedback` | Self-report: real comments only, accurately paraphrased |
| `tester_notes` | Evaluator interpretation, kept apart from observation and self-report |
| `data_source` | Where the row's information came from |
| `data_confidence` | How strong the evidence is (e.g. 'Participant count only', 'Observed and logged', 'Recalled later') |

Sibling files: `..._template.csv` (blank), `vr_disaster_simulator_synthetic_example.csv` (adds `record_type`; every row `SYNTHETIC_NOT_OBSERVED`, IDs `SX01`-`SX10`, never real), `validate_user_testing.py`, `build_user_testing_files.py`.
