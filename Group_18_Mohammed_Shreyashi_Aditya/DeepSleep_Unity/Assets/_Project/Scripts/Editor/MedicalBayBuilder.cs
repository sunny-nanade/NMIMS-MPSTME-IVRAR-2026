using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Fractured_Final.EditorTools.SceneBuilderUtil;

namespace Fractured_Final.EditorTools
{
    /// <summary>Builds SCN_MedicalBay (8 x 6 x 3 m) and the SCN_Setup alcove. Idempotent: rebuilds its own root each time.</summary>
    public static class MedicalBayBuilder
    {
        const string BedPath = "Assets/_Project/Models/gurney/Prefabs/hospital bed.prefab";
        const string CupPath = "Assets/_Project/Models/cup and tray/Plates, Bowls & Cups/Prefabs/Cups/Basic Ceramic.prefab";
        const string TrayPath = "Assets/_Project/Models/cup and tray/Plates, Bowls & Cups/Prefabs/Plates/Rectangle Plate.prefab";
        const string TerminalPath = "Assets/_Project/Models/scifi_terminal_2.glb";
        const string PodPath = "Assets/_Project/Models/sci-fi_pod_collection.glb";
        const string KeypadPath = Phase3Builder.KeypadPrefab;
        const string RackPath = Phase5Builder.RackPrefab; // PF_ServerRack, a variant of server_2
        const string TablePath = "Assets/_Project/Models/Sci Fi Modular Pack/Prefabs/Box3.prefab";
        const string PillarPath = Pack + "Elements/Element1.4m.prefab";

        // flip these after looking at a capture if the glb up axis is wrong
        public static Vector3 TerminalPreRotate = new Vector3(-90, 0, 0);
        public static Vector3 PodPreRotate = Vector3.zero;
        public static float PodRotY = 180f;

        static void ClearRoot(Scene s, params string[] names)
        {
            foreach (var r in s.GetRootGameObjects())
                foreach (var n in names) if (r.name == n) { Object.DestroyImmediate(r); break; }
        }

        static void RemoveDefaults(Scene s)
        {
            foreach (var r in s.GetRootGameObjects())
                if (r.name == "Main Camera" || r.name == "Directional Light") Object.DestroyImmediate(r);
        }

        static void SetAmbient(Color c)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = c;
            RenderSettings.fog = false;
        }

        [MenuItem("DeepSleep/Build/Medical Bay")]
        public static void BuildMedicalBay()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SCN_MedicalBay.unity", OpenSceneMode.Single);
            if (!System.IO.File.Exists(Phase3Builder.KeypadPrefab)) Phase3Builder.BuildPrefabs();
            if (!System.IO.File.Exists(Phase5Builder.CablePrefab(1)) || !System.IO.File.Exists(Phase5Builder.RackPrefab)) Phase5Builder.BuildPrefabs();
            RemoveDefaults(scene);
            ClearRoot(scene, "MedicalBay", "PF_PlayerRig", "PlayerSpawn", "CoreRoomSpawn", "Awakening", "EmergencyLighting");
            SetAmbient(new Color(0.07f, 0.07f, 0.09f));

            var root = new GameObject("MedicalBay").transform;
            var leaves = new List<GameObject>();
            var shell = Shell(root, "Shell", -4f, 4f, -3f, 3f, 3f, new[]
            {
                new Opening { side = 0, center = 0f, door = true },  // main welded door (west)
                new Opening { side = 1, center = 0f, door = true },  // hidden service door (east)
            }, leaves);
            if (leaves.Count > 0) leaves[0].name = "MainDoor_Welded";
            if (leaves.Count > 1) leaves[1].name = "ServiceDoor";

            // short dark vestibule behind the service door (Phase 4 puts the smoke-room entry trigger at its end)
            var vest = Shell(root, "Vestibule", 4.05f, 6.05f, -1.0f, 1.0f, 3f, new[] { new Opening { side = 0, center = 0f, door = false } });
            AddPointLight(vest.transform, "VestibuleLight", new Vector3(5.0f, 2.5f, 0f), new Color(0.6f, 0.7f, 0.9f), 0.25f, 4f);

            var entryZone = new GameObject("EntryZone");
            entryZone.transform.SetParent(vest.transform, false);
            entryZone.transform.position = new Vector3(5.6f, 1.3f, 0f);
            var ebc = entryZone.AddComponent<BoxCollider>(); ebc.size = new Vector3(0.8f, 2.6f, 1.6f); ebc.isTrigger = true;
            try { entryZone.tag = "EntryZone"; } catch { }
            var launcher = entryZone.AddComponent<BlockLauncher>();
            var lso = new SerializedObject(launcher);
            lso.FindProperty("zone").objectReferenceValue = ebc;
            lso.ApplyModifiedPropertiesWithoutUndo();

