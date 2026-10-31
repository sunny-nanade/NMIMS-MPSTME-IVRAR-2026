"""Validates the SYNTHETIC dataset, checks the summary matches the CSV, and checks the real dataset is untouched by synthetic rows."""
import csv, os, re, statistics, sys
import generate_synthetic_data as g

fail = []
rows = list(csv.DictReader(open(g.CSV_PATH, newline="")))
def need(c, m):
    if not c: fail.append(m)
need(len(rows) == 10, "expected 10 rows")
need(len({r["participant_id"] for r in rows}) == 10, "participant IDs not unique")
need(all(r["data_type"] == "SYNTHETIC_AI_GENERATED" for r in rows), "data_type must be SYNTHETIC_AI_GENERATED on every row")
need(all(g.LABEL in r["generation_notes"] for r in rows), "generation_notes must carry the synthetic label")
need(list(rows[0].keys()) == g.COLS, "columns differ from the specification")
RANGES = {"task_completion_rate_pct":(60,100),"victim_selection_success_pct":(70,100),"victim_grab_success_pct":(60,100),"victim_carry_success_pct":(55,100),
          "safe_zone_delivery_success_pct":(60,100),"triage_selection_success_pct":(70,100),"triage_tagging_success_pct":(70,100),
          "mean_task_completion_time_sec":(30,180),"mean_victim_rescue_time_sec":(20,150),"interaction_errors_count":(0,8),"accidental_grab_count":(0,5),
          "tracking_or_controller_issues_count":(0,5),"ease_of_use_rating":(1,5),"interaction_satisfaction_rating":(1,5),"overall_experience_rating":(1,5)}
RESCUE = ["victim_selection_success_pct","victim_grab_success_pct","victim_carry_success_pct","safe_zone_delivery_success_pct","mean_victim_rescue_time_sec"]
TRIAGE = ["triage_selection_success_pct","triage_tagging_success_pct"]
for r in rows:
    pid = r["participant_id"]; triage = r["scene_tested"] == "Triage Study"
    need(r["scene_tested"] in ("Factory","Earthquake","Flood","Triage Study"), f"{pid}: bad scene")
    for c, (lo, hi) in RANGES.items():
        if r[c] == "": continue
        v = float(r[c]); need(lo <= v <= hi, f"{pid}.{c}={v} outside {lo}-{hi}")
        if c.endswith("_rating") or c.endswith("_count"): need(v == int(v), f"{pid}.{c} must be an integer")
    for c in RESCUE: need((r[c] == "") == triage, f"{pid}.{c}: applicability wrong for scene {r['scene_tested']}")
    for c in TRIAGE: need((r[c] != "") == triage, f"{pid}.{c}: applicability wrong for scene {r['scene_tested']}")
    for c in ["task_completion_rate_pct","mean_task_completion_time_sec","interaction_errors_count","ease_of_use_rating","feedback_category"]:
        need(r[c] != "", f"{pid}.{c} missing")
    if triage: need(float(r["triage_tagging_success_pct"]) <= float(r["triage_selection_success_pct"]), f"{pid}: tagged > selected")
    else: need(float(r["task_completion_rate_pct"]) <= float(r["victim_selection_success_pct"]) + 1e-9, f"{pid}: completion > selection")
# errors vs time: more errors should not go with a shorter time overall (rank correlation sanity)
e = [float(r["interaction_errors_count"]) for r in rows]; t = [float(r["mean_task_completion_time_sec"]) for r in rows]
need(statistics.correlation(e, t) > 0, "errors and time should be positively related")

# summary must equal a fresh computation from the CSV
before = open(g.SUMMARY).read()
g.write_summary(rows)
need(open(g.SUMMARY).read() == before, "summary file does not match statistics computed from the CSV")
for fn in ["synthetic_task_success_rates.png","synthetic_average_ratings.png","synthetic_completion_times_per_participant.png","synthetic_scene_comparison.png"]:
    need(os.path.getsize(os.path.join(g.FIG, fn)) > 5000, f"missing/empty figure {fn}")
# real dataset must contain no synthetic rows
real = open("vr_disaster_simulator_user_testing.csv").read()
need("SYNTHETIC" not in real and "SP0" not in real and "SP10" not in real, "synthetic content found in the real dataset")
print("RESULT:", "FAIL" if fail else "PASS", f"({len(rows)} rows)")
for m in fail: print(" -", m)
sys.exit(1 if fail else 0)
