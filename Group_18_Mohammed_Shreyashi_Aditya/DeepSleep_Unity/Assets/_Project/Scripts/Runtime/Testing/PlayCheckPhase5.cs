#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        internal IEnumerator ClickByName(ScriptedPlayer sp, string name)
        {
            // an active element nearest to the head: a hidden copy of the same name elsewhere (e.g. in another section) must not be picked
            GameObject g = null; float best = float.MaxValue;
            foreach (var t in FindObjectsByType<Transform>())
            {
                if (t.name != name) continue;
                float d = (t.position - PlayerRig.Instance.Head.position).sqrMagnitude;
                if (d < best) { best = d; g = t.gameObject; }
            }
            if (g == null) g = FindAny(name);
            if (g == null) { Fail("Missing UI element " + name); yield break; }
            yield return sp.ClickRay(g.transform.position);
            yield return sp.Wait(0.25f);
        }

        internal IEnumerator TlxPass(ScriptedPlayer sp, QuestionnaireUI q, int[] values)
        {
            for (int page = 0; page < 6; page++)
            {
                yield return sp.Wait(0.3f);
                var slider = FindAny("Slider_TLX");
                var ws = slider.GetComponent<WorldUI.WorldSlider>();
                float x = values[page] / 100f - 0.5f;
                yield return sp.ClickRay(slider.transform.TransformPoint(new Vector3(x, 0, 0)));
                yield return sp.Wait(0.15f);
                yield return ClickByName(sp, page < 5 ? "Btn_Next" : "Btn_Finish");
            }
        }

        IEnumerator Phase5()
        {
            var voices = _voices;
            VoiceBus.Requested += id => voices.Add(id);
            var sfx = new List<SfxEvent>();
            SfxEvents.Raised += (e, p, i) => sfx.Add(e);
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SessionManager.Instance != null && SceneFlow.Instance != null && FindAnyObjectByType<CoolantRack>() != null, 10f);
            StudyConfig.Data.participant_id = "4";
            var sm = SessionManager.Instance;
            var rack = FindAnyObjectByType<CoolantRack>();
            var q = FindAnyObjectByType<QuestionnaireUI>();
            Check(rack != null && q != null, "CoolantRack and QuestionnaireUI are in SCN_MedicalBay");
            if (rack == null || q == null) yield break;
            yield return sp.Wait(0.8f);
            Check(rack.Current == CoolantRack.State.Waiting, "Rack waits until both blocks and questionnaires are done");

            // ---------------- TLX after block 1
            sm.JumpTo(Stage.Block1);
            EgressBlockController.MarkCompleted(1);
            TelemetryLogger.Instance.BeginBlock(1);
            QuestionnaireUI.BeginTlx(1);
            yield return sp.Wait(0.5f);
            Check(q.IsOpen && q.Current == QuestionnaireUI.Mode.Tlx && q.TlxBlock == 1, "Block 1 questionnaire opens");
            Check(voices.Contains("R140"), "R140 plays when the first questionnaire opens");
            yield return sp.LookAt(FindAny("QuestionnairePanel").transform.position);
            Shot("p5_tlx");
            yield return TlxPass(sp, q, new[] { 20, 10, 30, 15, 40, 25 });
            yield return sp.Wait(0.5f);
            var rows = TelemetryLogger.Instance.Rows;
            Check(!q.IsOpen && rows[1]["TLX_mental"] != "" && rows[1]["TLX_frustration"] != "" && rows[1]["TLX_raw_mean"] != "", "Six TLX answers and the raw mean are logged for block 1");
            float tlx1 = float.Parse(rows[1]["TLX_raw_mean"], System.Globalization.CultureInfo.InvariantCulture);
            Check(Mathf.Abs(tlx1 - 23.33f) < 6f, $"Block 1 raw TLX mean is {tlx1:F1} (answers were about 20, 10, 30, 15, 40, 25)");
            Check(sm.Current == Stage.Block2, "Finishing the first questionnaire advances to Block2");

            // ---------------- TLX after block 2
            EgressBlockController.MarkCompleted(2);
            TelemetryLogger.Instance.BeginBlock(2);
            QuestionnaireUI.BeginTlx(2);
            yield return sp.Wait(0.5f);
            Check(q.IsOpen && q.TlxBlock == 2, "Block 2 questionnaire opens");
            yield return TlxPass(sp, q, new[] { 40, 20, 50, 30, 60, 35 });
            yield return sp.Wait(0.5f);
            Check(!q.IsOpen && rows.ContainsKey(2) && rows[2]["TLX_raw_mean"] != "", "Block 2 TLX logged");
            Check(voices.Contains("R150"), "R150 plays after the second questionnaire (at the rack)");

            // ---------------- coolant rack
            yield return sp.Until(() => rack.Current == CoolantRack.State.Active, 4f);
            Check(rack.Current == CoolantRack.State.Active, "Rack becomes active after Block 2 and its questionnaire");
            Check(rack.Cables.Length == 3 && rack.Ports.Length == 6, "Three cables and six ports");
            var byName = new Dictionary<string, CoolantCable>();
            foreach (var c in rack.Cables) byName[c.CableName] = c;
            Check(byName["Red"].CorrectPort == 2 && byName["Blue"].CorrectPort == 5 && byName["Yellow"].CorrectPort == 3, "Red to port 2, blue to port 5, yellow to port 3");
            yield return sp.TeleportTo(new Vector3(1.5f, 0f, 0.9f));
            yield return sp.LookAt(byName["Blue"].transform.position);
            Shot("p5_cables");

            // wrong plug: blue into port 1
            var blue = byName["Blue"];
            Vector3 blueStart = blue.transform.position;
            yield return PlugBy(sp, blue, 1);
            yield return sp.Wait(0.3f);
            Check(rack.WrongPlugs == 1 && !blue.Plugged, "Blue cable into port 1 is a wrong plug");
            Check(sfx.Contains(SfxEvent.WrongPlug), "Wrong plug sound is raised");
            Check(rack.Locked, "Wrong plug starts the 3 s lockout");
            // while the lockout runs, a correct release is rejected (evaluated in the same frame as the check above)
            var red = byName["Red"];
            red.transform.position += byName["Red"].CorrectPort == 2 ? FindAny("Port_2").transform.position - red.PlugTip.position : Vector3.zero;
            rack.EvaluateRelease(red, Hand.Right);
            Check(!red.Plugged && rack.Current == CoolantRack.State.Active, "A correct plug during the lockout is rejected");
            red.ResetToTable();
            yield return sp.Until(() => Vector3.Distance(blue.transform.position, blueStart) < 0.2f, 4f);
            Check(Vector3.Distance(blue.transform.position, blueStart) < 0.2f, "The wrongly plugged cable goes back to the table");
            yield return sp.Until(() => !rack.Locked, 6f);
            yield return sp.Wait(0.2f);
            Check(!rack.Locked && rack.Current == CoolantRack.State.Active, "Lockout ends and nothing else happened (no fail state)");

            yield return PlugBy(sp, red, 2);
            Check(red.Plugged, "Red cable seats in port 2");
            yield return PlugBy(sp, blue, 5);
            Check(blue.Plugged, "Blue cable seats in port 5");
            Check(rack.Current == CoolantRack.State.Active, "Two cables are not enough");
            yield return PlugBy(sp, byName["Yellow"], 3);
            yield return sp.Wait(0.4f);
            Check(byName["Yellow"].Plugged && rack.Current == CoolantRack.State.Solved, "Yellow cable in port 3 solves the rack");
            Check(sm.Current == Stage.CoolantRouted, "Stage advances to CoolantRouted");
            Check(voices.Contains("R153") && sfx.Contains(SfxEvent.RackSolved), "R153 and the rack-solved sound play");
            Check(rack.LeverHandle.gameObject.activeSelf && Mathf.Abs(rack.LeverHandle.position.y - 1.1f) < 0.15f, $"Lever appears at {rack.LeverHandle.position.y:F2} m");
            yield return sp.LookAt(rack.LeverHandle.position);
            Shot("p5_rack_solved");

            // lever -> blast doors -> hallway
            TelemetryLogger.Log("evac_time_s", 95f, 1);
            TelemetryLogger.Log("evac_time_s", 130f, 2);
            sp.SetHandPos(rack.LeverHandle.position);
            yield return sp.Frames(3);
            sp.SetGrip(true);
            yield return sp.Frames(4);
            yield return sp.MoveHand(rack.LeverHandle.position + Vector3.down * 0.3f, 0.5f);
            yield return sp.Frames(4);
            yield return sp.ReleaseGrip();
            yield return sp.Wait(0.5f);
            Check(rack.Current == CoolantRack.State.LeverPulled && sm.Current == Stage.Escaped, "Pulling the lever starts the blast doors and advances to Escaped");
            Check(voices.Contains("H007") && voices.Contains("H008") && voices.Contains("R160") && sfx.Contains(SfxEvent.BlastDoor) && sfx.Contains(SfxEvent.Lever), "H007, H008, R160, lever and blast door sounds");
            Check(rows[2]["coolant_wrong_plugs"] == "1" && rows[2]["coolant_time_s"] != "", "Coolant time and wrong plugs are logged");
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneHallway && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy, 25f);
            Check(SceneManager.GetActiveScene().name == DS.SceneHallway, "SCN_HallwayDebrief loads after the blast doors");
            var rig = PlayerRig.Instance;
            Check(rig != null && Mathf.Abs(rig.CameraHeight - DS.EYE_HEIGHT) <= DS.HeightTolerance, "Camera height unchanged in the hallway");

            // ---------------- SUS + debrief
            q = FindAnyObjectByType<QuestionnaireUI>();
            yield return sp.Until(() => q != null && q.IsOpen && q.Current == QuestionnaireUI.Mode.Sus, 12f);
            Check(q.IsOpen && q.Current == QuestionnaireUI.Mode.Sus && sm.Current == Stage.Questionnaire, "SUS opens in the hallway and the stage is Questionnaire");
            string plaque0 = q.PlaqueText;
            Check(plaque0 == "MISSION LOG. Aethelgard, Deck 4 evacuation. Participant 4. Block 1 1:35. Block 2 2:10.", $"Plaque text matches the specification: {plaque0}");
            yield return sp.LookAt(FindAny("QuestionnairePanel").transform.position);
            Shot("p5_sus");
            int[] answers = { 5, 1, 5, 1, 5, 1, 5, 1, 5, 1 };
            for (int i = 0; i < 10; i++)
            {
                yield return sp.Wait(0.3f);
                for (int attempt = 0; attempt < 3 && !(q.AnsweredCurrent && q.PendingValue == answers[i]); attempt++)
                {
                    yield return ClickByName(sp, "Btn_" + answers[i]);
                    yield return sp.Until(() => q.AnsweredCurrent && q.PendingValue == answers[i], 1.5f);
                    if (attempt > 0) Note($"SUS item {i + 1}: needed attempt {attempt + 1} (pending {q.PendingValue})");
                }
                int pageBefore = q.Page;
                var modeBefore = q.Current;
                for (int attempt = 0; attempt < 3 && q.Page == pageBefore && q.Current == modeBefore; attempt++)
                {
                    yield return ClickByName(sp, i < 9 ? "Btn_Next" : "Btn_Finish");
                    yield return sp.Until(() => q.Page != pageBefore || q.Current != modeBefore, 1.5f);
                }
            }
            Note("SUS answers recorded: " + string.Join(",", q.SusValues));
            yield return sp.Wait(0.5f);
            Check(Mathf.Abs(q.SusScore - 100f) < 0.01f, $"SUS score is {q.SusScore:F1} for the all-best answers");
            Check(q.Current == QuestionnaireUI.Mode.Preference, "Preference question follows the SUS");
            Shot("p5_pref");
            yield return ClickByName(sp, "Btn_Block 2");
            yield return ClickByName(sp, "Btn_Finish");
            yield return sp.Wait(0.8f);
            Check(q.Finished && q.PreferenceChoice == "Block 2", "Preference recorded and the debrief finishes");
            Check(voices.Contains("R161"), "R161 plays at the end of the debrief");
            rows = TelemetryLogger.Instance.Rows;
            Check(rows[1]["SUS_score"] == "100" && rows[2]["SUS_score"] == "100", "SUS score is logged in both block rows");
            Check(SaveLoad.Instance.Debrief.preference == "Block 2" && SaveLoad.Instance.Debrief.plaque == plaque0, "Debrief record holds the preference and the plaque");
            Check(File.Exists(TelemetryLogger.Instance.FilePath) && File.ReadAllText(TelemetryLogger.Instance.FilePath).Contains("TLX_raw_mean"), "deepsleep_telemetry.csv was written");
            bool json = false;
            foreach (var f in Directory.GetFiles(Application.persistentDataPath, "deepsleep_session_4_*.json")) json = true;
            Check(json, "Session JSON was exported");
            sm.ResetSession();
            EgressBlockController.ResetStatics();
        }

        /// <summary>Grab a cable and release it with its plug tip on the given port.</summary>
        internal IEnumerator PlugBy(ScriptedPlayer sp, CoolantCable cable, int portIndex)
        {
            CoolantPort port = null;
            foreach (var p in FindAnyObjectByType<CoolantRack>().Ports) if (p.Index == portIndex) port = p;
            yield return sp.GrabObject(cable.Grabbable);
            yield return sp.Frames(2);
            Vector3 tipOffset = cable.PlugTip.position - cable.transform.position;
            Vector3 target = port.transform.position - tipOffset + sp.HandPos - cable.transform.position;
            yield return sp.MoveHand(target, 0.7f);
            yield return sp.Frames(3);
            yield return sp.ReleaseGrip();
            yield return sp.Frames(3);
        }
    }
}
#endif