            var props = new GameObject("Props").transform; props.SetParent(root, false);

            // wall panel in front of the service door (opens in Phase 3)
            var panel = Spawn(WallPiece, props, "WallPanel");
            panel.transform.localScale = new Vector3(1.3f / 2f, 2.4f / 3f, 1f);
            panel.transform.rotation = Quaternion.Euler(0, 90, 0);
            Align(panel, new Vector3(3.7f, 1.2f, 0f));
            SetTagLayerRecursive(panel, "Wall", "Wall");

            // cryo pods along the north wall
            var pods = Prop(PodPath, props, "CryoPods", -2f, 2.2f, PodRotY, 0f, 0f, PodPreRotate);
            ClampToWall(pods, 3f - 0.12f, true);

            // hospital bed against the south wall, rescaled to 0.9 m
            Prop(BedPath, props, "HospitalBed", -2.2f, -2.4f, 0f, 0.9f);

            // side table, tray, cup
            var table = Prop(TablePath, props, "SideTable", -0.45f, -2.45f, 0f, 0.9f);
            float tableTop = WorldBounds(table).max.y;
            var tray = Spawn(TrayPath, props, "Tray");
            float tw = WorldBounds(tray).size.x;
            if (tw > 0.001f) tray.transform.localScale = Vector3.one * (0.34f / tw);
            Align(tray, new Vector3(-0.45f, tableTop, -2.45f), true, true, true, true);
            EnsureColliders(tray);
            float trayTop = WorldBounds(tray).max.y;
            var cup = Prop(CupPath, props, "Cup", -0.45f, -2.45f, 0f, 0f, trayTop + 0.005f, null, false);
            var rb = cup.AddComponent<Rigidbody>(); rb.mass = 0.2f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var cc = cup.AddComponent<CapsuleCollider>(); cc.radius = 0.045f; cc.height = 0.1f; cc.center = new Vector3(0, 0.045f, 0);
            cup.AddComponent<Grabbable>();
            cup.tag = "Grabbable";
            SetTagLayerRecursive(cup, null, "Interactable");

            // terminal (rescaled to about 1.0 m high), keypad pedestal + keypad
            var term = Prop(TerminalPath, props, "Terminal", 0.5f, -0.6f, 90f, 1.0f, 0f, TerminalPreRotate);
            var ped = Prop(PillarPath, props, "KeypadPedestal", 0.5f, 0.8f, 90f, 1.4f);
            var keypad = Spawn(KeypadPath, props, "Keypad");
            keypad.transform.rotation = Quaternion.Euler(0, 90, 0); // the keypad front (buttons) is local -z; face west toward the player
            var pb = WorldBounds(ped);
            Align(keypad, new Vector3(pb.min.x - 0.01f, 1.1f, pb.center.z), false, true, true);
            var kb = WorldBounds(keypad);
            keypad.transform.position += new Vector3(pb.min.x - kb.max.x, 0, 0);

            // server rack against the north wall (Coolant Reroute, Phase 5)
            var rack = Prop(RackPath, props, "ServerRack", 2.5f, 2.2f, 180f);
            ClampToWall(rack, 3f - 0.12f, true);

            Phase3Builder.AddToMedicalBay(root, props, keypad, panel, leaves[1]);
            Phase5Builder.AddToMedicalBay(root, props, rack, leaves[0]);

            var speaker = new GameObject("HalcyonSpeaker"); speaker.transform.SetParent(root, false); speaker.transform.position = new Vector3(0f, 2.85f, 0f);

