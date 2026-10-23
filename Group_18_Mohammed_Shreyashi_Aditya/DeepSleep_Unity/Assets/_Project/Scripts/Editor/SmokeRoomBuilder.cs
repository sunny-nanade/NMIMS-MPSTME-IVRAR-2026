using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Fractured_Final.EditorTools.SceneBuilderUtil;

namespace Fractured_Final.EditorTools
{
    /// <summary>Builds SCN_SmokeRoom: both baked maze layouts (A and B) from Modular_SciFi_Pack Metal pieces, markers, zones and the block controller objects.</summary>
    public static class SmokeRoomBuilder
    {
        const string Metal = "Assets/Modular_SciFi_Pack (146 Objects, LowPoly)/Prefabs/Metal/";
        const string WallPath = Metal + "Wall1/Wall2_3.003.prefab";   // 2 x 3 plain slab, scaled to 2.6 m
        const string FloorPath = Metal + "Floor/Floor2_2.prefab";
        const string CeilingPath = Metal + "Ceiling/Ceiling2_2.prefab";
        const string DoorPath = Metal + "Door/Door2.prefab";
        const string DataDir = "Assets/_Project/Data/";
        public const float WallHeight = 2.6f;

        [MenuItem("DeepSleep/Build/Smoke Room")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/SCN_SmokeRoom.unity", OpenSceneMode.Single);
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name == "Main Camera" || r.name == "Directional Light" || r.name == "Maze" || r.name == "MazeRoot_A" || r.name == "MazeRoot_B"
                    || r.name == "SmokeEntrySpawn" || r.name == "EgressBlock" || r.name == "PF_PlayerRig") Object.DestroyImmediate(r);
            }
            var taA = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "MazeLayoutA.json");
            var taB = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "MazeLayoutB.json");
            var rootA = BuildLayout(JsonUtility.FromJson<MazeLayoutData>(taA.text), "MazeRoot_A");
            var rootB = BuildLayout(JsonUtility.FromJson<MazeLayoutData>(taB.text), "MazeRoot_B");
            rootB.SetActive(false);

            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.99f;
            RenderSettings.fogColor = new Color(0.24f, 0.20f, 0.16f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.16f, 0.13f, 0.11f);
            RenderSettings.skybox = null;

            var spawn = Marker(null, "SmokeEntrySpawn", Vector3.zero, 0f);
            var mazeGo = new GameObject("Maze");
            var mb = mazeGo.AddComponent<MazeBuilder>();
            var mso = new SerializedObject(mb);
            mso.FindProperty("layoutA").objectReferenceValue = taA;
            mso.FindProperty("layoutB").objectReferenceValue = taB;
            mso.FindProperty("rootA").objectReferenceValue = rootA;
            mso.FindProperty("rootB").objectReferenceValue = rootB;
            mso.FindProperty("entrySpawn").objectReferenceValue = spawn.transform;
            mso.ApplyModifiedPropertiesWithoutUndo();
            // place the spawn for layout A so the scene is usable on its own
            var spawnA = rootA.transform.Find("EntrySpawn");
            if (spawnA != null) spawn.transform.SetPositionAndRotation(spawnA.position, spawnA.rotation);

            var eg = new GameObject("EgressBlock");
            var tracker = eg.AddComponent<PathTracker>();
            var audioSwitch = eg.AddComponent<AudioConditionSwitch>();
            var smoke = eg.AddComponent<SmokeVisibility>();
            var mentor = eg.AddComponent<MentorDirector>();
            var mentorSo = new SerializedObject(mentor);
            mentorSo.FindProperty("audioSwitch").objectReferenceValue = audioSwitch;
            mentorSo.FindProperty("tracker").objectReferenceValue = tracker;
            mentorSo.ApplyModifiedPropertiesWithoutUndo();
            var egress = eg.AddComponent<EgressBlockController>();
            var eso = new SerializedObject(egress);
            eso.FindProperty("maze").objectReferenceValue = mb;
            eso.FindProperty("tracker").objectReferenceValue = tracker;
            eso.FindProperty("mentor").objectReferenceValue = mentor;
            eso.FindProperty("audioSwitch").objectReferenceValue = audioSwitch;
            eso.FindProperty("smoke").objectReferenceValue = smoke;
            eso.ApplyModifiedPropertiesWithoutUndo();

            var rigGo = AddRig(spawn.transform.position, spawn.transform.eulerAngles.y);
            rigGo.SetActive(false); // enabled by EgressBlockController only when the scene runs on its own (a duplicate rig in an additive load is risky)
            eso.FindProperty("standaloneRig").objectReferenceValue = rigGo;
            eso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DeepSleep] Smoke room built");
        }

        static GameObject BuildLayout(MazeLayoutData d, string rootName)
        {
            float C = d.cellSize, H = WallHeight;
            var root = new GameObject(rootName);
            var floors = Group(root, "Floors"); var ceilings = Group(root, "Ceilings"); var walls = Group(root, "Walls");
            var markers = Group(root, "Markers");
            var layout = MazeLayout.Parse(JsonUtility.ToJson(d));
            int n = d.grid;

            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (!layout.Used(c, r)) continue;
                    Vector3 ctr = layout.CellCenter(new MazeCell(c, r));
                    Tile(floors.transform, FloorPath, new Vector3(ctr.x, -0.1f, ctr.z), C, "Floor_" + c + "_" + r);
                    Tile(ceilings.transform, CeilingPath, new Vector3(ctr.x, H + 0.1f, ctr.z), C, "Ceiling_" + c + "_" + r);
                }

            // walls: each edge once. N and E edges of every cell, plus the W and S boundary.
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    bool used = layout.Used(c, r);
                    // east edge (between c and c+1) and north edge (between r and r+1)
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nc = c + MazeLayout.DirDc(dir), nr = r + MazeLayout.DirDr(dir);
                        bool outside = !layout.Inside(nc, nr);
                        bool primary = dir == 0 || dir == 1;          // N and E are owned by this cell
                        bool boundaryOwned = outside && (dir == 2 || dir == 3);
                        if (!primary && !boundaryOwned) continue;
                        bool anyUsed = used || (!outside && layout.Used(nc, nr));
                        if (!anyUsed) continue;
                        if (!outside && layout.IsOpen(c, r, dir)) continue;
                        // edge midpoint
                        Vector3 ctr = layout.CellCenter(new MazeCell(c, r));
                        Vector3 mid = ctr + new Vector3(MazeLayout.DirDc(dir), 0, MazeLayout.DirDr(dir)) * (C * 0.5f);
                        bool alongX = dir == 0 || dir == 2;           // N/S edges run along x
                        bool isExit = new MazeCell(c, r) == layout.Exit && dir == 0 && outside;
                        bool isStart = new MazeCell(c, r) == layout.Start && dir == 2 && outside;
                        if (isExit || isStart) DoorEdge(walls.transform, root.transform, mid, alongX, C, H, isExit);
                        else WallSlab(walls.transform, mid, alongX, C, 0f, H, "Wall_" + c + "_" + r + "_" + dir);
                    }
                }

            // markers and zones
            foreach (var j in d.junctions)
            {
                var g = Box(markers.transform, "Junction_" + j.c + "_" + j.r, layout.CellCenter(new MazeCell(j.c, j.r)) + Vector3.up * (H * 0.5f), new Vector3(C, H, C), "Junction");
            }
            foreach (var e in d.deadEnds)
                Box(markers.transform, "DeadEndCap_" + e.c + "_" + e.r, layout.CellCenter(new MazeCell(e.c, e.r)) + Vector3.up * (H * 0.5f), new Vector3(C, H, C), "DeadEndCap");
            Vector3 sc = layout.CellCenter(layout.Start), ec = layout.CellCenter(layout.Exit);
            Box(markers.transform, "EntryZone", sc + new Vector3(0, H * 0.5f, -C * 0.4f), new Vector3(1.6f, H, 1.2f), "EntryZone");
            Box(markers.transform, "ExitZone", ec + new Vector3(0, H * 0.5f, C * 0.4f), new Vector3(1.6f, H, 1.2f), "ExitZone");
            var anchor = new GameObject("ExitDoorAnchor"); anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = new Vector3(ec.x, 1.5f, ec.z + C * 0.5f - 0.15f);
            var sp = new GameObject("EntrySpawn"); sp.transform.SetParent(root.transform, false);
            sp.transform.position = new Vector3(sc.x, 0f, sc.z - 0.1f); sp.transform.rotation = Quaternion.identity;
            SetStatic(root);
            ReplaceEmissiveMaterials(root);
            return root;
        }

        /// <summary>
        /// The pack's Metal material has a white emission map (glowing seams in every floor, ceiling, wall and door piece). In the smoke room that would read as
        /// floor lights and guide lines, so every Metal material in the baked layouts is swapped for a copy with emission off.
        /// </summary>
        static void ReplaceEmissiveMaterials(GameObject root)
        {
            const string path = "Assets/_Project/Materials/M_SmokeMetal.mat";
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.IsKeywordEnabled("_EMISSION") || !m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent <= 0.01f) continue;
                    if (variant == null)
                    {
                        variant = new Material(m) { name = "M_SmokeMetal" };
                        variant.DisableKeyword("_EMISSION");
                        variant.SetColor("_EmissionColor", Color.black);
                        variant.SetTexture("_EmissionMap", null);
                        variant.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        AssetDatabase.CreateAsset(variant, path);
                    }
                    mats[i] = variant; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static GameObject Group(GameObject root, string name)
        {
            var g = new GameObject(name); g.transform.SetParent(root.transform, false); return g;
        }

        static void Tile(Transform parent, string path, Vector3 center, float cell, string name)
        {
            var g = Spawn(path, parent, name);
            g.transform.localScale = new Vector3(cell / 2f, 1f, cell / 2f);
            Align(g, center);
        }

        /// <summary>One wall slab centred on (mid) spanning 'width' along the wall, from y0 up to y1.</summary>
        static GameObject WallSlab(Transform parent, Vector3 mid, bool alongX, float width, float y0, float y1, string name, float widthOffset = 0f)
        {
            var g = Spawn(WallPath, parent, name);
            g.transform.localScale = new Vector3(width / 2f, (y1 - y0) / 3f, 1f);
            g.transform.rotation = Quaternion.Euler(0, alongX ? 0 : 90, 0);
            Vector3 c = new Vector3(mid.x, (y0 + y1) * 0.5f, mid.z) + (alongX ? Vector3.right : Vector3.forward) * widthOffset;
            Align(g, c);
            SetTagLayerRecursive(g, "Wall", "Wall");
            return g;
        }

        static void DoorEdge(Transform walls, Transform root, Vector3 mid, bool alongX, float C, float H, bool isExit)
        {
            float side = (C - DoorW) * 0.5f;
            Vector3 dirv = alongX ? Vector3.right : Vector3.forward;
            WallSlab(walls, mid, alongX, side, 0f, H, "DoorSideA", -(DoorW + side) * 0.5f);
            WallSlab(walls, mid, alongX, side, 0f, H, "DoorSideB", (DoorW + side) * 0.5f);
            WallSlab(walls, mid, alongX, DoorW, DoorH, H, "Lintel");
            var leaf = Spawn(DoorPath, root, isExit ? "ExitDoorLeaf" : "StartDoorLeaf");
            leaf.transform.localScale = new Vector3(DoorW / 2f, DoorH / 3f, 1f);
            leaf.transform.rotation = Quaternion.Euler(0, alongX ? 0 : 90, 0);
            Align(leaf, new Vector3(mid.x, DoorH * 0.5f, mid.z));
            SetTagLayerRecursive(leaf, "Wall", "Wall");
        }

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, string tag)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.position = center;
            var bc = g.AddComponent<BoxCollider>(); bc.size = size; bc.isTrigger = true;
            try { g.tag = tag; } catch { Debug.LogWarning("[DeepSleep] tag missing: " + tag); }
            return g;
        }

        static void SetStatic(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Renderer>() != null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
