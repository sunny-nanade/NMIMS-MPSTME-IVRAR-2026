#!/usr/bin/env python3
"""Analyse REAL triage-study files for IVRAR Group 16.

Usage:
  python3 Docs/tools/analyze_study.py --records telemetry/ --questionnaire telemetry/questionnaires.csv --out analysis/
  python3 Docs/tools/analyze_study.py --self-test     # checks the script on throw-away synthetic data (never saved)

Reads the unedited `<session>_records.csv` files written by the app and the filled questionnaire sheet.
Only rows with data_provenance == HumanParticipant and a consented, validated participant are analysed.
Everything else is listed in validation_report.txt. Nothing is ever imputed or invented.
If no valid participant exists the script reports N = 0 and writes no statistics.
"""
import argparse, csv, glob, math, os, re, statistics, sys, tempfile

RECORD_COLS = ("session_id,data_provenance,participant_id,protocol_perfusion,casualty_id,ground_truth,assigned,is_correct,"
               "true_red_under_triaged,expected_branch,assessment_start_utc,assessment_end_utc,assessment_start_s,"
               "assessment_end_s,assessment_latency_s,latency_valid,hazard_level,inputs_examined,airway_repositioned,"
               "obs_can_walk,obs_breathing,obs_breathing_after_airway,obs_resp_rate,obs_cap_refill_s,obs_radial_pulse,"
               "obs_follows_commands").split(",")
Q_COLS = ("participant_id,consent_form_signed_and_filed,date_of_session,build_commit,device,prior_triage_training,"
          "prior_vr_experience,sus_q1,sus_q2,sus_q3,sus_q4,sus_q5,sus_q6,sus_q7,sus_q8,sus_q9,sus_q10,sus_score,"
          "nasa_tlx_mental,nasa_tlx_physical,nasa_tlx_temporal,nasa_tlx_performance,nasa_tlx_effort,"
          "nasa_tlx_frustration,ssq_nausea,ssq_oculomotor,ssq_disorientation,notes").split(",")
TLX = ["nasa_tlx_mental", "nasa_tlx_physical", "nasa_tlx_temporal", "nasa_tlx_performance", "nasa_tlx_effort", "nasa_tlx_frustration"]
CATS = ["Green", "Yellow", "Red", "Black"]
PID = re.compile(r"^P[0-9]{2,3}$")
EXPECTED_CASUALTIES = 10


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        r = csv.DictReader(f)
        return r.fieldnames or [], list(r)


def num(x):
    try:
        v = float(str(x).strip())
        return v if math.isfinite(v) else None
    except ValueError:
        return None


def sus_from_items(row):
    items = [num(row.get("sus_q%d" % i)) for i in range(1, 11)]
    if any(v is None or v != int(v) or not 1 <= v <= 5 for v in items):
        return None
    return 2.5 * sum((v - 1) if i % 2 == 0 else (5 - v) for i, v in enumerate(items))  # i=0 is item 1 (odd)


def describe(vals):
    if not vals:
        return ("0", "NA", "NA", "NA")
    sd = statistics.stdev(vals) if len(vals) > 1 else float("nan")
    return (str(len(vals)), "%.3f" % statistics.mean(vals), "NA" if math.isnan(sd) else "%.3f" % sd, "%.3f" % statistics.median(vals))


