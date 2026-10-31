using System.Globalization;

namespace RescueSim.Triage
{
    /// <summary>Result of evaluating the START flow: tag, deciding branch and a human-readable reason.</summary>
    public readonly struct StartResult
    {
        public readonly TriageCategory category;
        public readonly TriageBranch branch;
        public readonly string reason;
        public StartResult(TriageCategory c, TriageBranch b, string r) { category = c; branch = b; reason = r; }
    }

    /// <summary>
    /// Pure implementation of the adult START (Simple Triage and Rapid Treatment) flow:
    /// Walk? -> Breathing (airway reposition)? -> RR &gt; 30? -> Perfusion -> Obeys commands?.
    /// Adapted from Bhoomi Bhandari's TriageEvaluator (branch feature/bhoomi-work, commit ea174c8), with an explicit
    /// perfusion-criterion switch so the two published START variants are never mixed.
    /// Educational prototype: it is not clinically validated.
    /// </summary>
    public static class STARTEvaluator
    {
        public const int RespiratoryRateThreshold = 30;
        public const float CapillaryRefillThresholdSeconds = 2f;

        public static bool PerfusionFails(VitalSigns v, PerfusionCriterion criterion) =>
            criterion == PerfusionCriterion.CapillaryRefill
                ? v.capillaryRefillSeconds > CapillaryRefillThresholdSeconds
                : !v.hasRadialPulse;

        /// <param name="airwayRepositioned">
        /// True when the airway manoeuvre has been done (ground truth always assumes it). If false and the casualty is
        /// not breathing, no Black tag can be justified yet: the result is Red / ApneaAirwayNotAttempted.
        /// </param>
        public static StartResult Evaluate(VitalSigns v, PerfusionCriterion criterion, bool airwayRepositioned = true)
        {
            if (v == null) return new StartResult(TriageCategory.None, TriageBranch.None, "No vital signs.");

            if (v.canWalk)
                return new StartResult(TriageCategory.Green, TriageBranch.Walking, "Can walk.");

            if (!v.isBreathing)
            {
                if (!airwayRepositioned)
                    return new StartResult(TriageCategory.Red, TriageBranch.ApneaAirwayNotAttempted, "Not breathing: reposition the airway first.");
                return v.breathingAfterAirwayReposition
                    ? new StartResult(TriageCategory.Red, TriageBranch.ApneaRespondsToAirway, "Not breathing; breathing restored after airway repositioning.")
                    : new StartResult(TriageCategory.Black, TriageBranch.ApneaNoResponseToAirway, "Not breathing and no breathing after airway repositioning.");
            }

            if (v.respiratoryRate > RespiratoryRateThreshold)
                return new StartResult(TriageCategory.Red, TriageBranch.RespiratoryRateAbove30,
                    "Respiratory rate " + v.respiratoryRate.ToString(CultureInfo.InvariantCulture) + "/min is above 30.");

            if (PerfusionFails(v, criterion))
                return new StartResult(TriageCategory.Red, TriageBranch.PoorPerfusion,
                    criterion == PerfusionCriterion.CapillaryRefill
                        ? "Capillary refill " + v.capillaryRefillSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s is above 2 s."
                        : "Radial pulse absent.");

            if (!v.followsCommands)
                return new StartResult(TriageCategory.Red, TriageBranch.CannotFollowCommands, "Cannot follow simple commands.");

            return new StartResult(TriageCategory.Yellow, TriageBranch.AllChecksPassed, "Breathing, perfused and obeys commands but cannot walk.");
        }

        /// <summary>Ground-truth category: START with the airway manoeuvre performed.</summary>
        public static TriageCategory GroundTruth(VitalSigns v, PerfusionCriterion criterion) => Evaluate(v, criterion, true).category;

        /// <summary>Text shown to the trainee after performing an examination.</summary>
        public static string Describe(ExamKind kind, VitalSigns v, PerfusionCriterion criterion)
        {
            switch (kind)
            {
                case ExamKind.Walking: return v.canWalk ? "Walks when told to" : "Cannot walk";
                case ExamKind.Breathing: return v.isBreathing ? "Breathing" : "Not breathing";
                case ExamKind.Airway:
                    if (v.isBreathing) return "Airway open (was already breathing)";
                    return v.breathingAfterAirwayReposition ? "Breathing after airway repositioned" : "Still not breathing after airway repositioned";
                case ExamKind.RespiratoryRate:
                    return v.isBreathing ? v.respiratoryRate.ToString(CultureInfo.InvariantCulture) + " breaths/min" : "No breathing to count";
                case ExamKind.Perfusion:
                    return criterion == PerfusionCriterion.CapillaryRefill
                        ? "Capillary refill " + v.capillaryRefillSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s"
                        : (v.hasRadialPulse ? "Radial pulse present" : "Radial pulse absent");
                case ExamKind.MentalStatus: return v.followsCommands ? "Follows simple commands" : "Does not follow commands";
                default: return "";
            }
        }

        public static string Label(ExamKind kind, PerfusionCriterion criterion)
        {
            switch (kind)
            {
                case ExamKind.Walking: return "Can walk?";
                case ExamKind.Breathing: return "Breathing?";
                case ExamKind.Airway: return "Reposition airway";
                case ExamKind.RespiratoryRate: return "Resp. rate";
                case ExamKind.Perfusion: return criterion == PerfusionCriterion.CapillaryRefill ? "Cap. refill" : "Radial pulse";
                default: return "Obeys commands?";
            }
        }
    }
}
