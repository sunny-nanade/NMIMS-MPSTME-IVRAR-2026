using System;
using System.Collections.Generic;
using UnityEngine;

namespace RescueSim.Core
{
    /// <summary>Data-driven description of a mission: briefing text, objectives, time limit and scoring weights.</summary>
    [CreateAssetMenu(menuName = "Rescue Sim/Mission Definition", fileName = "Mission_")]
    public class MissionDefinition : ScriptableObject
    {
        [Serializable]
        public class ObjectiveDefinition
        {
            public string id = "objective";
            public string title = "Objective";
            [TextArea] public string hint = "";
            [Tooltip("Required objectives must be complete for the mission to succeed.")]
            public bool required = true;
        }

        [Header("Identity")]
        public string missionId = "factory-fire";
        public string title = "Night Shift: Factory Fire";
        [TextArea(3, 6)] public string tagline = "";
        [TextArea(6, 20)] public string briefing = "";

        [Header("Campaign")]
        [Tooltip("0-based position in the campaign. Stage N+1 unlocks when stage N is completed.")]
        [Min(0)] public int stageIndex = 0;
        [Tooltip("Scene that runs this stage. Must be in Build Settings.")]
        public string sceneName = "FactoryRescue";
        [Tooltip("Short card text for the main menu.")]
        [TextArea(2, 4)] public string cardBlurb = "";
        public Color accent = new Color(1f, 0.55f, 0.1f, 1f);
        [Tooltip("Whether smoke/fume exposure is part of this stage (controls the wrist HUD bar).")]
        public bool usesExposure = true;

        [Header("Rules")]
        [Min(30f)] public float timeLimitSeconds = 480f;
        [Tooltip("How quickly player exposure (smoke/fumes) recovers per second in clean air.")]
        [Range(0f, 0.2f)] public float exposureRecoveryPerSecond = 0.04f;

        [Header("Objectives")]
        public List<ObjectiveDefinition> objectives = new List<ObjectiveDefinition>();

        [Header("Scoring")]
        public ScoreWeights scoring = ScoreWeights.Default;

        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;
    }
}
