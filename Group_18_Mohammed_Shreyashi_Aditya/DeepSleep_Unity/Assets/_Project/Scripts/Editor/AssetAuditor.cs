using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Generates Docs/ASSET_AUDIT.md: path, type, real-world size and role of every named asset.</summary>
    public static class AssetAuditor
    {
        const string Out = "Assets/_Project/Docs/ASSET_AUDIT.md";

        static readonly (string name, string search, string role)[] Named =
        {
            ("Keypad", "Keypad t:prefab", "PF_Keypad source"),
            ("server_2", "server_2 t:prefab", "PF_ServerRack source"),
            ("usb_type_c_cable", "usb_type_c_cable", "PF_Cable_01..03 source"),
            ("hospital bed", "hospital bed t:prefab", "Medical bay bed"),
            ("cup and tray", "Basic Ceramic t:prefab", "Cup (tray: plate prefabs)"),
            ("scifi_terminal_2", "scifi_terminal_2", "Central terminal"),
            ("monitor", "Monitor t:prefab", "Terminal alternative"),
            ("sci-fi_pod_collection", "sci-fi_pod_collection", "Cryo pods"),
            ("Sci-Fi Barrel_Grey", "Sci-Fi Barrel_Grey t:prefab", "Beacon inactive"),
            ("Sci-Fi Barrel_Blue", "Sci-Fi Barrel_Blue t:prefab", "Beacon correct"),
            ("Sci-Fi Barrel_Red", "Sci-Fi Barrel_Red t:prefab", "Beacon incorrect"),
            ("sci-fi_lab_machine", "sci-fi_lab_machine", "Prop"),
            ("shelving unit", "sci-fi_furniture_pack_aaa_shelving_unit_c", "Prop"),
            ("rugged_tablet", "rugged_tablet_0.1", "Prop"),
            ("barcode scanner", "barcode_scanner", "Prop"),
            ("USB hub", "USB hub t:prefab", "Prop"),
        };

        [MenuItem("DeepSleep/Tools/Generate Asset Audit")]
        public static void Generate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ASSET AUDIT");
            sb.AppendLine();
            sb.AppendLine("Sizes are world bounds (m) of the asset at scale 1. Flag column: OK / MISSING / SCALE?.");
            sb.AppendLine();
            sb.AppendLine("## Named assets");
            sb.AppendLine("| Name | Path | Type | Size W x H x D (m) | Role | Flag |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var n in Named)
            {
                var guids = AssetDatabase.FindAssets(n.search);
                string path = null;
                foreach (var g in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (p.EndsWith(".meta")) continue;
                    if (path == null || p.EndsWith(".prefab") || p.EndsWith(".glb")) path = p;
                    if (p.EndsWith(".prefab")) break;
                }
                if (path == null) { sb.AppendLine($"| {n.name} | - | - | - | {n.role} | MISSING |"); continue; }
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string size = "-", flag = "OK";
                if (go != null && TryBounds(go, out var b))
                {
                    size = $"{b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2}";
                    float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (mx > 15f || mx < 0.02f) flag = "SCALE?";
                }
                sb.AppendLine($"| {n.name} | {path} | {AssetDatabase.GetMainAssetTypeAtPath(path)?.Name} | {size} | {n.role} | {flag} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Modular_SciFi_Pack prefabs by type");
            string[] roots = { "Assets/Modular_SciFi_Pack (146 Objects, LowPoly)", "Assets/_Project/Models/Sci Fi Modular Pack" };
            var cats = new SortedDictionary<string, List<string>>();
            foreach (var root in roots)
            {
                foreach (var g in AssetDatabase.FindAssets("t:prefab", new[] { root }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go == null || !TryBounds(go, out var b)) continue;
                    string cat = Categorize(p);
                    if (!cats.ContainsKey(cat)) cats[cat] = new List<string>();
                    cats[cat].Add($"{p.Replace(root + "/", "")} : {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2}");
                }
            }
            foreach (var kv in cats)
            {
                sb.AppendLine($"### {kv.Key} ({kv.Value.Count})");
                foreach (var s in kv.Value) sb.AppendLine("- " + s);
                sb.AppendLine();
            }

            sb.AppendLine("## Photon / networking / XR Interaction Toolkit references (report only)");
            var hits = new List<string>();
            foreach (var p in AssetDatabase.GetAllAssetPaths())
            {
                if (!p.StartsWith("Assets/")) continue;
                string l = p.ToLowerInvariant();
                if (l.Contains("photon") || l.Contains("netcode") || l.Contains("xr interaction toolkit") || l.Contains("xri_")) hits.Add(p);
            }
            if (hits.Count == 0) sb.AppendLine("None found in Assets/.");
            else foreach (var h in hits) sb.AppendLine("- " + h);
            sb.AppendLine("Note: Packages/manifest.json contains com.meta.xr.sdk.all (multiplayer blocks editor assemblies ship inside it, unused).");

            File.WriteAllText(Out, sb.ToString());
            AssetDatabase.ImportAsset(Out);
            Debug.Log("[DeepSleep] Asset audit written to " + Out);
        }

        static string Categorize(string p)
        {
            string l = p.ToLowerInvariant();
            if (l.Contains("door")) return "door";
            if (l.Contains("ceiling")) return "ceiling";
            if (l.Contains("floor")) return "floor";
            if (l.Contains("wall") || l.Contains("element")) return "wall";
            if (l.Contains("box") || l.Contains("crate")) return "crate";
            if (l.Contains("terminal") || l.Contains("console")) return "console";
            if (l.Contains("window")) return "window";
            if (l.Contains("light")) return "light";
            if (l.Contains("pipe")) return "pipe";
            return "other";
        }

        public static bool TryBounds(GameObject asset, out Bounds b)
        {
            var go = Object.Instantiate(asset);
            go.hideFlags = HideFlags.HideAndDontSave;
            try { return TryBoundsInst(go, out b); }
            finally { Object.DestroyImmediate(go); }
        }

        static bool TryBoundsInst(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any)
            {
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds;
                    var size = Vector3.Scale(mb.size, mf.transform.lossyScale);
                    var nb = new Bounds(mf.transform.TransformPoint(mb.center), size);
                    if (!any) { b = nb; any = true; } else b.Encapsulate(nb);
                }
            }
            return any;
        }
    }
}
