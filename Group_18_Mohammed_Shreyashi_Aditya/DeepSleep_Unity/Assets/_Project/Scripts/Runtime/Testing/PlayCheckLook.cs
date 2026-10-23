#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Scenario "look": briefing, cup, cable drop, carrying while teleporting, beacon game, keypad, posters, exit door glow. Writes PNGs for review.</summary>
    public partial class PlayCheckRunner
    {
        public static string LookDir = "C:/Users/shrey/AppData/Local/Temp/claude/C--Users-shrey-Fractured-Final/fb19842d-d6be-4bf0-b3bf-5b626ef6f0e3/scratchpad/shots/";

        static Transform FindT(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t.name == name && t.gameObject.scene.IsValid()) return t;
            return null;
        }

        void LookShot(string name, Vector3 pos, Vector3 target, float fov = 70f)
        {
            try
            {
                Directory.CreateDirectory(LookDir);
                var go = new GameObject("LookCam");
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = fov; cam.nearClipPlane = 0.03f; cam.farClipPlane = 60f;
                go.transform.position = pos; go.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
                var rt = new RenderTexture(1280, 800, 24);
                cam.targetTexture = rt; cam.Render();
                var old = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(LookDir, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = old; cam.targetTexture = null; Destroy(rt); Destroy(tex); Destroy(go);
            }
            catch (System.Exception e) { Note("shot failed: " + e.Message); }
        }

        void HeadShot(string name)
        {
            var h = PlayerRig.Instance.Head;
            LookShot(name, h.position, h.position + h.forward, 80f);
        }

        IEnumerator LookCheck()
        {
            var voices = _voices;
            VoiceBus.Requested += id => voices.Add(id);
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null, 10f);
            yield return sp.Wait(1f);
            StageJump.Jump(Stage.Awakening);
            yield return sp.Until(() => SceneFlow.Section == DS.SceneMedicalBay && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy, 30f);
            Check(SceneFlow.Section == DS.SceneMedicalBay, "Jump to the Medical Bay worked");

            // ---- briefing before Rose
            yield return sp.Until(() => ObjectiveScreen.Active, 10f);
            Check(ObjectiveScreen.Active, "Mission briefing is shown on arrival");
            Check(!voices.Contains("R010"), "Rose has not started while the briefing is up");
            yield return sp.Wait(5f);
            HeadShot("01_briefing");
            float t0 = Time.time;
            yield return sp.Until(() => !ObjectiveScreen.Active, 40f);
            Note($"briefing visible for {Time.time - t0 + 5f:F1} s");
            Check(!ObjectiveScreen.Active, "Briefing goes away by itself");
            yield return sp.Until(() => voices.Contains("R010"), 10f);
            Check(voices.Contains("R010") && voices.Contains("H001"), "Rose (R010) and HALCYON (H001) start after the briefing");
            AudioSource hs = null;
            foreach (var a in HalcyonDirector.Instance.GetComponentsInChildren<AudioSource>(true)) if (a.name == "HalcyonSpeakerSource") hs = a;
            Check(hs != null && hs.pitch < 0.9f, $"HALCYON voice is pitched down (pitch {(hs != null ? hs.pitch : 0f):F2})");

            // ---- cup
            var cup = FindT("Cup");
            LookShot("02_cup", new Vector3(-0.15f, 1.25f, -1.85f), cup.position, 50f);
            var cupMr = cup.GetComponent<MeshRenderer>();
            Check(cupMr != null && cupMr.sharedMaterial.GetColor("_BaseColor").g > 0.7f, "Cup has the bright colour and its own mesh");

            // ---- cable lies on the table and a dropped cable stays above the floor
            var cableT = FindT("PF_Cable_01");
            var cable = cableT.GetComponent<CoolantCable>();
            Check(cableT.position.y > 0.88f && cableT.position.y < 1.0f, $"Cable rests on the table (y {cableT.position.y:F2})");
            LookShot("03_cables", new Vector3(1.1f, 1.55f, 0.95f), new Vector3(1.1f, 0.95f, 1.73f), 60f);
            LookShot("04_cable_poster", new Vector3(1.1f, 1.6f, 1.2f), new Vector3(1.1f, 1.62f, 2.93f), 55f);
            var rbc = cableT.GetComponent<Rigidbody>();
            cableT.position = new Vector3(-0.8f, 1.6f, 0.9f); rbc.position = cableT.position; rbc.linearVelocity = Vector3.zero;
            yield return sp.Wait(2.5f);
            Check(cableT.position.y > -0.02f && cableT.position.y < 0.25f, $"A dropped cable stops on the floor (y {cableT.position.y:F2})");
            LookShot("05_cable_on_floor", new Vector3(-0.8f, 1.2f, 0.2f), cableT.position, 60f);
            cable.Grabbable.ResetToStart();

            // ---- carrying an object does not stop teleport
            yield return sp.TeleportTo(new Vector3(-0.9f, 0f, -1.7f));
            var rig = PlayerRig.Instance;
            var loco = rig.GetComponent<PlayerLocomotion>();
            var cupG = cup.GetComponent<Grabbable>();
            yield return sp.GrabObject(cupG);
            Check(cupG.IsHeld, "Cup is held");
            Vector3 before = rig.Head.position; int tp0 = loco.TeleportCount;
            yield return sp.TeleportTo(new Vector3(-1.25f, 0f, -0.4f));
            Check(loco.TeleportCount > tp0 && (rig.Head.position - before).magnitude > 0.5f, "Teleport works while holding the cup");
            yield return sp.ReleaseGrip();
            yield return sp.Until(() => SessionManager.Instance.Current == Stage.EarCheck, 8f);
            Check(SessionManager.Instance.Current == Stage.EarCheck, "Cup + teleport still advance to the beacon game");

            // ---- beacon game
            var ec = FindAnyObjectByType<EarCheckController>();
            yield return sp.Until(() => ec.Active, 6f);
            Check(ec.Active && voices.Contains("R030"), "Beacon game started with R030");
            LookShot("06_beacon_station", new Vector3(-1.25f, 1.65f, -0.2f), new Vector3(-1.25f, 1.3f, -2.3f), 85f);
            LookShot("07_beacon_poster", new Vector3(-1.25f, 1.7f, -1.4f), new Vector3(-1.25f, 1.75f, -2.95f), 55f);
            var wrongB = System.Array.Find(ec.Beacons, x => x.Index != ec.Order[0]);
            yield return sp.PlaceGrabbable(wrongB.Grabbable, ec.Slots[0].position);
            yield return sp.Wait(1.5f);
            Check(ec.WrongPlacements == 1 && wrongB.PlacedSlot == 0, "Wrong beacon is sent back");
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                var b = System.Array.Find(ec.Beacons, x => x.Index == ec.Order[slot]);
                yield return sp.PlaceGrabbable(b.Grabbable, ec.Slots[slot].position);
                yield return sp.Until(() => ec.PlacedCount >= slot + 1, 4f);
                if (i == 2) LookShot("08_beacons_half", new Vector3(-1.25f, 1.65f, -0.2f), new Vector3(-0.8f, 0.9f, -1.3f), 70f);
            }
            yield return sp.Until(() => ec.Completed, 6f);
            Check(ec.Completed && voices.Contains("R032"), "Six beacons placed, game complete");
            LookShot("09_beacons_done", new Vector3(-1.25f, 1.65f, -0.2f), new Vector3(-0.8f, 0.9f, -1.3f), 70f);

            // ---- keypad
            var tk = FindAnyObjectByType<ThermalKeypad>();
            yield return sp.Until(() => tk.Current == ThermalKeypad.State.Ready, 6f);
            Check(tk.Current == ThermalKeypad.State.Ready, "Keypad is ready after the beacon game");
            LookShot("10_keypad", new Vector3(-0.3f, 1.12f, 0.84f), new Vector3(0.3f, 1.1f, 0.84f), 45f);
            LookShot("11_keypad_far", new Vector3(-1.0f, 1.6f, 0.84f), new Vector3(0.3f, 1.1f, 0.84f), 60f);
            yield return sp.Poke(tk.Key(1).position, Vector3.left);
            yield return sp.Wait(0.3f);
            Check(tk.WrongAttempts == 1, "Wrong key still registers");

            // ---- smoke room: louder Rose and the glowing exit
            StageJump.Jump(Stage.Block1);
            yield return sp.Until(() => SceneFlow.Section == DS.SceneSmokeRoom && EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 40f);
            var maze = FindAnyObjectByType<MazeBuilder>();
            Check(maze != null && maze.ExitHandle != null, "Exit door has a glowing frame and handle");
            if (maze != null && maze.ExitDoorLeaf != null)
            {
                var leaf = maze.ExitDoorLeaf.position;
                LookShot("12_exit_door", leaf + new Vector3(0, 1.4f, -1.6f), leaf + new Vector3(0, 1.1f, 0), 70f);
            }
            var mixer = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.mixer : null;
            yield return sp.Wait(1f);
            if (mixer != null) { mixer.GetFloat("VolRose", out float v); Note($"VolRose in the smoke room: {v:F1} dB"); Check(v > 16f, "Rose is louder in the smoke room"); }
        }
    }
}
#endif
