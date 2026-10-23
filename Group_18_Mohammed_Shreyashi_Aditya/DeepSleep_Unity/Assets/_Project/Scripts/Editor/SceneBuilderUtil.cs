using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Shared helpers for the scene builders: bounds-based placement of pack pieces and props, shells with door openings.</summary>
    public static class SceneBuilderUtil
    {
        public const string Pack = "Assets/Modular_SciFi_Pack (146 Objects, LowPoly)/Prefabs/Cleen/";
        public const string WallPiece = Pack + "Wall1/Wall2_3.prefab";    // 2 x 3 m slab
        public const string FloorPiece = Pack + "Floor/Floor2_2.prefab";   // 2 x 2 m
        public const string CeilingPiece = Pack + "Ceiling/Ceiling2_2.prefab";
        public const string DoorPiece = Pack + "Door/Door2.prefab";        // 2 x 3 m panel
        public const float DoorW = 0.9f, DoorH = 2.1f;

        public static GameObject Load(string path)
        {
            var a = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (a == null) Debug.LogError("[DeepSleep] builder: missing asset " + path);
            return a;
        }

        public static GameObject Spawn(string path, Transform parent, string name)
        {
            var a = Load(path);
            if (a == null) return new GameObject(name + "_MISSING");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(a);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
            return g;
        }

        public static Bounds WorldBounds(GameObject g)
        {
            Bounds b = new Bounds(g.transform.position, Vector3.zero); bool f = false;
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!f) { b = r.bounds; f = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Move g so that the chosen bounds point lands on target. Axes: which of x,y,z to align; y aligns the bottom when yBottom.</summary>
        public static void Align(GameObject g, Vector3 target, bool ax = true, bool ay = true, bool az = true, bool yBottom = false)
        {
            var b = WorldBounds(g);
            Vector3 d = Vector3.zero;
            if (ax) d.x = target.x - b.center.x;
            if (az) d.z = target.z - b.center.z;
            if (ay) d.y = yBottom ? target.y - b.min.y : target.y - b.center.y;
            g.transform.position += d;
        }

        public static void SetTagLayerRecursive(GameObject g, string tag, string layer)
        {
            int l = string.IsNullOrEmpty(layer) ? -1 : LayerMask.NameToLayer(layer);
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
            {
                if (l >= 0) t.gameObject.layer = l;
                if (!string.IsNullOrEmpty(tag)) { try { t.gameObject.tag = tag; } catch { } }
            }
        }

        public static void EnsureColliders(GameObject g)
        {
            if (g.GetComponentInChildren<Collider>() != null) return;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
                if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<BoxCollider>();
        }

        /// <summary>Spawn a prop, rotate, uniformly scale to a height (if > 0), place with bottom at yBottom and bounds centre at (cx, cz).</summary>
        public static GameObject Prop(string path, Transform parent, string name, float cx, float cz, float rotY = 0f, float height = 0f,
            float yBottom = 0f, Vector3? preRotate = null, bool colliders = true)
        {
            var g = Spawn(path, parent, name);
            g.transform.rotation = Quaternion.Euler(0, rotY, 0) * Quaternion.Euler(preRotate ?? Vector3.zero);
            if (height > 0f)
            {
                float h = WorldBounds(g).size.y;
                if (h > 0.0001f) g.transform.localScale = Vector3.one * (height / h);
            }
            Align(g, new Vector3(cx, yBottom, cz), true, true, true, true);
            if (colliders) EnsureColliders(g);
            return g;
        }

        public struct Opening { public int side; public float center; public bool door; }

        /// <summary>
        /// Builds floor, ceiling and four walls around [minX,maxX] x [minZ,maxZ]. Side: 0 west (x=min), 1 east (x=max), 2 south (z=min), 3 north (z=max).
        /// Openings are 0.9 x 2.1 m. When door is true a door leaf is placed in the opening (returned in leaves).
        /// </summary>
        public static GameObject Shell(Transform parent, string name, float minX, float maxX, float minZ, float maxZ, float height,
            Opening[] openings, List<GameObject> leaves = null)
        {
            var root = new GameObject(name); root.transform.SetParent(parent, false);
            var floors = new GameObject("Floor"); floors.transform.SetParent(root.transform, false);
            var ceil = new GameObject("Ceiling"); ceil.transform.SetParent(root.transform, false);
            var walls = new GameObject("Walls"); walls.transform.SetParent(root.transform, false);
            Tile(floors.transform, FloorPiece, minX, maxX, minZ, maxZ, -0.1f, "Floor");
            Tile(ceil.transform, CeilingPiece, minX, maxX, minZ, maxZ, height + 0.1f, "Ceiling");
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side >= 2;
                float line = side == 0 ? minX : side == 1 ? maxX : side == 2 ? minZ : maxZ;
                float a = alongX ? minX : minZ, b = alongX ? maxX : maxZ;
                var cuts = new List<Opening>();
                foreach (var o in openings) if (o.side == side) cuts.Add(o);
                cuts.Sort((p, q) => p.center.CompareTo(q.center));
                float cur = a;
                foreach (var o in cuts)
                {
                    WallRun(walls.transform, alongX, line, cur, o.center - DoorW * 0.5f, height, "W" + side);
                    // lintel
                    var lin = WallPiece3(walls.transform, alongX, line, o.center - DoorW * 0.5f, o.center + DoorW * 0.5f, DoorH, height, "Lintel" + side);
                    if (o.door)
                    {
                        var leaf = Spawn(DoorPiece, root.transform, "DoorLeaf" + side);
                        leaf.transform.localScale = new Vector3(DoorW / 2f, DoorH / 3f, 1f);
                        leaf.transform.rotation = Quaternion.Euler(0, alongX ? 0 : 90, 0);
                        Vector3 c = alongX ? new Vector3(o.center, DoorH * 0.5f, line) : new Vector3(line, DoorH * 0.5f, o.center);
                        Align(leaf, c);
                        SetTagLayerRecursive(leaf, "Wall", "Wall");
                        leaves?.Add(leaf);
                    }
                    cur = o.center + DoorW * 0.5f;
                }
                WallRun(walls.transform, alongX, line, cur, b, height, "W" + side);
            }
            return root;
        }

        static void Tile(Transform parent, string path, float minX, float maxX, float minZ, float maxZ, float yCenter, string n)
        {
            int nx = Mathf.CeilToInt((maxX - minX) / 2f - 0.001f), nz = Mathf.CeilToInt((maxZ - minZ) / 2f - 0.001f);
            float sx = (maxX - minX) / nx, sz = (maxZ - minZ) / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var g = Spawn(path, parent, n + "_" + i + "_" + j);
                    g.transform.localScale = new Vector3(sx / 2f, 1f, sz / 2f);
                    Align(g, new Vector3(minX + sx * (i + 0.5f), yCenter, minZ + sz * (j + 0.5f)));
                }
        }

        static void WallRun(Transform parent, bool alongX, float line, float a, float b, float height, string n)
        {
            float len = b - a;
            if (len < 0.05f) return;
            int cnt = Mathf.CeilToInt(len / 2f - 0.001f);
            float w = len / cnt;
            for (int i = 0; i < cnt; i++)
                WallPiece3(parent, alongX, line, a + w * i, a + w * (i + 1), height, height, n + "_" + i, true);
        }

        /// <summary>One wall slab spanning [a,b] along the wall; for lintels, from yTop up to height.</summary>
        static GameObject WallPiece3(Transform parent, bool alongX, float line, float a, float b, float h0, float height, string n, bool full = false)
        {
            float y0 = full ? 0f : h0, y1 = height;
            if (y1 - y0 < 0.05f) return null;
            var g = Spawn(WallPiece, parent, n);
            g.transform.localScale = new Vector3((b - a) / 2f, (y1 - y0) / 3f, 1f);
            g.transform.rotation = Quaternion.Euler(0, alongX ? 0 : 90, 0);
            float m = (a + b) * 0.5f;
            Align(g, alongX ? new Vector3(m, (y0 + y1) * 0.5f, line) : new Vector3(line, (y0 + y1) * 0.5f, m));
            SetTagLayerRecursive(g, "Wall", "Wall");
            return g;
        }

        public static GameObject Marker(Transform parent, string name, Vector3 pos, float yaw)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0, yaw, 0);
            return g;
        }

        public static GameObject AddRig(Vector3 pos, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabBuilder.PrefabPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.position = pos; rig.transform.rotation = Quaternion.Euler(0, yaw, 0);
            return rig;
        }

        public static Light AddPointLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = pos;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }
    }
}