            // lighting
            var lightRoot = new GameObject("EmergencyLighting");
            var el = lightRoot.AddComponent<EmergencyLighting>();
            var reds = new List<Light>();
            foreach (var p in new[] { new Vector2(-2.5f, -1.5f), new Vector2(2.5f, -1.5f), new Vector2(-2.5f, 1.5f), new Vector2(2.5f, 1.5f) })
                reds.Add(AddPointLight(lightRoot.transform, "RedPulse", new Vector3(p.x, 2.7f, p.y), new Color(1f, 0.1f, 0.05f), 1.2f, 7f));
            AddPointLight(lightRoot.transform, "Fill_A", new Vector3(-1.5f, 2.6f, 0f), new Color(0.6f, 0.7f, 0.9f), 0.35f, 7f);
            AddPointLight(lightRoot.transform, "Fill_B", new Vector3(2f, 2.6f, 0f), new Color(0.6f, 0.7f, 0.9f), 0.35f, 7f);
            var so = new SerializedObject(el);
            var pl = so.FindProperty("pulseLights"); pl.arraySize = reds.Count;
            for (int i = 0; i < reds.Count; i++) pl.GetArrayElementAtIndex(i).objectReferenceValue = reds[i];
            var sp = so.FindProperty("sparkPoints"); sp.arraySize = 2;
            sp.GetArrayElementAtIndex(0).objectReferenceValue = term.transform;
            sp.GetArrayElementAtIndex(1).objectReferenceValue = rack.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // oxygen display on the west wall beside the main door
            var ox = new GameObject("OxygenWallDisplay");
            ox.transform.SetParent(root, false);
            ox.transform.position = new Vector3(-3.86f, 1.7f, -1.7f);
            ox.transform.rotation = Quaternion.Euler(0, 270, 0);
            var oxd = ox.AddComponent<OxygenDisplay>();
            var oso = new SerializedObject(oxd);
            oso.FindProperty("size").vector2Value = new Vector2(0.9f, 0.45f);
            oso.ApplyModifiedPropertiesWithoutUndo();

            // spawns, rig, awakening
            Marker(null, "PlayerSpawn", new Vector3(-2f, 0f, 0.2f), 90f);
            Marker(null, "CoreRoomSpawn", new Vector3(2.5f, 0f, 0.4f), 0f);
            AddRig(new Vector3(-2f, 0f, 0.2f), 90f);

            var aw = new GameObject("Awakening").AddComponent<AwakeningController>();
            var aso = new SerializedObject(aw);
            aso.FindProperty("cup").objectReferenceValue = cup.GetComponent<Grabbable>();
            aso.FindProperty("tray").objectReferenceValue = tray.transform;
            aso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DeepSleep] Medical Bay built");
        }


        [MenuItem("DeepSleep/Build/Wrist Oxygen On Rig")]
        public static void AddWristOxygen()
        {
            var root = PrefabUtility.LoadPrefabContents(RigPrefabBuilder.PrefabPath);
            try
            {
                Transform hand = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "LeftHandAnchor") { hand = t; break; }
                if (hand == null) { Debug.LogError("[DeepSleep] LeftHandAnchor not found"); return; }
                var old = hand.Find("OxygenWrist"); if (old != null) Object.DestroyImmediate(old.gameObject);
                var go = new GameObject("OxygenWrist");
                go.transform.SetParent(hand, false);
                go.transform.localPosition = new Vector3(0f, 0.05f, -0.02f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var d = go.AddComponent<OxygenDisplay>();
                var so = new SerializedObject(d);
                so.FindProperty("mode").enumValueIndex = (int)OxygenDisplay.Mode.Wrist;
                so.FindProperty("size").vector2Value = new Vector2(0.12f, 0.06f);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, RigPrefabBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[DeepSleep] Wrist oxygen added to PF_PlayerRig");
        }

        /// <summary>Slide an object along z so that its far side touches the wall plane (north wall: max z = wallZ).</summary>
        static void ClampToWall(GameObject g, float wallZ, bool north)
        {
            var b = WorldBounds(g);
            g.transform.position += new Vector3(0, 0, north ? wallZ - b.max.z : 0);
        }

        [MenuItem("DeepSleep/Build/Setup Alcove")]
        public static void BuildSetupAlcove()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SCN_Setup.unity", OpenSceneMode.Single);
            Phase1TestArea.Remove();
            scene = SceneManager.GetActiveScene();
            ClearRoot(scene, "SetupAlcove", "SetupController");
            SetAmbient(new Color(0.09f, 0.1f, 0.13f));
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "Directional Light") Object.DestroyImmediate(r);

            var root = new GameObject("SetupAlcove").transform;
            Shell(root, "Shell", -3f, 3f, -2.5f, 3.5f, 3f, new Opening[0]);
            var pods = Prop(PodPath, root, "CryoPods", 0f, 3.0f, PodRotY, 0f, 0f, PodPreRotate);
            ClampToWall(pods, 3.5f - 0.12f, true);
            AddPointLight(root, "Fill", new Vector3(0f, 2.6f, 0.5f), new Color(0.7f, 0.8f, 1f), 0.9f, 9f);
            AddPointLight(root, "Accent", new Vector3(0f, 2.4f, 3.0f), new Color(0.3f, 0.6f, 1f), 0.6f, 6f);

            var sc = new GameObject("SetupController");
            sc.transform.position = new Vector3(0f, 1.45f, 1.5f);
            sc.AddComponent<SetupController>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DeepSleep] Setup alcove built");
        }
    }
}
