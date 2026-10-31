using System.Collections.Generic;

namespace RescueSim.Triage
{
    /// <summary>
    /// The fixed ten-casualty scenario. Identifiers C01..C10 never change between runs, so sessions are comparable.
    /// Findings are invented teaching data. The set covers every START branch and gives the same ground truth under
    /// both perfusion criteria (cap refill and radial pulse are set consistently).
    /// Ground truth: Green x3, Yellow x2, Red x4, Black x1.
    /// </summary>
    public static class TriageScenario
    {
        public const int CasualtyCount = 10;

        static VitalSigns V(bool walk, bool breathing, bool afterAirway, int rr, float refill, bool pulse, bool obeys, int hr, string obs) =>
            new VitalSigns { canWalk = walk, isBreathing = breathing, breathingAfterAirwayReposition = afterAirway, respiratoryRate = rr,
                capillaryRefillSeconds = refill, hasRadialPulse = pulse, followsCommands = obeys, heartRate = hr, observation = obs };

        public static List<CasualtyCase> Create() => new List<CasualtyCase>
        {
            new CasualtyCase("C01", V(true,  true,  false, 18, 1.2f, true,  true,  84,  "Minor burns on forearm, limping but walking."), "Walking wounded"),
            new CasualtyCase("C02", V(true,  true,  false, 24, 1.5f, true,  true,  96,  "Cuts to the face, shocked but walking."), "Walking wounded"),
            new CasualtyCase("C03", V(true,  true,  false, 20, 1.0f, true,  true,  78,  "Coughing from smoke."), "Walking wounded"),
            new CasualtyCase("C04", V(false, true,  false, 22, 1.5f, true,  true,  92,  "Broken lower leg, cannot stand."), "Delayed: stable"),
            new CasualtyCase("C05", V(false, true,  false, 28, 2.0f, true,  true,  104, "Crush injury to the arm, fast breathing but within limits."), "Delayed: borderline values (RR 28, refill exactly 2.0 s)"),
            new CasualtyCase("C06", V(false, true,  false, 36, 1.8f, true,  true,  128, "Chest trauma, breathing very fast."), "Immediate: respiratory rate"),
            new CasualtyCase("C07", V(false, true,  false, 26, 3.4f, false, true,  136, "Heavy bleeding from the thigh, pale and cold."), "Immediate: perfusion"),
            new CasualtyCase("C08", V(false, true,  false, 20, 1.6f, true,  false, 110, "Head injury, drifting in and out of consciousness."), "Immediate: mental status"),
            new CasualtyCase("C09", V(false, false, true,  0,  0.0f, false, false, 60,  "Unresponsive, lying motionless."), "Immediate: breathing returns after airway repositioning"),
            new CasualtyCase("C10", V(false, false, false, 0,  0.0f, false, false, 0,   "Unresponsive, lying motionless."), "Expectant (Black)"),
        };
    }
}
