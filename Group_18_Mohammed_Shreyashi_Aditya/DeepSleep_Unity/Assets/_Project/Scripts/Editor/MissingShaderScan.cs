using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// Finds pink (missing or unsupported) shaders: every material asset, every renderer of the open scene and of the Resources / Prefabs prefabs,
    /// and the shaders that runtime code looks up with Shader.Find (they must be in Always Included Shaders or a build shows them magenta).
    /// </summary>
    public static class MissingShaderScan
    {
        static readonly string[] RuntimeShaders =
        {
            "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Particles/Unlit", "Fractured_Final/HologramPanel"
        };

        [MenuItem("DeepSleep/Tools/Scan Missing Shaders")]
        public static void Menu() { Debug.Log("[DeepSleep] " + Run(false)); }

        static bool Bad(Material m, out string why)
        {
            why = null;
            if (m == null) { why = "null material"; return true; }
            if (m.shader == null) { why = "no shader"; return true; }
            string n = m.shader.name;
            if (n == "Hidden/InternalErrorShader" || n.Contains("InternalError")) { why = "error shader"; return true; }
            if (!m.shader.isSupported) { why = "unsupported: " + n; return true; }
            if (n == "Standard" || n.StartsWith("Legacy Shaders/") || n.StartsWith("Mobile/") || n.StartsWith("Nature/")) { why = "built-in pipeline shader " + n; return true; }
            return false;
        }

        /// <summary>Returns a one-line summary and writes Docs/Reports/missing_shaders.md. fix = add the runtime shaders to Always Included Shaders.</summary>
        public static string Run(bool fix)
        {
            var sb = new StringBuilder("# Missing shader scan\n\n");
            int bad = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                total++;
                if (Bad(m, out var why)) { bad++; sb.AppendLine($"- MATERIAL {path}: {why}"); }
            }
            int rendererBad = 0, rendererTotal = 0;
            void CheckRenderers(GameObject root, string label)
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer && r.sharedMaterials.Length == 0) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        rendererTotal++;
                        if (r is SkinnedMeshRenderer && mats[i] == null) continue; // SDK controller meshes get their material at run time (documented in KNOWN_ISSUES)
                        if (Bad(mats[i], out var why)) { rendererBad++; sb.AppendLine($"- RENDERER {label}/{PathOf(r.transform)} slot {i}: {why}"); }
                    }
                }
            }
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) CheckRenderers(root, "scene");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/_Project/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) CheckRenderers(go, path);
            }

            int notIncluded = 0;
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var always = gs.FindProperty("m_AlwaysIncludedShaders");
            var included = new HashSet<Shader>();
            for (int i = 0; i < always.arraySize; i++) { var s = always.GetArrayElementAtIndex(i).objectReferenceValue as Shader; if (s != null) included.Add(s); }
            foreach (var name in RuntimeShaders)
            {
                var sh = Shader.Find(name);
                if (sh == null) { sb.AppendLine($"- RUNTIME shader '{name}' not found in the project"); notIncluded++; continue; }
                if (included.Contains(sh)) continue;
                if (fix)
                {
                    always.arraySize++;
                    always.GetArrayElementAtIndex(always.arraySize - 1).objectReferenceValue = sh;
                    included.Add(sh);
                    sb.AppendLine($"- FIXED: '{name}' added to Always Included Shaders");
                }
                else { sb.AppendLine($"- RUNTIME shader '{name}' is looked up with Shader.Find but is not in Always Included Shaders"); notIncluded++; }
            }
            if (fix) { gs.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }

            string summary = $"{total} materials, {rendererTotal} renderer slots scanned: {bad} bad materials, {rendererBad} bad slots, {notIncluded} runtime shaders not guaranteed in a build";
            sb.Insert(0, summary + "\n\n");
            File.WriteAllText("Assets/_Project/Docs/Reports/missing_shaders.md", sb.ToString());
            return summary;
        }

        static string PathOf(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
    }
}
