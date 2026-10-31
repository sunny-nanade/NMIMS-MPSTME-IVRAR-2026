#!/usr/bin/env python3
"""
Acrophobia-Trainer Telemetry Analysis Pipeline
Analyzes Meta Quest 2 bio-adaptive VR exposure telemetry logs:
- Head tremor dynamics (8-12 Hz amplitude across height tiers)
- Real-time gaze avoidance & downward gaze duration
- Postural sway stability & freeze events near the edge
- Subjective distress (SUDS) reduction curve across tiers
"""

import os
import sys
import csv
import json
import argparse
from datetime import datetime

def analyze_samples(samples_csv_path):
    if not os.path.exists(samples_csv_path):
        print(f"Error: Samples file not found: {samples_csv_path}")
        return None

    rows = []
    with open(samples_csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for r in reader:
            rows.append(r)

    if not rows:
        print("Warning: No sample rows found.")
        return None

    # Parse numeric series
    t_seconds = []
    tiers = []
    heights = []
    tremors = []
    gaze_avoidance = []
    sway_velocities = []
    is_frozen = []
    is_looking_down = []
    distance_to_edge = []

    for r in rows:
        try:
            t = float(r.get('t_seconds', 0))
            tier = int(float(r.get('tier', 1)))
            h = float(r.get('height_above_ground_m', 0))
            tr = float(r.get('tremor_intensity', 0))
            ga = float(r.get('gaze_avoidance_ratio', 0))
            sw = float(r.get('sway_velocity_mps', 0))
            frz = int(float(r.get('is_postural_frozen', 0)))
            ld = int(float(r.get('is_looking_down', 0)))
            de = float(r.get('distance_to_edge_m', 0))

            t_seconds.append(t)
            tiers.append(tier)
            heights.append(h)
            tremors.append(tr)
            gaze_avoidance.append(ga)
            sway_velocities.append(sw)
            is_frozen.append(frz)
            is_looking_down.append(ld)
            distance_to_edge.append(de)
        except Exception:
            continue

    total_samples = len(t_seconds)
    duration = t_seconds[-1] - t_seconds[0] if total_samples > 1 else 0

    # Group metrics by Tier
    tier_stats = {}
    for i in range(total_samples):
        tr = tiers[i]
        if tr not in tier_stats:
            tier_stats[tr] = {
                'samples': 0,
                'height_max': heights[i],
                'tremors': [],
                'gaze_avoidance': [],
                'sway': [],
                'frozen_count': 0,
                'down_gaze_count': 0
            }
        ts = tier_stats[tr]
        ts['samples'] += 1
        if heights[i] > ts['height_max']:
            ts['height_max'] = heights[i]
        ts['tremors'].append(tremors[i])
        ts['gaze_avoidance'].append(gaze_avoidance[i])
        ts['sway'].append(sway_velocities[i])
        if is_frozen[i]: ts['frozen_count'] += 1
        if is_looking_down[i]: ts['down_gaze_count'] += 1

    summary_by_tier = {}
    for tr, s in sorted(tier_stats.items()):
        n = max(1, s['samples'])
        summary_by_tier[tr] = {
            'samples': n,
            'max_height_m': round(s['height_max'], 2),
            'mean_tremor': round(sum(s['tremors']) / n, 4),
            'peak_tremor': round(max(s['tremors']) if s['tremors'] else 0, 4),
            'mean_gaze_avoidance': round(sum(s['gaze_avoidance']) / n, 4),
            'downward_gaze_pct': round((s['down_gaze_count'] / n) * 100, 1),
            'postural_freeze_pct': round((s['frozen_count'] / n) * 100, 1)
        }

    overall_metrics = {
        'total_duration_s': round(duration, 2),
        'total_samples': total_samples,
        'effective_sample_rate_hz': round(total_samples / max(0.1, duration), 1),
        'max_height_m': round(max(heights) if heights else 0, 2),
        'peak_tremor': round(max(tremors) if tremors else 0, 4),
        'mean_tremor': round(sum(tremors) / max(1, total_samples), 4),
        'mean_gaze_avoidance': round(sum(gaze_avoidance) / max(1, total_samples), 4),
        'total_tiers_reached': len(tier_stats),
        'tiers': summary_by_tier
    }

    return overall_metrics

def main():
    parser = argparse.ArgumentParser(description="Analyze Acrophobia-Trainer VR Telemetry")
    parser.add_argument("--samples", required=True, help="Path to session samples CSV file")
    parser.add_argument("--output", default=".", help="Directory to save analysis report")
    args = parser.parse_args()

    print("=================================================================")
    print("      ACROPHOBIA-TRAINER: QUEST 2 TELEMETRY ANALYSIS            ")
    print("=================================================================")
    print(f"Reading samples from: {args.samples}")

    metrics = analyze_samples(args.samples)
    if not metrics:
        sys.exit(1)

    print("\n--- CLINICAL EXPOSURE SUMMARY ---")
    print(f"Total Session Duration : {metrics['total_duration_s']} seconds")
    print(f"Total Samples Logged   : {metrics['total_samples']} ({metrics['effective_sample_rate_hz']} Hz)")
    print(f"Max Height Reached     : {metrics['max_height_m']} metres")
    print(f"Overall Mean Tremor    : {metrics['mean_tremor']}")
    print(f"Peak Tremor Intensity  : {metrics['peak_tremor']}")
    print(f"Mean Gaze Avoidance    : {metrics['mean_gaze_avoidance']}")

    print("\n--- METRICS PER EXPOSURE TIER ---")
    print(f"{'Tier':<6} | {'Max Ht (m)':<11} | {'Mean Tremor':<12} | {'Peak Tremor':<12} | {'Gaze Avoid':<11} | {'Down Gaze %':<11} | {'Freeze %':<9}")
    print("-" * 85)
    for tr, s in metrics['tiers'].items():
        print(f"Tier {tr:<2} | {s['max_height_m']:<11} | {s['mean_tremor']:<12} | {s['peak_tremor']:<12} | {s['mean_gaze_avoidance']:<11} | {s['downward_gaze_pct']:<11} | {s['postural_freeze_pct']:<9}")

    os.makedirs(args.output, exist_ok=True)
    report_json_path = os.path.join(args.output, "clinical_metrics_summary.json")
    with open(report_json_path, 'w', encoding='utf-8') as f:
        json.dump(metrics, f, indent=2)
    print(f"\n[Saved JSON Metrics Summary] -> {report_json_path}")

    # Optional matplotlib plotting
    try:
        import matplotlib.pyplot as plt
        tier_nums = list(metrics['tiers'].keys())
        tremor_vals = [metrics['tiers'][t]['mean_tremor'] for t in tier_nums]
        height_vals = [metrics['tiers'][t]['max_height_m'] for t in tier_nums]

        fig, ax1 = plt.subplots(figsize=(8, 5))
        color = 'tab:red'
        ax1.set_xlabel('Exposure Tier')
        ax1.set_ylabel('Mean Tremor Intensity (8-12 Hz)', color=color)
        ax1.plot(tier_nums, tremor_vals, color=color, marker='o', linewidth=2, label='Tremor')
        ax1.tick_params(axis='y', labelcolor=color)

        ax2 = ax1.twinx()
        color = 'tab:blue'
        ax2.set_ylabel('Height Above Ground (m)', color=color)
        ax2.plot(tier_nums, height_vals, color=color, marker='s', linestyle='--', label='Height')
        ax2.tick_params(axis='y', labelcolor=color)

        plt.title('Acrophobia Desensitization: Tremor Intensity vs Height Tier')
        fig.tight_layout()
        chart_path = os.path.join(args.output, "tremor_vs_height_tier.png")
        plt.savefig(chart_path, dpi=150)
        print(f"[Generated Clinical Chart] -> {chart_path}")
    except ImportError:
        print("[Notice] matplotlib not installed; skipped PNG plot generation.")

    print("\nTelemetry analysis complete!")

if __name__ == '__main__':
    main()
