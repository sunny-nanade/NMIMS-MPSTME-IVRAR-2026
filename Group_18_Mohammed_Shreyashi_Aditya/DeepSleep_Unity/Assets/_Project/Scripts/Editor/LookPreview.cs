using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// Edit-mode preview of the props that are built in code at run time (no play mode, so no XR runtime starts).
    /// Runs the build methods on the open scene, renders PNGs, and the caller reloads the scene afterwards to discard everything.
    /// </summary>
    public static class LookPreview
    {
        static void Call(object o, string method, params object[] args)
        {
            var m = o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) { Debug.LogWarning("[DeepSleep] preview: no method " + method + " on " + o.GetType().Name); return; }
            m.Invoke(o, args);
        }

        static Transform Find(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t.name == name && t.gameObject.scene.IsValid()) return t;
            return null;
        }

        /// <summary>Builds the mission briefing far from the rooms and renders it.</summary>
        public static string Objective()
        {
            var go = new GameObject("PreviewObjective");
            go.transform.position = new Vector3(50f, 1.6f, 50f);
            var s = go.AddComponent<ObjectiveScreen>();
            Call(s, "Build");
            return ViewShot.Shoot("p_objective", new Vector3(50f, 1.6f, 48.3f), 0f, 0f, 60f);
        }

        public static string Run(string outDir)
        {
            var bay = Find("SEC_MedicalBay");
            bay.gameObject.SetActive(true);
            Physics.SyncTransforms();

            var ec = Object.FindAnyObjectByType<EarCheckController>();
            Call(ec, "Build");
            foreach (var b in ec.Beacons) b.Grabbable.enabled = true;
            foreach (var c in Object.FindObjectsByType<CoolantCable>())
            {
                Call(c.GetComponent<Grabbable>(), "Awake");
                var oldModel = c.transform.Find("Model"); if (oldModel != null) oldModel.gameObject.SetActive(false); // Destroy is not allowed in edit mode
                Call(c, "Awake");
            }
            Physics.SyncTransforms();
            foreach (var c in Object.FindObjectsByType<CoolantCable>()) Call(c, "SettleOnSurface");
            var rack = Object.FindAnyObjectByType<CoolantRack>();
            Call(rack, "BuildPoster");
            var aw = Object.FindAnyObjectByType<AwakeningController>();
            Call(aw, "StyleCup");
            var tk = Object.FindAnyObjectByType<ThermalKeypad>();
            Call(tk, "Awake");
            tk.Begin();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);

            string log = "";
            log += ViewShot.Shoot("p_station", new Vector3(-1.25f, 1.65f, -0.2f), 180f, 18f, 85f) + "\n";
            log += ViewShot.Shoot("p_poster", new Vector3(-1.25f, 1.7f, -1.3f), 180f, 0f, 55f) + "\n";
            log += ViewShot.Shoot("p_cup", new Vector3(-0.2f, 1.3f, -1.9f), 204f, 28f, 40f) + "\n";
            log += ViewShot.Shoot("p_cables", new Vector3(1.1f, 1.5f, 0.95f), 0f, 40f, 60f) + "\n";
            log += ViewShot.Shoot("p_cable_poster", new Vector3(1.1f, 1.6f, 1.2f), 0f, 0f, 55f) + "\n";
            log += ViewShot.Shoot("p_keypad", new Vector3(-0.3f, 1.12f, 0.84f), 90f, 0f, 45f) + "\n";
            log += ViewShot.Shoot("p_keypad_far", new Vector3(-1.0f, 1.6f, 0.84f), 90f, 12f, 60f) + "\n";
            var prefab = Resources.Load<GameObject>("PF_SmokeSection");
            if (prefab != null)
            {
                var inst = Object.Instantiate(prefab);
                inst.SetActive(true);
                var mb = inst.GetComponentInChildren<MazeBuilder>(true);
                if (mb != null)
                {
                    mb.ActivateLayout('A');
                    if (mb.ExitDoorLeaf != null)
                    {
                        Physics.SyncTransforms();
                        var l = mb.ExitDoorLeaf.position;
                        log += "exit leaf at " + l + " handle=" + (mb.ExitHandle != null) + "\n";
                        log += ViewShot.Shoot("p_exit_door", l + new Vector3(0, 0.6f, -1.8f), 0f, 10f, 70f) + "\n";
                        log += ViewShot.Shoot("p_exit_door_near", l + new Vector3(0, 0.6f, -0.9f), 0f, 5f, 80f) + "\n";
                    }
                }
            }
            return log;
        }

    }
}
