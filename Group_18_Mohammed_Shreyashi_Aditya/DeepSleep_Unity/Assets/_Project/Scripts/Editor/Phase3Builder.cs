using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Fractured_Final.EditorTools.SceneBuilderUtil;

namespace Fractured_Final.EditorTools
{
    /// <summary>Phase 3 content: PF_Keypad, and the Ear Check / keypad / trail objects inside SCN_MedicalBay.</summary>
    public static class Phase3Builder
    {
        const string PrefabDir = "Assets/_Project/Prefabs/";
        public const string KeypadPrefab = PrefabDir + "PF_Keypad.prefab";

        // trail route (world x, z) from beside the terminal to the wall panel on the east wall
        public static readonly Vector2[] TrailRoute =
        {
            new Vector2(1.05f, -0.6f), new Vector2(1.9f, -1.2f), new Vector2(2.9f, -0.9f), new Vector2(3.3f, -0.2f), new Vector2(3.35f, 0f)
        };

        [MenuItem("DeepSleep/Build/Phase 3 Prefabs")]
        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            BuildKeypad();
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] Phase 3 prefabs built");
        }

        static void BuildKeypad()
        {
            var src = Load("Assets/_Project/Models/Keypad/Prefabs/Keypad.prefab");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            g.name = "PF_Keypad";
            foreach (var c in g.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                string t = c.GetType().Name;
                if (t == "KeypadButton" || t == "Keypad" || t == "KeypadInteractionFPV") Object.DestroyImmediate(c);
            }
            foreach (var a in g.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a); // the asset's own source had no mixer group and is never used
            if (g.GetComponent<ThermalKeypad>() == null) g.AddComponent<ThermalKeypad>();
            PrefabUtility.SaveAsPrefabAsset(g, KeypadPrefab);
            Object.DestroyImmediate(g);
        }

        /// <summary>Called by MedicalBayBuilder after the shell and props exist.</summary>
        public static void AddToMedicalBay(Transform root, Transform props, GameObject keypad, GameObject wallPanel, GameObject serviceDoor)
        {
            var tk = keypad.GetComponent<ThermalKeypad>();

            // Ear Check
            var ear = new GameObject("EarCheck");
            ear.transform.SetParent(root, false);
            var ec = ear.AddComponent<EarCheckController>(); // builds its own tables, beacons and poster at run time
            var so = new SerializedObject(ec);
            so.FindProperty("keypad").objectReferenceValue = tk;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Trail
            var trail = new GameObject("Trail");
            trail.transform.SetParent(root, false);
            var wpRoot = new GameObject("TrailWaypoints").transform; wpRoot.SetParent(trail.transform, false);
            var wps = new List<Transform>();
            for (int i = 0; i < TrailRoute.Length; i++)
            {
                var w = new GameObject("WP_" + i).transform; w.SetParent(wpRoot, false);
                w.position = new Vector3(TrailRoute[i].x, 0f, TrailRoute[i].y);
                wps.Add(w);
            }
            // pull handle (primitive: no handle asset exists, listed in KNOWN_ISSUES)
            var handle = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            handle.name = "ServiceDoorHandle";
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(serviceDoor.transform, true);
            handle.transform.position = new Vector3(3.8f, 1.0f, 0.3f);
            handle.transform.localScale = new Vector3(0.04f, 0.15f, 0.04f);
            handle.GetComponent<Renderer>().sharedMaterial = HandleMaterial();

            var bt = trail.AddComponent<BreachTrail>();
            var tso = new SerializedObject(bt);
            var wa = tso.FindProperty("waypoints"); wa.arraySize = wps.Count;
            for (int i = 0; i < wps.Count; i++) wa.GetArrayElementAtIndex(i).objectReferenceValue = wps[i];
            tso.FindProperty("wallPanel").objectReferenceValue = wallPanel.transform;
            tso.FindProperty("serviceDoor").objectReferenceValue = serviceDoor.transform;
            tso.FindProperty("handle").objectReferenceValue = handle.transform;
            tso.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material HandleMaterial()
        {
            const string p = "Assets/_Project/Materials/M_Handle.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.55f, 0.58f, 0.62f));
            m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.6f);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }
    }
}
