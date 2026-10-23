#!/usr/bin/env python3
"""
MazeLayoutVerifier: generate, verify and rank maze layouts for SCN_SmokeRoom (Protocol: Deep Sleep).

Constraints (CLAUDE.md DECISIONS): 6 x 6 grid of 2 m cells, unique shortest path of 18 steps (36 m),
exactly 6 junction cells on that path, exactly 3 dead ends, at least 2 misleading junctions.
Misleading junction: a wrong branch whose first cell is closer (Manhattan) to the exit than the correct next cell.

Usage
  python Tools/MazeLayoutVerifier.py generate [--seed N] [--trials N] [--out Assets/_Project/Data]
  python Tools/MazeLayoutVerifier.py verify Assets/_Project/Data/MazeLayoutA.json Assets/_Project/Data/MazeLayoutB.json

Standard library only. Layout cells are stored flat: index = row * 6 + col, bit mask of open sides N=1 E=2 S=4 W=8.
Rows grow north (+z in Unity), columns grow east (+x). The start door is on the south wall of the start cell,
the exit door on the north wall of the exit cell.
"""
import argparse
import json
import os
import random
import sys
from collections import deque

N = 6
CELL = 2.0
STEPS = 18
JUNCTIONS = 6
DEAD_ENDS = 3
MIN_MISLEADING = 2
DIRS = {"N": (0, 1), "E": (1, 0), "S": (0, -1), "W": (-1, 0)}
BIT = {"N": 1, "E": 2, "S": 4, "W": 8}
OPP = {"N": "S", "E": "W", "S": "N", "W": "E"}


def inside(c, r):
    return 0 <= c < N and 0 <= r < N


def neighbours(c, r):
    for d, (dc, dr) in DIRS.items():
        if inside(c + dc, r + dr):
            yield d, (c + dc, r + dr)


def manhattan(a, b):
    return abs(a[0] - b[0]) + abs(a[1] - b[1])


def dir_between(a, b):
    for d, (dc, dr) in DIRS.items():
        if (a[0] + dc, a[1] + dr) == b:
            return d
    return None


# --------------------------------------------------------------------------- layout helpers
def empty_masks():
    return [[0] * N for _ in range(N)]  # masks[c][r]


def open_edge(masks, a, b):
    d = dir_between(a, b)
    masks[a[0]][a[1]] |= BIT[d]
    masks[b[0]][b[1]] |= BIT[OPP[d]]


def edges_of(masks):
    out = set()
    for c in range(N):
        for r in range(N):
            for d, (dc, dr) in DIRS.items():
                if masks[c][r] & BIT[d]:
                    out.add(frozenset(((c, r), (c + dc, r + dr))))
    return out


def open_neighbours(masks, cell):
    c, r = cell
    return [(c + DIRS[d][0], r + DIRS[d][1]) for d in DIRS if masks[c][r] & BIT[d]]


