"""Writes the user-testing CSVs. The REAL file contains only what is evidenced: 10 anonymous participants, no measurements.
The synthetic example is generated separately, is labelled SYNTHETIC_NOT_OBSERVED on every row, and uses IDs SX01-SX10."""
import csv, random

COLS = ["participant_id","participant_name","test_date","device_used","interaction_mode","scenes_tested",
        "factory_task_completed","earthquake_task_completed","flood_task_completed","triage_task_completed",
        "victim_selection_success","victim_grab_success","victim_carry_success","safe_zone_delivery_success","triage_tagging_success",
        "task_completion_time_seconds","interaction_errors","navigation_errors",
        "ease_of_use_rating","interaction_satisfaction_rating","overall_experience_rating","rating_scale",
        "participant_feedback","tester_notes","data_source","data_confidence"]

SRC = "Project owner statement that 10 people tested the game; no per-participant records exist in the repository"
CONF = "Participant count only (no measurements)"

def real(path, with_status):
    with open(path, "w", newline="") as f:
        w = csv.DictWriter(f, COLS); w.writeheader()
        for i in range(1, 11):
            row = {c: "" for c in COLS}
            row["participant_id"] = f"P{i:02d}"
            if with_status:
                row["data_source"] = SRC; row["data_confidence"] = CONF
            w.writerow(row)

real("vr_disaster_simulator_user_testing.csv", True)
real("vr_disaster_simulator_user_testing_template.csv", False)

# ---- synthetic illustration (NOT observed) ----
random.seed(7)
scols = ["record_type"] + COLS
with open("vr_disaster_simulator_synthetic_example.csv", "w", newline="") as f:
    w = csv.DictWriter(f, scols); w.writeheader()
    for i in range(1, 11):
        yn = lambda p: "Yes" if random.random() < p else "No"
        row = {c: "" for c in scols}
        row.update(record_type="SYNTHETIC_NOT_OBSERVED", participant_id=f"SX{i:02d}",
                   interaction_mode=random.choice(["Controllers","Hand Tracking"]), scenes_tested="Factory",
                   factory_task_completed=yn(0.8), victim_selection_success=yn(0.9), victim_grab_success=yn(0.8),
                   victim_carry_success=yn(0.8), safe_zone_delivery_success=yn(0.7),
                   task_completion_time_seconds=round(random.uniform(150, 420), 1),
                   interaction_errors=random.randint(0, 5), navigation_errors=random.randint(0, 3),
                   ease_of_use_rating=random.randint(3, 5), interaction_satisfaction_rating=random.randint(3, 5),
                   overall_experience_rating=random.randint(3, 5), rating_scale="ILLUSTRATIVE 1-5 (not a real questionnaire)",
                   participant_feedback="", tester_notes="Invented numbers to show how the summary would look. Do not cite.",
                   data_source="Random generator seed 7 (build_user_testing_files.py)", data_confidence="NONE - synthetic")
        w.writerow(row)
