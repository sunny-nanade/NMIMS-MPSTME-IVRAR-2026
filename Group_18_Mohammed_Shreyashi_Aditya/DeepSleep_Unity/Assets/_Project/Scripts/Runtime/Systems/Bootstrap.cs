using System;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Creates the persistent system objects before the first scene so every scene can be played on its own.</summary>
    public static class Bootstrap
    {
        static readonly string[] Systems =
        {
            "InputService", "SessionManager", "PauseManager", "ConditionRandomizer", "TelemetryLogger", "SceneFlow", "SectionLook",
            "ExperimenterPanel", "SaveLoad", "ProfilerCapture", "OxygenSystem",
            // later phases (added only if the class exists)
            "SfxDirector", "NarrativeVoiceDirector", "HalcyonDirector", "MentorDirector", "AudioConditionSwitch", "GuideHud",
        };

        public static GameObject Root { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (Root != null) return;
            StudyConfig.Load();
            Root = new GameObject("[DeepSleep Systems]");
            UnityEngine.Object.DontDestroyOnLoad(Root);
            foreach (var n in Systems)
            {
                var t = Type.GetType("Fractured_Final." + n + ", Fractured_Final.Runtime");
                if (t == null) continue;
                if (n == "MentorDirector" || n == "AudioConditionSwitch") continue; // scene-owned
                Root.AddComponent(t);
            }
            Root.AddComponent<MasterLimiterHost>();
            Application.targetFrameRate = DS.TARGET_FPS;
            DS.Log("Systems ready");
        }
    }
}