def evaluate(masks, start, exit_):
    """Return metrics dict, or None when the layout is structurally invalid."""
    dist = {start: 0}
    count = {start: 1}
    q = deque([start])
    order = []
    while q:
        cur = q.popleft()
        order.append(cur)
        for nb in open_neighbours(masks, cur):
            if nb not in dist:
                dist[nb] = dist[cur] + 1
                count[nb] = 0
                q.append(nb)
            if dist[nb] == dist[cur] + 1:
                count[nb] += count[cur]
    if exit_ not in dist:
        return None
    used = set(dist)
    # every cell with an opening must be reachable
    for c in range(N):
        for r in range(N):
            if masks[c][r] and (c, r) not in used:
                return None
    steps = dist[exit_]
    unique = count[exit_] == 1
    # reconstruct the shortest path (unique case)
    path = [exit_]
    cur = exit_
    while cur != start:
        prev = None
        for nb in open_neighbours(masks, cur):
            if dist.get(nb, 99) == dist[cur] - 1:
                prev = nb
                break
        path.append(prev)
        cur = prev
    path.reverse()
    deg = {cell: len(open_neighbours(masks, cell)) for cell in used}
    junctions = []
    misleading = []
    for i, cell in enumerate(path):
        if cell in (start, exit_):
            continue
        if deg[cell] >= 3:
            correct = path[i + 1]
            wrong = [nb for nb in open_neighbours(masks, cell) if nb not in (path[i - 1], path[i + 1])]
            mis = any(manhattan(w, exit_) < manhattan(correct, exit_) for w in wrong)
            junctions.append({
                "cell": cell, "index": i, "correct": dir_between(cell, correct),
                "wrong": [dir_between(cell, w) for w in wrong], "misleading": mis,
            })
            if mis:
                misleading.append(cell)
    dead_ends = [cell for cell in used if deg[cell] == 1 and cell not in (start, exit_)]
    edges = sum(deg.values()) // 2
    loops = edges - len(used) + 1
    # turns and straight runs on the path
    dirs = [dir_between(path[i], path[i + 1]) for i in range(len(path) - 1)]
    turns = sum(1 for i in range(1, len(dirs)) if dirs[i] != dirs[i - 1])
    longest = run = 1
    for i in range(1, len(dirs)):
        run = run + 1 if dirs[i] == dirs[i - 1] else 1
        longest = max(longest, run)
    left = right = 0
    cw = ["N", "E", "S", "W"]
    for i in range(1, len(dirs)):
        if dirs[i] != dirs[i - 1]:
            k = (cw.index(dirs[i]) - cw.index(dirs[i - 1])) % 4
            if k == 1:
                right += 1
            elif k == 3:
                left += 1
    # longest side branch depth from path junctions (cells not on path)
    on_path = set(path)
    depth = 0
    for j in junctions:
        seen = {j["cell"]}
        qq = deque([(j["cell"], 0)])
        while qq:
            cur, dd = qq.popleft()
            depth = max(depth, dd)
            for nb in open_neighbours(masks, cur):
                if nb not in seen and (nb not in on_path or nb == j["cell"]):
                    seen.add(nb)
                    qq.append((nb, dd + 1))
    return {
        "steps": steps, "unique": unique, "path": path, "junctions": junctions, "misleading": misleading,
        "dead_ends": sorted(dead_ends), "loops": loops, "used": len(used), "turns": turns,
        "longest_straight": longest, "left": left, "right": right, "branch_depth": depth,
    }


def meets_spec(m):
    return (m is not None and m["steps"] == STEPS and m["unique"] and len(m["junctions"]) == JUNCTIONS
            and len(m["dead_ends"]) == DEAD_ENDS and len(m["misleading"]) >= MIN_MISLEADING)


# --------------------------------------------------------------------------- generation
def random_path(rng, start, exit_, steps, budget=40000):
    path = [start]
    visited = {start}
    nodes = [0]

    def dfs(cell, remaining):
        nodes[0] += 1
        if nodes[0] > budget:
            return False
        if remaining == 0:
            return cell == exit_
        md = manhattan(cell, exit_)
        if md > remaining or (remaining - md) % 2:
            return False
        nbs = [n for _, n in neighbours(*cell) if n not in visited]
        rng.shuffle(nbs)
        for n in nbs:
            if n == exit_ and remaining != 1:
                continue
            path.append(n)
            visited.add(n)
            if dfs(n, remaining - 1):
                return True
            path.pop()
            visited.remove(n)
        return False

    return list(path) if dfs(start, steps) else None


def free_neighbours(cell, taken):
    return [n for _, n in neighbours(*cell) if n not in taken]


def chain_from(rng, first, taken, length):
    chain = [first]
    local = set(taken) | {first}
    while len(chain) < length:
        opts = free_neighbours(chain[-1], local)
        if not opts:
            return None
        n = rng.choice(opts)
        chain.append(n)
        local.add(n)
    return chain


def op_spur(rng, masks, path, taken):
    interior = path[1:-1]
    rng.shuffle(interior)
    for p in interior:
        opts = free_neighbours(p, taken)
        if not opts:
            continue
        length = rng.choice([1, 2, 2, 3])
        chain = chain_from(rng, rng.choice(opts), taken, length)
        if not chain:
            continue
        prev = p
        for cell in chain:
            open_edge(masks, prev, cell)
            taken.add(cell)
            prev = cell
        return True
    return False


