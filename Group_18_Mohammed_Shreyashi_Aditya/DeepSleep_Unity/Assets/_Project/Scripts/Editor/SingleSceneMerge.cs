using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// One-off conversion: merges SCN_Setup, SCN_MedicalBay, SCN_SmokeRoom and SCN_HallwayDebrief into SCN_Main (sections SEC_*, smoke room as the
    /// Resources prefab PF_SmokeSection) and swaps every heavy imported model mesh and textured model material for a box primitive and a flat colour.
    /// </summary>
    public static class SingleSceneMerge
    {
        const string Dir = "Assets/_Project/Scenes/";
        const string PrimMeshDir = "Assets/_Project/Meshes/Prim";
        const string PrimMatDir = "Assets/_Project/Materials/Prim";
        static readonly Dictionary<Object, Mesh> BoxCache = new Dictionary<Object, Mesh>();
        static readonly Dictionary<Object, Material> MatCache = new Dictionary<Object, Material>();
        static int _meshes, _mats, _colliders, _skinned;

        [MenuItem("DeepSleep/Merge Into One Scene")]
        public static void Run()
        {
            Directory.CreateDirectory(PrimMeshDir); Directory.CreateDirectory(PrimMatDir);
            AssetDatabase.Refresh();

            // 1. prefabs
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("PF_PlayerRig")) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                Lighten(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            // 2. merge the scenes
            var main = EditorSceneManager.OpenScene(Dir + "SCN_Setup.unity", OpenSceneMode.Single);
            EditorSceneManager.SaveScene(main, Dir + "SCN_Main.unity");
            main = SceneManager.GetActiveScene();
            var secSetup = new GameObject("SEC_Setup");
            var secBay = new GameObject("SEC_MedicalBay");
            var secHall = new GameObject("SEC_Hallway");

            Rehome(main, main, secSetup, true);

            var bay = EditorSceneManager.OpenScene(Dir + "SCN_MedicalBay.unity", OpenSceneMode.Additive);
            SceneManager.SetActiveScene(bay);
            var env = new Env(); // lighting of the Medical Bay becomes the lighting of the one scene
            Rehome(bay, main, secBay, false);

            var hall = EditorSceneManager.OpenScene(Dir + "SCN_HallwayDebrief.unity", OpenSceneMode.Additive);
            Rehome(hall, main, secHall, false);

            var smoke = EditorSceneManager.OpenScene(Dir + "SCN_SmokeRoom.unity", OpenSceneMode.Additive);
            var secSmoke = new GameObject("SEC_SmokeRoom");
            SceneManager.MoveGameObjectToScene(secSmoke, smoke);
            Rehome(smoke, smoke, secSmoke, false);
            Lighten(secSmoke);
            string prefabPath = "Assets/_Project/Resources/PF_SmokeSection.prefab";
            PrefabUtility.SaveAsPrefabAsset(secSmoke, prefabPath);
            Object.DestroyImmediate(secSmoke);

            SceneManager.SetActiveScene(main);
            env.Apply();
            EditorSceneManager.CloseScene(bay, true); EditorSceneManager.CloseScene(hall, true); EditorSceneManager.CloseScene(smoke, true);

            // 3. lighter scene
            Lighten(secSetup); Lighten(secBay); Lighten(secHall);
            secBay.SetActive(false); secHall.SetActive(false); secSetup.SetActive(true);

            EditorSceneManager.MarkSceneDirty(main);
            EditorSceneManager.SaveScene(main);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Dir + "SCN_Main.unity", true) };
            foreach (var n in new[] { "SCN_Setup", "SCN_MedicalBay", "SCN_SmokeRoom", "SCN_HallwayDebrief" })
                AssetDatabase.DeleteAsset(Dir + n + ".unity");
            AssetDatabase.SaveAssets();
            Debug.Log($"[DeepSleep] Merge done: {_meshes} mesh swaps, {_mats} flat materials, {_colliders} collider swaps, {_skinned} skinned renderers left alone");
        }

        // ------------------------------------------------------------------ merging
        static void Rehome(Scene from, Scene to, GameObject section, bool isMain)
        {
            foreach (var r in from.GetRootGameObjects())
            {
                if (r == section || r.name.StartsWith("SEC_")) continue;
                if (r.name.StartsWith("[BuildingBlock]") && isMain) continue; // shared, stays at the scene root
                if (r.name == "PF_PlayerRig")
                {
                    if (isMain) continue; // the one rig of the scene
                    Object.DestroyImmediate(r);
                    continue;
                }
                if (r.name == "PlayerSpawn" && isMain) r.name = "SetupSpawn";
                if (from != to) SceneManager.MoveGameObjectToScene(r, to);
                r.transform.SetParent(section.transform, true);
            }
        }

        class Env
        {
            AmbientMode mode; Color amb, sky, eq, gr; float ambI, refI; Material skybox; DefaultReflectionMode refMode; Cubemap cube; bool fog;
            public Env()
            {
                mode = RenderSettings.ambientMode; amb = RenderSettings.ambientLight; sky = RenderSettings.ambientSkyColor; eq = RenderSettings.ambientEquatorColor;
                gr = RenderSettings.ambientGroundColor; ambI = RenderSettings.ambientIntensity; refI = RenderSettings.reflectionIntensity; skybox = RenderSettings.skybox;
                refMode = RenderSettings.defaultReflectionMode; cube = RenderSettings.customReflectionTexture as Cubemap; fog = RenderSettings.fog;
            }
            public void Apply()
            {
                RenderSettings.ambientMode = mode; RenderSettings.ambientLight = amb; RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = eq;
                RenderSettings.ambientGroundColor = gr; RenderSettings.ambientIntensity = ambI; RenderSettings.reflectionIntensity = refI; RenderSettings.skybox = skybox;
                RenderSettings.defaultReflectionMode = refMode; if (cube != null) RenderSettings.customReflectionTexture = cube; RenderSettings.fog = fog;
            }
        }

        // ------------------------------------------------------------------ lightening
        static bool IsModelAsset(Object o)
        {
            if (o == null) return false;
            string p = AssetDatabase.GetAssetPath(o);
            return p.StartsWith("Assets/_Project/Models");
        }

        public static void Lighten(GameObject root)
        {
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (IsModelAsset(mf.sharedMesh)) { mf.sharedMesh = Box(mf.sharedMesh); _meshes++; }
            }
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (IsModelAsset(mc.sharedMesh)) { mc.sharedMesh = Box(mc.sharedMesh); _colliders++; }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer) { _skinned++; continue; }
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!IsModelAsset(mats[i]) && !UsesModelTexture(mats[i])) continue;
                    mats[i] = Flat(mats[i]); changed = true;
                }
                if (changed) r.sharedMaterials = mats;
                if (r is MeshRenderer) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static bool UsesModelTexture(Material m)
        {
            var sh = m.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
                if (sh.GetPropertyType(i) == ShaderPropertyType.Texture && IsModelAsset(m.GetTexture(sh.GetPropertyName(i)))) return true;
            return false;
        }

        static Mesh Box(Mesh src)
        {
            Object id = src;
            if (BoxCache.TryGetValue(id, out var m) && m != null) return m;
            var b = src.bounds;
            string safe = src.name.Replace(' ', '_').Replace('/', '_').Replace('.', '_');
            string path = $"{PrimMeshDir}/PB_{safe}_{Mathf.Abs((AssetDatabase.GetAssetPath(src) + src.name).GetHashCode()) % 100000}.asset";
            m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                m = BuildBox(b.center, b.size); m.name = "PB_" + safe;
                AssetDatabase.CreateAsset(m, path);
            }
            BoxCache[id] = m;
            return m;
        }

        static Mesh BuildBox(Vector3 c, Vector3 size)
        {
            Vector3 e = size * 0.5f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int a = 0; a < 3; a++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 nrm = axes[a] * sgn, u = axes[(a + 1) % 3], w = axes[(a + 2) % 3];
                    Vector3 o = c + Vector3.Scale(nrm, e);
                    int s = v.Count;
                    Vector3 du = Vector3.Scale(u, e), dw = Vector3.Scale(w, e);
                    v.Add(o - du - dw); v.Add(o + du - dw); v.Add(o + du + dw); v.Add(o - du + dw);
                    for (int k = 0; k < 4; k++) n.Add(nrm);
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                    bool flip = Vector3.Dot(Vector3.Cross(u, w), nrm) < 0;
                    if (!flip) { t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 }); }
                    else { t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 }); }
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Material Flat(Material src)
        {
            Object id = src;
            if (MatCache.TryGetValue(id, out var m) && m != null) return m;
            Color col = Color.white;
            foreach (var n in new[] { "_BaseColor", "baseColorFactor", "_Color" })
                if (src.HasProperty(n)) { col = src.GetColor(n); break; }
            foreach (var n in new[] { "_BaseMap", "baseColorTexture", "_MainTex" })
                if (src.HasProperty(n) && src.GetTexture(n) is Texture tex) { col *= Average(tex); break; }
            col.a = 1f;
            string safe = src.name.Replace(' ', '_').Replace('/', '_').Replace('.', '_');
            string path = $"{PrimMatDir}/PM_{safe}_{Mathf.Abs((AssetDatabase.GetAssetPath(src) + src.name).GetHashCode()) % 100000}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(sh) { name = "PM_" + safe };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", col); m.color = col;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(m);
            MatCache[id] = m; _mats++;
            return m;
        }

        static Color Average(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var t2 = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
            t2.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            Color c = Color.black; var px = t2.GetPixels(); foreach (var q in px) c += q; c /= px.Length; Object.DestroyImmediate(t2);
            return c;
        }
    }
}
