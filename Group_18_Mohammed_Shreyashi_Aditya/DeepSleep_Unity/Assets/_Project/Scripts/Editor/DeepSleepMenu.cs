using System;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Editor menu: DeepSleep > Run Playthrough, Jump to Stage. (Validate Project lives in ProjectValidator.)</summary>
    public static class DeepSleepMenu
    {
        static void Bot(int first, int last, bool coverage = false, bool timeline = false, float ts = 0f)
        {
            SessionState.SetBool("DS_bot_timeline", timeline); SessionState.SetFloat("DS_bot_ts", ts); SessionState.SetBool("DS_bot_pace", timeline); SessionState.SetBool("DS_bot_plain", false);
            SessionState.SetInt("DS_bot_first", first); SessionState.SetInt("DS_bot_last", last); SessionState.SetBool("DS_bot_cov", coverage);
            string stamp = DateTime.Now.ToString("yyyyMMdd");
            PlayCheckLauncher.Launch("bot", ProjectSetupTools.ScenePath("SCN_Main"), $"Assets/_Project/Docs/Reports/bot_run_{first}_{last}_{stamp}.md");
        }

        [MenuItem("DeepSleep/Run Playthrough/All 8 participants")] public static void RunPlaythrough() => Bot(1, 8);
        [MenuItem("DeepSleep/Run Playthrough/Participants 1 to 4")] public static void RunPlaythrough14() => Bot(1, 4);
        [MenuItem("DeepSleep/Run Playthrough/Participants 5 to 8")] public static void RunPlaythrough58() => Bot(5, 8);
        [MenuItem("DeepSleep/Run Playthrough/Participant 4 only, then coverage check")] public static void RunPlaythrough4() => Bot(4, 4, true);
        [MenuItem("DeepSleep/Run Playthrough/One participant (1)")] public static void RunPlaythrough1() => Bot(1, 1);

        [MenuItem("DeepSleep/Final/Flow check: participants 1 to 4 (x4, timeline)")]
        public static void FinalFlow()
        {
            Bot(1, 4, false, true, 4f);
        }

        [MenuItem("DeepSleep/Final/Flow check: participant 1 only (x4)")] static void F1() => Bot(1, 1, false, true, 4f);
        [MenuItem("DeepSleep/Final/Flow check: participant 2 only (x4)")] static void F2() => Bot(2, 2, false, true, 4f);
        [MenuItem("DeepSleep/Final/Flow check: participant 3 only (x4)")] static void F3() => Bot(3, 3, false, true, 4f);
        [MenuItem("DeepSleep/Final/Flow check: participant 4 only (x4)")] static void F4() => Bot(4, 4, false, true, 4f);

        static void Jump(Stage s)
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[DeepSleep] Enter play mode first (any scene), then use Jump to Stage."); return; }
            StageJump.Jump(s);
        }

        [MenuItem("DeepSleep/Jump to Stage/Setup")] static void J0() => Jump(Stage.Setup);
        [MenuItem("DeepSleep/Jump to Stage/Awakening")] static void J1() => Jump(Stage.Awakening);
        [MenuItem("DeepSleep/Jump to Stage/EarCheck")] static void J2() => Jump(Stage.EarCheck);
        [MenuItem("DeepSleep/Jump to Stage/KeypadSolved")] static void J3() => Jump(Stage.KeypadSolved);
        [MenuItem("DeepSleep/Jump to Stage/TrailFollowed")] static void J4() => Jump(Stage.TrailFollowed);
        [MenuItem("DeepSleep/Jump to Stage/Block1")] static void J5() => Jump(Stage.Block1);
        [MenuItem("DeepSleep/Jump to Stage/Block2")] static void J6() => Jump(Stage.Block2);
        [MenuItem("DeepSleep/Jump to Stage/CoolantRouted")] static void J7() => Jump(Stage.CoolantRouted);
        [MenuItem("DeepSleep/Jump to Stage/Escaped")] static void J8() => Jump(Stage.Escaped);
        [MenuItem("DeepSleep/Jump to Stage/Questionnaire")] static void J9() => Jump(Stage.Questionnaire);
    }
}
