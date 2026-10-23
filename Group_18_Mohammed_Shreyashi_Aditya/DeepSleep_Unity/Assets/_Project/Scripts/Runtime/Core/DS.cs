using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Project-wide constants (DECISIONS values) and the logging helper. Tunables on components default to these.</summary>
    public static class DS
    {
        public const string Prefix = "[DeepSleep]";
        public const float EYE_HEIGHT = 1.65f;
        public const int TARGET_FPS = 72;
        public const float EDITOR_FRAME_BUDGET_MS = 11f;
        public const float HeightTolerance = 0.05f;

        public const string SceneMain = "SCN_Main";
        public const string SceneSetup = "SCN_Setup";
        public const string SceneMedicalBay = "SCN_MedicalBay";
        public const string SceneSmokeRoom = "SCN_SmokeRoom";
        public const string SceneHallway = "SCN_HallwayDebrief";

        public static void Log(string msg) { Debug.Log($"{Prefix} {msg}"); }
        public static void Warn(string msg) { Debug.LogWarning($"{Prefix} {msg}"); }
        public static void Error(string msg) { Debug.LogError($"{Prefix} {msg}"); }
    }

    public enum Stage
    {
        Setup = 0,
        Awakening,
        EarCheck,
        KeypadSolved,
        TrailFollowed,
        Block1,
        Block2,
        CoolantRouted,
        Escaped,
        Questionnaire
    }

    public enum Hand { Left = 0, Right = 1 }

    public enum GuidanceCondition { Spatial, Stereo }
}
