using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Opens a scene, enters play mode and starts a PlayCheckRunner scenario. Survives the domain reload via SessionState.</summary>
    [InitializeOnLoad]
    public static class PlayCheckLauncher
    {
        const string KeyScenario = "DS_check_scenario";
        const string KeyReport = "DS_check_report";

        static PlayCheckLauncher()
        {
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.update += KeepTicking;
        }

        // An unfocused Editor throttles the player loop; keep scripted runs advancing.
        static void KeepTicking()
        {
            if (EditorApplication.isPlaying && !EditorApplication.isPaused && Object.FindAnyObjectByType<PlayCheckRunner>() != null)
                EditorApplication.QueuePlayerLoopUpdate();
        }

        public static void Launch(string scenario, string scenePath, string reportPath)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[DeepSleep] Already in play mode"); return; }
            if (File.Exists(reportPath + ".done")) { File.Delete(reportPath + ".done"); if (File.Exists(reportPath + ".done.meta")) File.Delete(reportPath + ".done.meta"); }
            SessionState.SetString(KeyScenario, scenario);
            SessionState.SetString(KeyReport, reportPath);
            PlayerSettings.runInBackground = true;
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.isPlaying = true;
        }

        public static bool IsDone(string reportPath) => File.Exists(reportPath + ".done");
        public static string Result(string reportPath) => File.Exists(reportPath + ".done") ? File.ReadAllText(reportPath + ".done") : "RUNNING";

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            string scn = SessionState.GetString(KeyScenario, "");
            if (string.IsNullOrEmpty(scn)) return;
            string report = SessionState.GetString(KeyReport, "");
            SessionState.EraseString(KeyScenario);
            var go = new GameObject("PlayCheckRunner");
            var r = go.AddComponent<PlayCheckRunner>();
            r.Scenario = scn;
            r.ReportPath = report;
        }

        [MenuItem("DeepSleep/Run Check/Look (briefing, props, posters, exit)")]
        public static void RunLook() => Launch("look", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/look_check.md");

        [MenuItem("DeepSleep/Run Check/Polish viewpoints (before)")]
        public static void RunPolishBefore() { SessionState.SetString("DS_polish_tag", "before"); Launch("polish", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/polish_check_before.md"); }

        [MenuItem("DeepSleep/Run Check/Polish viewpoints (after)")]
        public static void RunPolishAfter() { SessionState.SetString("DS_polish_tag", "after"); Launch("polish", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/polish_check_after.md"); }

        [MenuItem("DeepSleep/Run Check/Speckle probe (smoke)")]
        public static void RunSpeckle2() => Launch("speckle2", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/speckle_probe2.md");

        [MenuItem("DeepSleep/Run Check/Speckle probe")]
        public static void RunSpeckle() => Launch("speckle", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/speckle_probe.md");

        [MenuItem("DeepSleep/Run Check/Audit: smoke visibility")]
        public static void RunAuditSmoke() => Launch("audit_smoke", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/audit_smoke.md");

        [MenuItem("DeepSleep/Run Check/Audit: flow, puzzles, telemetry, leaks")]
        public static void RunAuditFlow() => Launch("audit_flow", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/audit_flow.md");

        [MenuItem("DeepSleep/Run Check/Guide HUD")]
        public static void RunGuideHud() => Launch("guide_hud", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/guide_hud.md");

        [MenuItem("DeepSleep/Final/Edge cases")]
        public static void RunFinalEdge() => Launch("final_edge", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/final_edge.md");

        [MenuItem("DeepSleep/Run Check/Polish: voice levels at the listener")]
        public static void RunPolishLevels() { SessionState.SetBool("DS_levels_limiter", true); Launch("polish_levels", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/polish_levels.md"); }

        [MenuItem("DeepSleep/Run Check/Audit: audio levels")]
        public static void RunAuditAudio() => Launch("audit_audio", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/audit_audio.md");

        [MenuItem("DeepSleep/Run Check/Level match (spatial vs stereo)")]
        public static void RunLevelMatch() => Launch("levelmatch", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/level_match.md");

        [System.Serializable]
        class LevelMatchData { public float stereo_gain_trim_db; public float[] distances_m; public float[] trim_db; public bool audio_available; }

        [MenuItem("DeepSleep/Apply Level Match")]
        public static void ApplyLevelMatch()
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Analysis", "data", "level_match.json"));
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("[DeepSleep] No level_match.json yet: run DeepSleep > Run Check > Level match first"); return; }
            var d = JsonUtility.FromJson<LevelMatchData>(System.IO.File.ReadAllText(path));
            if (d == null || !d.audio_available) { Debug.LogWarning("[DeepSleep] Level match had no audio device; the trim stays at 0 dB"); return; }
            var assets = Resources.Load<DeepSleepAssets>("DeepSleepAssets");
            var so = new SerializedObject(assets);
            so.FindProperty("stereoGainTrimDb").floatValue = d.stereo_gain_trim_db;
            var pd = so.FindProperty("stereoTrimDistances"); pd.arraySize = d.distances_m.Length;
            var pt = so.FindProperty("stereoTrimDb"); pt.arraySize = d.trim_db.Length;
            for (int i = 0; i < d.distances_m.Length; i++) { pd.GetArrayElementAtIndex(i).floatValue = d.distances_m[i]; pt.GetArrayElementAtIndex(i).floatValue = d.trim_db[i]; }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(assets); AssetDatabase.SaveAssets();
            Debug.Log($"[DeepSleep] Level match applied: mean trim {d.stereo_gain_trim_db:F2} dB, curve over {d.distances_m.Length} distances");
        }

        [MenuItem("DeepSleep/Run Check/Phase 6 audio")]
        public static void RunPhase6() => Launch("phase6", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/phase6_check.md");

        [MenuItem("DeepSleep/Run Check/Phase 5 coolant, questionnaires, hallway")]
        public static void RunPhase5() => Launch("phase5", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/phase5_check.md");

        [MenuItem("DeepSleep/Run Check/Phase 4 smoke room blocks")]
        public static void RunPhase4() => Launch("phase4", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/phase4_check.md");

        [MenuItem("DeepSleep/Run Check/Phase 3 ear check, keypad, trail")]
        public static void RunPhase3() => Launch("phase3", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/phase3_check.md");

        [MenuItem("DeepSleep/Run Check/Phase 2 setup and medical bay")]
        public static void RunPhase2() => Launch("phase2", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/phase2_check.md");

        [MenuItem("DeepSleep/Run Check/Phase 1 rig check")]
        public static void RunPhase1() => Launch("phase1", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/phase1_check.md");
    }
}
