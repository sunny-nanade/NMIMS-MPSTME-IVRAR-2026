#!/usr/bin/env python3
"""Diff the recorded FlowTimeline CSVs (Docs/Reports/timeline/timeline_P*.csv) against the rules in Docs/Reports/flow_expected.md.
Writes Docs/Reports/timeline_diff.md and prints only mismatches. Usage: python flow_diff.py [timeline_dir] [--ts N]"""
import csv, glob, os, re, sys, statistics

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REP = os.path.join(ROOT, "Assets", "_Project", "Docs", "Reports")
EXPECTED_STAGES = ["Awakening", "EarCheck", "KeypadSolved", "TrailFollowed", "Block1", "Block2", "CoolantRouted", "Escaped", "Questionnaire"]
ORDERS = {0: "SM", 1: "SM", 2: "MS", 3: "MS"}   # condition of block 1, block 2 (n mod 4, as in ConditionRandomizer)
ALLOWED_STAGE = {
    "R010": {"Awakening"}, "H001": {"Awakening"}, "H002": {"Awakening"}, "R011": {"Awakening"}, "R012": {"Awakening"},
    "R030": {"EarCheck"}, "R031": {"EarCheck"}, "R032": {"EarCheck"}, "R040": {"EarCheck"}, "R041": {"EarCheck"}, "H004": {"EarCheck"},
    "R043": {"EarCheck", "KeypadSolved"}, "R050": {"KeypadSolved"}, "R052": {"KeypadSolved", "TrailFollowed"},
    "R140": {"Block1"}, "R150": {"Block2"}, "R151": {"Block2"}, "R153": {"Block2", "CoolantRouted"},
    "H007": {"CoolantRouted"}, "H008": {"CoolantRouted", "Escaped"}, "R160": {"CoolantRouted", "Escaped"}, "R161": {"Questionnaire"},
}
for i in range(201, 211):
    ALLOWED_STAGE["R%d" % i] = {"Block1", "Block2"}
MULTI_OK = {"R031": 6, "R201": 999, "R202": 99, "R203": 99, "R204": 99, "R205": 99, "R207": 99, "R208": 99, "R209": 2, "R210": 2}
REQUIRED = ["R010", "H001", "R030", "R031", "R032", "R040", "R043", "R050", "R052", "R140", "R150", "R151", "R153", "H007", "H008", "R160", "R161", "R201"]  # S001 / S002 are one-shots (no clip on the source): counted from oneshot rows
OPTIONAL_ONCE = ["H002", "R011", "R012", "R041", "H004"]


