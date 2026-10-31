"""SYNTHETIC - AI-GENERATED DATA, NOT ACTUAL MEASUREMENTS.
Regenerates synthetic_vr_disaster_simulator_data.csv, synthetic_user_testing_summary.md and figures/*.png (seeded, deterministic).
Usage: python3 generate_synthetic_data.py   (then python3 validate_synthetic_data.py)"""
import csv, os, random, statistics
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt

SEED = 2026
CSV_PATH = "synthetic_vr_disaster_simulator_data.csv"
SUMMARY = "synthetic_user_testing_summary.md"
FIG = "figures"
LABEL = "SYNTHETIC - AI-GENERATED DATA, NOT ACTUAL MEASUREMENTS"
COLS = ["participant_id","participant_name","data_type","device_used","interaction_mode","scene_tested",
        "task_completion_rate_pct","victim_selection_success_pct","victim_grab_success_pct","victim_carry_success_pct",
        "safe_zone_delivery_success_pct","triage_selection_success_pct","triage_tagging_success_pct",
        "mean_task_completion_time_sec","mean_victim_rescue_time_sec","interaction_errors_count","accidental_grab_count",
        "tracking_or_controller_issues_count","ease_of_use_rating","interaction_satisfaction_rating","overall_experience_rating",
        "feedback_category","synthetic_feedback","generation_notes"]

# Assumed (simulated) testing design: scene, interaction mode, device, number of casualties in the scene.
DESIGN = [
    ("Factory",   "Quest controllers",              "Simulated assumption: Meta Quest 3", 3),
    ("Factory",   "Controller-free hand tracking",  "Simulated assumption: Meta Quest 3", 3),
    ("Factory",   "Editor simulation",              "Simulated assumption: Meta XR Simulator", 3),
    ("Earthquake","Quest controllers",              "Simulated assumption: Meta Quest 3", 2),
    ("Earthquake","Controller-free hand tracking",  "Simulated assumption: Meta Quest 3", 2),
    ("Flood",     "Quest controllers",              "Simulated assumption: Meta Quest 3", 2),
    ("Flood",     "Editor simulation",              "Simulated assumption: Meta XR Simulator", 2),
    ("Triage Study","Quest controllers",            "Simulated assumption: Meta Quest 3", 10),
    ("Triage Study","Controller-free hand tracking","Simulated assumption: Meta Quest 3", 10),
    ("Triage Study","Editor simulation",            "Simulated assumption: Meta XR Simulator", 10),
]
FEEDBACK = {
    "Smooth interaction": "Synthetic comment: grabbing and carrying felt natural after the first attempt.",
    "Grab difficulty": "Synthetic comment: it took a few tries to get the casualty to lift.",
    "Hand tracking reliability": "Synthetic comment: the hand sometimes lost tracking when moving quickly.",
    "Navigation": "Synthetic comment: finding the safe zone took longer than expected.",
    "Visual quality": "Synthetic comment: the casualties looked like real people; the scene was dark in places.",
    "Triage clarity": "Synthetic comment: the START checks were clear, but the tag buttons were easy to mis-press.",
}

def clamp(v, lo, hi): return max(lo, min(hi, v))

