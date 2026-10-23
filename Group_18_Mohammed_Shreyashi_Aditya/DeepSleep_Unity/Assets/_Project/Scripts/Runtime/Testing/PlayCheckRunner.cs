#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Editor-only host for scripted scenarios (phase checks). Started by PlayCheckLauncher after entering play mode.
    /// Writes a markdown report and leaves play mode when finished.
    /// </summary>
    public partial class PlayCheckRunner : MonoBehaviour
    {
        public string Scenario = "phase1";
        public string ReportPath = "Assets/_Project/Docs/Reports/check.md";

        readonly List<string> _lines = new List<string>();
        int _pass, _fail;
        int _errors, _warnings;
        readonly List<string> _errorTexts = new List<string>();
        readonly List<string> _warningTexts = new List<string>();

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                if (_errorTexts.Count < 20) _errorTexts.Add(msg);
            }
            else if (type == LogType.Warning) { _warnings++; string w = msg.Split('\n')[0]; if (_warningTexts.Count < 40 && !_warningTexts.Contains(w)) _warningTexts.Add(w); }
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null; yield return null;
            IEnumerator run = null;
            switch (Scenario)
            {
                case "phase1": run = Phase1(); break;
                case "phase2": run = Phase2(); break;
                case "phase3": run = Phase3(); break;
                case "phase4": run = Phase4(); break;
                case "phase5": run = Phase5(); break;
                case "phase6": run = Phase6(); break;
                case "bot": run = BotScenario(); break;
                case "levelmatch": run = LevelMatch(); break;
                case "audit_audio": run = AuditAudio(); break;
                case "audit_smoke": run = AuditSmoke(); break;
                case "audit_flow": run = AuditFlow(); break;
                case "final_edge": run = FinalEdge(); break;
                case "guide_hud": run = GuideHudCheck(); break;
                case "record": run = RecordRun(); break;
                case "look": run = LookCheck(); break;
                case "polish": run = PolishCheck(); break;
                case "speckle": run = SpeckleProbe(); break;
                case "speckle2": run = SmokeSpeckleProbe(); break;
                case "polish_levels": run = PolishLevels(); break;
                default: Fail("Unknown scenario " + Scenario); break;
            }
            if (run != null)
            {
                while (true)
                {
                    object cur = null;
                    try { if (!run.MoveNext()) break; cur = run.Current; }
                    catch (Exception e) { Fail("Exception: " + e); break; }
                    yield return cur;
                }
            }
            Finish();
        }

        // ------------------------------------------------------------------ helpers
        public void Check(bool ok, string what)
        {
            if (ok) _pass++; else _fail++;
            _lines.Add($"- [{(ok ? "PASS" : "FAIL")}] {what}");
            DS.Log($"CHECK {(ok ? "PASS" : "FAIL")}: {what}");
        }

        public void Fail(string what) { Check(false, what); }
        public void Note(string text) { _lines.Add("- " + text); }

        void Finish()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Check report: {Scenario}");
            sb.AppendLine();
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Result: {_pass} passed, {_fail} failed. Console during run: {_errors} errors, {_warnings} warnings.");
            sb.AppendLine();
            foreach (var l in _lines) sb.AppendLine(l);
            if (_warningTexts.Count > 0)
            {
                sb.AppendLine(); sb.AppendLine("## Warnings seen (distinct)");
                foreach (var w in _warningTexts) sb.AppendLine("- " + w.Replace("\n", " "));
            }
            if (_errorTexts.Count > 0)
            {
                sb.AppendLine(); sb.AppendLine("## Errors seen");
                foreach (var e in _errorTexts) sb.AppendLine("- " + e.Replace("\n", " "));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString());
            File.WriteAllText(ReportPath + ".done", _fail == 0 && _errors == 0 ? "OK" : "FAIL");
            ScriptedPlayer.Dispose();
            VoiceRules.BypassForTests = false;
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // ------------------------------------------------------------------ Phase 1 scenario
        IEnumerator Phase1()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null, 10f);
            var rig = PlayerRig.Instance;
            Check(rig != null, "PlayerRig present in SCN_Setup");
            if (rig == null) yield break;
            yield return sp.Wait(0.5f);

            Note($"InputService source={InputService.Instance.CurrentSource}, head={rig.Head.position}, root={rig.transform.position}, xrActive={UnityEngine.XR.XRSettings.isDeviceActive}");
            float h = rig.CameraHeight;
            Check(Mathf.Abs(h - DS.EYE_HEIGHT) <= DS.HeightTolerance, $"Camera height {h:F3} m is within 5 cm of EYE_HEIGHT {DS.EYE_HEIGHT}");

            var table = GameObject.Find("ReferenceTable_0.9m");
            if (table != null)
            {
                float top = table.GetComponent<Renderer>().bounds.max.y;
                float diff = rig.Head.position.y - top;
                Check(Mathf.Abs(diff - 0.75f) < 0.06f, $"Table top is {diff:F2} m below the camera (expected about 0.75)");
            }

            // stage rules
            var sm = SessionManager.Instance;
            Check(sm != null && sm.Current == Stage.Setup, "SessionManager starts at Setup");
            Check(!sm.Advance(Stage.Block1), "Skipping Setup -> Block1 is rejected");
            LogAssert(false);

            // grab / release
            var cube = GameObject.Find("GrabCube_1");
            var g = cube.GetComponent<Grabbable>();
            Vector3 start = cube.transform.position;
            yield return sp.GrabObject(g);
            Check(g.IsHeld, "Grip grabs a Grabbable");
            yield return sp.MoveHand(sp.HandPos + new Vector3(0, 0.25f, 0), 0.4f);
            yield return sp.Frames(3);
            Check(cube.transform.position.y > start.y + 0.15f, "Held object follows the hand");
            yield return sp.ReleaseGrip();
            Check(!g.IsHeld, "Releasing grip drops the object");
            yield return sp.Wait(0.8f);

            // hover outline
            var cube2 = GameObject.Find("GrabCube_2").GetComponent<Grabbable>();
            yield return sp.MoveHand(cube2.transform.position + Vector3.up * 0.02f, 0.2f);
            yield return sp.Frames(3);
            Check(cube2.Hovered, "Hovered Grabbable shows HoverOutline");
            yield return sp.MoveHand(rig.Head.position + rig.Head.forward * 0.35f, 0.2f);

            // teleport
            Vector3 before = rig.Head.position;
            yield return sp.TeleportTo(new Vector3(2f, 0f, -1.5f));
            Vector3 after = rig.Head.position;
            Check(Vector2.Distance(new Vector2(after.x, after.z), new Vector2(2f, -1.5f)) < 0.3f, $"Teleport moved the head to the target ({after.x:F2},{after.z:F2})");
            Check(Mathf.Abs(rig.CameraHeight - DS.EYE_HEIGHT) <= DS.HeightTolerance, "Camera height unchanged after teleport");

            // snap turn
            float y0 = rig.Head.eulerAngles.y;
            yield return sp.SnapTurn(1);
            float dy = Mathf.DeltaAngle(y0, rig.Head.eulerAngles.y);
            Check(Mathf.Abs(dy - 45f) < 1.5f, $"Snap turn rotated {dy:F1} degrees");

            // stage progression in order
            bool ok = true;
            foreach (var s in new[] { Stage.Awakening, Stage.EarCheck, Stage.KeypadSolved, Stage.TrailFollowed, Stage.Block1, Stage.Block2, Stage.CoolantRouted, Stage.Escaped, Stage.Questionnaire })
                ok &= sm.Advance(s);
            Check(ok && sm.Current == Stage.Questionnaire, "All ten stages advance in order");
            sm.ResetSession();
        }

        IEnumerator BotScenario()
        {
            int first = UnityEditor.SessionState.GetInt("DS_bot_first", 1), last = UnityEditor.SessionState.GetInt("DS_bot_last", 8);
            var bot = gameObject.AddComponent<PlaythroughBot>();
            yield return bot.Run(this, first, last);
        }

        void LogAssert(bool _) { }
    }
}
#endif
