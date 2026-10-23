using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    public class ConditionRandomizer : MonoBehaviour
    {
        public static ConditionRandomizer Instance { get; private set; }

        public struct BlockPlan
        {
            public GuidanceCondition condition;
            public char layout; // 'A' or 'B'
        }

        // participant_number mod 4 -> orders: S-A then M-B; S-B then M-A; M-A then S-B; M-B then S-A
        static readonly (GuidanceCondition c1, char l1, GuidanceCondition c2, char l2)[] Orders =
        {
            (GuidanceCondition.Spatial, 'A', GuidanceCondition.Stereo, 'B'),
            (GuidanceCondition.Spatial, 'B', GuidanceCondition.Stereo, 'A'),
            (GuidanceCondition.Stereo,  'A', GuidanceCondition.Spatial, 'B'),
            (GuidanceCondition.Stereo,  'B', GuidanceCondition.Spatial, 'A'),
        };

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public static int OrderIndex(int participantNumber) => ((participantNumber % 4) + 4) % 4;

        public static BlockPlan Plan(int participantNumber, int block)
        {
            var o = Orders[OrderIndex(participantNumber)];
            return block == 1 ? new BlockPlan { condition = o.c1, layout = o.l1 } : new BlockPlan { condition = o.c2, layout = o.l2 };
        }

        public static string OrderLabel(int participantNumber)
        {
            var o = Orders[OrderIndex(participantNumber)];
            return $"{Short(o.c1)}-{o.l1} then {Short(o.c2)}-{o.l2}";
        }

        public static string Short(GuidanceCondition c) => c == GuidanceCondition.Spatial ? "S" : "M";

        public BlockPlan PlanFor(int block) => Plan(StudyConfig.ParticipantNumber, block);
        public string CurrentOrderLabel => OrderLabel(StudyConfig.ParticipantNumber);
    }
}