def make_rows():
    rnd = random.Random(SEED)
    rows = []
    for i, (scene, mode, device, n) in enumerate(DESIGN, start=1):
        triage = scene == "Triage Study"
        hand = mode.startswith("Controller-free")
        # latent skill / difficulty: drives everything so the values are coherent
        skill = clamp(rnd.gauss(0.75, 0.15), 0.35, 1.0)
        errors = clamp(round((1 - skill) * 9 + rnd.uniform(-1, 1) + (1.5 if hand else 0)), 0, 8)
        accidental = clamp(round(errors * rnd.uniform(0.2, 0.6)), 0, 5)
        issues = clamp(round((2.5 if hand else 0.7) * rnd.uniform(0.3, 1.6) + (0.8 if mode == "Editor simulation" and rnd.random() < 0.3 else 0)), 0, 5)
        r = {c: "" for c in COLS}
        r.update(participant_id=f"SP{i:02d}", participant_name=f"Synthetic Participant {i:02d}", data_type="SYNTHETIC_AI_GENERATED",
                 device_used=device, interaction_mode=mode, scene_tested=scene)
        if triage:
            sel = clamp(round(n * clamp(skill + rnd.uniform(-0.1, 0.15), 0.7, 1.0)), 7, n)
            tag = clamp(round(sel * clamp(skill + rnd.uniform(-0.12, 0.12), 0.7, 1.0)), 7, sel)
            r["triage_selection_success_pct"] = round(100 * sel / n, 1)
            r["triage_tagging_success_pct"] = round(100 * tag / n, 1)
            r["task_completion_rate_pct"] = round(100 * tag / n, 1)           # tagged casualties / casualties
            t = 95 + errors * 9 + (1 - skill) * 40 + rnd.uniform(-8, 8)
            note = f"Triage: {n} casualties; selected {sel}, tagged {tag}. Rescue metrics not applicable (blank, not zero)."
        else:
            # Each synthetic participant makes A rescue attempts (casualties in the scene, repeated runs allowed).
            # Stage counts are nested (delivered <= carried <= grabbed <= selected <= attempts) and each stage ratio is kept inside the assignment's ranges.
            import math
            A = n * 2 + rnd.randint(0, 2)
            def draw(lo, hi, q):
                q = clamp(q, 0.0, 1.0); return int(clamp(hi - round((1 - q) * (hi - lo)), lo, hi))
            while True:
                sel_ok = draw(math.ceil(0.7 * A), A, skill + rnd.uniform(-0.15, 0.2))
                grab_ok = draw(math.ceil(0.6 * sel_ok), sel_ok, skill + rnd.uniform(-0.2, 0.15) - (0.08 if hand else 0))
                carry_ok = draw(math.ceil(0.55 * grab_ok), grab_ok, skill + rnd.uniform(-0.2, 0.15))
                deliv_ok = draw(math.ceil(0.6 * carry_ok), carry_ok, skill + rnd.uniform(-0.15, 0.2))
                if deliv_ok / A >= 0.6: break
            att, grab_att = A, sel_ok
            r["victim_selection_success_pct"] = round(100 * sel_ok / A, 1)
            r["victim_grab_success_pct"] = round(100 * grab_ok / sel_ok, 1)
            r["victim_carry_success_pct"] = round(100 * carry_ok / grab_ok, 1)
            r["safe_zone_delivery_success_pct"] = round(100 * deliv_ok / carry_ok, 1)
            r["task_completion_rate_pct"] = round(100 * deliv_ok / A, 1)       # delivered / rescue attempts
            errors = clamp(max(errors, round((A - deliv_ok) * 0.8)), 0, 8)
            n = A
            t = 40 + errors * 8 + n * 6 + (1 - skill) * 30 + rnd.uniform(-8, 8)
            per = clamp(t * rnd.uniform(0.5, 0.8) / (n / 3), 20, 150)
            r["mean_victim_rescue_time_sec"] = round(clamp(per, 20, 150), 1)
            note = (f"Rescue: {n} rescue attempts (casualties in scene, repeats allowed); attempts {att}, selected {sel_ok}, grabbed {grab_ok}/{grab_att}, carried {carry_ok}/{grab_ok}, "
                    f"delivered {deliv_ok}/{carry_ok}. Triage metrics not applicable (blank, not zero).")
        r["mean_task_completion_time_sec"] = round(clamp(t, 30, 180), 1)
        r["interaction_errors_count"] = errors; r["accidental_grab_count"] = accidental; r["tracking_or_controller_issues_count"] = issues
        def rate(bias): return int(clamp(round(1 + 4 * skill + bias - errors * 0.12 - issues * 0.1 + rnd.uniform(-0.6, 0.6)), 1, 5))
        r["ease_of_use_rating"] = rate(0.2); r["interaction_satisfaction_rating"] = rate(0.0); r["overall_experience_rating"] = rate(0.15)
        if triage: cat = "Triage clarity" if errors < 4 else rnd.choice(["Triage clarity", "Navigation"])
        elif hand and issues >= 2: cat = "Hand tracking reliability"
        elif accidental >= 2 or skill < 0.65: cat = "Grab difficulty"
        elif t > 140: cat = "Navigation"
        else: cat = rnd.choice(["Smooth interaction", "Visual quality"])
        r["feedback_category"] = cat; r["synthetic_feedback"] = FEEDBACK[cat]
        r["generation_notes"] = f"{LABEL}. Seed {SEED}. Simulated test condition. " + note
        rows.append(r)
    return rows

def num(rows, col): return [float(r[col]) for r in rows if r[col] != ""]
def mean_med(v): return (statistics.mean(v), statistics.median(v)) if v else (None, None)

