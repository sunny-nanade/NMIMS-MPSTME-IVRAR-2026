"""Validates the REAL dataset and prints statistics computed only from known values. Run: python3 validate_user_testing.py"""
import csv, statistics, sys

REAL = "vr_disaster_simulator_user_testing.csv"
TEMPLATE = "vr_disaster_simulator_user_testing_template.csv"
YN_COLS = ["factory_task_completed","earthquake_task_completed","flood_task_completed","triage_task_completed",
           "victim_selection_success","victim_grab_success","victim_carry_success","safe_zone_delivery_success","triage_tagging_success"]
NUM_COLS = ["task_completion_time_seconds","interaction_errors","navigation_errors",
            "ease_of_use_rating","interaction_satisfaction_rating","overall_experience_rating"]
OK_YN = {"Yes", "No", "Not tested", ""}
fail = []

def load(p):
    with open(p, newline="") as f:
        return list(csv.DictReader(f))

rows = load(REAL)
if len(rows) != 10: fail.append(f"{REAL}: expected 10 rows, found {len(rows)}")
ids = [r["participant_id"] for r in rows]
if len(set(ids)) != len(ids) or ids != [f"P{i:02d}" for i in range(1, 11)]: fail.append("participant IDs must be unique P01-P10")
if load(TEMPLATE) and [r["participant_id"] for r in load(TEMPLATE)] != ids: fail.append("template IDs differ")
if any("SYNTHETIC" in "".join(r.values()) for r in rows): fail.append("synthetic marker inside the real dataset")
if any(r["participant_name"] for r in rows) and any(not r["data_source"] for r in rows if r["participant_name"]): fail.append("name without source")
for r in rows:
    for c in YN_COLS:
        if r[c] not in OK_YN: fail.append(f"{r['participant_id']}.{c}: bad value {r[c]!r}")
    for c in NUM_COLS:
        if r[c]:
            try:
                if float(r[c]) < 0: fail.append(f"{r['participant_id']}.{c}: negative")
            except ValueError: fail.append(f"{r['participant_id']}.{c}: not numeric")

print(f"Participants: {len(rows)}")
with_results = [r for r in rows if any(r[c] for c in YN_COLS + NUM_COLS)]
print(f"Records with any recorded measurement: {len(with_results)}   without: {len(rows) - len(with_results)}")
for c in YN_COLS:
    known = [r[c] for r in rows if r[c] in ("Yes", "No")]
    print(f"{c}: {known.count('Yes')}/{len(known)} known outcomes" + (f" = {100*known.count('Yes')/len(known):.1f}%" if known else " (no known outcomes, no rate)"))
for c in NUM_COLS:
    v = [float(r[c]) for r in rows if r[c]]
    print(f"{c}: n={len(v)}" + (f" mean={statistics.mean(v):.2f} median={statistics.median(v):.2f}" if v else " (none recorded)"))
print("RESULT:", "FAIL" if fail else "PASS")
for m in fail: print(" -", m)
sys.exit(1 if fail else 0)
