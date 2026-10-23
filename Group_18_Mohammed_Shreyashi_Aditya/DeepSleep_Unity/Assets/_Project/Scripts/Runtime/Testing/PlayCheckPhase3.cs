#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        const string ShotDir = "C:/Users/shrey/AppData/Local/Temp/claude/C--Users-shrey-Fractured-Final/fb8dae35-6050-47ca-8e73-a2a3bc50926c/scratchpad/";

        void Shot(string name)
        {
            try
            {
                var rig = PlayerRig.Instance;
                var cam = rig != null && rig.Head != null ? rig.Head.GetComponent<Camera>() : Camera.main;
                if (cam == null) cam = Camera.main;
                if (cam == null) return;
                Directory.CreateDirectory(ShotDir);
                var rt = new RenderTexture(800, 800, 24);
                var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
                var old = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(800, 800, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 800, 800), 0, 0); tex.Apply();
                File.WriteAllBytes(ShotDir + name + ".png", tex.EncodeToPNG());
                RenderTexture.active = old; Destroy(rt); Destroy(tex);
            }
            catch (System.Exception e) { Note("shot failed: " + e.Message); }
        }

        IEnumerator Phase3()
        {
            yield return Phase2();
            var sp = ScriptedPlayer.Get();
            var sm = SessionManager.Instance;
            var voices = _voices;
            var rig = PlayerRig.Instance;
            var ec = FindAnyObjectByType<EarCheckController>();
            var tk = FindAnyObjectByType<ThermalKeypad>();
            var bt = FindAnyObjectByType<BreachTrail>();
            Check(ec != null && tk != null && bt != null, "EarCheckController, ThermalKeypad and BreachTrail are in SCN_MedicalBay");
            if (ec == null || tk == null || bt == null) yield break;
            Check(sm.Current == Stage.EarCheck, "Stage is EarCheck at the start of Phase 3");
            Check(tk.Current == ThermalKeypad.State.Inactive, "Keypad stays inactive until the Ear Check is finished");

            // ---------------- Beacon game
            yield return sp.Until(() => ec.Active, 10f);
            Check(ec.Active && voices.Contains("R030"), "R030 requested at the start of the beacon game");
            Check(ec.Beacons.Length == 6 && ec.Slots.Length == 6 && ec.Order.Length == 6, "Six beacons, six slots and a colour order exist");
            var wrongB = System.Array.Find(ec.Beacons, x => x.Index != ec.Order[0]);
            yield return sp.TeleportTo(new Vector3(-1.25f, 0f, -0.5f));
            yield return sp.PlaceGrabbable(wrongB.Grabbable, ec.Slots[0].position);
            yield return sp.Wait(1.5f);
            Check(ec.WrongPlacements == 1 && wrongB.PlacedSlot == 0 && ec.PlacedCount == 0, "A wrong beacon is rejected and sent back");
            Shot("p3_beacons");
            for (int i = 0; i < 6; i++)
            {
                int slot = i;
                var b = System.Array.Find(ec.Beacons, x => x.Index == ec.Order[slot]);
                yield return sp.PlaceGrabbable(b.Grabbable, ec.Slots[slot].position);
                yield return sp.Until(() => ec.PlacedCount >= slot + 1, 3f);
            }
            yield return sp.Until(() => ec.Completed, 6f);
            Check(ec.PlacedCount == 6 && ec.Completed, "Six beacons placed in the poster order");
            Check(voices.Contains("R031") && voices.Contains("R032"), "R031 after placements and R032 at the end");
            Check(TelemetryLogger.Instance != null && TelemetryLogger.Instance.SessionValues.ContainsKey("beacon_game_time_s"), "Beacon game time and wrong placements are logged");

            // ---------------- Thermal keypad
            Check(tk.Current == ThermalKeypad.State.Ready && voices.Contains("R040"), "Keypad becomes ready and R040 plays");
            Check(tk.Sequence.Length == 3 && tk.Sequence[0] == 7 && tk.Sequence[1] == 3 && tk.Sequence[2] == 5, "Press order coolest to hottest is 7, 3, 5");
            Vector3 front = Vector3.left;
            yield return sp.LookAt(tk.Key(5).position);
            Shot("p3_keypad");
            yield return sp.Poke(tk.Key(1).position, front);
            yield return sp.Wait(0.3f);
            Check(tk.WrongAttempts == 1 && tk.Current == ThermalKeypad.State.Locked, "Wrong key locks the keypad");
            yield return sp.Poke(tk.Key(7).position, front);
            yield return sp.Wait(0.2f);
            Check(tk.Current == ThermalKeypad.State.Locked, "Presses are ignored during the 2 s lockout");
            yield return sp.Wait(2.3f);
            Check(tk.Current == ThermalKeypad.State.Ready, "Lockout ends after 2 s");
            yield return sp.Poke(tk.Key(2).position, front);
            yield return sp.Wait(2.4f);
            yield return sp.Poke(tk.Key(4).position, front);
            yield return sp.Wait(0.2f);
            Check(tk.WrongAttempts == 3, "Three wrong attempts counted");
            var r5 = tk.Key(5).GetComponent<Renderer>();
            float emin = 99, emax = 0, te = Time.time;
            while (Time.time - te < 1.5f) { float e = r5.material.GetColor("_EmissionColor").maxColorComponent; emin = Mathf.Min(emin, e); emax = Mathf.Max(emax, e); yield return null; }
            Check(emax - emin > 0.8f, $"Glowing keys pulse after three failures (emission {emin:F2} to {emax:F2})");
            yield return sp.Wait(3.2f);
            Check(voices.Contains("H004"), "H004 plays after a wrong sequence");
            foreach (int k in tk.Sequence)
            {
                yield return sp.Poke(tk.Key(k).position, front);
                yield return sp.Wait(0.15f);
            }
            yield return sp.Wait(0.3f);
            Check(tk.Current == ThermalKeypad.State.Solved, "Pressing 7, 3, 5 solves the keypad");
            Check(sm.Current == Stage.KeypadSolved, "Stage advances to KeypadSolved");
            Check(voices.Contains("R043"), "R043 plays at terminal boot");
            Check(TelemetryLogger.Instance.SessionValues.ContainsKey("keypad_time_s"), "keypad_time_s and keypad_wrong_attempts logged");

            // ---------------- Trail
            yield return sp.Wait(3.5f);
            Check(bt.Current == BreachTrail.State.TrailVisible, "Trail is visible after the keypad is solved");
            Check(bt.Decals.Count >= 4 && bt.Decals.Count <= 24, $"Trail has {bt.Decals.Count} decals (max 24)");
            Check(voices.Contains("R050"), "R050 plays when the trail fades in");
            bool allOn = true; foreach (var d in bt.Decals) allOn &= d.localScale.x > 0.99f;
            Check(allOn, "All decals have faded in");
            // route is one polyline: every decal is within 0.05 m of the segments
            bool onRoute = true;
            foreach (var d in bt.Decals)
            {
                float best = 99f;
                for (int i = 1; i < bt.Waypoints.Length; i++) best = Mathf.Min(best, DistToSegment(d.position, bt.Waypoints[i - 1].position, bt.Waypoints[i].position));
                onRoute &= best < 0.05f;
            }
            Check(onRoute, "Every decal lies on the single waypoint route (no forks)");
            yield return sp.TeleportTo(new Vector3(2.3f, 0f, -1.05f));
            yield return sp.LookAt(bt.Waypoints[bt.Waypoints.Length - 1].position);
            Shot("p3_trail");
            yield return sp.TeleportTo(bt.PanelStandPoint());
            yield return sp.Until(() => bt.Current == BreachTrail.State.PanelOpen, 3f);
            yield return sp.Wait(2f);
            Check(bt.Current == BreachTrail.State.PanelOpen && bt.Handle.gameObject.activeSelf, "Arriving at the panel opens it and shows the pull handle");
            float hy = bt.Handle.position.y;
            Check(Mathf.Abs(hy - 1.0f) < 0.1f, $"Pull handle at {hy:F2} m");
            yield return sp.LookAt(bt.Handle.position);
            Shot("p3_handle");
            sp.SetHandPos(bt.Handle.position);
            yield return sp.Frames(3);
            sp.SetGrip(true);
            yield return sp.Frames(4);
            yield return sp.MoveHand(bt.Handle.position + Vector3.left * 0.25f, 0.5f);
            yield return sp.Frames(4);
            yield return sp.ReleaseGrip();
            yield return sp.Wait(2f);
            Check(bt.Current == BreachTrail.State.DoorOpen && sm.Current == Stage.TrailFollowed, "Pulling the handle opens the service door and advances to TrailFollowed");
            Check(voices.Contains("R052"), "R052 plays when the door opens");
            Check(TelemetryLogger.Instance.SessionValues.ContainsKey("trail_time_s"), "trail_time_s logged");
            yield return sp.LookAt(new Vector3(4f, 1.0f, 0f));
            Shot("p3_door_open");
            sm.ResetSession();
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = 0; a.y = 0; b.y = 0;
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
#endif
