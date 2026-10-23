using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// DeepSleep > Setup > Polish look. Replaces the 300 MB texture skybox with the code-written StarSky shader, calms the specular noise of the pack
    /// materials (sparkle under moving point lights), lifts the ambient so rooms read, and removes empty leftover roots. Safe to run twice.
    /// </summary>
    public static class LookPolish
    {
        public const string SkyMatPath = "Assets/_Project/Materials/M_StarSky.mat";
        const string PackMats = "Assets/Modular_SciFi_Pack (146 Objects, LowPoly)/Materials";

        [MenuItem("DeepSleep/Setup/Polish look")]
        public static string Run()
        {
            var log = new System.Text.StringBuilder();
            var sky = EnsureSky();
            var dark = EnsureDarkCube();
            foreach (var n in new[] { "Cleen", "CleenTransparent", "Metal", "MetallTransparent" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{PackMats}/{n}.mat");
                if (m == null) continue;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0.3f);
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 0.45f);
                if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
                if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
                m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                EditorUtility.SetDirty(m);
                log.AppendLine("material calmed: " + n);
            }
            AssetDatabase.SaveAssets();

            EditorSceneManager.SaveOpenScenes();
            foreach (var (name, ambient, useSky) in new[]
            {
                ("SCN_Setup", new Color(0.34f, 0.40f, 0.52f), true),
                ("SCN_MedicalBay", new Color(0.33f, 0.35f, 0.44f), true),
                ("SCN_HallwayDebrief", new Color(0.36f, 0.40f, 0.50f), true),
            })
            {
                var sc = EditorSceneManager.OpenScene($"Assets/_Project/Scenes/{name}.unity", OpenSceneMode.Single);
                if (useSky) RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = dark;
                RenderSettings.reflectionIntensity = 1f;
                foreach (var r in sc.GetRootGameObjects())
                    if (r.name.EndsWith("_MISSING") && r.transform.childCount == 0 && r.GetComponents<Component>().Length == 1) { Object.DestroyImmediate(r); log.AppendLine("removed empty root in " + name); }
                AddCeilingPanels(sc, log);
                EditorSceneManager.MarkSceneDirty(sc);
                EditorSceneManager.SaveScene(sc);
                log.AppendLine("scene polished: " + name);
            }
            return log.ToString();
        }

        /// <summary>Emissive ceiling light panels (primitive cubes, listed in KNOWN_ISSUES) so the rooms have a believable light source and read in the headset.</summary>
        static void AddCeilingPanels(Scene sc, System.Text.StringBuilder log)
        {
            foreach (var r in sc.GetRootGameObjects()) if (r.name == "CeilingPanels") Object.DestroyImmediate(r);
            var b = new Bounds(); bool any = false;
            foreach (var r in sc.GetRootGameObjects())
            {
                if (r.name.StartsWith("PF_PlayerRig") || r.name.Contains("Spawn")) continue;
                foreach (var mr in r.GetComponentsInChildren<MeshRenderer>())
                    if (mr.bounds.size.x < 30f && mr.bounds.size.z < 30f) { if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds); }
            }
            if (!any) return;
            Physics.SyncTransforms();
            float ceilY = 2.93f;
            if (Physics.Raycast(new Vector3(b.center.x, 1.8f, b.center.z), Vector3.up, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) ceilY = hit.point.y - 0.03f;
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_CeilingPanel.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f));
                mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(0.55f, 0.8f, 1f) * 1.6f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                AssetDatabase.CreateAsset(mat, "Assets/_Project/Materials/M_CeilingPanel.mat");
            }
            var root = new GameObject("CeilingPanels").transform;
            bool alongX = b.size.x >= b.size.z;
            float len = alongX ? b.size.x : b.size.z;
            int n = Mathf.Max(1, Mathf.FloorToInt((len - 1f) / 2.6f));
            int rows = (alongX ? b.size.z : b.size.x) > 4.5f ? 2 : 1;
            for (int i = 0; i < n; i++)
                for (int rw = 0; rw < rows; rw++)
                {
                    float a = -len / 2f + len * (i + 0.5f) / n;
                    float s = rows == 1 ? 0f : (rw == 0 ? -1f : 1f) * ((alongX ? b.size.z : b.size.x) * 0.22f);
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    c.name = "CeilingPanel";
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.transform.SetParent(root, false);
                    Vector3 pos = alongX ? new Vector3(b.center.x + a, ceilY, b.center.z + s) : new Vector3(b.center.x + s, ceilY, b.center.z + a);
                    c.transform.position = pos;
                    c.transform.localScale = alongX ? new Vector3(1.4f, 0.04f, 0.16f) : new Vector3(0.16f, 0.04f, 1.4f);
                    var mr2 = c.GetComponent<MeshRenderer>(); mr2.sharedMaterial = mat; mr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    GameObjectUtility.SetStaticEditorFlags(c, StaticEditorFlags.BatchingStatic);
                }
            log.AppendLine($"ceiling panels in {sc.name}: {n * rows} at y {ceilY:0.00}");
        }

        /// <summary>Black reflection cubemap: the star sky must not be mirrored as white speckle on every glossy surface.</summary>
        static Cubemap EnsureDarkCube()
        {
            const string path = "Assets/_Project/Materials/CUBE_DarkReflection.asset";
            var c = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (c != null) return c;
            c = new Cubemap(16, TextureFormat.RGBA32, false);
            var px = new Color[16 * 16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.02f, 0.02f, 0.03f, 1f);
            for (int f = 0; f < 6; f++) c.SetPixels(px, (CubemapFace)f);
            c.Apply();
            AssetDatabase.CreateAsset(c, path);
            return c;
        }

        static Material EnsureSky()
        {
            var sh = Shader.Find("Fractured_Final/StarSky");
            var m = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, SkyMatPath); }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
