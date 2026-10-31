using System.Collections.Generic;
using System.Linq;
using RescueSim.Survivors;
using UnityEditor;
using UnityEngine;
using static RescueSim.EditorTools.BuilderUtil;

namespace RescueSim.EditorTools
{
    /// <summary>
    /// Builds the visible human bodies for casualties from Microsoft Rocketbox avatars (MIT licence, see
    /// Assets/_Project/Art/Characters/Rocketbox/LICENSE-Rocketbox-MIT.txt). The bodies are purely visual: colliders, rigidbodies and
    /// animators are removed so gameplay colliders stay in charge. One prefab per avatar is generated into Prefabs/Characters.
    /// </summary>
    public static class CasualtyModels
    {
        public const string RocketboxDir = Root + "/Art/Characters/Rocketbox";
        public const string HumanPrefabDir = PrefabsDir + "/Characters";

        /// <summary>Avatars used (folder name == fbx name == texture prefix source).</summary>
        public static readonly string[] Avatars =
        {
            "Male_Adult_01", "Female_Adult_01", "Male_Adult_05", "Female_Adult_04",
            "Male_Adult_10", "Female_Adult_08", "Construction_Male_01", "Construction_Female_01",
        };

        /// <summary>Named survivors in the rescue stages and the avatar each one wears (workers in the factory, residents elsewhere).</summary>
        static readonly Dictionary<string, string> BySurvivor = new Dictionary<string, string>
        {
            { "Dana", "Construction_Female_01" }, { "Marcus", "Construction_Male_01" }, { "Priya", "Female_Adult_08" },
            { "Amira", "Female_Adult_01" }, { "Ben", "Male_Adult_05" },
            { "Lena", "Female_Adult_04" }, { "Omar", "Male_Adult_10" },
        };

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>($"{RocketboxDir}/{Avatars[0]}/{Avatars[0]}.fbx") != null;

        public static string AvatarFor(string casualtyName, int index)
        {
            if (casualtyName != null && BySurvivor.TryGetValue(casualtyName, out var a)) return a;
            return Avatars[Mathf.Abs(index) % Avatars.Length];
        }

        // ---- Prefab generation -------------------------------------------------------------------------------------------

        public static GameObject HumanPrefab(string avatar)
        {
            string path = $"{HumanPrefabDir}/Human_{avatar}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            return BuildHumanPrefab(avatar, path);
        }

        static void ConfigureImporter(string fbxPath)
        {
            var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (mi == null) return;
            bool dirty = false;
            if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
            if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }   // keeps the skinned meshes; Animator removed in the prefab
            if (mi.isReadable) { mi.isReadable = false; dirty = true; }
            if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (dirty) mi.SaveAndReimport();
        }

