# Phase-2 rover asset note

`urdf/perseverance_surrogate.urdf` is a **simplified research physics surrogate**, not NASA flight CAD.

It is designed specifically for fast PyBullet RL training and includes:

- six independently driven wheels;
- front and rear steering joints;
- left/right rocker joints;
- left/right bogie joints;
- 0.525 m wheel diameter;
- Perseverance-like body / mast proportions.

Reference geometry / configuration:

- NASA Mars 2020 Perseverance Rover 3D resource: https://science.nasa.gov/3d-resources/mars-2020-perseverance-rover/
- NASA rover components / wheels / rocker-bogie description: https://science.nasa.gov/mission/mars-2020-perseverance/rover-components/

The official NASA GLB should be used later as the high-quality **Unity visual mesh**. It is intentionally not used as the PyBullet collision model because a high-detail render mesh is inappropriate for millions of RL physics steps.
