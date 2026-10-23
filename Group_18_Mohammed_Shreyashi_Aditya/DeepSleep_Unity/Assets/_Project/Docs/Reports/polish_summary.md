# Polish summary (Final-final)

Game logic, stage order, DECISIONS values, telemetry fields and the study manipulation were not changed. Nothing was committed (owner's instruction, 2026-10-19); all changes are in the project files.

## What changed (visual)
- **Lighting.** All sections share one scene at the same coordinates, so baked lightmaps and probes are not possible. Instead: one directional key light whose colour and angle are set per section (`SectionLook`), at most one pulsing red light per section, soft additive light-pool quads under lamps, custom dark reflection cubemaps, and a flat ambient colour per section. No shadows.
- **Per-section grade.** `SectionLook` presets for Setup (cool), Medical Bay (dim blue, red pulses) and Hallway (warm). The smoke room keeps the exact look it was calibrated with (fog, ambient, key light are captured and restored).
- **Decor.** Scorch marks, streaks, hanging cables, debris, hazard frame around the service-door panel, server-rack details, LED strips, plaque board (all code-built meshes and plain materials; see KNOWN_ISSUES for the primitive list).
- **Post-processing.** One light Volume per section (neutral tonemapping, subtle bloom), off in the smoke room.
- **Sparkle fix.** Specular highlights are off and smoothness is capped on our materials (`MeshFactory.Lit`), which removed the white-dot flicker. A faint lattice of bright dots can remain on smoke-room walls (pack mesh and material); documented.
- **UI.** Common panel style (`UiStyle`), larger setup panel (1.7 x 1.15 m), poster builder for the beacon and coolant posters.

## Quest settings
- Android quality level 0 uses `Mobile_RPAsset`: MSAA 4, HDR off, no depth or opaque texture, render scale 1.0.
- Android texture overrides set on the pack textures (smaller max size, compressed) to keep memory down.
- Fog instead of particles in the smoke room; at most 24 emissive decals on the trail.

## Render stats (Editor Game view; compare before and after, not Quest numbers)
| viewpoint set | draw calls before | draw calls after | triangles before | triangles after | realtime lights before | after |
|---|---|---|---|---|---|---|
| Setup alcove | 73 | 75 | 137,891 | 137,895 | 23 | 25 |
| Medical Bay (beacon game) | 184 | 275 | 142,490 | 145,686 | 91 | 104 |
| Medical Bay, rack looking north | 94 | 148 | 71,481 | 72,777 | 91 | 104 |
| Hallway | 53 | 76 | 70,279 | 71,533 | 8 | 20 |
| Smoke room | 127 | 127 | 81,207 | 80,815 | 5 | 5 |

(The "realtime lights" columns in `polish_stats_*.md` count lights per section scan; shadowed lights are 0 everywhere.) The bay and hallway cost more draw calls after the decor was added; the smoke room, which is the frame-rate-critical part, is unchanged. **Not measured on a Quest** (see HEADSET_DAY_CHECKLIST.md).

## Shader check
`DeepSleep > Tools > Scan Missing Shaders`: 85 materials, 536 renderer slots scanned; 0 bad materials, 0 bad slots, 0 runtime shaders missing from Always Included Shaders (report: `missing_shaders.md`). The SDK controller meshes render magenta in the Editor only; their materials are assigned on device.

## Screenshots
Six viewpoints per section before and after: `Docs/Screens/polish/before` and `.../after` (plus `speckle*` for the sparkle fix).
