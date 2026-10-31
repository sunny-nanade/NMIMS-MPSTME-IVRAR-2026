# Group 14 Assets — Faculty Scaffold vs Implemented Unity Assets

The professor-provided root-level `Assets/index.html` is an **A-Frame classroom starter**. Root-level `Assets/Scripts/HighLatencyNetworkSimulator.cs` and `PredictiveGhostRoverManager.cs` are **faculty scaffolds, not validated components of the submitted Phase 1 rover scene**. Their presence must not be used as proof of a completed predictive ghost-avatar experiment.

The actual working simulation assets are already present in:

- `../Phase1/Unity/Assets/` (models/materials/textures, rover `.glb`, scenes, scripts and Unity `.meta` files)
- `../Phase1/Unity/Assets/StreamingAssets/` (route CSVs and v1.5f scene metadata)
- `../Phase1/Python/assets/` (six-wheel PyBullet surrogate URDF)
- `../Phase1/Python/unity_hirise_export/` (heightmaps and Unity alignment metadata)

These paths are relative to the **Group 14 root**, not this file. Do not duplicate gigabytes of Unity/terrain/model binaries in the faculty starter `Assets/` folder. If the faculty requires the root `Assets/index.html` to be a runnable original assignment, that component remains **Not Implemented in Phase 1** and requires a separate approved scope/demonstration.

**Redistribution caution:** confirm licensing for third-party PolyOne assets and Mars rover `.glb` before publishing the public repository.
