# Check report: phase2

Date: 2026-10-17 00:12:58
Result: 31 passed, 0 failed. Console during run: 0 errors, 0 warnings.

- [PASS] SCN_Setup has the rig and SetupController
- [PASS] Temporary test area is gone
- [PASS] Camera height 1.650 m in SCN_Setup
- [PASS] Setup starts on the consent page
- DIAG src=Scripted hi=True hover=True hit=True Btn_Continue origin=(0.00, 1.35, 0.00) dir=(0.00, -0.15, 0.99) btn=(0.00, 1.13, 1.49) layer=9 col=True
- [PASS] Ray click on the button moves to the height page
- [PASS] No low-height warning at standing eye height
- [PASS] Audio check page
- [PASS] R001 played during the consent and height pages (it starts with the scene, not with a button)
- [PASS] R002 played when the tone check started, before the first tone
- [PASS] All four tones confirmed by facing + trigger (4); wrong-direction pull ignored
- [PASS] R003 played right after the fourth tone was confirmed
- [PASS] Comfort page reached
- [PASS] Volume slider set by ray (75)
- [PASS] SCN_MedicalBay loaded after Start
- [PASS] Stage is Awakening
- [PASS] Camera height 1.650 m in SCN_MedicalBay
- [PASS] R010 and H001 requested on arrival
- [PASS] Main door 0.90 x 2.10 m
- [PASS] Service door 0.90 x 2.10 m
- [PASS] Bed height 0.90 m
- [PASS] Table height 0.90 m
- [PASS] Ceiling at 3.00 m
- [PASS] Medical bay footprint 8.3 x 6.2 m
- [PASS] Keypad centre at 1.10 m
- [PASS] Terminal 1.00 m high
- [PASS] Oxygen counts down (19:53)
- [PASS] Emergency light pulses over a 2 s period (intensity 0.36 to 1.20)
- [PASS] R011 requested when near the tray
- [PASS] Teleport alone does not finish Awakening
- [PASS] Cup can be grabbed
- [PASS] Awakening completes (cup + teleport) and advances to EarCheck
- H002 timing (20 s) not waited in this run
