#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Scripted playthrough of the whole game on the Editor fallback rig, through InputService only (via ScriptedPlayer).
    /// DECISIONS: ear error 10 degrees, wrong-turn rate 0.15 per junction, 8 participants. Rows go to deepsleep_telemetry_bot.csv (persistentDataPath)
    /// and Analysis/data/bot_telemetry.csv. The bot does not react to the guidance condition: it only exercises every path and fills every telemetry field.
    /// A few participants do something extra so that every line of the voice manifest is reached (idle at the tray, long wait at the keypad, one wrong
    /// keypad press, a wrong plug, one block left to time out).
    /// </summary>
    public class PlaythroughBot : MonoBehaviour
    {
        [SerializeField] float earErrorDegrees = 10f;
        [SerializeField] float wrongTurnRate = 0.15f;
        [SerializeField] int participants = 8;
        [SerializeField] float timeScale = 3f;

        public float EarErrorDegrees => earErrorDegrees;
        public float WrongTurnRate => wrongTurnRate;
        public int Participants => participants;
        public readonly HashSet<string> Covered = new HashSet<string>();

        PlayCheckRunner _h;
        ScriptedPlayer _sp;
        System.Random _rng;
        int _n;
        int _wrongTurnsMade;
        int _x;          // participant number used for the coverage extras; -1 = plain run (no extras)
        bool _pace;      // listen to every voice line before the next action (human pace)
        bool _human;     // reading, looking and hesitating like a person (the recorded run)

        IEnumerator Dwell(float seconds)
        {
            if (_human) yield return _sp.Wait(seconds);
        }

        /// <summary>Human pace: wait until Rose and HALCYON have finished (and nothing is queued) before acting.</summary>
        IEnumerator Listen(float max = 30f)
        {
            if (!_pace) yield break;
            yield return _sp.Wait(0.3f);
            yield return _sp.Until(() =>
            {
                var r = NarrativeVoiceDirector.Instance; var h = HalcyonDirector.Instance;
                return (r == null || !r.IsBusy) && (h == null || (!h.IsSpeaking && h.OldestWaitingSince < 0f));
            }, max);
        }

        IEnumerator ListenFor(string id, float max = 30f)
        {
            if (!_pace) yield break;
            yield return _sp.Until(() => (NarrativeVoiceDirector.Instance != null && NarrativeVoiceDirector.Instance.Played.Contains(id)) || (HalcyonDirector.Instance != null && HalcyonDirector.Instance.Played.Contains(id)), max);
            yield return Listen(max);
        }

        static string DataDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Analysis", "data"));

        public IEnumerator Run(PlayCheckRunner host, int first, int last)
        {
            _h = host; _sp = ScriptedPlayer.Get();
            Directory.CreateDirectory(DataDir);
            TelemetryLogger.FileNameOverride = "deepsleep_telemetry_bot.csv";
            ProfilerCapture.FileNameOverride = "profiler_log_bot.csv";
            ProfilerCapture.Enabled = true;
            VoiceBus.Requested += id => Covered.Add(id);
            string covFile = Path.Combine(DataDir, "bot_coverage.txt");
            if (first <= 1)
            {
                foreach (var f in new[] { Path.Combine(DataDir, "bot_telemetry.csv"), covFile, Path.Combine(Application.persistentDataPath, "deepsleep_telemetry_bot.csv"), Path.Combine(Application.persistentDataPath, "profiler_log_bot.csv") })
                    if (File.Exists(f)) File.Delete(f);
            }
            else if (File.Exists(covFile)) foreach (var l in File.ReadAllLines(covFile)) if (l.Length > 0) Covered.Add(l);
            if (UnityEditor.SessionState.GetFloat("DS_bot_ts", 0f) > 0f) timeScale = UnityEditor.SessionState.GetFloat("DS_bot_ts", timeScale);
            Time.timeScale = timeScale;
            _h.Note($"Bot: participants {first} to {last}, ear error {earErrorDegrees} deg, wrong-turn rate {wrongTurnRate}, time scale {timeScale}");
            for (int n = first; n <= last; n++)
            {
                yield return Participant(n);
                File.WriteAllLines(covFile, new List<string>(Covered));
            }
            Time.timeScale = 1f;
            ProfilerCapture.Instance?.Flush();
            string prof = Path.Combine(Application.persistentDataPath, "profiler_log_bot.csv");
            if (File.Exists(prof)) File.Copy(prof, Path.Combine(DataDir, "bot_profiler_log.csv"), true);
            if (last >= participants || UnityEditor.SessionState.GetBool("DS_bot_cov", false)) CheckCoverage();
        }

        // ------------------------------------------------------------------ one participant
        IEnumerator Participant(int n)
        {
            _n = n; _rng = new System.Random(5000 + n); _wrongTurnsMade = 0;
            _pace = UnityEditor.SessionState.GetBool("DS_bot_pace", false);
            _human = UnityEditor.SessionState.GetBool("DS_bot_human", false);
            _x = UnityEditor.SessionState.GetBool("DS_bot_plain", false) ? -1 : n;
            StudyConfig.Data.participant_id = "B" + n;
            var sm = SessionManager.Instance;
            sm.ResetSession(); EgressBlockController.ResetStatics();
            TelemetryLogger.Instance.ResetAll(); OxygenSystem.Instance.ResetTimer(); SaveLoad.Instance.ResetDebrief();
            _h.Note($"--- participant {n} ({ConditionRandomizer.OrderLabel(n)}) ---");
            VoiceLatency.Reset();
            FlowTimeline tl = null;
            if (UnityEditor.SessionState.GetBool("DS_bot_timeline", false))
                tl = FlowTimeline.Begin(Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Docs", "Reports", "timeline", $"timeline_{(UnityEditor.SessionState.GetString("DS_bot_timeline_name", "") is { Length: > 0 } nm ? nm : "P" + n)}.csv")));
            SceneManager.LoadScene(DS.SceneMain, LoadSceneMode.Single);
            yield return null; yield return null;

            yield return SetupFlow();
            yield return AwakeningFlow();
            yield return EarCheckFlow();
            yield return KeypadFlow();
            yield return TrailFlow();
            for (int b = 1; b <= 2; b++)
            {
                yield return BlockFlow(b);
                yield return TlxFlow(b);
            }
            yield return CoolantFlow();
            yield return HallwayFlow();
            yield return CollectAndCheck(n);
            string latDir = Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Docs", "Reports", "timeline"));
            Directory.CreateDirectory(latDir);
            File.WriteAllText(Path.Combine(latDir, $"voice_latency_P{n}{(_human ? "_realtime" : "")}.csv"), VoiceLatency.ToCsv());
            if (tl != null)
            {
                _h.Note($"P{n}: timeline written, console errors {tl.Errors}, warnings {tl.Warnings}" + (tl.ErrorTexts.Count > 0 ? "; first error: " + tl.ErrorTexts[0] : "") + (tl.WarningTexts.Count > 0 ? "; first warning: " + tl.WarningTexts[0] : ""));
                _h.Check(tl.Errors == 0, $"P{n}: zero console errors during the run");
                _h.Check(tl.Warnings == 0, $"P{n}: zero console warnings during the run" + (tl.WarningTexts.Count > 0 ? " (" + tl.WarningTexts[0] + ")" : ""));
                tl.End();
            }
        }

        // ------------------------------------------------------------------ flows
        IEnumerator SetupFlow()
        {
            yield return _sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && FindAnyObjectByType<SetupController>() != null, 15f);
            var sc = FindAnyObjectByType<SetupController>();
            yield return _sp.Wait(0.5f);
            yield return Dwell(25f); // reads the consent text
            yield return _h.ClickByName(_sp, "Btn_Continue");   // consent -> height
            yield return Dwell(10f);
            sc.OnNext();                                        // height -> audio check
            yield return Dwell(10f);
            sc.OnNext();                                        // plays the one beep, then the comfort page
            yield return _sp.Until(() => sc.Current == SetupController.Page.Comfort, 15f);
            yield return Dwell(10f); // comfort page
            var slider = PlayCheckRunner.FindAny("Slider_Volume");
            if (slider != null) yield return _sp.ClickRay(slider.transform.TransformPoint(new Vector3(0.49f, 0, 0)));
            Covered.Add("S002");
            sc.OnNext();
            yield return _sp.Until(() => SceneFlow.Section == DS.SceneMedicalBay && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy && FindAnyObjectByType<AwakeningController>() != null, 25f);
        }

        IEnumerator AwakeningFlow()
        {
            var aw = FindAnyObjectByType<AwakeningController>();
            yield return _sp.Wait(0.6f);
            yield return ListenFor("H001"); // R010 and H001
            yield return Dwell(12f); // looks around the damaged Medical Bay
            if (_x == 3) yield return _sp.Until(() => HalcyonDirector.Instance != null && HalcyonDirector.Instance.Played.Contains("H002"), 40f);
            yield return _sp.TeleportTo(new Vector3(-1.3f, 0f, -1.7f));
            if (_x == 1) yield return _sp.Wait(7f); // R011 then R012 five seconds later
            if (_pace) { yield return ListenFor("R011"); yield return ListenFor("R012", 15f); }
            yield return Dwell(5f);
            yield return _sp.GrabObject(aw.Cup);
            yield return _sp.Wait(0.4f);
            yield return _sp.ReleaseGrip();
            yield return _sp.Until(() => SessionManager.Instance.Current == Stage.EarCheck, 6f);
        }

        IEnumerator EarCheckFlow()
        {
            var ec = FindAnyObjectByType<EarCheckController>();
            yield return _sp.Until(() => ec.Active, 30f);
            yield return _sp.TeleportTo(new Vector3(-1.25f, 0f, -0.5f));
            yield return Dwell(8f); // reads the poster
            for (int slot = 0; slot < 6; slot++)
            {
                var b = System.Array.Find(ec.Beacons, x => x.Index == ec.Order[slot]);
                if ((_human && slot == 0) | (_rng.NextDouble() < wrongTurnRate)) // the recorded run always places the wrong beacon first
                {
                    // a slip: the beacon goes on the wrong slot first and comes back
                    int other = (slot + 1) % 6;
                    if (ec.Order[other] != b.Index && ec.Slots[other] != null && ec.PlacedCount <= other)
                        yield return _sp.PlaceGrabbable(b.Grabbable, ec.Slots[other].position);
                    yield return _sp.Wait(1.2f);
                }
                yield return Dwell(2f);
                yield return _sp.PlaceGrabbable(b.Grabbable, ec.Slots[slot].position);
                yield return _sp.Until(() => ec.PlacedCount >= slot + 1, 4f);
            }
            yield return _sp.Until(() => ec.Completed, 8f);
        }

        IEnumerator KeypadFlow()
        {
            var tk = FindAnyObjectByType<ThermalKeypad>();
            yield return _sp.Until(() => tk.Current == ThermalKeypad.State.Ready, 40f);
            yield return _sp.LookAt(tk.Key(5).position);
            yield return ListenFor("R040");
            yield return Dwell(20f); // studies the glowing keys
            if (_x == 2) yield return _sp.Wait(32f); // idle at the keypad: R041
            if (_x == 5) { yield return _sp.Poke(tk.Key(1).position, Vector3.left); yield return _sp.Wait(2.6f); } // one wrong press: H004
            foreach (int k in tk.Sequence) { yield return Dwell(3f); yield return _sp.Poke(tk.Key(k).position, Vector3.left); yield return _sp.Wait(0.3f); }
            yield return _sp.Until(() => SessionManager.Instance.Current == Stage.KeypadSolved, 6f);
            yield return ListenFor("R043");
        }

        IEnumerator TrailFlow()
        {
            var bt = FindAnyObjectByType<BreachTrail>();
            yield return _sp.Until(() => bt.Current == BreachTrail.State.TrailVisible, 10f);
            yield return _sp.Wait(1.5f);
            yield return ListenFor("R050");
            yield return Dwell(10f); // follows the glowing marks along the floor
            yield return _sp.TeleportTo(bt.PanelStandPoint(), 0.6f); // hand raised: the terminal is in the way of a low ray
            yield return _sp.Until(() => bt.Current == BreachTrail.State.PanelOpen, 6f);
            yield return _sp.Wait(2.2f);
            yield return Dwell(8f);
            _sp.SetHandPos(bt.Handle.position);
            yield return _sp.Frames(3);
            _sp.SetGrip(true);
            yield return _sp.Frames(4);
            yield return _sp.MoveHand(bt.Handle.position + Vector3.left * 0.25f, 0.5f);
            yield return _sp.Frames(4);
            yield return _sp.ReleaseGrip();
            yield return _sp.Until(() => SessionManager.Instance.Current == Stage.TrailFollowed, 8f);
            yield return ListenFor("R052");
        }

        IEnumerator BlockFlow(int block)
        {
            yield return _sp.Wait(0.5f);
            yield return Dwell(8f);
            yield return _sp.TeleportTo(new Vector3(3.4f, 0f, 0f));
            yield return _sp.TeleportTo(new Vector3(5.6f, 0f, 0f));
            yield return _sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active && EgressBlockController.Instance.Block == block, 30f);
            var eg = EgressBlockController.Instance;
            if (eg == null || !eg.Active) { _h.Fail($"Participant {_n} block {block} did not start"); yield break; }
            if (_x == 4 && block == 2)
            {
                eg.TimeoutSeconds = 26f; // one block is left to time out (idle nudge R207 and the timeout line R210)
                yield return _sp.Until(() => eg.Finished, 60f);
            }
            else
            {
                if (_x == 1 && block == 1) yield return BumpWall(eg); // S020 wall contact
                yield return WalkMaze(eg, _x == 1 && block == 1);
                if (!eg.Finished) { yield return _sp.Until(() => eg.Finished, 8f); }
                if (!eg.Finished) { _h.Note($"Participant {_n} block {block}: exit not reached, forcing timeout"); eg.Finish(true); }
            }
            foreach (var id in eg.Mentor.Played) Covered.Add(id);
            yield return _sp.Until(() => SceneFlow.Section == DS.SceneMedicalBay && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy, 20f);
            yield return _sp.Wait(0.5f);
        }

        IEnumerator Walk(MazeLayout l, MazeCell c) { yield return _sp.WalkTo(l.CellCenter(c), 0.35f, 10f); yield return Dwell(3f); }

        IEnumerator BumpWall(EgressBlockController eg)
        {
            var l = eg.Maze.Layout; var s = l.Start;
            int dir = -1;
            foreach (int d in new[] { 0, 1, 3 }) if (!l.IsOpen(s.c, s.r, d)) { dir = d; break; }
            if (dir < 0) yield break;
            Vector3 c = l.CellCenter(s);
            yield return _sp.WalkTo(c + new Vector3(MazeLayout.DirDc(dir), 0, MazeLayout.DirDr(dir)) * 1.8f, 0.2f, 3.5f);
            yield return _sp.WalkTo(c, 0.35f, 4f);
        }

        IEnumerator WalkMaze(EgressBlockController eg, bool forceDeadEnd)
        {
            var l = eg.Maze.Layout; var path = l.Path;
            MazeCell forcedJ = default; List<MazeCell> forced = forceDeadEnd ? PlayCheckRunner.DetourToDeadEnd(l, out forcedJ) : null;
            int i = 1;
            while (i < path.Count && !eg.Finished)
            {
                yield return Walk(l, path[i]);
                var cell = path[i];
                if (l.IsJunction(cell)) yield return Dwell(8f); // stops at a junction to listen and decide
                if (forced != null && cell == forcedJ)
                {
                    // a guaranteed trip into a dead end so that R205 is reached
                    _wrongTurnsMade++;
                    foreach (var c in forced) yield return Walk(l, c);
                    yield return _sp.Wait(0.5f);
                    for (int k = forced.Count - 2; k >= 0; k--) yield return Walk(l, forced[k]);
                    yield return Walk(l, forcedJ);
                    forced = null;
                }
                if (l.IsJunction(cell) && _rng.NextDouble() < wrongTurnRate)
                {
                    var wrongs = new List<MazeCell>();
                    for (int d = 0; d < 4; d++)
                    {
                        if (!l.IsOpen(cell.c, cell.r, d)) continue;
                        var nb = new MazeCell(cell.c + MazeLayout.DirDc(d), cell.r + MazeLayout.DirDr(d));
                        if (nb != path[i - 1] && nb != path[i + 1]) wrongs.Add(nb);
                    }
                    if (wrongs.Count > 0)
                    {
                        _wrongTurnsMade++;
                        int jumpedTo = -1;
                        var w = wrongs[_rng.Next(wrongs.Count)];
                        var trail = new List<MazeCell> { cell };
                        var prev = cell; var cur = w;
                        while (true)
                        {
                            yield return Walk(l, cur);
                            trail.Add(cur);
                            if (l.OnPath(cur)) { int pi = l.PathIndex(cur); if (pi > i) jumpedTo = pi; break; }
                            if (l.IsDeadEnd(cur)) break;
                            var opts = new List<MazeCell>();
                            for (int d = 0; d < 4; d++)
                            {
                                if (!l.IsOpen(cur.c, cur.r, d)) continue;
                                var nb = new MazeCell(cur.c + MazeLayout.DirDc(d), cur.r + MazeLayout.DirDr(d));
                                if (nb != prev && !trail.Contains(nb)) opts.Add(nb);
                            }
                            if (opts.Count == 0) break;
                            prev = cur; cur = opts[_rng.Next(opts.Count)];
                        }
                        if (jumpedTo >= 0) { i = jumpedTo + 1; continue; }
                        for (int k = trail.Count - 2; k >= 0; k--) yield return Walk(l, trail[k]);
                    }
                }
                i++;
            }
            if (!eg.Finished) yield return _sp.WalkTo(l.CellCenter(l.Exit) + new Vector3(0, 0, 0.9f), 0.3f, 8f);
        }

        IEnumerator TlxFlow(int block)
        {
            var q = FindAnyObjectByType<QuestionnaireUI>();
            yield return _sp.Until(() => q != null && q.IsOpen && q.TlxBlock == block, 10f);
            if (block == 1) yield return ListenFor("R140");
            var v = new int[6];
            for (int i = 0; i < 6; i++) v[i] = 10 + _rng.Next(60);
            yield return Dwell(18f); // six sliders, read one by one
            yield return _h.TlxPass(_sp, q, v);
            yield return _sp.Wait(0.5f);
            if (block == 2) yield return ListenFor("R151", 40f); // R150, then R151 ten seconds later
        }

        IEnumerator CoolantFlow()
        {
            var rack = FindAnyObjectByType<CoolantRack>();
            yield return _sp.Until(() => rack.Current == CoolantRack.State.Active, 12f);
            if (_x == 6) yield return _sp.Wait(11f); // R151 comes 10 s after R150
            yield return _sp.TeleportTo(new Vector3(1.5f, 0f, 0.9f));
            var by = new Dictionary<string, CoolantCable>();
            foreach (var c in rack.Cables) by[c.CableName] = c;
            yield return Dwell(15f); // matches cable colours to the labelled ports
            if (_x == 7) { yield return _h.PlugBy(_sp, by["Blue"], 1); yield return _sp.Until(() => !rack.Locked, 8f); }
            yield return _h.PlugBy(_sp, by["Red"], 2);
            yield return Dwell(5f);
            yield return _h.PlugBy(_sp, by["Blue"], 5);
            yield return Dwell(5f);
            yield return _h.PlugBy(_sp, by["Yellow"], 3);
            yield return _sp.Until(() => rack.Current == CoolantRack.State.Solved, 5f);
            yield return ListenFor("R153");
            yield return Dwell(6f);
            sp_Lever(rack);
            yield return LeverPull(rack);
            yield return _sp.Until(() => SceneFlow.Section == DS.SceneHallway && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy, 40f);
        }

        void sp_Lever(CoolantRack rack) { _sp.SetHandPos(rack.LeverHandle.position); }

        IEnumerator LeverPull(CoolantRack rack)
        {
            yield return _sp.Frames(3);
            _sp.SetGrip(true);
            yield return _sp.Frames(4);
            yield return ListenFor("H007"); // HALCYON when the lever is grabbed
            yield return _sp.MoveHand(rack.LeverHandle.position + Vector3.down * 0.3f, 0.5f);
            yield return _sp.Frames(4);
            yield return _sp.ReleaseGrip();
        }

        IEnumerator HallwayFlow()
        {
            var q = FindAnyObjectByType<QuestionnaireUI>();
            yield return _sp.Until(() => q != null && q.IsOpen && q.Current == QuestionnaireUI.Mode.Sus, 20f);
            for (int i = 0; i < 10; i++)
            {
                int ans = (i % 2 == 0) ? 4 + _rng.Next(2) : 1 + _rng.Next(2); // positive items high, negative items low
                yield return _sp.Wait(0.3f);
                yield return Dwell(5f); // reads the statement
                for (int a = 0; a < 3 && !(q.AnsweredCurrent && q.PendingValue == ans); a++)
                {
                    yield return _h.ClickByName(_sp, "Btn_" + ans);
                    yield return _sp.Until(() => q.AnsweredCurrent && q.PendingValue == ans, 1.5f);
                }
                int pageBefore = q.Page; var modeBefore = q.Current;
                for (int a = 0; a < 3 && q.Page == pageBefore && q.Current == modeBefore; a++)
                {
                    yield return _h.ClickByName(_sp, i < 9 ? "Btn_Next" : "Btn_Finish");
                    yield return _sp.Until(() => q.Page != pageBefore || q.Current != modeBefore, 1.5f);
                }
            }
            string[] opts = { "Block 1", "Block 2", "No difference" };
            string pick = opts[_rng.Next(3)];
            yield return _sp.Wait(0.3f);
            yield return Dwell(8f);
            yield return _h.ClickByName(_sp, "Btn_" + pick);
            yield return _h.ClickByName(_sp, "Btn_Finish");
            yield return _sp.Until(() => q.Finished, 4f);
            yield return ListenFor("R161");
            yield return Dwell(6f);
        }

        // ------------------------------------------------------------------ results
        IEnumerator CollectAndCheck(int n)
        {
            var sm = SessionManager.Instance; var tel = TelemetryLogger.Instance;
            yield return _sp.Wait(0.3f);
            foreach (var id in SfxDirector.Instance.Played) Covered.Add(id);
            var expected = new[] { "Awakening", "EarCheck", "KeypadSolved", "TrailFollowed", "Block1", "Block2", "CoolantRouted", "Escaped", "Questionnaire" };
            _h.Check(string.Join(">", sm.History) == string.Join(">", expected), $"P{n}: stages ran in order ({string.Join(">", sm.History)})");
            bool rows = tel.Rows.ContainsKey(1) && tel.Rows.ContainsKey(2);
            _h.Check(rows, $"P{n}: two telemetry rows (one per block)");
            if (!rows) yield break;
            for (int b = 1; b <= 2; b++)
            {
                var empty = tel.EmptyFields(b);
                _h.Check(empty.Count == 0, $"P{n} block {b}: every telemetry field is filled" + (empty.Count > 0 ? " (empty: " + string.Join(",", empty) + ")" : ""));
                var plan = ConditionRandomizer.Plan(n, b);
                _h.Check(tel.Rows[b]["condition"] == ConditionRandomizer.Short(plan.condition) && tel.Rows[b]["layout"] == plan.layout.ToString(), $"P{n} block {b}: condition {tel.Rows[b]["condition"]} layout {tel.Rows[b]["layout"]} match the order {ConditionRandomizer.OrderLabel(n)}");
            }
            bool json = false;
            foreach (var f in Directory.GetFiles(Application.persistentDataPath, "deepsleep_session_B" + n + "_*.json")) json = true;
            _h.Check(json && File.Exists(tel.FilePath), $"P{n}: session JSON and bot CSV were written");
            _h.Note($"P{n}: wrong turns made {_wrongTurnsMade}; block times {tel.Rows[1]["evac_time_s"]} s / {tel.Rows[2]["evac_time_s"]} s; SUS {tel.Rows[1]["SUS_score"]}");
            AppendRows(tel);
        }

        void AppendRows(TelemetryLogger tel)
        {
            string path = Path.Combine(DataDir, "bot_telemetry.csv");
            var sb = new StringBuilder();
            if (File.Exists(path))
            {
                // re-running a participant replaces that participant's rows
                var keep = new List<string>();
                foreach (var line in File.ReadAllLines(path)) if (!line.StartsWith(StudyConfig.Data.participant_id + ",")) keep.Add(line);
                File.WriteAllLines(path, keep);
            }
            else sb.AppendLine(string.Join(",", TelemetryLogger.Header));
            foreach (int b in new[] { 1, 2 })
            {
                var cells = new List<string>();
                foreach (var h in TelemetryLogger.Header) cells.Add(tel.Rows[b].TryGetValue(h, out var v) ? v : "");
                sb.AppendLine(string.Join(",", cells));
            }
            File.AppendAllText(path, sb.ToString());
        }

        void CheckCoverage()
        {
            var manifest = DeepSleepAssets.Instance.voiceManifest.text.Split('\n');
            var missing = new List<string>();
            int total = 0;
            foreach (var line in manifest)
            {
                var p = line.Trim().Split(',');
                if (p.Length < 3 || p[0] == "ID") continue;
                string id = p[0], stage = p[2];
                if (stage == "none") continue;
                total++;
                if (!Covered.Contains(id)) missing.Add(id);
            }
            _h.Check(missing.Count == 0, $"Every triggered manifest ID was reached during the playthrough ({total - missing.Count}/{total})" + (missing.Count > 0 ? "; missing: " + string.Join(",", missing) : ""));
        }
    }
}
#endif
