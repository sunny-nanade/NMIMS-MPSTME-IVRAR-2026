using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Small procedural meshes for props that primitives cannot show well (a cup with a handle, a bendy cable).</summary>
    public static class MeshFactory
    {
        /// <summary>Lathe: profile points (radius, height) are revolved around Y. Normals are smooth.</summary>
        public static Mesh Lathe(string name, IList<Vector2> profile, int segments)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int rows = profile.Count;
            for (int r = 0; r < rows; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    verts.Add(new Vector3(Mathf.Cos(a) * profile[r].x, profile[r].y, Mathf.Sin(a) * profile[r].x));
                    uvs.Add(new Vector2((float)s / segments, (float)r / (rows - 1)));
                }
            int stride = segments + 1;
            for (int r = 0; r < rows - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i = r * stride + s;
                    tris.Add(i); tris.Add(i + stride); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + stride); tris.Add(i + stride + 1);
                }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A mug: outer wall, rim, inner wall, floor, plus a D-shaped handle on the +X side. Origin at the centre of the height.</summary>
        public static Mesh Cup(float height = 0.095f, float radius = 0.042f, float wall = 0.004f)
        {
            float h = height * 0.5f;
            var profile = new List<Vector2>
            {
                new Vector2(0.001f, -h), new Vector2(radius * 0.85f, -h), new Vector2(radius * 0.92f, -h + 0.006f),
                new Vector2(radius, -h + 0.02f), new Vector2(radius * 1.04f, h - 0.004f), new Vector2(radius * 1.04f, h),
                new Vector2(radius * 1.04f - wall, h), new Vector2(radius - wall, h - 0.004f), new Vector2(radius - wall, -h + 0.012f),
                new Vector2(0.001f, -h + 0.010f)
            };
            var body = Lathe("CupBody", profile, 28);
            // handle: a half ring in the XY plane, swept as a tube
            var path = new List<Vector3>();
            float hr = height * 0.28f; Vector3 c = new Vector3(radius * 1.04f, 0f, 0f);
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / 10f);
                path.Add(c + new Vector3(Mathf.Cos(a) * hr * 0.9f, Mathf.Sin(a) * hr, 0f));
            }
            var handle = Tube("CupHandle", path, 0.0065f, 8, true);
            return Combine("Cup", body, handle);
        }

        /// <summary>A tube along a polyline (ring cross-sections, optional flat end caps).</summary>
        public static Mesh Tube(string name, IList<Vector3> path, float radius, int sides, bool caps)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int n = path.Count;
            Vector3 prevN = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (i < n - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).normalized;
                if (i > 0 && i < n - 1) t = (path[i + 1] - path[i - 1]).normalized;
                Vector3 nrm = i == 0 ? Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                     : (prevN - Vector3.Dot(prevN, t) * t).normalized; // parallel transport keeps the tube from twisting
                prevN = nrm;
                Vector3 bin = Vector3.Cross(t, nrm);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    verts.Add(path[i] + (nrm * Mathf.Cos(a) + bin * Mathf.Sin(a)) * radius);
                    uvs.Add(new Vector2((float)s / sides, (float)i / (n - 1)));
                }
            }
            int stride = sides + 1;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int k = i * stride + s;
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + stride);
                    tris.Add(k + 1); tris.Add(k + stride + 1); tris.Add(k + stride);
                }
            if (caps)
                for (int end = 0; end < 2; end++)
                {
                    int ring = end == 0 ? 0 : (n - 1) * stride;
                    int centre = verts.Count;
                    verts.Add(path[end == 0 ? 0 : n - 1]); uvs.Add(Vector2.zero);
                    for (int s = 0; s < sides; s++)
                    {
                        if (end == 0) { tris.Add(centre); tris.Add(ring + s + 1); tris.Add(ring + s); }
                        else { tris.Add(centre); tris.Add(ring + s); tris.Add(ring + s + 1); }
                    }
                }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static Mesh Combine(string name, params Mesh[] meshes)
        {
            var ci = new CombineInstance[meshes.Length];
            for (int i = 0; i < meshes.Length; i++) { ci[i].mesh = meshes[i]; ci[i].transform = Matrix4x4.identity; }
            var m = new Mesh { name = name };
            m.CombineMeshes(ci, true, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>URP Lit material with a colour, smoothness and optional emission (falls back to Unlit when Lit is missing).</summary>
        public static Material Lit(Color color, float smoothness = 0.25f, float metallic = 0f, Color? emission = null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { color = color };
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f); // direct-light glints on small bevels sparkle in VR
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (emission.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return m;
        }
    }
}
