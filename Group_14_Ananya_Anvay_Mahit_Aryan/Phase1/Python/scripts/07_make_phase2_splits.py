import argparse
import shutil
from pathlib import Path

import numpy as np
import pandas as pd

from mars_rover.terrain import (
    generate_procedural,
    load_geotiff_window,
    load_terrain,
    save_terrain,
    scan_geotiff_difficulty_windows,
    terrain_difficulty_metrics,
)


def difficulty_quota(count: int):
    base = count // 3
    q = {"easy": base, "medium": base, "hard": base}
    remainder = count - 3 * base
    # Extra samples favor medium/hard rather than making an already-easy curriculum easier.
    order = ["hard", "medium", "easy"]
    for i in range(remainder):
        q[order[i]] += 1
    return q


def pick_unique(pool, n, used, rng):
    candidates = [r for r in pool if (r["center_x"], r["center_y"]) not in used]
    if len(candidates) < n:
        raise RuntimeError(f"Need {n} unique windows but only {len(candidates)} remain")
    idx = rng.choice(len(candidates), size=n, replace=False)
    selected = [candidates[int(i)] for i in np.atleast_1d(idx)]
    for r in selected:
        used.add((r["center_x"], r["center_y"]))
    return selected


def make_preview(root: Path, manifest: pd.DataFrame):
    import matplotlib.pyplot as plt

    rows = []
    for source in ["procedural", "HiRISE_Jezero"]:
        for difficulty in ["easy", "medium", "hard"]:
            m = manifest[(manifest.source == source) & (manifest.difficulty == difficulty)]
            if len(m):
                rows.append((source, difficulty, m.iloc[0]))
    if not rows:
        return
    fig, axes = plt.subplots(2, 3, figsize=(15, 9), constrained_layout=True)
    axes = np.asarray(axes).ravel()
    for ax in axes:
        ax.axis("off")
    for ax, (source, difficulty, rec) in zip(axes, rows):
        t = load_terrain(root / rec["split"] / rec["file"])
        ax.imshow(t["height"], origin="lower", cmap="terrain")
        if t["obstacle"].any():
            mask = np.ma.masked_where(~t["obstacle"], t["obstacle"])
            ax.imshow(mask, origin="lower", cmap="gray_r", alpha=0.45)
        ax.set_title(f"{source} — {difficulty}\nscore={rec['difficulty_score']:.1f}")
        ax.axis("off")
    out = Path("outputs/figures/phase2_curriculum_preview.png")
    out.parent.mkdir(parents=True, exist_ok=True)
    fig.suptitle("Phase-2 terrain curriculum: easy → medium → hard", fontsize=16)
    fig.savefig(out, dpi=160)
    plt.close(fig)
    print("Saved", out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="data/phase2")
    ap.add_argument("--hirise", default="data/raw/jezero_dtm.tif")
    ap.add_argument("--procedural-train", type=int, default=36)
    ap.add_argument("--procedural-val", type=int, default=8)
    ap.add_argument("--procedural-test", type=int, default=8)
    ap.add_argument("--real-train", type=int, default=8)
    ap.add_argument("--real-val", type=int, default=2)
    ap.add_argument("--real-test", type=int, default=2)
    ap.add_argument("--size", type=int, default=129)
    ap.add_argument("--crop-px", type=int, default=129)
    ap.add_argument("--candidate-windows", type=int, default=240,
                    help="HiRISE windows scanned/ranked before easy/medium/hard selection")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--no-preview", action="store_true")
    args = ap.parse_args()

    root = Path(args.out)
    if root.exists() and args.force:
        shutil.rmtree(root)
    for split in ["train", "val", "test"]:
        (root / split).mkdir(parents=True, exist_ok=True)

    manifest = []
    counts = {"train": args.procedural_train, "val": args.procedural_val, "test": args.procedural_test}
    seed_base = {"train": 1000, "val": 3000, "test": 5000}

    print("Generating procedural easy/medium/hard curriculum...")
    for split, count in counts.items():
        quota = difficulty_quota(count)
        serial = 0
        for difficulty in ["easy", "medium", "hard"]:
            for _ in range(quota[difficulty]):
                seed = seed_base[split] + serial
                serial += 1
                t = generate_procedural(
                    size=args.size,
                    meters_per_cell=1.0,
                    seed=seed,
                    crater_count=7,
                    boulder_count=30,
                    difficulty=difficulty,
                )
                t["difficulty"] = difficulty
                metrics = terrain_difficulty_metrics(t["height"], t["meters_per_cell"], t["obstacle"])
                filename = f"procedural_{difficulty}_{seed}.npz"
                save_terrain(root / split / filename, t)
                manifest.append({"split": split, "file": filename, "source": "procedural",
                                 "difficulty": difficulty, "seed": seed, **metrics})

    hirise = Path(args.hirise)
    if hirise.exists():
        print(f"Scanning {args.candidate_windows} HiRISE candidates for terrain difficulty...")
        ranked = scan_geotiff_difficulty_windows(
            hirise,
            crop_size_px=args.crop_px,
            candidate_count=args.candidate_windows,
            seed=777,
        )
        pools = {d: [r for r in ranked if r["difficulty"] == d] for d in ["easy", "medium", "hard"]}
        print("HiRISE candidate ranges:")
        for d in ["easy", "medium", "hard"]:
            scores = [r["difficulty_score"] for r in pools[d]]
            print(f"  {d:6s}: n={len(scores):3d} score {min(scores):.1f}..{max(scores):.1f}")

        rng = np.random.default_rng(778)
        used = set()
        specifications = [
            ("train", args.real_train, 7000),
            ("val", args.real_val, 8000),
            ("test", args.real_test, 9000),
        ]
        for split, count, seed0 in specifications:
            quota = difficulty_quota(count)
            serial = 0
            for difficulty in ["easy", "medium", "hard"]:
                chosen = pick_unique(pools[difficulty], quota[difficulty], used, rng)
                for rec in chosen:
                    # Harder tiles also contain a somewhat denser local boulder field.
                    boulders = {"easy": 18, "medium": 26, "hard": 34}[difficulty]
                    t = load_geotiff_window(
                        hirise,
                        center_xy=(rec["center_x"], rec["center_y"]),
                        crop_size_px=args.crop_px,
                        out_size=args.size,
                        seed=seed0 + serial,
                        boulder_count=boulders,
                    )
                    t["difficulty"] = difficulty
                    metrics = terrain_difficulty_metrics(t["height"], t["meters_per_cell"], t["obstacle"])
                    filename = f"jezero_{difficulty}_{split}_{serial:02d}.npz"
                    save_terrain(root / split / filename, t)
                    manifest.append({
                        "split": split, "file": filename, "source": "HiRISE_Jezero",
                        "difficulty": difficulty, "seed": seed0 + serial,
                        "center_x": rec["center_x"], "center_y": rec["center_y"],
                        "rank_score_before_boulders": rec["difficulty_score"], **metrics,
                    })
                    serial += 1
        print(f"Included difficulty-ranked, held-out HiRISE windows from {hirise}")
    else:
        print(f"HiRISE GeoTIFF not found at {hirise}; generated procedural curriculum only.")

    df = pd.DataFrame(manifest)
    df.to_csv(root / "manifest.csv", index=False)
    for split in ["train", "val", "test"]:
        print("\n", split.upper(), len(list((root / split).glob("*.npz"))), "terrain tiles")
        print(df[df.split == split].groupby(["source", "difficulty"]).size().to_string())
    print("\nSaved Phase-2 splits under", root)
    print("Manifest:", root / "manifest.csv")
    if not args.no_preview:
        make_preview(root, df)


if __name__ == "__main__":
    main()