def op_fork(rng, masks, path, taken):
    interior = path[1:-1]
    rng.shuffle(interior)
    for p in interior:
        opts = free_neighbours(p, taken)
        if not opts:
            continue
        stem = chain_from(rng, rng.choice(opts), taken, rng.choice([1, 2]))
        if not stem:
            continue
        local = set(taken) | set(stem)
        tips = free_neighbours(stem[-1], local)
        if len(tips) < 2:
            continue
        rng.shuffle(tips)
        a, b = tips[0], tips[1]
        prev = p
        for cell in stem:
            open_edge(masks, prev, cell)
            prev = cell
        open_edge(masks, stem[-1], a)
        open_edge(masks, stem[-1], b)
        taken.update(stem)
        taken.update([a, b])
        return True
    return False


def op_loop(rng, masks, path, taken):
    for _ in range(40):
        i = rng.randint(1, len(path) - 5)
        j = rng.randint(i + 3, len(path) - 2)
        p, q = path[i], path[j]
        starts = free_neighbours(p, taken)
        ends = set(free_neighbours(q, taken))
        if not starts or not ends:
            continue
        s = rng.choice(starts)
        # random walk through free cells until it touches a cell next to q
        chain = [s]
        local = set(taken) | {s}
        ok = False
        for _step in range(7):
            if chain[-1] in ends:
                ok = True
                break
            opts = free_neighbours(chain[-1], local)
            if not opts:
                break
            # bias toward q
            opts.sort(key=lambda n: manhattan(n, q) + rng.random() * 1.5)
            n = opts[0]
            chain.append(n)
            local.add(n)
        if not ok and chain[-1] in ends:
            ok = True
        if not ok:
            continue
        if len(chain) + 1 <= (j - i):  # a loop must be longer than the path between its ends
            continue
        prev = p
        for cell in chain:
            open_edge(masks, prev, cell)
            prev = cell
        open_edge(masks, prev, q)
        taken.update(chain)
        return True
    return False


def candidate(rng):
    sc = rng.randrange(N)
    ec = rng.randrange(N)
    if abs(sc - ec) % 2 == 0:
        return None
    start, exit_ = (sc, 0), (ec, N - 1)
    path = random_path(rng, start, exit_, STEPS)
    if path is None:
        return None
    masks = empty_masks()
    for a, b in zip(path, path[1:]):
        open_edge(masks, a, b)
    taken = set(path)
    for _ in range(rng.choice([1, 2, 2, 3])):
        if not op_loop(rng, masks, path, taken):
            return None
    for _ in range(rng.choice([0, 1, 1])):
        if not op_fork(rng, masks, path, taken):
            return None
    for _ in range(rng.choice([0, 1, 2, 3])):
        if not op_spur(rng, masks, path, taken):
            return None
    return masks, start, exit_


# --------------------------------------------------------------------------- ranking
def transforms():
    n = N - 1
    return [
        lambda c, r: (c, r), lambda c, r: (n - c, r), lambda c, r: (c, n - r), lambda c, r: (n - c, n - r),
        lambda c, r: (r, c), lambda c, r: (n - r, c), lambda c, r: (r, n - c), lambda c, r: (n - r, n - c),
    ]


def jaccard(a, b):
    return len(a & b) / max(1, len(a | b))


def similarity(la, lb):
    """Highest overlap of open-edge sets and of shortest-path cells over all 8 symmetries (0 = unrelated, 1 = identical)."""
    ea, eb = edges_of(la["masks"]), edges_of(lb["masks"])
    pa = set(la["m"]["path"])
    best_e = best_p = 0.0
    for g in transforms():
        eg = {frozenset(g(*cell) for cell in e) for e in eb}
        pg = {g(*cell) for cell in lb["m"]["path"]}
        best_e = max(best_e, jaccard(ea, eg))
        best_p = max(best_p, jaccard(pa, pg))
    return best_e, best_p


def feature_distance(ma, mb):
    ja = [j["index"] for j in ma["junctions"]]
    jb = [j["index"] for j in mb["junctions"]]
    junction_gap = sum(abs(x - y) for x, y in zip(ja, jb)) / len(ja)
    return (abs(ma["turns"] - mb["turns"]) / 4.0
            + abs(ma["longest_straight"] - mb["longest_straight"]) / 2.0
            + abs(len(ma["misleading"]) - len(mb["misleading"])) / 1.0
            + junction_gap / 3.0
            + abs(ma["used"] - mb["used"]) / 6.0
            + abs(ma["loops"] - mb["loops"]) / 1.0
            + abs(ma["branch_depth"] - mb["branch_depth"]) / 2.0
            + abs((ma["left"] - ma["right"]) - (mb["left"] - mb["right"])) / 4.0)


