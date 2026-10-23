# Check report: phase5

Date: 2026-10-15 22:16:36
Result: 41 passed, 0 failed. Console during run: 0 errors, 0 warnings.

- [PASS] CoolantRack and QuestionnaireUI are in SCN_MedicalBay
- [PASS] Rack waits until both blocks and questionnaires are done
- [PASS] Block 1 questionnaire opens
- [PASS] R140 plays when the first questionnaire opens
- [PASS] Six TLX answers and the raw mean are logged for block 1
- [PASS] Block 1 raw TLX mean is 23.3 (answers were about 20, 10, 30, 15, 40, 25)
- [PASS] Finishing the first questionnaire advances to Block2
- [PASS] Block 2 questionnaire opens
- [PASS] Block 2 TLX logged
- [PASS] R150 plays after the second questionnaire (at the rack)
- [PASS] Rack becomes active after Block 2 and its questionnaire
- [PASS] Three cables and six ports
- [PASS] Red to port 2, blue to port 5, yellow to port 3
- [PASS] Blue cable into port 1 is a wrong plug
- [PASS] Wrong plug sound is raised
- [PASS] Wrong plug starts the 3 s lockout
- [PASS] A correct plug during the lockout is rejected
- [PASS] The wrongly plugged cable goes back to the table
- [PASS] Lockout ends and nothing else happened (no fail state)
- [PASS] Red cable seats in port 2
- [PASS] Blue cable seats in port 5
- [PASS] Two cables are not enough
- [PASS] Yellow cable in port 3 solves the rack
- [PASS] Stage advances to CoolantRouted
- [PASS] R153 and the rack-solved sound play
- [PASS] Lever appears at 1.10 m
- [PASS] Pulling the lever starts the blast doors and advances to Escaped
- [PASS] H007, H008, R160, lever and blast door sounds
- [PASS] Coolant time and wrong plugs are logged
- [PASS] SCN_HallwayDebrief loads after the blast doors
- [PASS] Camera height unchanged in the hallway
- [PASS] SUS opens in the hallway and the stage is Questionnaire
- [PASS] Plaque text matches the specification: MISSION LOG. Aethelgard, Deck 4 evacuation. Participant 4. Block 1 1:35. Block 2 2:10.
- SUS answers recorded: 5,1,5,1,5,1,5,1,5,1
- [PASS] SUS score is 100.0 for the all-best answers
- [PASS] Preference question follows the SUS
- [PASS] Preference recorded and the debrief finishes
- [PASS] R161 plays at the end of the debrief
- [PASS] SUS score is logged in both block rows
- [PASS] Debrief record holds the preference and the plaque
- [PASS] deepsleep_telemetry.csv was written
- [PASS] Session JSON was exported