def analyse(records_dir, q_path, out_dir):
    rep = []
    def log(s): rep.append(s)

    # ---- questionnaires ----
    q = {}
    if q_path and os.path.exists(q_path):
        hdr, rows = read_csv(q_path)
        if hdr != Q_COLS: log("QUESTIONNAIRE HEADER differs from template: %s" % [c for c in Q_COLS if c not in hdr])
        for r in rows:
            pid = (r.get("participant_id") or "").strip()
            if not pid: continue
            probs = []
            if not PID.match(pid): probs.append("participant_id must look like P01 (TEST is not a human participant)")
            if pid in q: probs.append("duplicate questionnaire row")
            if (r.get("consent_form_signed_and_filed") or "").strip().lower() != "yes": probs.append("consent_form_signed_and_filed is not 'yes'")
            s = sus_from_items(r)
            if s is None: probs.append("SUS items missing or not integers 1-5")
            else:
                rec = num(r.get("sus_score"))
                if rec is not None and abs(rec - s) > 0.01: probs.append("recorded sus_score %.1f != recomputed %.1f" % (rec, s))
            tl = [num(r.get(c)) for c in TLX]
            if any(v is None or not 0 <= v <= 100 for v in tl): probs.append("NASA-TLX scales missing or outside 0-100")
            for c in ("ssq_nausea", "ssq_oculomotor", "ssq_disorientation"):
                v = (r.get(c) or "").strip()
                if v and num(v) is None: probs.append("%s not numeric" % c)
            q[pid] = {"row": r, "sus": s, "tlx": (sum(tl) / 6 if not any(v is None for v in tl) else None), "probs": probs}
    else:
        log("No questionnaire file supplied/found: SUS and NASA-TLX will not be reported.")

    # ---- telemetry ----
    sessions = {}
    files = sorted(glob.glob(os.path.join(records_dir, "**", "*_records.csv"), recursive=True))
    if not files: log("No *_records.csv files found under %s" % records_dir)
    for fp in files:
        hdr, rows = read_csv(fp)
        name = os.path.relpath(fp, records_dir)
        if hdr != RECORD_COLS:
            log("EXCLUDED %s: header does not match the 26-column records format" % name); continue
        if not rows:
            log("EXCLUDED %s: no rows" % name); continue
        provs = {r["data_provenance"] for r in rows}
        pids = {r["participant_id"] for r in rows}
        sids = {r["session_id"] for r in rows}
        if provs != {"HumanParticipant"}:
            log("EXCLUDED %s: provenance %s (only HumanParticipant counts as human data)" % (name, sorted(provs))); continue
        if len(pids) != 1 or len(sids) != 1:
            log("EXCLUDED %s: mixed participant or session ids" % name); continue
        pid = pids.pop()
        probs = []
        if not PID.match(pid): probs.append("participant_id %r invalid" % pid)
        cids = [r["casualty_id"] for r in rows]
        if len(set(cids)) != len(cids): probs.append("duplicate casualty rows")
        if len(rows) != EXPECTED_CASUALTIES: probs.append("%d of %d casualties tagged (partial session)" % (len(rows), EXPECTED_CASUALTIES))
        if len({r["protocol_perfusion"] for r in rows}) != 1: probs.append("mixed protocol_perfusion")
        for r in rows:
            if r["ground_truth"] not in CATS or r["assigned"] not in CATS: probs.append("%s: category not Green/Yellow/Red/Black" % r["casualty_id"])
            ok = r["ground_truth"] == r["assigned"]
            if (r["is_correct"].strip().lower() == "true") != ok: probs.append("%s: is_correct disagrees with ground_truth/assigned" % r["casualty_id"])
            under = r["ground_truth"] == "Red" and r["assigned"] in ("Yellow", "Green")
            if (r["true_red_under_triaged"].strip().lower() == "true") != under: probs.append("%s: true_red_under_triaged inconsistent" % r["casualty_id"])
            if r["latency_valid"].strip().lower() == "true" and (num(r["assessment_latency_s"]) is None or num(r["assessment_latency_s"]) < 0):
                probs.append("%s: latency flagged valid but not a non-negative number" % r["casualty_id"])
        if pid in sessions: probs.append("second session file for the same participant")
        if probs: log("EXCLUDED %s (%s):\n    - %s" % (name, pid, "\n    - ".join(sorted(set(probs)))))
        else: sessions[pid] = {"file": name, "rows": rows}
        # a participant may only be used if both files are valid

    valid = []
    for pid, s in sessions.items():
        if pid not in q: log("EXCLUDED %s: no questionnaire row for %s" % (s["file"], pid)); continue
        if q[pid]["probs"]: log("EXCLUDED %s: questionnaire problems for %s:\n    - %s" % (s["file"], pid, "\n    - ".join(q[pid]["probs"]))); continue
        valid.append(pid)
    for pid in q:
        if pid not in sessions and not q[pid]["probs"]: log("Questionnaire for %s has no valid telemetry session" % pid)

    valid.sort()
    n = len(valid)
    log("VALID participants (N): %d %s" % (n, valid))
    if n < 10: log("WARNING: N < 10. The assignment needs at least 10 real participants; report the real N and the shortfall.")

    os.makedirs(out_dir, exist_ok=True)
    per = []; pooled = {g: {a: 0 for a in CATS} for g in CATS}
    for pid in valid:
        rows = sessions[pid]["rows"]
        corr = sum(r["ground_truth"] == r["assigned"] for r in rows)
        red = [r for r in rows if r["ground_truth"] == "Red"]
        under = sum(r["assigned"] in ("Yellow", "Green") for r in red)
        lats = [num(r["assessment_latency_s"]) for r in rows if r["latency_valid"].strip().lower() == "true"]
        for r in rows: pooled[r["ground_truth"]][r["assigned"]] += 1
        per.append({"participant_id": pid, "protocol_perfusion": rows[0]["protocol_perfusion"], "classified": len(rows), "correct": corr,
                    "accuracy": corr / len(rows), "true_red": len(red), "true_red_under_triaged": under,
                    "under_triage_rate": (under / len(red)) if red else None,
                    "mean_latency_s": statistics.mean(lats) if lats else None, "median_latency_s": statistics.median(lats) if lats else None,
                    "sus": q[pid]["sus"], "nasa_tlx_raw_mean": q[pid]["tlx"]})
    fmt = lambda v: "NA" if v is None else ("%.4f" % v if isinstance(v, float) else v)
    with open(os.path.join(out_dir, "per_participant.csv"), "w", newline="") as f:
        w = csv.writer(f); keys = list(per[0].keys()) if per else ["participant_id"]; w.writerow(keys)
        for p in per: w.writerow([fmt(p[k]) for k in keys])
    with open(os.path.join(out_dir, "pooled_confusion.csv"), "w", newline="") as f:
        w = csv.writer(f); w.writerow(["ground_truth\\assigned"] + CATS)
        for g in CATS: w.writerow([g] + [pooled[g][a] for a in CATS])
    pr = sum(p["true_red"] for p in per); pu = sum(p["true_red_under_triaged"] for p in per)
    pc = sum(p["correct"] for p in per); pn = sum(p["classified"] for p in per)
    with open(os.path.join(out_dir, "results_summary.csv"), "w", newline="") as f:
        w = csv.writer(f); w.writerow(["measure", "N", "mean", "SD", "median"])
        for label, key in (("accuracy (per participant)", "accuracy"), ("under_triage_rate (per participant)", "under_triage_rate"),
                           ("mean_latency_s (per participant)", "mean_latency_s"), ("SUS", "sus"), ("NASA-TLX raw mean", "nasa_tlx_raw_mean")):
            w.writerow([label] + list(describe([p[key] for p in per if p[key] is not None])))
        w.writerow(["pooled accuracy (all casualties)", pn, "NA" if not pn else "%.4f" % (pc / pn), "", ""])
        w.writerow(["pooled under-triage (all true Red)", pr, "NA" if not pr else "%.4f" % (pu / pr), "", ""])
    try:
        import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
        if per:
            sus = [p["sus"] for p in per]; lat = [p["mean_latency_s"] for p in per if p["mean_latency_s"] is not None]
            for data, name, yl in ((sus, "sus_boxplot.png", "SUS (0-100)"), (lat, "latency_boxplot.png", "mean latency per participant (s)")):
                fig, ax = plt.subplots(figsize=(3.2, 4)); ax.boxplot(data); ax.set_ylabel(yl); ax.set_title("N=%d" % len(data)); fig.tight_layout()
                fig.savefig(os.path.join(out_dir, name), dpi=150); plt.close(fig)
    except ImportError:
        log("matplotlib not installed: no plots written (the CSV outputs are complete).")
    with open(os.path.join(out_dir, "validation_report.txt"), "w") as f: f.write("\n".join(rep) + "\n")
    print("\n".join(rep)); return n


