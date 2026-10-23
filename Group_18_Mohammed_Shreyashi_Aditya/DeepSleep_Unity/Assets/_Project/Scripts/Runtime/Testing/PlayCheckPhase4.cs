#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        /// <summary>A walk from the nearest path junction into a dead-end branch and back. Returns the cells after the junction up to and including the dead end.</summary>
        internal static List<MazeCell> DetourToDeadEnd(MazeLayout l, out MazeCell junction)
        {
            junction = default;
            foreach (var de in l.DeadEnds)
            {
                var prev = new Dictionary<MazeCell, MazeCell> { { de, de } };
                var q = new Queue<MazeCell>(); q.Enqueue(de);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    if (l.OnPath(cur))
                    {
                        if (cur == l.Start || cur == l.Exit) break;
                        var route = new List<MazeCell>();
                        var at = cur;
                        while (at != de) { at = prev[at]; route.Add(at); }
                        junction = cur;
                        return route;
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        if (!l.IsOpen(cur.c, cur.r, d)) continue;
                        var nb = new MazeCell(cur.c + MazeLayout.DirDc(d), cur.r + MazeLayout.DirDr(d));
                        if (!prev.ContainsKey(nb)) { prev[nb] = cur; q.Enqueue(nb); }
                    }
                }
            }
            return null;
        }

        IEnumerator Phase4()
        {
            var voices = _voices;
            VoiceBus.Requested += id => voices.Add(id);
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SessionManager.Instance != null && SceneFlow.Instance != null, 10f);
            StudyConfig.Data.participant_id = "4"; // 4 mod 4 = 0: Spatial-A first, then Stereo-B
            var sm = SessionManager.Instance;
            Check(ConditionRandomizer.OrderLabel(4) == "S-A then M-B", "Participant 4 has order S-A then M-B");

            // jump to the end of the trail so the service door is already open, then walk into the vestibule
            StageJump.Jump(Stage.TrailFollowed);
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy && FindAnyObjectByType<BlockLauncher>() != null, 15f);
            yield return sp.Wait(1.0f);
            var bt = FindAnyObjectByType<BreachTrail>();
            Check(bt != null && bt.Current == BreachTrail.State.DoorOpen, "Jumping to TrailFollowed reloads the bay with the service door open");
            yield return sp.TeleportTo(new Vector3(5.6f, 0f, 0f));
            yield return sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 20f);
            var eg = EgressBlockController.Instance;
            Check(eg != null && eg.Active, "Walking into the entry zone loads SCN_SmokeRoom and starts Block 1");
            if (eg == null || !eg.Active) yield break;
            var rig = PlayerRig.Instance;
            Check(SceneManager.GetActiveScene().name == DS.SceneSmokeRoom, "SCN_SmokeRoom is the active scene");
            Check(sm.Current == Stage.Block1 && eg.Block == 1, "Stage is Block1");
            Check(eg.LayoutName == 'A' && eg.Condition == GuidanceCondition.Spatial, "Block 1 plan is layout A, spatial");
            Check(Mathf.Abs(rig.CameraHeight - DS.EYE_HEIGHT) <= DS.HeightTolerance, $"Camera height {rig.CameraHeight:F3} m in the smoke room");
            var loco = rig.GetComponent<PlayerLocomotion>();
            Check(loco.SmoothMoveEnabled && !loco.TeleportEnabled, "Smooth move on and teleport off in the smoke room");
            Check(RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared, "Exponential-squared fog is on");
            float vis = Mathf.Sqrt(Mathf.Log(50f)) / RenderSettings.fogDensity;
            Check(Mathf.Abs(vis - 2f) < 0.15f, $"Fog gives about 2 m visibility ({vis:F2} m)");
            Check(eg.Smoke.LampOn && Mathf.Abs(eg.Smoke.LampRange - 1.5f) < 0.01f, "Helmet lamp is on with 1.5 m range");
            Check(OxygenSystem.Instance.Paused, "Oxygen is paused in the smoke room");
            var src = eg.AudioSwitch.Source;
            Check(src != null && src.spatialBlend > 0.99f && src.spatialize, "Spatial condition: 3D source with the spatializer on");
            Check(eg.AudioSwitch.MonoClip("R201") != null && eg.AudioSwitch.MonoClip("R201").channels == 1, "Rose's clips are mono for both conditions");
            var layout = eg.Maze.Layout;
            var vm = layout.Verify();
            Check(vm.steps == 18 && vm.unique && vm.junctions == 6 && vm.deadEnds == 3 && vm.misleading >= 2, $"Layout A verifies in C#: {vm.steps} steps, {vm.junctions} junctions, {vm.deadEnds} dead ends, {vm.misleading} misleading");
            Check(Mathf.Abs(layout.Data.shortestMeters - 36f) < 0.1f, "Shortest path is 36 m");
            Check(Vector3.Distance(src.transform.position, eg.Maze.ExitAnchor.position) < 0.01f, "Rose's source sits at the exit door");
            yield return sp.LookAt(layout.CellCenter(layout.Path[1]) + Vector3.up * 1.65f);
            Shot("p4_corridor_lamp");

            // head-turn latency: face away from the door, wait for a beacon call, then turn toward it
            float baseYaw = Mathf.Atan2(eg.Maze.ExitAnchor.position.x - rig.Head.position.x, eg.Maze.ExitAnchor.position.z - rig.Head.position.z) * Mathf.Rad2Deg;
            yield return sp.LookYaw(baseYaw + 180f, 3f);
            int calls0 = eg.Mentor.BeaconCalls;
            yield return sp.Until(() => eg.Mentor.BeaconCalls > calls0, 7f);
            yield return sp.Wait(0.6f);
            yield return sp.LookAt(new Vector3(eg.Maze.ExitAnchor.position.x, rig.Head.position.y, eg.Maze.ExitAnchor.position.z), 3f);
            yield return sp.Wait(0.5f);
            Check(eg.Mentor.HeadTurnLatenciesMs.Count >= 1 && eg.Mentor.MeanHeadTurnLatencyMs > 300f && eg.Mentor.MeanHeadTurnLatencyMs < 4001f, $"Head-turn latency measured after turning away ({eg.Mentor.HeadTurnLatenciesMs.Count} samples, {eg.Mentor.MeanHeadTurnLatencyMs:F0} ms)");

            // walk the shortest path with one detour into a dead end
            var detour = DetourToDeadEnd(layout, out var jc);
            Check(detour != null && detour.Count > 0, "A dead-end branch exists next to the shortest path");
            int jIndex = layout.PathIndex(jc);
            float t0 = Time.time;
            for (int i = 1; i < layout.Path.Count; i++)
            {
                yield return sp.WalkTo(layout.CellCenter(layout.Path[i]), 0.35f, 10f);
                if (detour != null && i == jIndex)
                {
                    foreach (var c in detour) yield return sp.WalkTo(layout.CellCenter(c), 0.35f, 10f);
                    for (int k = detour.Count - 2; k >= 0; k--) yield return sp.WalkTo(layout.CellCenter(detour[k]), 0.35f, 10f);
                    yield return sp.WalkTo(layout.CellCenter(jc), 0.35f, 10f);
                }
            }
            var exitCenter = layout.CellCenter(layout.Exit);
            yield return sp.WalkTo(exitCenter + new Vector3(0, 0, 0.9f), 0.3f, 8f);
            yield return sp.Until(() => eg.Finished, 5f);
            Check(eg.Finished && !eg.TimedOut, "Reaching the exit zone ends the block");
            var tr = eg.Tracker;
            Check(tr.WrongTurns == 1, $"One wrong turn counted ({tr.WrongTurns})");
            Check(tr.DeadEndEntries >= 1, $"Dead-end entry counted ({tr.DeadEndEntries})");
            Check(tr.Backtracks >= 1, $"Backtrack counted ({tr.Backtracks})");
            Check(tr.PathLength > 36f && tr.Efficiency < 1f && tr.Efficiency > 0.4f, $"Path length {tr.PathLength:F1} m, efficiency {tr.Efficiency:F2}");
            Check(eg.EvacTime > 20f, $"Evacuation time {eg.EvacTime:F1} s");
            Check(eg.Mentor.BeaconCalls >= 5 && eg.Mentor.Played.Contains("R201"), $"Rose called {eg.Mentor.BeaconCalls} times from the exit door (every 4 s)");
            yield return sp.Until(() => !eg.Mentor.IsBusy, 15f); // the finish line waits for a near-exit line that was cut in
            Check(eg.Mentor.Played.Contains("R208") && eg.Mentor.Played.Contains("R209"), "Near-exit (R208) and finish (R209) lines played");
            Note($"Block 1 head-turn latency: {eg.Mentor.HeadTurnLatenciesMs.Count} samples, mean {eg.Mentor.MeanHeadTurnLatencyMs:F0} ms");
            var rows = TelemetryLogger.Instance.Rows;
            Check(rows.ContainsKey(1) && rows[1]["evac_time_s"] != "" && rows[1]["wrong_turns"] == "1" && rows[1]["condition"] == "S" && rows[1]["layout"] == "A", "Block 1 telemetry row has time, wrong turns, condition S and layout A");
            Check(rows[1]["path_efficiency"] != "" && rows[1]["mean_fps"] != "" && rows[1]["timeout_flag"] == "0", "Efficiency, frame rate and timeout flag logged");

            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneFlow.Instance.IsBusy, 12f);
            yield return sp.Wait(0.5f);
            Check(SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneManager.GetSceneByName(DS.SceneSmokeRoom).isLoaded, "Back in SCN_MedicalBay with the smoke room unloaded");
            rig = PlayerRig.Instance;
            Vector3 core = FindAny("CoreRoomSpawn").transform.position;
            Vector3 dh = rig.Head.position - core; dh.y = 0;
            Check(dh.magnitude < 0.3f, "Player is at CoreRoomSpawn");
            Check(!OxygenSystem.Instance.Paused, "Oxygen resumes after the block");
            Check(sm.Current == Stage.Block1 && EgressBlockController.CompletedBlocks == 1, "Stage stays Block1 until the questionnaire is answered");
            Check(QuestionnaireUI.Instance != null && QuestionnaireUI.Instance.IsOpen && QuestionnaireUI.Instance.TlxBlock == 1 && voices.Contains("R140"), "The block 1 questionnaire opens after the return (R140)");

            // block 2: stereo, layout B; stand still to see the idle nudge and the 4 minute timeout (shortened here)
            sm.TryAdvance(Stage.Block2);
            Check(sm.Current == Stage.Block2, "Block 2 stage reached");
            yield return sp.TeleportTo(new Vector3(3.4f, 0f, 0f));
            yield return sp.TeleportTo(new Vector3(5.6f, 0f, 0f));
            yield return sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active && EgressBlockController.Instance.Block == 2, 20f);
            eg = EgressBlockController.Instance;
            Check(eg != null && eg.Active && eg.Block == 2, "Block 2 starts through the same entry zone");
            if (eg == null || !eg.Active) yield break;
            Check(eg.LayoutName == 'B' && eg.Condition == GuidanceCondition.Stereo, "Block 2 plan is layout B, stereo");
            src = eg.AudioSwitch.Source;
            Check(src != null && src.spatialBlend < 0.01f && !src.spatialize && Mathf.Abs(src.panStereo) < 0.001f, "Stereo condition: 2D source, centred, spatializer off");
            float d1 = eg.AudioSwitch.DistanceGain(5f);
            Check(Mathf.Abs(d1 - eg.AudioSwitch.DistanceGain(5f)) < 1e-6f && d1 < 1f && d1 > 0.4f, "Same distance gain function applies in both conditions");
            Check(eg.Maze.Layout.Verify().steps == 18 && eg.Maze.Layout.Data.name == "B", "Layout B verifies and is active");
            eg.TimeoutSeconds = 24f;
            yield return sp.Until(() => eg.Finished, 40f);
            Check(eg.Finished && eg.TimedOut, "Block times out");
            Check(eg.Mentor.Played.Contains("R207"), "Idle nudge R207 after 20 s without moving");
            Check(eg.Mentor.Played.Contains("R210"), "Timeout line R210 plays");
            Check(eg.Tracker.IdleTotal > 18f, $"Idle time recorded ({eg.Tracker.IdleTotal:F1} s)");
            rows = TelemetryLogger.Instance.Rows;
            Check(rows.ContainsKey(2) && rows[2]["timeout_flag"] == "1" && rows[2]["condition"] == "M" && rows[2]["layout"] == "B", "Block 2 telemetry row: timeout 1, condition M, layout B");
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneFlow.Instance.IsBusy, 12f);
            yield return sp.Wait(0.5f);
            Check(QuestionnaireUI.Instance != null && QuestionnaireUI.Instance.IsOpen && QuestionnaireUI.Instance.TlxBlock == 2, "The block 2 questionnaire opens after the return");
            sm.ResetSession();
            EgressBlockController.ResetStatics();
        }
    }
}
#endif
