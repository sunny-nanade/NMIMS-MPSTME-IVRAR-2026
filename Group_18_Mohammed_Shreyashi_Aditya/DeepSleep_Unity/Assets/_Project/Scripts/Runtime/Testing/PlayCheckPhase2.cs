#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        static bool BoundsOf(string name, out Bounds b)
        {
            b = new Bounds(); bool f = false;
            var go = FindAny(name);
            if (go == null) return false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!f) { b = r.bounds; f = true; } else b.Encapsulate(r.bounds);
            }
            return f;
        }

        public static GameObject FindAny(string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                {
                    var t = FindChild(root.transform, name);
                    if (t != null) return t.gameObject;
                }
            }
            return null;
        }

        static Transform FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindChild(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        readonly List<string> _voices = new List<string>();

        IEnumerator Phase2()
        {
            var voices = _voices;
            VoiceBus.Requested += id => voices.Add(id);
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && FindAnyObjectByType<SetupController>() != null, 10f);
            var rig = PlayerRig.Instance;
            var sc = FindAnyObjectByType<SetupController>();
            Check(rig != null && sc != null, "SCN_Setup has the rig and SetupController");
            if (rig == null || sc == null) yield break;
            yield return sp.Wait(0.6f);
            Check(GameObject.Find("TEMP_Phase1TestArea") == null, "Temporary test area is gone");
            Check(Mathf.Abs(rig.CameraHeight - DS.EYE_HEIGHT) <= DS.HeightTolerance, $"Camera height {rig.CameraHeight:F3} m in SCN_Setup");

            Check(sc.Current == SetupController.Page.Consent, "Setup starts on the consent page");
            yield return sp.LookAt(sc.transform.position);
            Shot("p2_setup_consent");
            var btn = FindAny("Btn_Continue");
            yield return sp.PointAt(btn.transform.position);
            yield return sp.Frames(3);
            {
                var inp = InputService.Instance; var hi = HandInteractor.For(Hand.Right);
                bool hit = Physics.Raycast(inp.RayOrigin(Hand.Right), inp.RayDirection(Hand.Right), out var rh, 6f, LayerMask.GetMask("Interactable") | 1, QueryTriggerInteraction.Collide);
                Note($"DIAG src={inp.CurrentSource} hi={(hi != null)} hover={(hi != null && hi.RayHover != null)} hit={hit} {(hit ? rh.collider.name : "")} origin={inp.RayOrigin(Hand.Right)} dir={inp.RayDirection(Hand.Right)} btn={btn.transform.position} layer={btn.layer} col={btn.GetComponent<Collider>() != null}");
            }
            yield return sp.ClickRay(btn.transform.position);
            Check(sc.Current == SetupController.Page.Height, "Ray click on the button moves to the height page");
            Check(!sc.HeightWarning, "No low-height warning at standing eye height");
            sc.OnNext();
            Check(sc.Current == SetupController.Page.AudioCheck, "Audio check page");
            var narr = NarrativeVoiceDirector.Instance;
            Check(narr != null && narr.Played.Contains("R001"), "R001 played during the consent and height pages (it starts with the scene, not with a button)");
            sc.OnNext();
            // R002 announces the tones and the first tone waits for it
            yield return sp.Until(() => narr.Played.Contains("R002") && !narr.IsBusy, 40f);
            Check(narr.Played.Contains("R002"), "R002 played when the tone check started, before the first tone");
            for (int i = 0; i < 4; i++)
            {
                yield return sp.Wait(0.3f);
                if (i == 0) { yield return sp.LookYaw(sc.ToneBaseYaw + 180f); yield return sp.ClickRay(rig.Head.position + rig.Head.forward * 3f); }
                yield return sp.LookYaw(sc.ToneBaseYaw + SetupController.ToneYawOffsets[i]);
                yield return sp.ClickRay(rig.Head.position + rig.Head.forward * 3f);
                int idx = i;
                yield return sp.Until(() => sc.TonesConfirmed > idx, 4f);
            }
            Check(sc.TonesConfirmed == 4, $"All four tones confirmed by facing + trigger ({sc.TonesConfirmed}); wrong-direction pull ignored");
            yield return sp.Until(() => narr.Played.Contains("R003"), 10f);
            Check(narr.Played.Contains("R003"), "R003 played right after the fourth tone was confirmed");
            yield return sp.Until(() => sc.Current == SetupController.Page.Comfort, 3f);
            Check(sc.Current == SetupController.Page.Comfort, "Comfort page reached");
            yield return sp.LookYaw(sc.ToneBaseYaw);
            var slider = FindAny("Slider_Volume");
            if (slider != null)
            {
                var ws = slider.GetComponent<WorldUI.WorldSlider>();
                yield return sp.ClickRay(slider.transform.TransformPoint(new Vector3(0.25f, 0, 0)));
                Check(ws.HasValue && ws.Value > 60f, $"Volume slider set by ray ({ws.Value})");
            }
            sc.OnNext();
            var sm = SessionManager.Instance;
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && PlayerRig.Instance != null && SceneFlow.Instance != null && !SceneFlow.Instance.IsBusy, 15f);
            yield return sp.Wait(0.6f);
            Check(SceneManager.GetActiveScene().name == DS.SceneMedicalBay, "SCN_MedicalBay loaded after Start");
            Check(sm.Current == Stage.Awakening, "Stage is Awakening");
            rig = PlayerRig.Instance;
            Check(Mathf.Abs(rig.CameraHeight - DS.EYE_HEIGHT) <= DS.HeightTolerance, $"Camera height {rig.CameraHeight:F3} m in SCN_MedicalBay");
            Check(voices.Contains("R010") && voices.Contains("H001"), "R010 and H001 requested on arrival");

            Bounds b;
            if (BoundsOf("MainDoor_Welded", out b)) Check(Mathf.Abs(b.size.y - 2.1f) < 0.05f && Mathf.Abs(b.size.z - 0.9f) < 0.05f, $"Main door {b.size.z:F2} x {b.size.y:F2} m");
            else Fail("Main door missing");
            if (BoundsOf("ServiceDoor", out b)) Check(Mathf.Abs(b.size.y - 2.1f) < 0.05f && Mathf.Abs(b.size.z - 0.9f) < 0.05f, $"Service door {b.size.z:F2} x {b.size.y:F2} m");
            else Fail("Service door missing");
            if (BoundsOf("HospitalBed", out b)) Check(Mathf.Abs(b.max.y - 0.9f) < 0.05f, $"Bed height {b.max.y:F2} m"); else Fail("Bed missing");
            if (BoundsOf("SideTable", out b)) Check(Mathf.Abs(b.max.y - 0.9f) < 0.05f, $"Table height {b.max.y:F2} m"); else Fail("Table missing");
            if (BoundsOf("Ceiling", out b)) Check(b.min.y >= 2.6f, $"Ceiling at {b.min.y:F2} m"); else Fail("Ceiling missing");
            if (BoundsOf("Shell", out b)) Check(Mathf.Abs(b.size.x - 8.4f) < 0.7f && Mathf.Abs(b.size.z - 6.4f) < 0.7f, $"Medical bay footprint {b.size.x:F1} x {b.size.z:F1} m");
            if (BoundsOf("Keypad", out b)) Check(Mathf.Abs(b.center.y - 1.1f) < 0.1f, $"Keypad centre at {b.center.y:F2} m");
            if (BoundsOf("Terminal", out b)) Check(Mathf.Abs(b.size.y - 1.0f) < 0.1f, $"Terminal {b.size.y:F2} m high");

            yield return sp.LookAt(new Vector3(-3.86f, 1.7f, -1.7f));
            Shot("p2_oxygen_wall");
            var ox = OxygenSystem.Instance;
            float o0 = ox.Remaining;
            Light pulse = null;
            foreach (var l in FindAny("EmergencyLighting").GetComponentsInChildren<Light>()) if (l.name == "RedPulse") { pulse = l; break; }
            float lmin = 99, lmax = 0, t0 = Time.time;
            while (Time.time - t0 < 2.3f) { lmin = Mathf.Min(lmin, pulse.intensity); lmax = Mathf.Max(lmax, pulse.intensity); yield return null; }
            Check(ox.Running && ox.Remaining < o0, $"Oxygen counts down ({ox.Formatted})");
            Check(lmax - lmin > 0.3f, $"Emergency light pulses over a 2 s period (intensity {lmin:F2} to {lmax:F2})");

            var aw = FindAnyObjectByType<AwakeningController>();
            var cup = aw.Cup;
            yield return sp.TeleportTo(new Vector3(-1.3f, 0f, -1.7f));
            yield return sp.Wait(0.3f);
            Check(voices.Contains("R011"), "R011 requested when near the tray");
            Check(sm.Current == Stage.Awakening, "Teleport alone does not finish Awakening");
            yield return sp.GrabObject(cup);
            yield return sp.Wait(0.4f);
            Check(aw.CupGrabbed, "Cup can be grabbed");
            Check(aw.Completed && sm.Current == Stage.EarCheck, "Awakening completes (cup + teleport) and advances to EarCheck");
            yield return sp.ReleaseGrip();
            Note("H002 timing (20 s) not waited in this run");
        }
    }
}
#endif
