"""Generate a measured Phase 1 plot ONLY if a genuine benchmark CSV exists.

Faculty's original generator used synthetic teleoperation datasets and invented trial
statistics. This revised file preserves the expected filename but refuses fake data.
"""
from pathlib import Path
import argparse
from plot_real_phase1_benchmark import main as plot_benchmark

if __name__ == "__main__":
    # The plotting module owns the --csv CLI, including provenance checks.
    plot_benchmark()
