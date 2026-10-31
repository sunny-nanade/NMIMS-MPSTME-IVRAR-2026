using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RescueSim.EditorTools
{
    /// <summary>Writes a plain-text report of the open scene (hierarchy, bounds, lights, triangle counts) to Temp/scene_report.txt.</summary>
    public static class SceneReport
    {
        [MenuItem("Rescue Sim/Dump Scene Report")]
        public static void Dump()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine($"Scene: {scene.path}");

            long tris = 0; int renderers = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf.sharedMesh == null) continue;
                tris += mf.sharedMesh.triangles.Length / 3;
                renderers++;
            }
            sb.AppendLine($"MeshFilters: {renderers}, triangles: {tris}");

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            int shadowLights = 0;
            foreach (var l in lights) if (l.shadows != LightShadows.None) shadowLights++;
            sb.AppendLine($"Lights: {lights.Length} (shadow casting: {shadowLights})");
            foreach (var l in lights)
                sb.AppendLine($"  LIGHT {Path(l.transform)} type={l.type} mode={l.lightmapBakeType} shadows={l.shadows} range={l.range:F1} intensity={l.intensity:F2} pos={l.transform.position:F1}");

            sb.AppendLine($"Skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}  AmbientMode={RenderSettings.ambientMode}");

            sb.AppendLine("--- Hierarchy (depth<=3) with combined renderer bounds ---");
            foreach (var root in scene.GetRootGameObjects())
                Walk(root.transform, 0, sb);

            var overall = new Bounds(); bool init = false;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!init) { overall = r.bounds; init = true; } else overall.Encapsulate(r.bounds);
            }
            sb.AppendLine($"OVERALL BOUNDS center={overall.center:F1} size={overall.size:F1}");

            var path = Path2("Temp/scene_report.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[RescueSim] Scene report written to {path}");
        }

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            var b = Combined(t);
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            string prefab = src != null ? AssetDatabase.GetAssetPath(src) : "";
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name} pos={t.position:F1} rot={t.eulerAngles:F0} size={(b.HasValue ? b.Value.size.ToString("F1") : "-")} children={t.childCount} {(prefab != "" ? "[" + System.IO.Path.GetFileName(prefab) + "]" : "")}");
            if (depth >= 3) return;
            foreach (Transform c in t) Walk(c, depth + 1, sb);
        }

        static Bounds? Combined(Transform t)
        {
            Bounds? b = null;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds; else { var v = b.Value; v.Encapsulate(r.bounds); b = v; }
            }
            return b;
        }

        static string Path(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        static string Path2(string rel) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", rel));
    }
}