        static void ConfigureTexture(string path, bool alpha)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool dirty = false;
            if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; dirty = true; }
            if (ti.alphaIsTransparency != alpha) { ti.alphaIsTransparency = alpha; dirty = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
            var android = ti.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.format != TextureImporterFormat.ASTC_6x6 || android.maxTextureSize != 1024)
            {
                android.overridden = true; android.format = alpha ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_6x6; android.maxTextureSize = 1024;
                ti.SetPlatformTextureSettings(android); dirty = true;
            }
            if (dirty) ti.SaveAndReimport();
        }

        static Texture2D Tex(string avatar, string key, out string found)
        {
            found = null;
            foreach (var f in AssetDatabase.FindAssets("t:Texture2D", new[] { $"{RocketboxDir}/{avatar}" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(f);
                if (System.IO.Path.GetFileNameWithoutExtension(p).Contains(key)) { found = p; return AssetDatabase.LoadAssetAtPath<Texture2D>(p); }
            }
            return null;
        }

        static Material MakeMaterial(string avatar, string slot)
        {
            string key = slot.Contains("opacity") ? "opacity" : slot.Contains("helmet") ? "helmet" : slot.Contains("tools") ? "tools" : slot.Contains("head") ? "head" : "body";
            string matPath = $"{MaterialsDir}/Characters/Mat_{avatar}_{key}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) return existing;

            var tex = Tex(avatar, key + "_color", out string texPath);
            bool alpha = key == "opacity";
            if (texPath != null) ConfigureTexture(texPath, alpha);
            tex = texPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) : null;

            EnsureDir(MaterialsDir + "/Characters");
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Mat_{avatar}_{key}" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", key == "head" ? 0.18f : 0.08f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.SetFloat("_SpecularHighlights", 0f);
            if (alpha)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f); m.renderQueue = 2450;
            }
            m.EnableKeyword("_EMISSION");                         // HoverHighlight drives _EmissionColor through a property block
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        /// <summary>Picks the cheapest level of detail that is still a full body (see the LOD note in the report).</summary>
        static SkinnedMeshRenderer[] ChooseLod(GameObject inst, out string lod)
        {
            var all = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var key in new[] { "lowpoly_33", "midpoly_42", "midpoly_49", "hipoly_81" })
            {
                var pick = all.Where(r => r.name.Contains(key) || (r.transform.parent != null && r.transform.parent.name.Contains(key))).ToArray();
                if (pick.Length > 0) { lod = key; return pick; }
            }
            lod = "all"; return all;
        }

        static int Tris(SkinnedMeshRenderer r) => r.sharedMesh == null ? 0 : (int)(r.sharedMesh.triangles.Length / 3);

        static GameObject BuildHumanPrefab(string avatar, string path)
        {
            string fbx = $"{RocketboxDir}/{avatar}/{avatar}.fbx";
            ConfigureImporter(fbx);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (src == null) { Debug.LogError("[CasualtyModels] Missing " + fbx); return null; }

            var root = new GameObject("Human_" + avatar);
            var inst = (GameObject)Object.Instantiate(src, root.transform);
            inst.name = avatar;
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;

            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam);

            var keep = ChooseLod(inst, out string lod);
            var keepSet = new HashSet<SkinnedMeshRenderer>(keep);
            int tris = 0;
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!keepSet.Contains(r)) { Object.DestroyImmediate(r.gameObject); continue; }
                tris += Tris(r);
                var slots = r.sharedMaterials;
                var mats = new Material[Mathf.Max(1, slots.Length)];
                for (int i = 0; i < mats.Length; i++)
                {
                    string slotName = (i < slots.Length && slots[i] != null ? slots[i].name : r.name).ToLowerInvariant();
                    mats[i] = MakeMaterial(avatar, slotName);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;     // Quest 2: no per-character shadow pass
                r.updateWhenOffscreen = false;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            PoseArmsDown(inst.transform);

            // Fit: feet on the origin, natural height, centred.
            float target = avatar.StartsWith("Female") || avatar.Contains("_Female") ? 1.66f : 1.78f;
            var b = BoundsOf(inst);
            float k = b.size.y > 0.01f ? target / b.size.y : 1f;
            inst.transform.localScale = Vector3.one * k;
            b = BoundsOf(inst);
            inst.transform.position += root.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);

            EnsureDir(HumanPrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            string line = $"{avatar}: LOD '{lod}', {tris} triangles, height {target:0.00} m, meshes kept {keep.Length} -> {path}";
            Debug.Log("[CasualtyModels] " + line);
            System.IO.Directory.CreateDirectory("Builds");
            System.IO.File.AppendAllText("Builds/CasualtyModelsReport.txt", line + "\n");
            return prefab;
        }

        /// <summary>The avatars are modelled in a T-pose; lower the upper arms to the body's sides so the pose reads as a person at rest.</summary>
        static void PoseArmsDown(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                bool left = n == "Bip01 L UpperArm", rightArm = n == "Bip01 R UpperArm";
                if (!left && !rightArm) continue;
                Transform child = t.Find(left ? "Bip01 L Forearm" : "Bip01 R Forearm");
                if (child == null && t.childCount > 0) child = t.GetChild(0);
                if (child == null) continue;
                Vector3 cur = (child.position - t.position).normalized;
                float side = left ? -1f : 1f;                                    // model faces +Z; its right arm is on -X in Unity coordinates
                if (Mathf.Abs(cur.x) < 0.3f) side = Mathf.Sign(cur.x == 0 ? 1 : cur.x);
                else side = Mathf.Sign(cur.x);
                Vector3 want = new Vector3(side * 0.22f, -0.97f, 0.04f).normalized;
                t.rotation = Quaternion.FromToRotation(cur, want) * t.rotation;
            }
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ---- Using the prefabs -------------------------------------------------------------------------------------------

        /// <summary>Instantiates the avatar prefab under <paramref name="parent"/> (feet at the parent's origin, facing +Z).</summary>
        public static Transform AddHuman(Transform parent, string avatar)
        {
            var prefab = HumanPrefab(avatar);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = "Human";
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            return go.transform;
        }

        /// <summary>Gives a survivor-prefab instance its human body and wires the appearance component.</summary>
        public static void DressSurvivor(GameObject instance, string avatar)
        {
            var vis = instance.transform.Find("Visual");
            if (vis == null || !Available) return;
            var old = vis.Find("Human"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var human = AddHuman(vis, avatar);
            var app = instance.GetComponent<CasualtyAppearance>() ?? instance.AddComponent<CasualtyAppearance>();
            app.Configure(human, avatar);
            EditorUtility.SetDirty(app);
        }

        public static CasualtyAppearance AddAppearance(GameObject owner, Transform human, string avatar)
        {
            var app = owner.GetComponent<CasualtyAppearance>() ?? owner.AddComponent<CasualtyAppearance>();
            app.Configure(human, avatar);
            EditorUtility.SetDirty(app);
            return app;
        }

        [MenuItem("Rescue Sim/Characters/Rebuild human prefabs")]
        public static void RebuildAll()
        {
            foreach (var a in Avatars)
            {
                string path = $"{HumanPrefabDir}/Human_{a}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
                BuildHumanPrefab(a, path);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
