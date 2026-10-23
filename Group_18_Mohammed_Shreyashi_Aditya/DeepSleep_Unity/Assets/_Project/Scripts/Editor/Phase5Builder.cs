using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Fractured_Final.EditorTools.SceneBuilderUtil;

namespace Fractured_Final.EditorTools
{
    /// <summary>Phase 5 content: PF_Cable_01..03, the coolant rack fittings in SCN_MedicalBay, and SCN_HallwayDebrief.</summary>
    public static class Phase5Builder
    {
        const string PrefabDir = "Assets/_Project/Prefabs/";
        const string CableModel = "Assets/_Project/Models/usb_type_c_cable.glb";
        const string TablePath = "Assets/_Project/Models/Sci Fi Modular Pack/Prefabs/Box3.prefab";
        public static string CablePrefab(int n) { return PrefabDir + "PF_Cable_0" + n + ".prefab"; }

        static readonly string[] CableNames = { "Red", "Blue", "Yellow" };
        static readonly Color[] CableColors = { new Color(0.9f, 0.1f, 0.1f), new Color(0.15f, 0.35f, 1f), new Color(1f, 0.85f, 0.1f) };
        static readonly int[] CablePorts = { 2, 5, 3 };

        [MenuItem("DeepSleep/Build/Phase 5 Prefabs")]
        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            for (int i = 0; i < 3; i++) BuildCable(i);
            BuildRack();
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] Phase 5 prefabs built");
        }

        public const string RackSource = "Assets/_Project/Models/server/Server Room/Prefabs/server_2.prefab";
        public const string RackPrefab = PrefabDir + "PF_ServerRack.prefab";

        static void BuildRack()
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(Load(RackSource));
            g.name = "PF_ServerRack";
            PrefabUtility.SaveAsPrefabAsset(g, RackPrefab);
            Object.DestroyImmediate(g);
        }

        static void BuildCable(int i)
        {
            var root = new GameObject("PF_Cable_0" + (i + 1));
            var model = Spawn(CableModel, root.transform, "Model");
            var b = WorldBounds(model);
            float longest = Mathf.Max(b.size.x, b.size.z);
            float s = 0.5f / Mathf.Max(0.01f, longest);
            model.transform.localScale = Vector3.one * s;
            Align(model, root.transform.position);
            b = WorldBounds(model);
            bool alongX = b.size.x >= b.size.z;
            var tip = new GameObject("PlugTip").transform; tip.SetParent(root.transform, false);
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            tip.position = b.center + axis * (Mathf.Max(b.size.x, b.size.z) * 0.5f);
            tip.rotation = Quaternion.LookRotation(axis, Vector3.up);
            var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.1f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<Grabbable>();
            var cc = root.AddComponent<CoolantCable>();
            var so = new SerializedObject(cc);
            so.FindProperty("cableName").stringValue = CableNames[i];
            so.FindProperty("tint").colorValue = CableColors[i];
            so.FindProperty("correctPort").intValue = CablePorts[i];
            so.FindProperty("plugTip").objectReferenceValue = tip;
            so.ApplyModifiedPropertiesWithoutUndo();
            EnsureColliders(root);
            try { root.tag = "Grabbable"; } catch { }
            SetTagLayerRecursive(root, null, "Interactable");
            PrefabUtility.SaveAsPrefabAsset(root, CablePrefab(i + 1));
            Object.DestroyImmediate(root);
        }

        static Material MakeMaterial(string name, Color c, float metallic, float smooth)
        {
            string p = "Assets/_Project/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t); // primitive: no asset exists for panels, sockets and the lever (KNOWN_ISSUES)
            g.name = name; g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = scale;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        public static void AddToMedicalBay(Transform root, Transform props, GameObject rackGo, GameObject blastDoor)
        {
            // cable table + cables
            var table = Prop(TablePath, props, "CableTable", 1.1f, 1.75f, 0f, 0.9f);
            float top = WorldBounds(table).max.y;
            var cableList = new List<CoolantCable>();
            for (int i = 0; i < 3; i++)
            {
                var c = (GameObject)PrefabUtility.InstantiatePrefab(Load(CablePrefab(i + 1)));
                c.transform.SetParent(props, false);
                c.transform.rotation = Quaternion.Euler(0, 90 + i * 8, 0);
                var cb = WorldBounds(c);
                c.transform.position += new Vector3(0.8f + 0.3f * i, top + 0.03f, 1.75f) - new Vector3(cb.center.x, cb.min.y, cb.center.z);
                cableList.Add(c.GetComponent<CoolantCable>());
            }

            // panel, ports, labels
            var plateMat = MakeMaterial("M_CoolantPlate", new Color(0.12f, 0.13f, 0.15f), 0.5f, 0.4f);
            var socketMat = MakeMaterial("M_PortSocket", new Color(0.02f, 0.02f, 0.03f), 0.2f, 0.2f);
            Prim(PrimitiveType.Cube, "CoolantPanel", props, new Vector3(2.5f, 1.39f, 1.585f), new Vector3(0.75f, 0.5f, 0.02f), plateMat);
            var portRoot = new GameObject("Ports").transform; portRoot.SetParent(props, false);
            var ports = new List<CoolantPort>();
            for (int n = 1; n <= 6; n++)
            {
                int col = (n - 1) % 3, row = (n - 1) / 3;
                Vector3 p = new Vector3(2.25f + 0.25f * col, 1.5f - 0.22f * row, 1.57f);
                var g = new GameObject("Port_" + n);
                g.transform.SetParent(portRoot, false);
                g.transform.position = p; g.transform.rotation = Quaternion.Euler(0, 180, 0); // forward points out of the rack face
                var sc = g.AddComponent<SphereCollider>(); sc.radius = 0.04f; sc.isTrigger = true;
                try { g.tag = "Port"; } catch { }
                var cp = g.AddComponent<CoolantPort>();
                var pso = new SerializedObject(cp); pso.FindProperty("index").intValue = n; pso.ApplyModifiedPropertiesWithoutUndo();
                ports.Add(cp);
                Prim(PrimitiveType.Cube, "Socket_" + n, g.transform, p + new Vector3(0, 0, -0.002f), new Vector3(0.07f, 0.035f, 0.012f), socketMat);
                var label = WorldUI.Text(portRoot, n.ToString(), WorldUI.Cm(0.04f), p + new Vector3(0, 0.055f, -0.004f), new Vector2(0.1f, 0.06f), TextAlignmentOptions.Center, new Color(0.85f, 0.95f, 1f));
                label.name = "PortLabel_" + n;
                label.transform.position = p + new Vector3(0, 0.055f, -0.004f);
            }

            // lever
            var handleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_Handle.mat") ?? plateMat;
            Prim(PrimitiveType.Cube, "LeverSlot", props, new Vector3(3.1f, 1.2f, 1.58f), new Vector3(0.05f, 0.45f, 0.015f), socketMat);
            var lever = Prim(PrimitiveType.Capsule, "LeverHandle", props, new Vector3(3.1f, 1.1f, 1.5f), new Vector3(0.05f, 0.05f, 0.05f), handleMat);

            // rack logic
            var rackLogic = new GameObject("CoolantRack");
            rackLogic.transform.SetParent(root, false);
            var cr = rackLogic.AddComponent<CoolantRack>();
            var so = new SerializedObject(cr);
            var ca = so.FindProperty("cables"); ca.arraySize = cableList.Count;
            for (int i = 0; i < cableList.Count; i++) ca.GetArrayElementAtIndex(i).objectReferenceValue = cableList[i];
            var pa = so.FindProperty("ports"); pa.arraySize = ports.Count;
            for (int i = 0; i < ports.Count; i++) pa.GetArrayElementAtIndex(i).objectReferenceValue = ports[i];
            so.FindProperty("leverHandle").objectReferenceValue = lever.transform;
            so.FindProperty("blastDoor").objectReferenceValue = blastDoor != null ? blastDoor.transform : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            var q = new GameObject("Questionnaire");
            q.transform.SetParent(root, false);
            q.AddComponent<QuestionnaireUI>();
        }

        // ------------------------------------------------------------------ hallway
        [MenuItem("DeepSleep/Build/Hallway Debrief")]
        public static void BuildHallway()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SCN_HallwayDebrief.unity", OpenSceneMode.Single);
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "Main Camera" || r.name == "Directional Light" || r.name == "Hallway" || r.name == "HallwaySpawn" || r.name == "PF_PlayerRig" || r.name == "EmergencyLighting" || r.name == "Questionnaire")
                    Object.DestroyImmediate(r);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.09f);
            RenderSettings.fog = false;

            var root = new GameObject("Hallway").transform;
            Shell(root, "Shell", -1f, 1f, 0f, 9f, 3f, new Opening[0]);
            var lightRoot = new GameObject("EmergencyLighting");
            var el = lightRoot.AddComponent<EmergencyLighting>();
            var reds = new List<Light>();
            foreach (float z in new[] { 2f, 5f, 8f }) reds.Add(AddPointLight(lightRoot.transform, "RedPulse", new Vector3(0, 2.7f, z), new Color(1f, 0.1f, 0.05f), 1.2f, 6f));
            AddPointLight(lightRoot.transform, "Fill", new Vector3(0, 2.6f, 6.5f), new Color(0.6f, 0.7f, 0.9f), 0.5f, 7f);
            var so = new SerializedObject(el);
            var pl = so.FindProperty("pulseLights"); pl.arraySize = reds.Count;
            for (int i = 0; i < reds.Count; i++) pl.GetArrayElementAtIndex(i).objectReferenceValue = reds[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var speaker = new GameObject("HalcyonSpeaker"); speaker.transform.SetParent(root, false); speaker.transform.position = new Vector3(0f, 2.85f, 4.5f);

            // mission-log plaque on the far wall
            var plaqueGo = new GameObject("Plaque");
            plaqueGo.transform.SetParent(root, false);
            plaqueGo.transform.position = new Vector3(0f, 1.7f, 8.85f);
            var tmp = plaqueGo.AddComponent<TextMeshPro>();
            tmp.text = SaveLoad.Plaque("1", 0f, 0f);
            tmp.fontSize = WorldUI.Cm(0.12f) * WorldUI.FontScale;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = new Color(0.8f, 0.95f, 1f);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(1.8f, 1.0f);
            Marker(null, "HallwaySpawn", new Vector3(0f, 0f, 4.5f), 0f);

            var q = new GameObject("Questionnaire");
            var qui = q.AddComponent<QuestionnaireUI>();
            var qso = new SerializedObject(qui);
            qso.FindProperty("plaque").objectReferenceValue = tmp;
            qso.ApplyModifiedPropertiesWithoutUndo();

            AddRig(new Vector3(0f, 0f, 4.5f), 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DeepSleep] Hallway built");
        }
    }
}