def stats(rows):
    s = {"n": len(rows)}
    s["scenes"] = {sc: sum(1 for r in rows if r["scene_tested"] == sc) for sc in ["Factory", "Earthquake", "Flood", "Triage Study"]}
    s["modes"] = {m: sum(1 for r in rows if r["interaction_mode"] == m) for m in sorted({r["interaction_mode"] for r in rows})}
    for col in COLS:
        if col.endswith("_pct") or col.endswith("_sec") or col.endswith("_rating") or col.endswith("_count"):
            v = num(rows, col); s[col] = (len(v), *mean_med(v), sum(v))
    cats = {}
    for r in rows: cats[r["feedback_category"]] = cats.get(r["feedback_category"], 0) + 1
    s["cats"] = cats
    return s

def f(x, d=1): return "n/a" if x is None else f"{x:.{d}f}"

def write_summary(rows):
    s = stats(rows)
    top = max(s["cats"].values()); tops = sorted(k for k, v in s["cats"].items() if v == top)
    L = [f"# Synthetic user-testing summary\n", f"> **{LABEL}.** Generated for an academic exercise at the professor's request. None of these figures are results from the real people who tested the game, and they are not evidence of verified headset performance.\n",
         "All statistics below are computed from `synthetic_vr_disaster_simulator_data.csv` by `generate_synthetic_data.py`. Each uses only records where the metric is applicable and populated (n shown); blank cells are **not** zeros. No inferential statistics, error bars or significance claims are made.\n",
         f"- Synthetic participant count: **{s['n']}**",
         "- Records per scene: " + ", ".join(f"{k} {v}" for k, v in s["scenes"].items()),
         "- Records per interaction mode (simulated assumptions): " + ", ".join(f"{k} {v}" for k, v in s["modes"].items()), "",
         "| Metric | n (applicable records) | Mean | Median |", "|---|---:|---:|---:|"]
    labels = [("task_completion_rate_pct","Task completion rate (%)"),("victim_selection_success_pct","Victim selection success (%)"),
              ("victim_grab_success_pct","Victim grab success (%)"),("victim_carry_success_pct","Victim carry success (%)"),
              ("safe_zone_delivery_success_pct","Safe-zone delivery success (%)"),("triage_selection_success_pct","Triage selection success (%)"),
              ("triage_tagging_success_pct","Triage tagging success (%)"),("mean_task_completion_time_sec","Task completion time (s)"),
              ("mean_victim_rescue_time_sec","Victim rescue time (s), rescue scenes only"),("ease_of_use_rating","Ease-of-use rating (1-5)"),
              ("interaction_satisfaction_rating","Interaction satisfaction (1-5)"),("overall_experience_rating","Overall experience (1-5)"),
              ("interaction_errors_count","Interaction errors per participant")]
    for c, lab in labels:
        n, m, md, _ = s[c]; L.append(f"| {lab} | {n} | {f(m,2)} | {f(md,2)} |")
    n, m, md, tot = s["interaction_errors_count"]
    L += ["", f"- Interaction errors: total **{int(tot)}**, average **{f(m,2)}** per participant (n={n}).",
          f"- Most common synthetic feedback category: **{', '.join(tops)}** ({top} of {s['n']}). Counts: " + ", ".join(f"{k} {v}" for k, v in sorted(s['cats'].items())) + ".", "",
          "## Assumed testing design (simulated)", "Ten synthetic participants were assigned one scene and one interaction condition each (Quest controllers, controller-free hand tracking, or Editor simulation). These conditions are assumptions, not records of what the real testers used. Rescue success percentages are per-stage ratios (successes / the previous stage's successes, so grab is out of selected, carry out of grabbed, delivery out of carried; selection is out of attempts); task completion rate is delivered attempts out of rescue attempts (rescue) or tagged casualties out of 10 (triage). Triage metrics are blank for rescue scenes and rescue metrics are blank for the Triage Study.", "",
          "## Limits", "Ten generated rows, one scene each, so scene-wise comparisons rest on 2-3 records and are illustrative only. Values were drawn from a seeded random model with a shared latent skill factor, then constrained to the ranges the assignment specified. Do not report them as measured outcomes.", "",
          "Figures: `figures/` (each titled SYNTHETIC)."]
    open(SUMMARY, "w").write("\n".join(L) + "\n")