def self_test():
    """Exercises the code on throw-away data in a temp folder. The data are deleted and are NOT study data."""
    import random
    random.seed(1)
    with tempfile.TemporaryDirectory() as d:
        truth = ["Green", "Yellow", "Red", "Red", "Black", "Red", "Yellow", "Green", "Red", "Yellow"]
        q = [dict.fromkeys(Q_COLS, "")]
        qrows = []
        for i in (1, 2):
            pid = "P%02d" % i
            rows = []
            for k, t in enumerate(truth):
                a = t if k != 2 else "Yellow"
                rows.append(dict.fromkeys(RECORD_COLS, "") | {"session_id": "S%d" % i, "data_provenance": "HumanParticipant", "participant_id": pid,
                    "protocol_perfusion": "CapillaryRefill", "casualty_id": "C%02d" % (k + 1), "ground_truth": t, "assigned": a,
                    "is_correct": str(t == a), "true_red_under_triaged": str(t == "Red" and a != "Red"), "assessment_latency_s": str(2 + k), "latency_valid": "True"})
            with open(os.path.join(d, "S%d_records.csv" % i), "w", newline="") as f:
                w = csv.DictWriter(f, RECORD_COLS); w.writeheader(); w.writerows(rows)
            qr = dict.fromkeys(Q_COLS, "") | {"participant_id": pid, "consent_form_signed_and_filed": "yes"}
            for j in range(1, 11): qr["sus_q%d" % j] = "4" if j % 2 else "2"
            qr["sus_score"] = "75"
            for c in TLX: qr[c] = "40"
            qrows.append(qr)
        qrows[1]["sus_score"] = "99"  # deliberately wrong: must be excluded
        with open(os.path.join(d, "q.csv"), "w", newline="") as f:
            w = csv.DictWriter(f, Q_COLS); w.writeheader(); w.writerows(qrows)
        n = analyse(d, os.path.join(d, "q.csv"), os.path.join(d, "out"))
        assert n == 1, n
        with open(os.path.join(d, "out", "results_summary.csv")) as f: txt = f.read()
        assert "pooled under-triage (all true Red),4,0.2500" in txt.replace("\r", ""), txt
        assert "accuracy (per participant),1,0.900" in txt.replace("\r", ""), txt
    print("SELF-TEST OK (synthetic data discarded; no result was produced)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--records"); ap.add_argument("--questionnaire"); ap.add_argument("--out", default="analysis"); ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test: self_test()
    elif a.records: analyse(a.records, a.questionnaire, a.out)
    else: ap.error("give --records DIR (and --questionnaire FILE) or --self-test")
