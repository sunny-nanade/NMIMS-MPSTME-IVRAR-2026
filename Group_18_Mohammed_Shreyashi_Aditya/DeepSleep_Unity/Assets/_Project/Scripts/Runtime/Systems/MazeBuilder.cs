using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Holds the two baked layouts of SCN_SmokeRoom (MazeRoot_A / MazeRoot_B, built from Modular_SciFi_Pack pieces by the editor tool
    /// DeepSleep/Build/Smoke Room), activates the one chosen by ConditionRandomizer and exposes its data and anchors.
    /// </summary>
    public class MazeBuilder : MonoBehaviour
    {
        [SerializeField] TextAsset layoutA;
        [SerializeField] TextAsset layoutB;
        [SerializeField] GameObject rootA;
        [SerializeField] GameObject rootB;
        [SerializeField] Transform entrySpawn;

        public MazeLayout Layout { get; private set; }
        public char ActiveName { get; private set; }
        public GameObject ActiveRoot { get; private set; }
        public Transform ExitAnchor { get; private set; }
        public Transform ExitDoorLeaf { get; private set; }
        public BoxCollider ExitZone { get; private set; }
        /// <summary>The glowing pull handle of the exit door; it slides open together with the door leaf.</summary>
        public Transform ExitHandle { get; private set; }

        public MazeLayout LoadLayout(char name)
        {
            var ta = name == 'A' ? layoutA : layoutB;
            return ta != null ? MazeLayout.Parse(ta.text) : null;
        }

        public void ActivateLayout(char name)
        {
            ActiveName = name;
            if (rootA != null) rootA.SetActive(name == 'A');
            if (rootB != null) rootB.SetActive(name == 'B');
            ActiveRoot = name == 'A' ? rootA : rootB;
            Layout = LoadLayout(name);
            if (ActiveRoot == null) { DS.Warn("MazeBuilder: no root for layout " + name); return; }
            ExitAnchor = Find(ActiveRoot.transform, "ExitDoorAnchor");
            ExitDoorLeaf = Find(ActiveRoot.transform, "ExitDoorLeaf");
            BuildExitGlow();
            var zone = Find(ActiveRoot.transform, "ExitZone");
            ExitZone = zone != null ? zone.GetComponent<BoxCollider>() : null;
            var spawn = Find(ActiveRoot.transform, "EntrySpawn");
            if (spawn != null && entrySpawn != null) entrySpawn.SetPositionAndRotation(spawn.position, spawn.rotation);
            DS.Log($"Maze layout {name} active ({(Layout != null ? Layout.Data.shortestMeters : 0)} m shortest path)");
        }

        /// <summary>A glowing green frame and handle on the exit door, so the way out is unmistakable once the player stands in front of it.</summary>
        void BuildExitGlow()
        {
            ExitHandle = null;
            if (ExitDoorLeaf == null) return;
            var old = Find(ActiveRoot.transform, "ExitGlow");
            if (old != null) Destroy(old.gameObject);
            Bounds b = default; bool any = false;
            foreach (var r in ExitDoorLeaf.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any) return;
            var root = new GameObject("ExitGlow").transform;
            root.SetParent(ActiveRoot.transform, false);
            var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var green = new Color(0.25f, 1f, 0.45f); glow.SetColor("_BaseColor", green); glow.color = green;
            var white = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var hot = new Color(0.85f, 1f, 0.9f); white.SetColor("_BaseColor", hot); white.color = hot;

            // the approach side is the one facing the maze: the door leaf is in a wall along X, the player arrives from -Z (see the exit anchor)
            float zFace = b.min.z - 0.06f;
            float w = b.size.x, h = b.size.y, cx = b.center.x;
            Bar(root, "FrameL", new Vector3(cx - w * 0.5f - 0.03f, h * 0.5f, zFace), new Vector3(0.07f, h + 0.07f, 0.05f), glow);
            Bar(root, "FrameR", new Vector3(cx + w * 0.5f + 0.03f, h * 0.5f, zFace), new Vector3(0.07f, h + 0.07f, 0.05f), glow);
            Bar(root, "FrameTop", new Vector3(cx, h + 0.035f, zFace), new Vector3(w + 0.13f, 0.07f, 0.05f), glow);
            var handle = Bar(root, "Handle", new Vector3(cx + w * 0.5f - 0.14f, 1.0f, zFace - 0.03f), new Vector3(0.045f, 0.42f, 0.05f), white);
            Bar(handle.transform, "HandleHalo", Vector3.zero, new Vector3(0.1f, 0.5f, 0.02f), glow).transform.localPosition = new Vector3(0, 0, 0.03f);
            ExitHandle = handle.transform;
        }

        static GameObject Bar(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); // primitive: exit door glow (KNOWN_ISSUES)
            g.name = name; Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            if (parent.name == "ExitGlow") g.transform.position = pos; else g.transform.localPosition = pos;
            if (parent.name == "ExitGlow") g.transform.localScale = size;
            else g.transform.localScale = new Vector3(size.x / parent.lossyScale.x, size.y / parent.lossyScale.y, size.z / parent.lossyScale.z);
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        public bool InsideExitZone(Vector3 world)
        {
            if (ExitZone == null) return false;
            Vector3 flat = new Vector3(world.x, ExitZone.bounds.center.y, world.z);
            return ExitZone.bounds.Contains(flat);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }
    }
}