def pair_score(la, lb):
    se, sp = similarity(la, lb)
    d = feature_distance(la["m"], lb["m"])
    return (1.0 - se) * 1.5 + (1.0 - sp) * 1.5 - d, {"edge_overlap": se, "path_overlap": sp, "difficulty_gap": d}


# --------------------------------------------------------------------------- json
def to_json(name, masks, start, exit_, m):
    cells = [masks[c][r] for r in range(N) for c in range(N)]
    return {
        "name": name, "grid": N, "cellSize": CELL,
        "start": {"c": start[0], "r": start[1], "side": "S"},
        "exit": {"c": exit_[0], "r": exit_[1], "side": "N"},
        "cells": cells,
        "path": [{"c": c, "r": r} for c, r in m["path"]],
        "shortestSteps": m["steps"], "shortestMeters": m["steps"] * CELL,
        "junctions": [{"c": j["cell"][0], "r": j["cell"][1], "index": j["index"], "correct": j["correct"],
                       "wrong": j["wrong"], "misleading": j["misleading"]} for j in m["junctions"]],
        "deadEnds": [{"c": c, "r": r} for c, r in m["dead_ends"]],
        "metrics": {"turns": m["turns"], "longestStraight": m["longest_straight"], "left": m["left"], "right": m["right"],
                    "loops": m["loops"], "usedCells": m["used"], "branchDepth": m["branch_depth"],
                    "misleading": len(m["misleading"])},
    }


