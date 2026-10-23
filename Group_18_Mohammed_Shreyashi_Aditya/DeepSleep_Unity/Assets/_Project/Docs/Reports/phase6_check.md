# Check report: phase6

Date: 2026-10-15 22:19:31
Result: 18 passed, 0 failed. Console during run: 0 errors, 0 warnings.

- [PASS] NarrativeVoiceDirector, HalcyonDirector and SfxDirector exist (created by Bootstrap)
- [PASS] All 65 manifest IDs resolve to non-empty clips (0 missing, 0 empty)
- [PASS] R010 (Rose) and H001 (HALCYON) play on arrival
- [PASS] Rose and HALCYON never speak over each other
- [PASS] Both voices went through their own director
- [PASS] Ambience ducks (Ducked snapshot) while Rose speaks
- [PASS] Ambience returns to Normal when the line ends
- [PASS] Queued lines play in order, R201 is ignored and a cancelled line is dropped (R030,R031)
- [PASS] H004 plays once within 30 s (1)
- [PASS] Key press plays S040 as a 3D sound
- [PASS] Key press pitch rises across the nine keys (0.79 to 1.26)
- [PASS] SFX events map to their clips (10/10)
- [PASS] UI sound S091 is 2D
- [PASS] Teleport plays S090
- [PASS] SCN_MedicalBay loops A001
- [PASS] Smoke room plays A004 + A006, mono and 2D (identical in both ears)
- [PASS] Back in the bay only A001 remains (the smoke ambience faded out and was removed)
- [PASS] Hallway plays A001 + A005
