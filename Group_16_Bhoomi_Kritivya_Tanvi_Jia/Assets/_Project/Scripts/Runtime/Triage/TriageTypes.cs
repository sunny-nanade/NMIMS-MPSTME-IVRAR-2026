using System;
using UnityEngine;

namespace RescueSim.Triage
{
    /// <summary>START triage tag colours. None = not yet assigned.</summary>
    public enum TriageCategory { None = 0, Green = 1, Yellow = 2, Red = 3, Black = 4 }

    /// <summary>
    /// Which perfusion test the START variant uses.
    /// CapillaryRefill: original START (Newport Beach, 1983) and the team's title sheet: refill &gt; 2 s fails.
    /// RadialPulse: 1996 revision (Benson, Koenig, Schultz): absent radial pulse fails.
    /// Only one criterion is ever active per session; they are never mixed.
    /// </summary>
    public enum PerfusionCriterion { CapillaryRefill = 0, RadialPulse = 1 }

    /// <summary>The branch of the START flow that decided the category.</summary>
    public enum TriageBranch
    {
        None = 0,
        Walking,
        ApneaAirwayNotAttempted,
        ApneaNoResponseToAirway,
        ApneaRespondsToAirway,
        RespiratoryRateAbove30,
        PoorPerfusion,
        CannotFollowCommands,
        AllChecksPassed
    }

    /// <summary>The examinations a trainee can perform on a casualty.</summary>
    public enum ExamKind { Walking = 0, Breathing, Airway, RespiratoryRate, Perfusion, MentalStatus }

    /// <summary>What kind of person produced a session's data. Written to every telemetry row.</summary>
    public enum DataProvenance { SoftwareTest = 0, Pilot, HumanParticipant }

    /// <summary>The clinical findings of one casualty. Educational scenario data, not patient data.</summary>
    [Serializable]
    public class VitalSigns
    {
        public bool canWalk;
        public bool isBreathing = true;
        [Tooltip("If not breathing at first: does repositioning the airway restore breathing?")]
        public bool breathingAfterAirwayReposition;
        public int respiratoryRate = 18;
        public float capillaryRefillSeconds = 1.5f;
        public bool hasRadialPulse = true;
        public bool followsCommands = true;
        public int heartRate = 80;
        [TextArea(1, 3)] public string observation = "";

        public VitalSigns Clone() => (VitalSigns)MemberwiseClone();
    }
}