def from_json(data):
    masks = empty_masks()
    for i, v in enumerate(data["cells"]):
        masks[i % N][i // N] = v
    start = (data["start"]["c"], data["start"]["r"])
    exit_ = (data["exit"]["c"], data["exit"]["r"])
    return masks, start, exit_


def render(masks, start, exit_, path):
    """ASCII picture: 2 characters per cell width."""
    on_path = set(path)
    lines = []
    for r in range(N - 1, -1, -1):
        top = ""
        mid = ""
        for c in range(N):
            m = masks[c][r]
            top += "+" + ("  " if m & BIT["N"] else "--")
            ch = "S" if (c, r) == start else "E" if (c, r) == exit_ else ("o" if (c, r) in on_path else ("." if m else "#"))
            mid += ("  " if m & BIT["W"] else "|") + ch
        lines.append(top + "+")
        lines.append(mid + ("  " if masks[N - 1][r] & BIT["E"] else "|"))
    lines.append("+--" * N + "+")
    return "\n".join(lines)


# --------------------------------------------------------------------------- commands
def cmd_generate(args):
    rng = random.Random(args.seed)
    found = []
    seen = set()
    for t in range(args.trials):
        cand = candidate(rng)
        if cand is None:
            continue
        masks, start, exit_ = cand
        m = evaluate(masks, start, exit_)
        if not meets_spec(m):
            continue
        key = tuple(masks[c][r] for c in range(N) for r in range(N))
        if key in seen:
            continue
        seen.add(key)
        found.append({"masks": masks, "start": start, "exit": exit_, "m": m})
        if len(found) >= args.keep:
            break
    print(f"{len(found)} valid layouts after {t + 1} trials (seed {args.seed})")
    if len(found) < 2:
        print("not enough valid layouts; raise --trials", file=sys.stderr)
        return 1
    ranked = []
    for i in range(len(found)):
        for j in range(i + 1, len(found)):
            s, det = pair_score(found[i], found[j])
            ranked.append((s, i, j, det))
    ranked.sort(key=lambda x: -x[0])
    top = ranked[:10]
    best = top[0]
    la, lb = found[best[1]], found[best[2]]
    out = args.out
    os.makedirs(out, exist_ok=True)
    for name, lay in (("A", la), ("B", lb)):
        with open(os.path.join(out, f"MazeLayout{name}.json"), "w") as fh:
            json.dump(to_json(name, lay["masks"], lay["start"], lay["exit"], lay["m"]), fh, indent=1)
    report = [f"# Maze layout report", "", f"Seed {args.seed}, {len(found)} valid layouts, {len(ranked)} pairs ranked.", "",
              "Constraints: 6 x 6 cells of 2 m, unique shortest path 18 steps (36 m), 6 junctions on it, 3 dead ends, at least 2 misleading junctions.", "",
              "## Top 10 pairs", "", "| rank | score | edge overlap (best symmetry) | path overlap | difficulty gap |", "|---|---|---|---|---|"]
    for k, (s, i, j, det) in enumerate(top, 1):
        report.append(f"| {k} | {s:.3f} | {det['edge_overlap']:.2f} | {det['path_overlap']:.2f} | {det['difficulty_gap']:.2f} |")
    report += ["", "Pair 1 is used as Layout A and Layout B."]
    for name, lay in (("A", la), ("B", lb)):
        m = lay["m"]
        report += ["", f"## Layout {name}", "", f"start {lay['start']} exit {lay['exit']}, used cells {m['used']}, turns {m['turns']}, longest straight {m['longest_straight']}, loops {m['loops']}, branch depth {m['branch_depth']}",
                   f"junctions: " + ", ".join(f"{j['cell']}@{j['index']} correct {j['correct']} wrong {j['wrong']}{' MISLEADING' if j['misleading'] else ''}" for j in m["junctions"]),
                   f"dead ends: {m['dead_ends']}", "", "```", render(lay["masks"], lay["start"], lay["exit"], m["path"]), "```"]
    rep_path = args.report
    os.makedirs(os.path.dirname(rep_path), exist_ok=True)
    with open(rep_path, "w") as fh:
        fh.write("\n".join(report) + "\n")
    print(f"wrote MazeLayoutA.json, MazeLayoutB.json to {out} and {rep_path}")
    print(render(la["masks"], la["start"], la["exit"], la["m"]["path"]))
    print()
    print(render(lb["masks"], lb["start"], lb["exit"], lb["m"]["path"]))
    return 0


def verify_file(path):
    with open(path) as fh:
        data = json.load(fh)
    masks, start, exit_ = from_json(data)
    m = evaluate(masks, start, exit_)
    problems = []
    if m is None:
        problems.append("structurally invalid (exit unreachable or isolated cells)")
    else:
        if m["steps"] != STEPS:
            problems.append(f"shortest path is {m['steps']} steps, expected {STEPS}")
        if not m["unique"]:
            problems.append("shortest path is not unique")
        if len(m["junctions"]) != JUNCTIONS:
            problems.append(f"{len(m['junctions'])} junctions on the shortest path, expected {JUNCTIONS}")
        if len(m["dead_ends"]) != DEAD_ENDS:
            problems.append(f"{len(m['dead_ends'])} dead ends, expected {DEAD_ENDS}")
        if len(m["misleading"]) < MIN_MISLEADING:
            problems.append(f"{len(m['misleading'])} misleading junctions, expected at least {MIN_MISLEADING}")
        stored = [(p["c"], p["r"]) for p in data.get("path", [])]
        if stored != m["path"]:
            problems.append("stored path does not match the computed shortest path")
        if abs(data.get("shortestMeters", 0) - m["steps"] * CELL) > 0.01:
            problems.append("stored shortestMeters is wrong")
    return problems, m


def cmd_verify(args):
    status = 0
    for path in args.files:
        problems, m = verify_file(path)
        if problems:
            status = 1
            print(f"FAIL {path}")
            for p in problems:
                print("  - " + p)
        else:
            print(f"PASS {path}: {m['steps']} steps = {m['steps'] * CELL:.0f} m, {len(m['junctions'])} junctions, "
                  f"{len(m['dead_ends'])} dead ends, {len(m['misleading'])} misleading, {m['loops']} loops")
    return status


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("generate")
    g.add_argument("--seed", type=int, default=20260415)
    g.add_argument("--trials", type=int, default=400000)
    g.add_argument("--keep", type=int, default=150)
    g.add_argument("--out", default=os.path.join(root, "Assets", "_Project", "Data"))
    g.add_argument("--report", default=os.path.join(root, "Assets", "_Project", "Docs", "Reports", "maze_layout_report.md"))
    v = sub.add_parser("verify")
    v.add_argument("files", nargs="+")
    args = ap.parse_args()
    sys.exit(cmd_generate(args) if args.cmd == "generate" else cmd_verify(args))


if __name__ == "__main__":
    main()
