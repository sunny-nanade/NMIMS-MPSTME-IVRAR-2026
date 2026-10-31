using System.Collections.Generic;
using UnityEngine;

namespace Group10Gestures.Interaction
{
    /// <summary>
    /// Pure geometry helpers for the room-drawing system (no scene state).
    /// A room is a simple polygon on the XZ floor plane that is extruded upward into walls.
    /// Floor is triangulated with ear clipping, so concave (L-shaped) rooms work.
    /// Limitation: self-intersecting polygons are not supported.
    /// </summary>
    public static class RoomMeshBuilder
    {
        /// <summary>Signed polygon area in the XZ plane (shoelace formula). Sign gives winding.</summary>
        public static float SignedArea(IList<Vector3> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                Vector3 c = p[i];
                Vector3 n = p[(i + 1) % p.Count];
                a += c.x * n.z - n.x * c.z;
            }
            return 0.5f * a;
        }

        /// <summary>Ray-casting point-in-polygon test in the XZ plane.</summary>
        public static bool PointInPolygon(Vector3 pt, IList<Vector3> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector3 a = poly[i];
                Vector3 b = poly[j];
                if ((a.z > pt.z) != (b.z > pt.z) &&
                    pt.x < (b.x - a.x) * (pt.z - a.z) / (b.z - a.z) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Distance (XZ plane) from a point to the polygon; 0 if the point is inside.</summary>
        public static float DistanceToPolygon(Vector3 p, IList<Vector3> poly)
        {
            if (PointInPolygon(p, poly)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i];
                Vector3 b = poly[(i + 1) % poly.Count];
                var pa = new Vector2(p.x - a.x, p.z - a.z);
                var ba = new Vector2(b.x - a.x, b.z - a.z);
                float h = ba.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
                float d = (pa - ba * h).magnitude;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Ear-clipping triangulation. Returns triangle index triples into pts.</summary>
        public static List<int> Triangulate(IList<Vector3> pts)
        {
            var tris = new List<int>();
            if (pts.Count < 3) return tris;

            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++) idx.Add(i);
            bool ccw = SignedArea(pts) > 0f;

            int guard = 0;
            while (idx.Count > 3 && guard++ < 2000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];
                    Vector3 a = pts[i0], b = pts[i1], c = pts[i2];

                    float cz = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);

                    // Collinear vertex (e.g. an extra corner in the middle of a straight wall): drop it.
                    if (Mathf.Abs(cz) < 1e-6f)
                    {
                        idx.RemoveAt(i);
                        clipped = true;
                        break;
                    }

                    bool convex = ccw ? cz > 0f : cz < 0f;
                    if (!convex) continue;

                    bool blocked = false;
                    for (int j = 0; j < idx.Count; j++)
                    {
                        int k = idx[j];
                        if (k == i0 || k == i1 || k == i2) continue;
                        Vector3 p = pts[k];
                        if ((p - a).sqrMagnitude < 1e-8f || (p - b).sqrMagnitude < 1e-8f || (p - c).sqrMagnitude < 1e-8f) continue;
                        if (PointInTri(p, a, b, c)) { blocked = true; break; }
                    }
                    if (blocked) continue;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate input: fall back to a fan below
            }

            if (idx.Count >= 3)
            {
                // Either exactly one triangle remains, or we hit a degenerate case (fan fallback).
                for (int i = 1; i < idx.Count - 1; i++)
                {
                    tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]);
                }
            }
            return tris;
        }

        private static float Side(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            return (p1.x - p3.x) * (p2.z - p3.z) - (p2.x - p3.x) * (p1.z - p3.z);
        }

        private static bool PointInTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = Side(p, a, b);
            float d2 = Side(p, b, c);
            float d3 = Side(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        /// <summary>Flat floor mesh (normals up) at height y.</summary>
        public static Mesh BuildFloor(IList<Vector3> corners, float y)
        {
            var verts = new List<Vector3>();
            for (int i = 0; i < corners.Count; i++)
            {
                verts.Add(new Vector3(corners[i].x, y, corners[i].z));
            }

            List<int> tris = Triangulate(verts);
            var idx = new List<int>();
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Vector3 ab = verts[b] - verts[a];
                Vector3 ac = verts[c] - verts[a];
                float ny = ab.z * ac.x - ab.x * ac.z; // Cross(ab, ac).y
                if (ny < 0f) { int tmp = b; b = c; c = tmp; } // make the face point up
                idx.Add(a); idx.Add(b); idx.Add(c);
            }

            var mesh = new Mesh { name = "RoomFloor" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flat double-sided roof slab at height y (visible from inside and from above).</summary>
        public static Mesh BuildRoof(IList<Vector3> corners, float y)
        {
            Mesh top = BuildFloor(corners, y); // faces up
            var tv = new List<Vector3>();
            var ti = new List<int>();
            top.GetVertices(tv);
            top.GetTriangles(ti, 0);
            UnityEngine.Object.Destroy(top);

            int n = tv.Count;
            var verts = new List<Vector3>(tv);
            verts.AddRange(tv);

            var idx = new List<int>(ti);
            for (int i = 0; i + 2 < ti.Count; i += 3)
            {
                idx.Add(n + ti[i]); idx.Add(n + ti[i + 2]); idx.Add(n + ti[i + 1]); // reversed: faces down
            }

            var normals = new List<Vector3>();
            for (int i = 0; i < n; i++) normals.Add(Vector3.up);
            for (int i = 0; i < n; i++) normals.Add(Vector3.down);

            var mesh = new Mesh { name = "RoomRoof" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Double-sided walls: one quad per polygon edge, extruded upward by height.</summary>
        public static Mesh BuildWalls(IList<Vector3> corners, float height)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var idx = new List<int>();
            Vector3 up = Vector3.up * height;

            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % corners.Count];
                Vector3 e = b - a;
                if (e.sqrMagnitude < 1e-6f) continue;

                Vector3 nrm = Vector3.Cross(e, Vector3.up).normalized;
                int s = v.Count;

                // Front face
                v.Add(a); v.Add(b); v.Add(b + up); v.Add(a + up);
                n.Add(nrm); n.Add(nrm); n.Add(nrm); n.Add(nrm);
                idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
                idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);

                // Back face (reversed winding, opposite normal) so walls are visible from inside and outside
                v.Add(a); v.Add(b); v.Add(b + up); v.Add(a + up);
                n.Add(-nrm); n.Add(-nrm); n.Add(-nrm); n.Add(-nrm);
                idx.Add(s + 4); idx.Add(s + 6); idx.Add(s + 5);
                idx.Add(s + 4); idx.Add(s + 7); idx.Add(s + 6);
            }

            var mesh = new Mesh { name = "RoomWalls" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
