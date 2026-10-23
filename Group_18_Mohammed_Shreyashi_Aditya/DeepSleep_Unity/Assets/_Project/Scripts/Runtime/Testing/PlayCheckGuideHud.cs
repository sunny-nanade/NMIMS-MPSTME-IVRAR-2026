#if UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>GuideHud: the 10 s orientation hologram appears once on waking, then the goal card follows the stage.</summary>
    public partial class PlayCheckRunner
    {
        IEnumerator GuideHudCheck()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SessionManager.Instance != null && SceneFlow.Instance != null, 10f);
            StudyConfig.Data.participant_id = "4";
            TelemetryLogger.FileNameOverride = "deepsleep_telemetry_bot.csv";
            Check(GuideHud.Instance != null, "GuideHud is created by Bootstrap");
            yield return sp.Wait(0.8f);

            StageJump.Jump(Stage.Awakening);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 30f);
            float arrived = Time.realtimeSinceStartup;
            yield return RealUntil(() => NarrativeVoiceDirector.Instance.Played.Contains("R010"), 12f);
            float r010After = Time.realtimeSinceStartup - arrived;
            Check(NarrativeVoiceDirector.Instance.Played.Contains("R010") && VoiceGate.Open, $"R010 plays {r010After:F1} s after the scene went idle and the voice gate is open (gate holds lines at most {VoiceGate.MaxWaitSeconds:F1} s after a load)");
            yield return RealUntil(() => GuideHud.Instance.IntroVisible, 8f);
            float t0 = Time.realtimeSinceStartup;
            Check(GuideHud.Instance.IntroVisible, "Orientation hologram appears after arriving in the Medical Bay");
            var head = PlayerRig.Instance.Head;
            var holo = GameObject.Find("OrientationHologram");
            if (holo != null)
            {
                Vector3 d = holo.transform.position - head.position;
                Check(d.magnitude > 1.0f && d.magnitude < 2.0f && Vector3.Angle(head.forward, d) < 40f, $"Hologram sits {d.magnitude:F2} m in front of the face ({Vector3.Angle(head.forward, d):F0} deg off gaze)");
            }
            else Check(false, "OrientationHologram object exists");
            yield return new WaitForSecondsRealtime(3f);
            var shotDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Analysis", "screens", "review"));
            System.IO.Directory.CreateDirectory(shotDir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, "hud_intro.png"));
            yield return RealUntil(() => !GuideHud.Instance.IntroVisible, 14f);
            float shown = Time.realtimeSinceStartup - t0;
            Check(!GuideHud.Instance.IntroVisible && shown > 4.5f && shown < 11.5f, $"Hologram stays about 10 s ({shown:F1} s after it appeared) and then hides");
            yield return RealUntil(() => GuideHud.Instance.GoalVisible, 4f);
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, "hud_goal.png"));
            Check(GuideHud.Instance.GoalVisible && GuideHud.Instance.CurrentGoal.Contains("cup"), $"Goal card shows the first goal: '{GuideHud.Instance.CurrentGoal}'");

            StageJump.Jump(Stage.EarCheck);
            yield return RealUntil(() => SceneIdle && SessionManager.Instance.Current == Stage.EarCheck, 20f);
            yield return RealUntil(() => GuideHud.Instance.CurrentGoal.Contains("ring"), 4f);
            Check(GuideHud.Instance.CurrentGoal.Contains("ring"), $"EarCheck goal: '{GuideHud.Instance.CurrentGoal}'");
            Check(!GuideHud.Instance.IntroVisible, "The orientation hologram does not return on a later stage jump");

            StageJump.Jump(Stage.KeypadSolved);
            yield return RealUntil(() => SceneIdle && SessionManager.Instance.Current == Stage.KeypadSolved, 20f);
            yield return RealUntil(() => GuideHud.Instance.CurrentGoal.Contains("trail") || GuideHud.Instance.CurrentGoal.Contains("handle"), 4f);
            Check(GuideHud.Instance.CurrentGoal.Contains("trail") || GuideHud.Instance.CurrentGoal.Contains("handle"), $"Trail goal: '{GuideHud.Instance.CurrentGoal}'");

            StageJump.Jump(Stage.Escaped);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneHallway && SceneIdle, 30f);
            yield return RealUntil(() => GuideHud.Instance.CurrentGoal.Length > 0, 4f);
            Check(GuideHud.Instance.CurrentGoal.Length > 0, $"Hallway goal: '{GuideHud.Instance.CurrentGoal}'");
        }
    }
}
#endif
