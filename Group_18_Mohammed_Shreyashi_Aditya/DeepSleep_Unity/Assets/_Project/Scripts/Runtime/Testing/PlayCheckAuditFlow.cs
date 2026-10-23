#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>Audit scenario for the general sweep (section C): stage flow abuse, player, puzzles, restart, leaks, rendering stats.</summary>
    public partial class PlayCheckRunner
    {
        IEnumerator RealUntil(System.Func<bool> cond, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        bool SceneIdle => SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy;

        struct Counts { public int rigs, listeners, eventSystems, sources, lights, materials, roots, voiceSubs, sfxSubs, arrivedSubs, stageSubs; }

        Counts CountObjects()
        {
            var c = new Counts();
            c.rigs = FindObjectsByType<PlayerRig>(FindObjectsInactive.Include).Length;
            c.listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length;
            c.eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include).Length;
            c.sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include).Length;
            c.lights = FindObjectsByType<Light>(FindObjectsInactive.Include).Length;
            c.materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            c.roots = Bootstrap.Root != null ? Bootstrap.Root.scene.rootCount : -1;
            c.voiceSubs = VoiceBus.SubscriberCount; c.sfxSubs = SfxEvents.SubscriberCount;
            c.arrivedSubs = SceneFlow.Instance != null ? SceneFlow.Instance.SceneArrivedSubscribers : -1;
            c.stageSubs = SessionManager.Instance != null ? SessionManager.Instance.StageChangedSubscribers : -1;
            return c;
        }

        string Describe(Counts c) { return $"rigs {c.rigs}, listeners {c.listeners}, eventSystems {c.eventSystems}, audioSources {c.sources}, lights {c.lights}, materials {c.materials}, persistent roots {c.roots}, voice subscribers {c.voiceSubs}, sfx subscribers {c.sfxSubs}, SceneArrived {c.arrivedSubs}, StageChanged {c.stageSubs}"; }

        void HeightCheck(string where)
        {
            var rig = PlayerRig.Instance;
            bool ok = rig != null && Mathf.Abs(rig.CameraHeight - DS.EYE_HEIGHT) <= DS.HeightTolerance && Mathf.Abs(rig.transform.position.y) < 0.05f;
            Check(ok, $"Camera height {(rig != null ? rig.CameraHeight : 0f):F3} m and rig on the floor (y {(rig != null ? rig.transform.position.y : 0f):F3}) {where}");
        }

        IEnumerator PerfSample(string where)
        {
            yield return new WaitForSecondsRealtime(1.0f);
            float t0 = Time.realtimeSinceStartup; int frames = 0; float worst = 0;
            long dc = 0, sp = 0, tris = 0, verts = 0; int n = 0;
            while (Time.realtimeSinceStartup - t0 < 3f)
            {
                float dt = Time.unscaledDeltaTime; worst = Mathf.Max(worst, dt); frames++;
                dc += UnityEditor.UnityStats.drawCalls; sp += UnityEditor.UnityStats.setPassCalls; tris += UnityEditor.UnityStats.triangles; verts += UnityEditor.UnityStats.vertices; n++;
                yield return null;
            }
            float mean = (Time.realtimeSinceStartup - t0) / Mathf.Max(1, frames) * 1000f;
            Note($"PERF {where}: draw calls {dc / Mathf.Max(1, n)}, SetPass calls {sp / Mathf.Max(1, n)}, triangles {tris / Mathf.Max(1, n)}, vertices {verts / Mathf.Max(1, n)}, Editor frame time mean {mean:F1} ms, worst {worst * 1000f:F1} ms ({frames} frames)");
        }

        // a spot in front of the wall panel: the last waypoint itself touches the wall, where the teleport capsule check rightly refuses
        static Vector3 PanelStandPoint(BreachTrail bt)
        {
            var w = bt.Waypoints; Vector3 last = w[w.Length - 1].position, prev = w[w.Length - 2].position;
            Vector3 dir = last - prev; dir.y = 0; dir.Normalize();
            Vector3 p = last - dir * 0.6f; p.y = 0f; return p;
        }

        IEnumerator AuditFlow()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SessionManager.Instance != null && SceneFlow.Instance != null, 10f);
            StudyConfig.Data.participant_id = "4";
            var sm = SessionManager.Instance;
            int hallwayArrivals = 0; SceneFlow.Instance.SceneArrived += s => { if (s == DS.SceneHallway) hallwayArrivals++; };
            yield return sp.Wait(0.8f);
            HeightCheck("in SCN_Setup");
            yield return PerfSample("SCN_Setup");

            // ================================================================ stage machine
            Check(!sm.Advance(Stage.Block1), "Skipping a stage is rejected");
            Check(!sm.Advance(Stage.Setup), "Repeating the current stage is rejected");
            Check(sm.Advance(Stage.Awakening), "The next stage is accepted");
            Check(!sm.Advance(Stage.Awakening), "The same stage twice is rejected");
            Check(!sm.TryAdvance(Stage.Awakening), "TryAdvance to the current stage does nothing");
            int hist = sm.History.Count; sm.JumpTo(Stage.Setup); sm.JumpTo(Stage.Setup);
            Check(sm.Current == Stage.Setup, "Jumping back and repeating a jump leaves a consistent stage");
            sm.ResetSession();
            Check(sm.Current == Stage.Setup && sm.History.Count == 0, "ResetSession clears the stage and history");

            // scene reloads: twice in a row, and a pause during a load
            SceneFlow.Instance.GoToMedicalBay(); SceneFlow.Instance.GoToMedicalBay();
            PauseManager.Instance.SetPaused(true);
            yield return new WaitForSecondsRealtime(2.5f);
            PauseManager.Instance.SetPaused(false);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 25f);
            yield return new WaitForSecondsRealtime(0.8f);
            Check(SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle && Time.timeScale == 1f && !PauseManager.Instance.IsPaused, $"A pause during a scene load and a double load request end in a normal, unpaused bay (scene {SceneManager.GetActiveScene().name}, idle {SceneIdle}, timeScale {Time.timeScale}, paused {PauseManager.Instance.IsPaused})");
            var cnt = CountObjects();
            Check(cnt.rigs == 1 && cnt.listeners == 1 && cnt.eventSystems <= 1, $"After the double load: one rig, one AudioListener, {cnt.eventSystems} EventSystem ({Describe(cnt)})");
            HeightCheck("in SCN_MedicalBay");
            yield return PerfSample("SCN_MedicalBay");

            // ================================================================ player
            var rig = PlayerRig.Instance; var loco = rig.GetComponent<PlayerLocomotion>();
            Check(loco.TeleportEnabled && !loco.SmoothMoveEnabled, "Medical Bay: teleport on, smooth move off");
            yield return sp.TeleportTo(new Vector3(-1.2f, 0f, 0f));
            HeightCheck("after a teleport");
            yield return sp.SnapTurn(1); yield return sp.SnapTurn(-1);
            HeightCheck("after snap turns");
            int tc = loco.TeleportCount;
            var bed = FindAny("HospitalBed"); Bounds bb; BoundsOf("HospitalBed", out bb);
            yield return sp.TeleportTo(new Vector3(bb.center.x, bb.max.y, bb.center.z));
            Check(loco.TeleportCount == tc, "A teleport onto the bed top is refused (floor level only)");
            yield return sp.TeleportTo(new Vector3(0.5f, 0f, -0.6f));
            Check(loco.TeleportCount == tc, "A teleport into the terminal footprint is refused (never inside a prop)");
            yield return sp.TeleportTo(new Vector3(-4.3f, 0f, -1f));
            Check(loco.TeleportCount == tc, "A teleport through the west wall is refused");
            foreach (var pt in new[] { new Vector3(-2f, 0f, 1.5f), new Vector3(-1f, 0f, 2f), new Vector3(1.5f, 0f, 2f), new Vector3(2f, 0f, -2f), new Vector3(-2f, 0f, -2f) })
            {
                yield return sp.TeleportTo(pt);
                if (loco.TeleportCount > tc) break;
            }
            Check(loco.TeleportCount == tc + 1, "A normal floor teleport still works (at least one of five open floor points accepted)");
            rig.PlaceAt(new Vector3(-2f, 0f, 0.2f), 90f);
            HeightCheck("after a respawn (PlaceAt)");
            Check(Mathf.Abs(rig.transform.position.y) < 0.01f, "Rig stays on the floor");

            // ================================================================ puzzles: keypad
            sm.JumpTo(Stage.EarCheck);
            var tk = FindAnyObjectByType<ThermalKeypad>(); var bt = FindAnyObjectByType<BreachTrail>(); var rack = FindAnyObjectByType<CoolantRack>();
            Vector3 front = Vector3.left;
            yield return sp.Poke(tk.Key(1).position, front); yield return sp.Wait(0.3f);
            Check(tk.WrongAttempts == 0 && tk.Current == ThermalKeypad.State.Inactive, "Keys do nothing before the Ear Check has finished");
            Check(bt.Current == BreachTrail.State.Idle, "The trail is not visible before the keypad is solved");
            tk.Begin();
            // hold a key down: tip stays inside the key collider
            sp.SetHandPos(tk.Key(1).position + front * 0.1f); yield return sp.Frames(3);
            sp.SetHandPos(tk.Key(1).position); yield return sp.Frames(6);
            yield return new WaitForSecondsRealtime(2.8f);
            Check(tk.WrongAttempts == 1, $"A key held down counts as one press ({tk.WrongAttempts} wrong attempts)");
            yield return sp.MoveHand(tk.Key(1).position + front * 0.15f, 0.3f); yield return sp.Frames(3);
            yield return sp.Until(() => tk.Current == ThermalKeypad.State.Ready, 6f);
            Check(tk.Current == ThermalKeypad.State.Ready, "The 2 s lockout ends by itself");
            int solvedEvents = 0; tk.Solved += () => solvedEvents++;
            foreach (int k in tk.Sequence) { yield return sp.Poke(tk.Key(k).position, front); yield return sp.Wait(0.15f); }
            yield return sp.Wait(0.4f);
            yield return sp.Poke(tk.Key(5).position, front); yield return sp.Poke(tk.Key(7).position, front); yield return sp.Wait(0.3f);
            int ksCount = sm.History.FindAll(x => x == "KeypadSolved").Count;
            Check(tk.Current == ThermalKeypad.State.Solved && solvedEvents == 1 && ksCount == 1, $"Solving happens once and extra presses are ignored (solved events {solvedEvents}, KeypadSolved entered {ksCount} time)");

            // trail and service door
            yield return sp.Until(() => bt.Current == BreachTrail.State.TrailVisible, 6f);
            Check(!bt.Handle.gameObject.activeSelf, "The pull handle is not usable before the panel is open");
            sp.SetHandPos(bt.Handle.position); yield return sp.Frames(3); sp.SetGrip(true); yield return sp.Frames(4);
            yield return sp.MoveHand(bt.Handle.position + Vector3.left * 0.3f, 0.4f); yield return sp.ReleaseGrip();
            Check(bt.Current == BreachTrail.State.TrailVisible && sm.Current == Stage.KeypadSolved, "Pulling where the handle will be, before the panel opens, does nothing");
            rig.PlaceAt(new Vector3(5.6f, 0f, 0f), 90f); // inside the entry zone at the wrong stage
            yield return new WaitForSecondsRealtime(1.0f);
            Check(SceneManager.sceneCount == 1 && SceneIdle && sm.Current == Stage.KeypadSolved, "Standing in the smoke-room entry zone at the wrong stage launches nothing");
            yield return sp.TeleportTo(bt.PanelStandPoint());
            yield return sp.Wait(1.0f);
            Check(bt.Current == BreachTrail.State.TrailVisible, "A single long jump to the end of the trail from the far side of the room does not open the panel (out of teleport range)");
            rig.PlaceAt(new Vector3(2.3f, 0f, -1.05f), 90f); // the player walked here; a teleport from the entry zone would be beyond the teleport range
            yield return sp.Wait(0.3f);
            yield return sp.LookAt(bt.Waypoints[bt.Waypoints.Length - 1].position);
            int tcTrail = loco.TeleportCount;
            yield return sp.TeleportTo(bt.PanelStandPoint());
            yield return sp.Until(() => bt.Current == BreachTrail.State.PanelOpen, 3f);
            Note($"trail diag: teleports accepted {loco.TeleportCount - tcTrail}, head {rig.Head.position}, stand point {bt.PanelStandPoint()}, last waypoint {bt.Waypoints[bt.Waypoints.Length - 1].position}, state {bt.Current}");
            yield return sp.Wait(1.0f);
            for (int pass = 0; pass < 2; pass++)
            {
                yield return sp.LookAt(bt.Handle.position);
                sp.SetHandPos(bt.Handle.position); yield return sp.Frames(3); sp.SetGrip(true); yield return sp.Frames(4);
                yield return sp.MoveHand(bt.Handle.position + Vector3.left * 0.25f, 0.5f); yield return sp.Frames(4); yield return sp.ReleaseGrip();
                yield return sp.Wait(2f);
            }
            int tfCount = sm.History.FindAll(x => x == "TrailFollowed").Count;
            Check(bt.Current == BreachTrail.State.DoorOpen && tfCount == 1, $"The service door opens once and pulling again changes nothing (TrailFollowed entered {tfCount} time, trail state {bt.Current}, stage {sm.Current}, history {string.Join(">", sm.History)})");

            // ================================================================ puzzles: rack and lever
            var cables = rack.Cables;
            Vector3 cableStart = cables[0].transform.position;
            Check(rack.Current == CoolantRack.State.Waiting, "The rack waits until both blocks are done");
            var qui = FindAnyObjectByType<QuestionnaireUI>();
            cables[0].Grabbable.ForceRelease(); rack.EvaluateRelease(cables[0], Hand.Right);
            Check(!cables[0].Plugged, "A cable cannot be plugged before the rack is active");
            sm.JumpTo(Stage.Block2); EgressBlockController.MarkCompleted(2);
            yield return sp.Until(() => rack.Current == CoolantRack.State.Active, 5f);
            // a cable thrown far away and one that falls out of the world come back
            cables[1].transform.position = new Vector3(30f, 3f, 30f);
            var cup = FindAnyObjectByType<AwakeningController>().Cup; cup.transform.position = new Vector3(-30f, 2f, 0f);
            yield return sp.Until(() => Vector3.Distance(cables[1].transform.position, cables[1].Grabbable.transform.position) < 0.01f && cables[1].transform.position.x < 6f && cup.transform.position.x > -6f, 4f);
            yield return sp.Wait(0.5f);
            Check(cables[1].transform.position.x < 6f && cables[1].transform.position.y > -1f && cup.transform.position.x > -6f && cup.transform.position.y > -1f, "A cable and the cup thrown far away respawn at their start");
            // wrong plug then a pause: the lockout still ends
            yield return sp.TeleportTo(new Vector3(1.5f, 0f, 0.9f));
            yield return PlugBy(sp, cables[1], 1);
            yield return sp.Wait(0.3f);
            Check(rack.Locked, "A wrong plug locks the rack");
            PauseManager.Instance.SetPaused(true); yield return new WaitForSecondsRealtime(1.0f); PauseManager.Instance.SetPaused(false);
            yield return sp.Until(() => !rack.Locked, 8f);
            Check(!rack.Locked, "The lockout ends even after a pause");
            var by = new Dictionary<string, CoolantCable>(); foreach (var c in cables) by[c.CableName] = c;
            yield return PlugBy(sp, by["Red"], 2); yield return PlugBy(sp, by["Blue"], 5); yield return PlugBy(sp, by["Yellow"], 3);
            yield return sp.Wait(0.4f);
            Check(rack.Current == CoolantRack.State.Solved, "The three correct plugs solve the rack");
            int escapedBefore = sm.History.FindAll(x => x == "Escaped").Count;
            for (int pass = 0; pass < 2; pass++)
            {
                sp.SetHandPos(rack.LeverHandle.position); yield return sp.Frames(3); sp.SetGrip(true); yield return sp.Frames(4);
                yield return sp.MoveHand(rack.LeverHandle.position + Vector3.down * 0.3f, 0.4f); yield return sp.Frames(3); yield return sp.ReleaseGrip();
                yield return sp.Wait(0.6f);
            }
            int escapedAfter = sm.History.FindAll(x => x == "Escaped").Count;
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneHallway && SceneIdle, 30f);
            yield return new WaitForSecondsRealtime(2f);
            Check(escapedAfter - escapedBefore == 1 && hallwayArrivals == 1, $"The lever works once; Escaped entered once and the hallway loaded once ({hallwayArrivals} arrival)");
            HeightCheck("in SCN_HallwayDebrief");
            rig = PlayerRig.Instance; loco = rig.GetComponent<PlayerLocomotion>();
            Check(loco.TeleportEnabled && !loco.SmoothMoveEnabled, "Hallway: teleport on, smooth move off");
            yield return PerfSample("SCN_HallwayDebrief");

            // ================================================================ experimenter restart mid-game (hallway -> restart)
            VoiceBus.Request("R150"); VoiceBus.Request("R151"); VoiceBus.Request("H007");
            yield return new WaitForSecondsRealtime(0.5f);
            var panelC = FindAnyObjectByType<ExperimenterPanel>();
            panelC.Toggle();
            yield return new WaitForSecondsRealtime(0.3f);
            Check(PauseManager.Instance.IsPaused && GameObject.Find("ExperimenterPanel") != null, "The experimenter menu opens and pauses the game");
            typeof(ExperimenterPanel).GetMethod("Restart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panelC, null);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneSetup && SceneIdle && !PauseManager.Instance.IsPaused, 25f);
            yield return new WaitForSecondsRealtime(1.0f);
            var narr = NarrativeVoiceDirector.Instance; var hal = HalcyonDirector.Instance;
            Check(SceneManager.GetActiveScene().name == DS.SceneSetup && sm.Current == Stage.Setup && Time.timeScale == 1f && !AudioListener.pause, "Restart returns to SCN_Setup at stage Setup, unpaused");
            Check(!narr.IsSpeaking && narr.QueueLength == 0 && !hal.IsSpeaking && EgressBlockController.CompletedBlocks == 0 && !OxygenSystem.Instance.Running, "Restart clears queued voice lines, completed blocks and the oxygen timer");
            Check(TelemetryLogger.Instance.Rows.Count == 0, "Restart clears the telemetry rows");
            cnt = CountObjects();
            Check(cnt.rigs == 1 && cnt.listeners == 1, $"After the restart: one rig and one AudioListener ({Describe(cnt)})");

            // restart in the middle of a smoke block
            sm.JumpTo(Stage.Awakening);
            SceneFlow.Instance.GoToMedicalBay();
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 25f);
            StageJump.Jump(Stage.TrailFollowed);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 25f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return sp.TeleportTo(new Vector3(5.6f, 0f, 0f));
            yield return RealUntil(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 25f);
            yield return new WaitForSecondsRealtime(2f);
            rig = PlayerRig.Instance; loco = rig.GetComponent<PlayerLocomotion>();
            Check(loco.SmoothMoveEnabled && !loco.TeleportEnabled, "Smoke room: smooth move on, teleport off");
            HeightCheck("in SCN_SmokeRoom");
            yield return PerfSample("SCN_SmokeRoom");
            // walls block the player
            Vector3 startPos = rig.Head.position;
            for (int i = 0; i < 40; i++) { InputService.Instance.ScriptedSetMove(new Vector2(0, 1)); yield return null; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f) { yield return sp.WalkTo(startPos + Vector3.forward * 6f, 0.2f, 3f); }
            float zNow = rig.Head.position.z;
            Check(zNow < 1.95f && Mathf.Abs(rig.transform.position.y) < 0.05f, $"Pushing against the wall for 4 s: head z {zNow:F2} stays inside the start cell (wall at 2.0) and the rig stays on the floor");
            HeightCheck("after pushing against a wall");
            InputService.Instance.ScriptedSetMove(Vector2.zero);
            panelC = FindAnyObjectByType<ExperimenterPanel>();
            panelC.Toggle();
            yield return new WaitForSecondsRealtime(0.3f);
            typeof(ExperimenterPanel).GetMethod("Restart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panelC, null);
            yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneSetup && SceneIdle && !PauseManager.Instance.IsPaused, 25f);
            yield return new WaitForSecondsRealtime(1.0f);
            Check(SceneManager.sceneCount == 1 && EgressBlockController.Instance == null && !RenderSettings.fog, $"Restart in the middle of a block unloads the smoke room ({SceneManager.sceneCount} scene loaded) and the fog is gone");

            // ================================================================ reload cycles: no leaks
            var counts = new List<Counts>();
            for (int cycle = 0; cycle < 3; cycle++)
            {
                sm.ResetSession(); EgressBlockController.ResetStatics();
                SceneFlow.Instance.GoToMedicalBay();
                yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 25f);
                StageJump.Jump(Stage.TrailFollowed);
                yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 25f);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return sp.TeleportTo(new Vector3(5.6f, 0f, 0f));
                yield return RealUntil(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 25f);
                yield return new WaitForSecondsRealtime(1f);
                if (EgressBlockController.Instance == null || !EgressBlockController.Instance.Active)
                {
                    Check(false, $"Cycle {cycle + 1}: the smoke block did not start (scene {SceneManager.GetActiveScene().name}, stage {sm.Current}, scenes loaded {SceneManager.sceneCount})");
                    yield break;
                }
                EgressBlockController.Instance.Finish(true);
                yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && SceneIdle, 25f);
                SceneFlow.Instance.GoToHallway();
                yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneHallway && SceneIdle, 25f);
                SceneFlow.Instance.LoadSceneForJump(DS.SceneSetup);
                yield return RealUntil(() => SceneManager.GetActiveScene().name == DS.SceneSetup && SceneIdle, 25f);
                yield return new WaitForSecondsRealtime(1.5f);
                var c = CountObjects(); counts.Add(c);
                Note($"cycle {cycle + 1} (Setup, Bay, Smoke, Bay, Hallway, Setup): {Describe(c)}; managed memory {System.GC.GetTotalMemory(false) / 1048576f:F0} MB");
            }
            var a = counts[1]; var b = counts[2];
            Check(b.rigs == 1 && b.listeners == 1 && b.eventSystems <= 1, "After three full reload cycles: one rig, one AudioListener, at most one EventSystem");
            Check(b.sources == a.sources && b.lights == a.lights && b.roots == a.roots, $"Audio sources ({a.sources} to {b.sources}), lights ({a.lights} to {b.lights}) and persistent objects ({a.roots} to {b.roots}) do not grow between cycles 2 and 3");
            Check(b.voiceSubs == a.voiceSubs && b.sfxSubs == a.sfxSubs && b.arrivedSubs == a.arrivedSubs && b.stageSubs == a.stageSubs, $"Event subscribers do not pile up (voice {a.voiceSubs}/{b.voiceSubs}, sfx {a.sfxSubs}/{b.sfxSubs}, SceneArrived {a.arrivedSubs}/{b.arrivedSubs}, StageChanged {a.stageSubs}/{b.stageSubs})");
            Check(b.materials - a.materials <= 12, $"Material objects do not leak per cycle ({a.materials} to {b.materials})");
            sm.ResetSession(); EgressBlockController.ResetStatics();
        }
    }
}
#endif