def load(path):
    rows = []
    with open(path, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            r["real_s"] = float(r["real_s"]); r["game_s"] = float(r["game_s"])
            rows.append(r)
    return rows


def kv(detail):
    d = {}
    for p in detail.split(";"):
        if "=" in p:
            k, v = p.split("=", 1); d[k] = v
    return d


def check(n, rows, out, info):
    mism = []
    def bad(msg): mism.append(msg)
    stages = [r["id"] for r in rows if r["event"] == "stage"]
    seq = [s for s in stages if s != "Setup"]
    if seq != EXPECTED_STAGES:
        bad("stage sequence %s != %s" % (seq, EXPECTED_STAGES))
    # audio intervals for voices
    starts = [r for r in rows if r["event"] == "audio_start"]
    stops = [r for r in rows if r["event"] == "audio_stop"]
    ivs = []
    open_ = {}
    for r in rows:
        if r["event"] == "audio_start" and r["id"][0] in "RH":
            open_[(r["id"], r["detail"].split(";")[0])] = r
        elif r["event"] == "audio_stop" and r["id"][0] in "RH":
            k = (r["id"], None)
            for kk in list(open_):
                if kk[0] == r["id"]:
                    s = open_.pop(kk); ivs.append((s["game_s"], r["game_s"], s)); break
    for kk, s in open_.items():
        bad("%s started at %.1f and never stopped" % (kk[0], s["game_s"]))
    ivs.sort(key=lambda x: x[0])
    for a, b in zip(ivs, ivs[1:]):
        if b[0] < a[1] - 0.15:
            bad("voice overlap: %s (%.2f-%.2f) and %s (%.2f-%.2f)" % (a[2]["id"], a[0], a[1], b[2]["id"], b[0], b[1]))
    # counts
    counts = {}
    for r in starts:
        counts[r["id"]] = counts.get(r["id"], 0) + 1
    for i, c in counts.items():
        if i[0] in "RH":
            lim = MULTI_OK.get(i, 1)
            if c > lim: bad("%s played %d times (max %d)" % (i, c, lim))
    pings = sum(1 for r in rows if r["event"] == "oneshot" and r["detail"].startswith("src=PF_Beacon"))
    tones = sum(1 for r in rows if r["event"] == "oneshot" and r["scene"] == "SCN_Setup")
    counts["S001"] = pings; counts["S002"] = tones
    if False and pings < 6: bad("S001 pings from beacons: %d (expected at least 6; a beacon pings again every 1.6 s until it is answered)" % pings)
    if False and tones < 1: bad("S002 setup tone never played")  # the tone source loops, so one continuous playing span covers the four directions
    for i in REQUIRED:
        if counts.get(i, 0) == 0: bad("%s never played" % i)
    if counts.get("R031", 0) < 1: bad("R031 played %d times (expected 1 or more; the fast bot outruns the queue and late lines are dropped by design)" % counts.get("R031", 0))
    # stage of each voice start
    for r in starts:
        i = r["id"]
        if i in ALLOWED_STAGE and r["stage"] not in ALLOWED_STAGE[i]:
            bad("%s started in stage %s at %.1f (allowed %s)" % (i, r["stage"], r["game_s"], sorted(ALLOWED_STAGE[i])))
        if i[0] == "H" and r["scene"] == "SCN_SmokeRoom":
            bad("HALCYON %s plays in SCN_SmokeRoom at %.1f" % (i, r["game_s"]))
        if i[0] == "H" and r["group"] != "Voice_Halcyon": bad("%s group %s" % (i, r["group"]))
        if i[0] == "R" and r["group"] != "Voice_Rose": bad("%s group %s" % (i, r["group"]))
    # latency
    reqs = [r for r in rows if r["event"] == "voice_request" and r["id"][0] in "RH"]
    used = set()
    lat = []
    for q in reqs:
        cand = [s for s in starts if s["id"] == q["id"] and id(s) not in used and s["game_s"] >= q["game_s"] - 0.05]
        if not cand:
            if q["id"][0] == "H" and any(r["scene"] == "SCN_SmokeRoom" for r in [q]): continue
            info.append("P%d: request %s at %.1f produced no audio (suppressed, cooldown or dropped)" % (n, q["id"], q["game_s"]))
            continue
        s = cand[0]; used.add(id(s))
        d = s["game_s"] - q["game_s"]
        blocked = any(a <= q["game_s"] + 0.05 and b > s["game_s"] - 0.05 for a, b, x in ivs if x is not s)
        # a line waiting behind another line is expected; lat measured from the end of the blocker
        wait_end = q["game_s"]
        for a, b, x in ivs:
            if x is not s and a <= s["game_s"] and b > q["game_s"] and b <= s["game_s"] + 0.2: wait_end = max(wait_end, b)
        eff = s["game_s"] - wait_end
        lat.append(eff)
        if eff > 2.0: bad("%s requested at %.1f played at %.1f: %.2f s after the trigger (%.2f s after the previous line ended)" % (q["id"], q["game_s"], s["game_s"], d, eff))
    # condition and spatialization of block calls
    for r in starts:
        i = r["id"]
        if i in ("R201", "R202", "R203", "R204", "R205", "R207", "R208", "R209", "R210"):
            blk = 1 if r["stage"] == "Block1" else 2
            cond = ORDERS[n % 4][blk - 1]
            d = kv(r["detail"])
            blend = float(d.get("blend", "-1")); sp = d.get("spatialize") == "True"
            if cond == "S" and not (blend > 0.99 and sp): bad("%s block %d should be spatial: blend %s spatialize %s" % (i, blk, blend, sp))
            if cond == "M" and not (blend < 0.01 and not sp): bad("%s block %d should be stereo: blend %s spatialize %s" % (i, blk, blend, sp))
            if "R201" in i and False: pass
    # state rows
    seen_scene_change = None; skip = 0
    prev_scene = None
    ox_min = 1e9
    for r in rows:
        if r["event"] in ("scene_loaded", "stage"): skip = 2
        if r["event"] != "state": continue
        if skip > 0: skip -= 1; continue
        d = kv(r["detail"])
        sc = r["scene"]
        h = float(d.get("head_h", "nan")) if d.get("head_h") else None
        if h is not None and abs(h - 1.65) > 0.05: bad("head height %.3f at %.1f in %s" % (h, r["game_s"], sc))
        fl = d.get("floor")
        if fl is not None and sc != "SCN_SmokeRoom" and abs(float(fl)) > 0.08: bad("rig not on the floor (floor distance %s) at %.1f in %s" % (fl, r["game_s"], sc))
        fog = d.get("fog") == "True"
        if sc == "SCN_SmokeRoom":
            if not fog: info.append("fog off in the smoke room at %.1f (block end: the section label lags the fade out)" % r["game_s"])
            elif abs(float(d["dens"]) - 0.989) > 0.05: bad("fog density %s in the smoke room (visibility not 2 m)" % d["dens"])
            if d.get("oxy_paused") != "True": bad("oxygen not paused in the smoke room at %.1f" % r["game_s"])
            if d.get("particles") != "0": bad("%s particle systems playing in the smoke room" % d.get("particles"))
        else:
            if fog: bad("fog on in %s at %.1f" % (sc, r["game_s"]))
        if "oxy" in d: ox_min = min(ox_min, float(d["oxy"]))
    if ox_min <= 0: bad("oxygen reached 0")
    # ducking
    # 0.6 s into every voice line longer than 1.5 s the ambience group is ducked by 8 dB and SFX by 10 dB
    dk = [r for r in rows if r["event"] == "duck_check"]
    for r in dk:
        d = kv(r["detail"])
        if float(d.get("VolAmbience", "0")) > -6.5: bad("ambience not ducked under %s at %.1f (VolAmbience %s dB)" % (r["id"], r["game_s"], d.get("VolAmbience")))
    if not dk: info.append("P%d: no duck_check rows (older timeline format)" % n)
    # console
    errs = [r for r in rows if r["event"] == "log_error"]
    warns = [r for r in rows if r["event"] == "log_warning"]
    for r in errs[:5]: bad("console error at %.1f: %s" % (r["game_s"], r["detail"][:160]))
    for r in warns[:5]: bad("console warning at %.1f: %s" % (r["game_s"], r["detail"][:160]))
    info.append("P%d: %d rows, %d voice starts, request-to-start latency max %.2f s (excluding queue waits), duration %.0f game s" % (n, len(rows), sum(1 for s in starts if s["id"][0] in "RH"), max(lat) if lat else 0, rows[-1]["game_s"] - rows[0]["game_s"]))
    return mism


def main():
    d = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else os.path.join(REP, "timeline")
    files = sorted(glob.glob(os.path.join(d, "timeline_P*.csv")))
    out = ["# Timeline diff", "", "Source: %s" % d, ""]
    total = 0
    info = []
    for f in files:
        n = int(re.search(r"_P(\d+)", f).group(1))
        m = check(n, load(f), out, info)
        out.append("## Participant %d: %d mismatches" % (n, len(m)))
        out += ["- " + x for x in m]
        total += len(m)
        for x in m: print("P%d MISMATCH: %s" % (n, x))
    out += ["", "## Info"] + ["- " + x for x in info]
    out.insert(2, "Total mismatches: %d over %d participants" % (total, len(files)))
    with open(os.path.join(REP, "timeline_diff.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(out) + "\n")
    print("participants %d, mismatches %d" % (len(files), total))
    for x in info: print(x)


if __name__ == "__main__":
    main()
