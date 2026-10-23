# Check report: audit_smoke

Date: 2026-10-16 20:48:34
Result: 19 passed, 0 failed. Console during run: 0 errors, 0 warnings.

- [medical bay before the smoke room] active scene SCN_MedicalBay; fog=False mode=ExponentialSquared density=0.010 colour=RGBA(0.500, 0.500, 0.500, 1.000) ambient=RGBA(0.070, 0.070, 0.090, 1.000) skybox=Default-Skybox; visibility (2 % transmittance) n/a
- [PASS] Fog is off in SCN_MedicalBay before the smoke room
- [smoke room, block 1] active scene SCN_SmokeRoom; fog=True mode=ExponentialSquared density=0.989 colour=RGBA(0.100, 0.100, 0.110, 1.000) ambient=RGBA(0.025, 0.025, 0.030, 1.000) skybox=none; visibility (2 % transmittance) 2.00 m
- [PASS] Block 1: active scene is SCN_SmokeRoom, fog on, ExponentialSquared, density 0.989, visibility 2.00 m
- Camera: farClip 1000 m, clearFlags SolidColor, background RGBA(0.100, 0.100, 0.110, 1.000), renderPostProcessing False, volumeLayerMask 1
- [PASS] Far clip plane (1000 m) does not cut the fog short
- [PASS] No URP Volume exists in any loaded scene (0); nothing can override or hide the fog
- [PASS] Post-processing has no volume to apply
- Active lights while in the smoke room: HelmetLamp@SCN_MedicalBay
- [PASS] No particle systems in the smoke scene (0); smoke is fog only
- [PASS] No exit signs or arrows in the smoke scene
- [PASS] No lights are placed in the smoke scene (0); the only light is the helmet lamp
- [PASS] No emissive materials (floor strips, panels) in the smoke scene (0)
- [PASS] Helmet lamp: spot light, range 1.5 m, child of the head
- [PASS] The hidden Medical Bay lights are off while in the smoke room (0 active)
- Longest straight run found: 10.9 m toward (0.00, 0.00, -1.00)
- [PASS] A corridor with at least 8.5 m to a wall exists for the distance shots (10.9 m)
- Reference (wall at 8 m, lamp on): centre luminance 0.1025; fog colour luminance 0.1007
- wall at 8.0 m: contrast against fog 0.0000 with the lamp, 0.0000 without the lamp (visible if above 0.02)
- wall at 6.0 m: contrast against fog 0.0000 with the lamp, 0.0000 without the lamp (visible if above 0.02)
- wall at 4.0 m: contrast against fog 0.0000 with the lamp, 0.0000 without the lamp (visible if above 0.02)
- wall at 3.0 m: contrast against fog 0.0000 with the lamp, 0.0000 without the lamp (visible if above 0.02)
- wall at 2.0 m: contrast against fog 0.0019 with the lamp, 0.0019 without the lamp (visible if above 0.02)
- wall at 1.5 m: contrast against fog 0.0025 with the lamp, 0.0029 without the lamp (visible if above 0.02)
- wall at 1.0 m: contrast against fog 0.2741 with the lamp, 0.0063 without the lamp (visible if above 0.02)
- bright wall at 8.0 m (line of sight 8.03 m): fogged 0.103, unfogged 0.348, fog 0.007 -> transmittance 0.000, theory 0.000
- bright wall at 4.0 m (line of sight 4.05 m): fogged 0.103, unfogged 0.276, fog 0.007 -> transmittance 0.000, theory 0.000
- bright wall at 3.0 m (line of sight 3.07 m): fogged 0.103, unfogged 0.284, fog 0.007 -> transmittance 0.000, theory 0.000
- bright wall at 2.5 m (line of sight 2.58 m): fogged 0.103, unfogged 0.287, fog 0.007 -> transmittance 0.002, theory 0.001
- bright wall at 2.0 m (line of sight 2.10 m): fogged 0.115, unfogged 0.302, fog 0.007 -> transmittance 0.029, theory 0.013
- bright wall at 1.5 m (line of sight 1.63 m): fogged 0.141, unfogged 0.264, fog 0.007 -> transmittance 0.143, theory 0.073
- bright wall at 1.0 m (line of sight 1.19 m): fogged 0.180, unfogged 0.241, fog 0.007 -> transmittance 0.442, theory 0.249
- bright wall at 0.5 m (line of sight 0.82 m): fogged 0.228, unfogged 0.243, fog 0.007 -> transmittance 0.852, theory 0.518
- Measured visibility distance (transmittance 2 %, line of sight): 2.17 m; configured 2 m
- [PASS] Rendered fog gives a visibility of 2.17 m (target about 2 m)
- [PASS] Measured transmittance: 1 m 0.44, 3 m 0.000, 4 m 0.000: walls farther than 2 m are not visible
- Lamp effect on the wall (centre luminance): at 1 m 0.2804, at 1.5 m 0.0055, at 3 m 0.0000
- [PASS] The helmet lamp lights a wall at 1 m and has no effect at 3 m (range 1.5 m)
- [medical bay after block 1] active scene SCN_MedicalBay; fog=False mode=ExponentialSquared density=0.010 colour=RGBA(0.500, 0.500, 0.500, 1.000) ambient=RGBA(0.070, 0.070, 0.090, 1.000) skybox=Default-Skybox; visibility (2 % transmittance) n/a
- [PASS] Fog is off again in SCN_MedicalBay after block 1
- [PASS] Camera clear flags and background are restored after the smoke room (Skybox, RGBA(0.192, 0.302, 0.475, 0.020))
- [smoke room, block 2] active scene SCN_SmokeRoom; fog=True mode=ExponentialSquared density=0.989 colour=RGBA(0.100, 0.100, 0.110, 1.000) ambient=RGBA(0.025, 0.025, 0.030, 1.000) skybox=none; visibility (2 % transmittance) 2.00 m
- [PASS] Block 2: fog is back (density 0.989, visibility 2.00 m)
- [hallway] active scene SCN_HallwayDebrief; fog=False mode=ExponentialSquared density=0.010 colour=RGBA(0.500, 0.500, 0.500, 1.000) ambient=RGBA(0.070, 0.070, 0.090, 1.000) skybox=Default-Skybox; visibility (2 % transmittance) n/a
- [PASS] Fog is off in SCN_HallwayDebrief after the blocks