def charts(rows):
    os.makedirs(FIG, exist_ok=True)
    tag = "SYNTHETIC - NOT ACTUAL MEASUREMENTS"
    cols = [("task_completion_rate_pct","Task\ncompletion"),("victim_selection_success_pct","Victim\nselection"),("victim_grab_success_pct","Victim\ngrab"),
            ("victim_carry_success_pct","Victim\ncarry"),("safe_zone_delivery_success_pct","Safe-zone\ndelivery"),("triage_selection_success_pct","Triage\nselection"),("triage_tagging_success_pct","Triage\ntagging")]
    fig, ax = plt.subplots(figsize=(9, 4.5))
    vals = []; ns = []
    for c, _ in cols: v = num(rows, c); vals.append(statistics.mean(v)); ns.append(len(v))
    b = ax.bar([l for _, l in cols], vals, color="#7a8fb0")
    for rect, v, n in zip(b, vals, ns): ax.text(rect.get_x() + rect.get_width() / 2, v + 1, f"{v:.1f}%\n(n={n})", ha="center", fontsize=8)
    ax.set_ylim(0, 115); ax.set_ylabel("Mean success rate (%)"); ax.set_title(f"Task success rates (applicable records only)\n{tag}", fontsize=10)
    fig.tight_layout(); fig.savefig(f"{FIG}/synthetic_task_success_rates.png", dpi=150); plt.close(fig)

    fig, ax = plt.subplots(figsize=(6.5, 4.5))
    rc = [("ease_of_use_rating","Ease of use"),("interaction_satisfaction_rating","Interaction\nsatisfaction"),("overall_experience_rating","Overall\nexperience")]
    vals = [statistics.mean(num(rows, c)) for c, _ in rc]
    b = ax.bar([l for _, l in rc], vals, color="#a58f7a")
    for rect, v in zip(b, vals): ax.text(rect.get_x() + rect.get_width() / 2, v + 0.08, f"{v:.2f}", ha="center", fontsize=9)
    ax.set_ylim(0, 5.5); ax.set_ylabel("Mean rating (1-5, n=10)"); ax.set_title(f"Average ratings\n{tag}", fontsize=10)
    fig.tight_layout(); fig.savefig(f"{FIG}/synthetic_average_ratings.png", dpi=150); plt.close(fig)

    fig, ax = plt.subplots(figsize=(9, 4.5))
    ids = [r["participant_id"] for r in rows]; t = [float(r["mean_task_completion_time_sec"]) for r in rows]
    colors = {"Factory":"#c98b4a","Earthquake":"#8a6e5a","Flood":"#5a86a8","Triage Study":"#6aa07a"}
    ax.bar(ids, t, color=[colors[r["scene_tested"]] for r in rows])
    for i, v in enumerate(t): ax.text(i, v + 2, f"{v:.0f}", ha="center", fontsize=8)
    ax.set_ylabel("Mean task completion time (s)"); ax.set_title(f"Completion time per synthetic participant (colour = scene)\n{tag}", fontsize=10)
    ax.legend(handles=[plt.Rectangle((0, 0), 1, 1, color=c) for c in colors.values()], labels=list(colors), fontsize=8)
    fig.tight_layout(); fig.savefig(f"{FIG}/synthetic_completion_times_per_participant.png", dpi=150); plt.close(fig)

    fig, ax = plt.subplots(figsize=(7, 4.5))
    scenes = ["Factory", "Earthquake", "Flood", "Triage Study"]
    m = [statistics.mean(float(r["task_completion_rate_pct"]) for r in rows if r["scene_tested"] == s) for s in scenes]
    k = [sum(1 for r in rows if r["scene_tested"] == s) for s in scenes]
    b = ax.bar([f"{s}\n(n={n})" for s, n in zip(scenes, k)], m, color=[colors[s] for s in scenes])
    for rect, v in zip(b, m): ax.text(rect.get_x() + rect.get_width() / 2, v + 1, f"{v:.1f}%", ha="center", fontsize=9)
    ax.set_ylim(0, 115); ax.set_ylabel("Mean task completion rate (%)"); ax.set_title(f"Scene-wise task completion (2-3 records per scene; illustrative)\n{tag}", fontsize=10)
    fig.tight_layout(); fig.savefig(f"{FIG}/synthetic_scene_comparison.png", dpi=150); plt.close(fig)

if __name__ == "__main__":
    rows = make_rows()
    with open(CSV_PATH, "w", newline="") as fh:
        w = csv.DictWriter(fh, COLS); w.writeheader(); w.writerows(rows)
    write_summary(rows); charts(rows)
    print("wrote", CSV_PATH, SUMMARY, FIG)
