# Acrophobia-Trainer

An automated bio-adaptive VR exposure therapy system which can dynamically modulate vertical environmental height based on real-time gaze avoidance and head tremor telemetry to facilitate gradual acrophobia desensitization.

---

## Telemetry Collection & Data Export Module

The project includes a comprehensive, high-frequency, zero-allocation telemetry pipeline designed specifically for clinical exposure therapy research on **Meta Quest 2 / Quest headsets** and local simulation.

### 1. Collected Quest 2 Telemetry Channels

Data is recorded at a configurable fixed rate (default **10 Hz**, up to 72 Hz) without hot-path garbage collection:

* **Headset Tracking & Dynamics**:
  * 3D Position (`head_x`, `head_y`, `head_z`), Height above ground (`height_above_ground_m`)
  * Head Linear Velocity (`head_linear_vel_mps`) & Linear Acceleration (`head_accel_mps2`)
  * Head Angular Velocity (`head_ang_vel_degps`) & Pitch angle (`head_pitch_deg`)
* **Bio-Adaptive Indicators**:
  * **Head Tremor Intensity** (`tremor_intensity` filtered at 8–12 Hz vestibular band)
  * **Gaze Avoidance Ratio** (`gaze_avoidance_ratio`) & Downward gaze duration (`downward_gaze_cumulative_s`)
  * **Postural Sway & Freezing** (`sway_velocity_mps`, `com_wander_m`, `is_postural_frozen`)
  * **Edge Proximity & Hesitation** (`distance_to_edge_m`, `is_near_edge`, `hesitation_index`)
* **Touch Controller Tracking & Inputs**:
  * Left & Right Controller 3D Positions and Velocities
  * Trigger & Grip analog axes (`0.0` to `1.0`)
  * Thumbstick 2D deflection vectors
  * Primary (A / X) and Secondary (B / Y) button states
* **Quest 2 System Diagnostics**:
  * App Framerate (`fps`)
  * Battery Level percentage (`battery_pct`)
  * User Proximity Sensor (`is_user_present`) & 6DoF Tracking status (`tracking_valid`)
* **Discrete Clinical Events**:
  * Subjective Units of Distress Scale (**SUDS**) ratings (0–10) with contextual timestamps
  * Tier transitions (height increases/plateaus)
  * Edge retreat events and safety recovery pauses

---

### 2. Export Formats for Analysis

Every session automatically packages three export artifacts into `Application.persistentDataPath/TelemetryExports/`:

1. **`session_<ID>_samples.csv`**: Dense, synchronized multi-modal time-series ready for Pandas, NumPy, R, and SPSS.
2. **`session_<ID>_events.csv`**: Timestamped discrete events (SUDS ratings, retreat triggers, tier changes).
3. **`session_<ID>_summary.json`**: Self-contained clinical session summary with participant metadata, hardware specifications, dwell times per height tier, peak/mean tremor, and initial vs. final SUDS delta.

---

### 3. Unity Editor Management Window

Open the manager directly in Unity:
**`Tools > Acrophobia > Telemetry & Export Manager`**

* **Session Browser**: View all recorded sessions, sample counts, and SUDS progression.
* **1-Click Explorer**: Open the local telemetry folder directly in Windows Explorer.
* **Export Bundles**: Copy all CSVs and JSON summaries to any target analysis folder.
* **Quest 2 ADB Bridge**: Pull telemetry directly from a USB-connected or AirLink Meta Quest 2 to your PC with one click (`adb pull`).
* **Python Pipeline Runner**: Trigger the statistical analysis script directly from the Unity Editor.

---

### 4. Running the Python Analysis Pipeline

You can analyze any exported session using the built-in script:

```bash
python Tools/TelemetryAnalysis/analyze_telemetry.py --samples "path/to/session_<ID>_samples.csv" --output "AnalysisResults"
```

This generates:
* Formatted clinical metrics table across all exposure tiers.
* `clinical_metrics_summary.json` with aggregate metrics.
* Tremor vs. Height Tier curves (with `matplotlib`).
