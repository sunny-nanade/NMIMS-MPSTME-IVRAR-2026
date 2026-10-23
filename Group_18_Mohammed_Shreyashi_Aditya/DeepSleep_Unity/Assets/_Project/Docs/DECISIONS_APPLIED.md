# DECISIONS APPLIED

Choices the agent made where the brief left them open. Dimensions are metres.

## Phase 2: scene layout and scale table (measured in the Phase 2 check)
| Item | Value |
|---|---|
| Camera height | 1.65 (EYE_HEIGHT, Floor tracking origin, never offset) |
| Medical Bay footprint | x -4..4, z -3..3 (about 8 x 6), ceiling 3.0 |
| Doors | 0.9 x 2.1 (main welded door west wall z=0, service door east wall z=0) |
| Bed, side table | 0.9 high (bed rescaled uniformly from 0.74) |
| Terminal | rescaled to 1.0 high (glb pre-rotated -90 about X so its long axis is vertical) |
| Keypad | centre 1.1 high on a 1.4 m Element pillar on the terminal's north side |
| Rack (server_2) | 1.6 x 2.45, north wall at x=2.5; CoreRoomSpawn (2.5,0,0.8) facing north |
| PlayerSpawn | (-2,0,0.2) facing east; oxygen readout on the west wall at 1.7 |
| Setup alcove | 6 x 6 x 3, panels 1.5 m ahead of the spawn at 1.45 |

Wall pieces are Cleen Wall2_3 slabs scaled horizontally to fill runs; lintels and door leaves are scaled slabs (Door2 leaf). Pivots of pack pieces are not at the centre, so everything is placed by bounds.

## Trail waypoints
Route (world x, z, metres), one polyline, no forks, from beside the terminal (0.5, -0.6) to the wall panel on the east wall (x about 3.7, opposite the main door):
(1.05, -0.6) -> (1.9, -1.2) -> (2.9, -0.9) -> (3.3, -0.2) -> (3.35, 0.0). Arrow decals every 0.3 m (11 decals, cap 24). Panel reveals when the head is within 1.1 m of it and slides 1.5 m south along the wall; service door handle at (3.8, 1.0, 0.3); pull 0.15 m toward the room opens it (door slides 0.95 m south).

## Phase 3 other choices
- Ear Check ring at (-1.2, 0, 0); beacons are placed at the head position and facing when the trials start; azimuth positive is to the right, elevation positive is up.
- Keypad faces west (the asset's front is local -z) on a 1.4 m pack pillar; numerals come from the model; the keypad display shows ---- / stars / LOCK / OK.
- Beacons are scaled to 0.4 m high (barrels are 1.24 m).

## Phase 4 maze choice
- Verifier seed 20260415, 445,189 trials, 120 valid layouts, 7,140 pairs. Pair score = (1 - best-symmetry edge overlap) * 1.5 + (1 - best-symmetry path overlap) * 1.5 - difficulty gap (turns, longest straight, misleading count, junction spacing, used cells, loops, branch depth, left/right balance). The top-ranked pair is A and B (edge overlap 0.49, path overlap 0.41, difficulty gap 0.28).
- Layout A: start (3,0), exit (4,5). Layout B: start (4,0), exit (5,5). Start door is in the south wall, exit door in the north wall. Rows grow north (+z), columns east (+x), grid origin at world (0,0,0).
- Junction = path cell (not start / exit) with 3 or more openings. Misleading = a wrong branch whose first cell is closer (Manhattan) to the exit than the correct next cell. Wrong turn = stepping from a shortest-path cell onto a cell that is not on it. Backtrack = stepping back into the cell just left. Efficiency = 36 m / travelled metres, at most 1.
- Visibility: fog density = sqrt(ln 50) / 2 m = 0.99 (2 % transmittance at 2 m).
- Rose block lines all come from one source at the exit door; engine rolloff is flat and the same distance gain (1.0 at 1 m to 0.4 at 20 m) is applied in code in both conditions.
- Head-turn latency: from each beacon call that starts with the head more than 45 degrees off the door, until the head is within 30 degrees of it (capped at 4 s).

## Phase 5 choices
- Rack panel on the south face of server_2 at x 2.25 / 2.5 / 2.75, y 1.5 / 1.28 (ports 1 to 3 top row, 4 to 6 bottom row), lever at (3.1, 1.1, 1.5) pulled 0.25 m down. Cables rest on a 0.9 m table at (1.1, 1.75). CoreRoomSpawn is (2.5, 0, 0.4) facing north.
- The cables are 0.5 m long (the model is 0.19 m). Plug tip snaps within 0.1 m of a port.
- TLX questions use the standard NASA-TLX wording; the performance scale runs Perfect to Failure. SUS uses the standard 10 items; score = sum(odd - 1, 5 - even) * 2.5, written to both block rows.
- Order: block end -> return at rack -> TLX (R140 after block 1) -> Block2 stage -> second block -> TLX (then R150, R151 10 s later) -> rack -> lever -> blast door -> hallway -> SUS -> preference -> plaque and export.

## Guidance and polish (2026-10-18, owner request)
- The owner asked for a hologram, which overrides the earlier "no hologram" project rule for this one use: GuideHud shows a 10 s orientation hologram (controls only, no story) when the player first wakes in the Medical Bay, once per session.
- GuideHud also shows a CURRENT GOAL card on every stage and sub-step change (8 s) and again after 25 s without progress. In the smoke blocks it shows once at the start and never as an idle nudge, so it cannot act as extra guidance between the spatial and stereo conditions.
- Look: StarSky skybox, dark reflection cubemap, calmer pack material specular, brighter flat ambient, emissive ceiling panels (see KNOWN_ISSUES).
