using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// DeepSleep > Run Check > Audit: materials and shaders. Lists every renderer slot in our four scenes and every prefab under Assets/_Project
    /// whose material is missing or whose shader is not usable in URP 17.5 (error shader, built-in Standard / Legacy / Unlit, not supported).
    /// These render solid magenta. Writes Docs/Reports/audit_materials.md and returns the number of problems.
    /// </summary>
    public static class MaterialAudit
    {
        static readonly string[] Scenes = { "SCN_Setup", "SCN_MedicalBay", "SCN_SmokeRoom", "SCN_HallwayDebrief" };

        public static bool Bad(Material m, out string why)
        {
            why = null;
            if (m == null) { why = "missing material"; return true; }
            var sh = m.shader;
            if (sh == null) { why = "missing shader"; return true; }
            string n = sh.name;
            if (n == "Hidden/InternalErrorShader" || n.StartsWith("Hidden/InternalError")) { why = "error shader"; return true; }
            if (!sh.isSupported) { why = "shader not supported on this platform: " + n; return true; }
            bool urp = n.StartsWith("Universal Render Pipeline/") || n.StartsWith("Shader Graphs/") || n.StartsWith("Sprites/") || n.StartsWith("UI/") || n.StartsWith("TextMeshPro/") || n.StartsWith("Hidden/") || n.StartsWith("Fractured") || n == "HoverOutline" || n.StartsWith("DeepSleep");
            if (!urp && (n == "Standard" || n.StartsWith("Standard (") || n.StartsWith("Legacy Shaders/") || n.StartsWith("Mobile/") || n.StartsWith("Nature/"))) { why = "built-in shader not rendered by URP: " + n; return true; }
            return false;
        }

        public const string ControllerMaterialPath = "Assets/_Project/Materials/MAT_Controller.mat";

        /// <summary>Plain URP/Lit graphite material for every renderer slot whose material is missing (the controller placeholder meshes of the Meta rig lost theirs).</summary>
        [MenuItem("DeepSleep/Build/Fix Missing Materials")]
        public static void FixMissing()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ControllerMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_Controller" };
                mat.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.2f));
                mat.SetFloat("_Metallic", 0.25f); mat.SetFloat("_Smoothness", 0.55f);
                AssetDatabase.CreateAsset(mat, ControllerMaterialPath);
            }
            int fixedSlots = 0;
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var root = PrefabUtility.LoadPrefabContents(p);
                bool dirty = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    var mats = r.sharedMaterials; bool ch = false;
                    for (int i = 0; i < mats.Length; i++) if (Bad(mats[i], out _)) { mats[i] = mat; ch = true; fixedSlots++; }
                    if (ch) { r.sharedMaterials = mats; dirty = true; }
                }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, p);
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] Fix Missing Materials: " + fixedSlots + " slots in prefabs now use MAT_Controller");
        }

        [MenuItem("DeepSleep/Run Check/Audit: materials and shaders")]
        public static void Run() { int n = Audit(true); Debug.Log("[DeepSleep] Material audit: " + n + " problems"); }

        public static int Audit(bool openScenes, string reportPath = "Assets/_Project/Docs/Reports/audit_materials.md")
        {
            var sb = new StringBuilder();
            int problems = 0, slots = 0;
            sb.AppendLine("# Audit: materials and shaders\n");
            sb.AppendLine("Date: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            var shaderCount = new SortedDictionary<string, int>();
            if (openScenes)
            {
                EditorSceneManager.SaveOpenScenes();
                string back = SceneManager.GetActiveScene().path;
                foreach (var sn in Scenes)
                {
                    string path = ProjectSetupTools.ScenePath(sn);
                    var sc = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var root in sc.GetRootGameObjects()) Scan(root, "scene " + sn, sb, shaderCount, ref problems, ref slots);
                }
                if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            }
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) Scan(go, "prefab " + p, sb, shaderCount, ref problems, ref slots);
            }
            sb.AppendLine("\n## Shaders in use (renderer slots)");
            foreach (var kv in shaderCount) sb.AppendLine("- " + kv.Key + ": " + kv.Value);
            sb.Insert(sb.ToString().IndexOf("Date:"), "Result: " + problems + " problem slots out of " + slots + " scanned.\n\n");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, sb.ToString());
            return problems;
        }

        static void Scan(GameObject root, string where, StringBuilder sb, SortedDictionary<string, int> shaderCount, ref int problems, ref int slots)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer psr && psr.renderMode == ParticleSystemRenderMode.None) continue;
                var mats = r.sharedMaterials;
                if (mats.Length == 0) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    slots++;
                    string sn = mats[i] != null && mats[i].shader != null ? mats[i].shader.name : "(none)";
                    shaderCount[sn] = shaderCount.TryGetValue(sn, out int c) ? c + 1 : 1;
                    if (Bad(mats[i], out string why))
                    {
                        problems++;
                        sb.AppendLine($"- [{where}] {HierarchyPath(r.transform)} slot {i}: {why}");
                    }
                }
            }
        }

        static string HierarchyPath(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
