#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>Final edge cases, one pass each: pause mid-line, restart mid-game, stage jumps forward and back, 4-minute block timeout,
    /// keypad idle 30 s, wrong plug during a voice line, reloading a scene twice.</summary>
    public partial class PlayCheckRunner
    {
        int _bothSpeaking;
        bool _watchOverlap;

        IEnumerator OverlapWatch()
        {
            while (_watchOverlap)
            {
                var n = NarrativeVoiceDirector.Instance; var h = HalcyonDirector.Instance;
                if (n != null && h != null && n.IsSpeaking && h.IsSpeaking) _bothSpeaking++;
                yield return null;
            }
        }

        static int ActiveCount<T>() where T : Component
        {
            int n = 0; foreach (var c in FindObjectsByType<T>(FindObjectsInactive.Exclude)) if (c.gameObject.activeInHierarchy) n++;
            return n;
        }

        static List<string> PlayingVoices()
        {
            var l = new List<string>();
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
                if (s.isPlaying && s.clip != null && s.clip.name.Length == 4 && (s.clip.name[0] == 'R' || s.clip.name[0] == 'H')) l.Add(s.clip.name);
            return l;
        }

        static AudioSource RoseSource()
        {
            var go = GameObject.Find("RoseComms");
            return go != null ? go.GetComponent<AudioSource>() : null;
        }

        IEnumerator FinalEdge()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SessionManager.Instance != null && SceneFlow.Instance != null, 10f);
            StudyConfig.Data.participant_id = "4";
            TelemetryLogger.FileNameOverride = "deepsleep_telemetry_bot.csv";
            var sm = SessionManager.Instance;
            var tl = FlowTimeline.Begin(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "_Project", "Docs", "Reports", "timeline", "timeline_edge.csv")));
            yield return sp.Wait(0.8f);

            // ================================================================ 1. pause and resume in the middle of a line
            _watchOverlap = true; _bothSpeaking = 0; StartCoroutine(OverlapWatch());
            StageJump.Jump(Stage.Awakening);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle && NarrativeVoiceDirector.Instance.IsSpeaking, 30f);
            var rose = RoseSource();
            yield return new WaitForSecondsRealtime(1.5f);
            float t1 = rose.time;
            PauseManager.Instance.SetPaused(true);
            yield return new WaitForSecondsRealtime(1.2f);
            float t2 = rose.time;
            Check(AudioListener.pause && Time.timeScale == 0f && Mathf.Abs(t2 - t1) < 0.1f, $"Pause mid-line freezes R010 (clip time {t1:F2} to {t2:F2} s while paused, timeScale {Time.timeScale})");
            PauseManager.Instance.SetPaused(false);
            yield return new WaitForSecondsRealtime(1.0f);
            float t3 = rose.time;
            bool lineEnded = rose.clip != null && !rose.isPlaying && t2 > rose.clip.length - 1.5f; // paused so close to the end that the line finished within the second
            Check(!AudioListener.pause && Time.timeScale == 1f && (t3 > t2 + 0.5f || lineEnded), $"Resume continues the same line (clip time {t2:F2} to {t3:F2} s)");
            yield return RealUntil(() => HalcyonDirector.Instance.Played.Contains("H001") && !HalcyonDirector.Instance.IsSpeaking, 40f);
            int r010 = 0; foreach (var id in NarrativeVoiceDirector.Instance.Played) if (id == "R010") r010++;
            Check(r010 == 1 && HalcyonDirector.Instance.Played.Contains("H001") && _bothSpeaking == 0, $"R010 played once, H001 followed it, no overlap ({_bothSpeaking} overlapping frames)");

            // ================================================================ 2. experimenter restart in the middle of a line
            StageJump.Jump(Stage.Awakening);
            yield return RealUntil(() => SceneIdle && NarrativeVoiceDirector.Instance.IsSpeaking, 30f);
            VoiceBus.Request("R011"); VoiceBus.Request("H002");
            yield return new WaitForSecondsRealtime(0.5f);
            var panel = FindAnyObjectByType<ExperimenterPanel>();
            panel.Toggle();
            yield return new WaitForSecondsRealtime(0.3f);
            typeof(ExperimenterPanel).GetMethod("Restart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panel, null);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneSetup && SceneIdle && !PauseManager.Instance.IsPaused, 30f);
            yield return new WaitForSecondsRealtime(3f);
            var playing = PlayingVoices();
            Check(sm.Current == Stage.Setup && playing.Count == 0 && NarrativeVoiceDirector.Instance.QueueLength == 0 && !HalcyonDirector.Instance.IsSpeaking, $"Restart mid-line: Setup, no voice playing ({string.Join(",", playing)}), queues empty");
            var cnt = CountObjects();
            Check(cnt.rigs == 1 && cnt.listeners == 1, $"After the restart: one rig, one AudioListener ({Describe(cnt)})");
            HeightCheck("after the restart");

            // ================================================================ 3. Jump to Stage forward and back
            var seq = new[] { Stage.Block2, Stage.EarCheck, Stage.CoolantRouted, Stage.Awakening, Stage.Escaped, Stage.TrailFollowed, Stage.Block1, Stage.Setup };
            Counts first = default, last = default;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                foreach (var s in seq)
                {
                    StageJump.Jump(s);
                    bool smoke = s == Stage.Block1 || s == Stage.Block2;
                    yield return RealUntil(() => SceneIdle && SceneManager.GetActiveScene().name == (smoke ? DS.SceneSmokeRoom : StageJump.SceneFor(s)), 45f);
                    yield return new WaitForSecondsRealtime(1.2f);
                    var pv = PlayingVoices(); var stale = new List<string>();
                    foreach (var id in pv) if (VoiceRules.IsStale(id, s)) stale.Add(id);
                    var c = CountObjects();
                    int activeRigs = ActiveCount<PlayerRig>(), activeListeners = ActiveCount<AudioListener>();
                    bool ok = (sm.Current == s || (s == Stage.Escaped && sm.Current == Stage.Questionnaire)) && activeRigs == 1 && activeListeners == 1 && stale.Count == 0
                        && SceneManager.sceneCount == 1 && RenderSettings.fog == smoke
                        && OxygenSystem.Instance.Paused == smoke
                        && (smoke ? EgressBlockController.Instance != null : EgressBlockController.Instance == null);
                    if (cycle == 0 || !ok) Check(ok, $"Jump to {s}: stage {sm.Current}, scenes {SceneManager.sceneCount}, fog {RenderSettings.fog}, oxygen paused {OxygenSystem.Instance.Paused}, active rigs {activeRigs}, active listeners {activeListeners}, stale voices [{string.Join(",", stale)}]");
                    var rig = PlayerRig.Instance;
                    if (!smoke) HeightCheck("after the jump to " + s);
                    if (smoke && EgressBlockController.Instance != null) { yield return sp.Wait(0.3f); }
                }
                if (cycle == 0) first = CountObjects(); else last = CountObjects();
            }
            Check(last.rigs == first.rigs && last.sources == first.sources && last.voiceSubs == first.voiceSubs && last.sfxSubs == first.sfxSubs && last.stageSubs == first.stageSubs && last.arrivedSubs == first.arrivedSubs,
                $"Two full jump cycles leave no stale objects or listeners ({Describe(first)} | {Describe(last)})");

            // ================================================================ 4. keypad idle 30 s (R041)
            StageJump.Jump(Stage.EarCheck);
            yield return RealUntil(() => SceneIdle && SceneManager.GetActiveScene().name == DS.SceneMedicalBay, 30f);
            yield return new WaitForSecondsRealtime(1f);
            var tk = FindAnyObjectByType<ThermalKeypad>();
            float r041 = -1f, begun = Time.time;
            System.Action<string> hook = id => { if (id == "R041" && r041 < 0f) r041 = Time.time - begun; };
            VoiceBus.Requested += hook;
            Time.timeScale = 4f;
            tk.Begin(); begun = Time.time;
            yield return RealUntil(() => r041 >= 0f, 20f);
            Time.timeScale = 1f;
            VoiceBus.Requested -= hook;
            Check(r041 > 29f && r041 < 31.5f, $"Keypad idle: R041 requested {r041:F1} s after the keypad opened (expected 30)");

            // ================================================================ 5. wrong plug during a voice line
            StageJump.Jump(Stage.EarCheck);
            yield return RealUntil(() => SceneIdle && SceneManager.GetActiveScene().name == DS.SceneMedicalBay, 30f);
            yield return new WaitForSecondsRealtime(1f);
            sm.JumpTo(Stage.Block2); EgressBlockController.MarkCompleted(2); // both blocks done, back at the rack (as after the second TLX)
            yield return RealUntil(() => FindAnyObjectByType<CoolantRack>() != null && FindAnyObjectByType<CoolantRack>().Current == CoolantRack.State.Active, 10f);
            yield return new WaitForSecondsRealtime(1f);
            var rack = FindAnyObjectByType<CoolantRack>();
            yield return sp.TeleportTo(new Vector3(1.5f, 0f, 0.9f));
            var by = new Dictionary<string, CoolantCable>(); foreach (var cb in rack.Cables) by[cb.CableName] = cb;
            bool wrongSfx = false; System.Action<SfxEvent, Vector3, int> sh = (e, p, i) => { if (e == SfxEvent.WrongPlug) wrongSfx = true; };
            SfxEvents.Raised += sh;
            VoiceBus.Request("R153");
            yield return RealUntil(() => NarrativeVoiceDirector.Instance.IsSpeaking, 10f);
            string before = NarrativeVoiceDirector.Instance.Current; rose = RoseSource(); float rt0 = rose.time;
            float lockStart = Time.time;
            yield return PlugBy(sp, by["Blue"], 1);
            bool lockedDuring = rack.Locked;
            yield return RealUntil(() => !rack.Locked, 8f);
            float lockLen = Time.time - lockStart;
            bool lineAlive = NarrativeVoiceDirector.Instance.IsSpeaking || NarrativeVoiceDirector.Instance.Played.FindAll(x => x == "R153").Count == 1;
            Check(wrongSfx && lockedDuring && rack.WrongPlugs >= 1, $"Wrong plug during {before}: S062 raised, lockout active, {rack.WrongPlugs} wrong plug(s)");
            Check(lockLen > 2.5f && lockLen < 6f, $"Lockout ended after about 3 s (measured {lockLen:F1} s including the grab)");
            yield return RealUntil(() => !NarrativeVoiceDirector.Instance.IsSpeaking, 20f);
            int r153 = NarrativeVoiceDirector.Instance.Played.FindAll(x => x == "R153").Count;
            Check(lineAlive && r153 == 1 && rose.time >= rt0, $"The voice line was not cut or repeated by the wrong plug (R153 played {r153} time)");
            SfxEvents.Raised -= sh;
            yield return PlugBy(sp, by["Red"], 2);
            Check(by["Red"].Plugged, "After the lockout the correct plug (red to 2) works");

            // ================================================================ 6. reload a scene twice
            Counts a = default, b2 = default;
            for (int k = 0; k < 2; k++)
            {
                SceneFlow.Instance.LoadSceneForJump(DS.SceneMedicalBay);
                yield return RealUntil(() => SceneIdle && SceneManager.GetActiveScene().name == DS.SceneMedicalBay, 30f);
                yield return new WaitForSecondsRealtime(1f);
                var c = CountObjects();
                int aw = FindObjectsByType<AwakeningController>(FindObjectsInactive.Include).Length, kp = FindObjectsByType<ThermalKeypad>(FindObjectsInactive.Include).Length, rk = FindObjectsByType<CoolantRack>(FindObjectsInactive.Include).Length;
                Check(c.rigs == 1 && c.listeners == 1 && aw == 1 && kp == 1 && rk == 1, $"Reload {k + 1} of SCN_MedicalBay: one rig, one listener, one AwakeningController/ThermalKeypad/CoolantRack ({aw}/{kp}/{rk})");
                HeightCheck("after reload " + (k + 1));
                if (k == 0) a = c; else b2 = c;
            }
            Check(a.sources == b2.sources && a.lights == b2.lights && a.voiceSubs == b2.voiceSubs && a.stageSubs == b2.stageSubs && a.arrivedSubs == b2.arrivedSubs, $"Second reload equals the first ({Describe(a)} | {Describe(b2)})");

            // ================================================================ 7. real 4-minute block timeout (last: it takes four game minutes)
            StageJump.Jump(Stage.Block1);
            yield return RealUntil(() => SceneIdle && EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 45f);
            var eg = EgressBlockController.Instance;
            Check(eg != null && Mathf.Approximately(eg.TimeoutSeconds, 240f), $"Block timeout is 240 s ({(eg != null ? eg.TimeoutSeconds : 0f)})");
            float t0 = Time.time;
            Time.timeScale = 4f;
            yield return RealUntil(() => eg.Finished, 150f);
            Time.timeScale = 1f;
            float took = Time.time - t0;
            Check(eg.Finished && eg.TimedOut && took > 238f && took < 243f, $"Idle player: block ends by timeout after {took:F1} game s (expected 240)");
            yield return new WaitForSecondsRealtime(8f);
            Check(eg.Mentor.Played.Contains("R207") && eg.Mentor.Played.Contains("R210"), "Timeout block played the idle nudge R207 and the timeout line R210");
            yield return RealUntil(() => SceneIdle && SceneManager.GetActiveScene().name == DS.SceneMedicalBay, 40f);
            yield return new WaitForSecondsRealtime(1f);
            HeightCheck("after the timeout");
            Check(TelemetryLogger.Instance.Rows.ContainsKey(1) && TelemetryLogger.Instance.Rows[1]["timeout_flag"] != "" , "Timeout block wrote its telemetry row with the timeout flag");

            // ================================================================ telemetry exported twice: no duplicate rows
            var tel = TelemetryLogger.Instance;
            string f1 = tel.Flush(); string f2 = tel.Flush();
            int lines = 0; string path = tel.FilePath;
            if (System.IO.File.Exists(path)) foreach (var l in System.IO.File.ReadAllLines(path)) foreach (var kv in tel.Rows) if (l.StartsWith(kv.Value["participant_id"] + "," + kv.Value["session_id"] + ",") && l.Split(',')[3] == kv.Key.ToString()) lines++;
            Check(lines == tel.Rows.Count, $"Telemetry flushed twice: {lines} CSV row(s) for participant 4, {tel.Rows.Count} block row(s) in memory");
            string e1 = SaveLoad.Instance.Export(), e2 = SaveLoad.Instance.Export();
            Check(!string.IsNullOrEmpty(e1) && e1 == e2, "Session export twice writes the same single file");

            _watchOverlap = false;
            Check(_bothSpeaking == 0, $"Rose and HALCYON never spoke at the same time during the edge cases ({_bothSpeaking} frames)");
            Note($"Timeline errors {tl.Errors}, warnings (ours) {tl.Warnings}");
            tl.End();
        }
    }
}
#endif
