using System.Collections.Generic;
using System.IO;
using RescueSim.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RescueSim.EditorTools
{
    /// <summary>Shared helpers for the scene builder: generated materials, particle systems and serialized-field wiring.</summary>
    public static class BuilderUtil
    {
        public const string Root = "Assets/_Project";
        public const string MaterialsDir = Root + "/Materials";
        public const string PrefabsDir = Root + "/Prefabs";
        public const string ScenesDir = Root + "/Scenes";
        public const string DataDir = Root + "/Data";

        public static void EnsureDir(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            // The folder can exist on disk before the AssetDatabase knows about it; creating it again would
            // produce "Name 1", "Name 2", ... duplicates, so import the existing folder instead.
            if (Directory.Exists(assetPath)) { AssetDatabase.ImportAsset(assetPath); if (AssetDatabase.IsValidFolder(assetPath)) return; }
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        // ---- Serialized wiring ----------------------------------------------------------------

        public static void Ref(Object target, string field, Object value)
        {
            if (target == null) { Debug.LogError($"[SceneBuilder] Ref({field}) on null target"); return; }
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[SceneBuilder] Field '{field}' not found on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Refs<T>(Object target, string field, IList<T> values) where T : Object
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[SceneBuilder] Field '{field}' not found on {target.GetType().Name}"); return; }
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Val(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[SceneBuilder] Field '{field}' not found on {target.GetType().Name}"); return; }
            switch (value)
            {
                case float f: p.floatValue = f; break;
                case int i: if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = i; else p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case string s: p.stringValue = s; break;
                case Vector3 v: p.vector3Value = v; break;
                case Color c: p.colorValue = c; break;
                default: Debug.LogError("[SceneBuilder] Unsupported value type " + value?.GetType()); break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Materials ------------------------------------------------------------------------

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static void ResetCache() => Cache.Clear();

        static Material Save(Material m, string name)
        {
            EnsureDir(MaterialsDir);
            string path = $"{MaterialsDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(m, existing);
                Object.DestroyImmediate(m);
                m = existing;
            }
            else AssetDatabase.CreateAsset(m, path);
            Cache[name] = m;
            return m;
        }

        public static Material Lit(string name, Color color, float smoothness = 0.25f, float metallic = 0f, bool emissive = false)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emissive)
            {
                // Black emission + keyword so HoverHighlight can drive _EmissionColor through a property block.
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return Save(m, name);
        }

        public static Material Unlit(string name, Color color)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            m.SetColor("_BaseColor", color);
            return Save(m, name);
        }

        public static Texture2D SoftCircle()
        {
            string path = $"{MaterialsDir}/SoftCircle.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            EnsureDir(MaterialsDir);
            const int n = 64;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SoftCircle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        public static Material Particle(string name, Color tint, bool additive)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("RescueSim/SoftParticle")) { name = name };
            m.SetTexture("_MainTex", SoftCircle());
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.SrcAlpha : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)RenderQueue.Transparent;
            return Save(m, name);
        }

        /// <summary>Screen-space overlay material (ComfortVignette shader). aperture &lt; 0 gives a uniform tint.</summary>
        public static Material Overlay(string name, float aperture, int queue)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("RescueSim/ComfortVignette")) { name = name };
            m.SetFloat("_Aperture", aperture);
            m.SetColor("_BaseColor", new Color(0, 0, 0, 0));
            m.renderQueue = queue;
            return Save(m, name);
        }

        // ---- Particles ------------------------------------------------------------------------

        public struct PS
        {
            public Material material;
            public float lifetime, speed, size, rate, gravity, coneAngle, coneRadius;
            public int max;
            public Gradient color;
            public bool loop;
            public bool playOnAwake;
            public float startSizeMax;
        }

        public static Gradient Fade(Color c, float peakAlpha = 1f)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.15f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public static Gradient Flame()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.35f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.5f), new GradientColorKey(new Color(0.4f, 0.05f, 0f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public static ParticleSystem MakeParticles(Transform parent, string name, PS d, Vector3 localPos, Vector3 localEuler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = d.loop;
            main.playOnAwake = d.playOnAwake;
            main.duration = 2f;
            main.startLifetime = d.lifetime;
            main.startSpeed = d.speed;
            main.startSize = d.startSizeMax > d.size ? new ParticleSystem.MinMaxCurve(d.size, d.startSizeMax) : new ParticleSystem.MinMaxCurve(d.size);
            main.startColor = Color.white;
            main.gravityModifier = d.gravity;
            main.maxParticles = d.max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var em = ps.emission;
            em.rateOverTime = d.rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = d.coneAngle;
            shape.radius = d.coneRadius;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = d.color;

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.4f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = d.material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ---- Transparent materials and generated textures (new stages) -----------------------

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        public static Material LitTransparent(string name, Color color, float smoothness = 0.9f, Texture tex = null, Vector2? tiling = null)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            MakeTransparent(m);
            return Save(m, name);
        }

        public static Material UnlitTransparent(string name, Color color, Texture tex = null)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            m.SetColor("_BaseColor", color);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            MakeTransparent(m);
            return Save(m, name);
        }

        public static Material LitTextured(string name, Color tint, Texture tex, Vector2 tiling, float smoothness = 0.2f)
        {
            if (Cache.TryGetValue(name, out var c) && c != null) return c;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetFloat("_Smoothness", smoothness);
            return Save(m, name);
        }

        static Texture2D SaveTexture(string name, int w, int h, System.Func<int, int, Color> pixel, bool mips, TextureWrapMode wrap)
        {
            string path = $"{MaterialsDir}/{name}.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            EnsureDir(MaterialsDir);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, mips) { name = name, wrapMode = wrap };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = pixel(x, y);
            tex.SetPixels(px);
            tex.Apply(mips);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>White chevron arrow pointing +Y (up the texture) on transparent background.</summary>
        public static Texture2D ArrowTexture() => SaveTexture("Tex_Arrow", 128, 128, (x, y) =>
        {
            float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 128f;
            // Chevron: two slanted bands meeting at the top centre.
            float d = Mathf.Abs(u - 0.5f);
            float apex = 0.82f - d * 1.1f;            // upper edge of band
            bool inBand = v < apex && v > apex - 0.24f && d < 0.42f;
            float a = inBand ? 1f : 0f;
            return new Color(1f, 1f, 1f, a);
        }, true, TextureWrapMode.Clamp);

        /// <summary>Tileable soft ripple pattern (greyscale, alpha 1) used for animated water and concrete mottling.</summary>
        public static Texture2D RippleTexture() => SaveTexture("Tex_Ripple", 128, 128, (x, y) =>
        {
            float u = x / 128f * Mathf.PI * 2f, v = y / 128f * Mathf.PI * 2f;
            float a = Mathf.Sin(u * 3f + Mathf.Sin(v * 2f) * 1.4f) * 0.5f + 0.5f;
            float b = Mathf.Sin(v * 4f + Mathf.Sin(u * 3f) * 1.1f) * 0.5f + 0.5f;
            float g = 0.78f + 0.22f * (a * b);
            return new Color(g, g, g, 1f);
        }, true, TextureWrapMode.Repeat);

        /// <summary>Tileable mottled concrete (greyscale).</summary>
        public static Texture2D ConcreteTexture() => SaveTexture("Tex_Concrete", 128, 128, (x, y) =>
        {
            float u = x / 128f, v = y / 128f;
            float n = Mathf.PerlinNoise(u * 8f, v * 8f) * 0.5f + Mathf.PerlinNoise(u * 24f + 5f, v * 24f + 9f) * 0.3f;
            // Make it tile by blending towards wrapped samples.
            float n2 = Mathf.PerlinNoise((u - 1f) * 8f + 16f, v * 8f) * 0.5f;
            float g = 0.55f + 0.35f * Mathf.Lerp(n, n2 + 0.2f, u * u * 0.0f);
            return new Color(g, g, g, 1f);
        }, true, TextureWrapMode.Repeat);

        public static Mesh SaveMesh(Mesh mesh, string name)
        {
            mesh.name = name;
            string path = $"{Root}/Generated/{name}.asset";
            EnsureDir(Root + "/Generated");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Torus in the XZ plane (hole along Y). Used for the rescue buoy.</summary>
        public static Mesh TorusMesh(float majorRadius, float minorRadius, int major = 24, int minor = 10)
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i <= major; i++)
            {
                float a = i / (float)major * Mathf.PI * 2f;
                var centre = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * majorRadius;
                for (int j = 0; j <= minor; j++)
                {
                    float b = j / (float)minor * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b));
                    verts.Add(centre + n * minorRadius); norms.Add(n);
                }
            }
            for (int i = 0; i < major; i++)
                for (int j = 0; j < minor; j++)
                {
                    int p = i * (minor + 1) + j, q = (i + 1) * (minor + 1) + j;
                    tris.AddRange(new[] { p, q, p + 1, p + 1, q, q + 1 });
                }
            var m = new Mesh { name = "Torus" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- Misc -----------------------------------------------------------------------------

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) { var c = go.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }
            return go;
        }

        public static SpatialSound Sound(GameObject host, SpatialSound.Cue loop = SpatialSound.Cue.None, float volume = 0.7f, float min = 1.5f, float max = 25f)
        {
            if (host.GetComponent<AudioSource>() == null) host.AddComponent<AudioSource>();
            var s = host.AddComponent<SpatialSound>();
            Val(s, "loopCue", (int)loop);
            Val(s, "volume", volume);
            Val(s, "minDistance", min);
            Val(s, "maxDistance", max);
            return s;
        }

        public static GameObject SavePrefab(GameObject sceneObject, string name)
        {
            EnsureDir(PrefabsDir);
            string path = $"{PrefabsDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(sceneObject, path);
            Object.DestroyImmediate(sceneObject);
            return prefab;
        }

        public static T Add<T>(GameObject go) where T : Component => go.AddComponent<T>();
    }
}
