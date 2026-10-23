using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Wall posters built from primitives (a cube frame, flat quads, TextMeshPro text). Unlit, so they read in the dark Medical Bay.
    /// A poster root faces the way its forward axis points AWAY from the viewer; local +X is the viewer's right.
    /// </summary>
    public static class PosterBuilder
    {
        static Mesh _disc;

        static Mesh Disc()
        {
            if (_disc != null) return _disc;
            const int seg = 32;
            var v = new Vector3[seg + 1]; var uv = new Vector2[seg + 1]; var tri = new int[seg * 3]; var nrm = new Vector3[seg + 1];
            v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f); nrm[0] = Vector3.back;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f); uv[i + 1] = new Vector2(v[i + 1].x + 0.5f, v[i + 1].y + 0.5f); nrm[i + 1] = Vector3.back;
                tri[i * 3] = 0; tri[i * 3 + 1] = (i + 1) % seg + 1; tri[i * 3 + 2] = i + 1; // seen from -Z
            }
            _disc = new Mesh { name = "PosterDisc", vertices = v, uv = uv, normals = nrm, triangles = tri };
            return _disc;
        }

        /// <summary>A framed board of the given size (metres). Children go in local space, Z negative is toward the viewer.</summary>
        public static Transform Board(Transform parent, string name, Vector3 worldPos, Quaternion rot, Vector2 size, Color background, Color frame)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(worldPos, rot);
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube); // primitive: poster frame (KNOWN_ISSUES)
            back.name = "Frame";
            Object.Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(root, false);
            back.transform.localScale = new Vector3(size.x + 0.06f, size.y + 0.06f, 0.03f);
            back.transform.localPosition = new Vector3(0, 0, 0.015f);
            back.GetComponent<MeshRenderer>().sharedMaterial = WorldUI.ColorMaterial(frame);
            WorldUI.Panel(root, "Paper", size, background, new Vector3(0, 0, -0.002f));
            return root;
        }

        public static GameObject Quad(Transform board, string name, Vector2 size, Color c, Vector3 localPos)
        {
            return WorldUI.Panel(board, name, size, c, new Vector3(localPos.x, localPos.y, -0.004f + localPos.z));
        }

        public static GameObject Circle(Transform board, string name, float diameter, Color c, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(board, false);
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, -0.004f + localPos.z);
            go.transform.localScale = new Vector3(diameter, diameter, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Disc();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = WorldUI.ColorMaterial(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        /// <summary>Text whose capital height is roughly lineMetres, centred at localPos.</summary>
        public static TextMeshPro Label(Transform board, string text, float lineMetres, Vector3 localPos, Color c, float width, bool bold = true)
        {
            var t = WorldUI.Text(board, text, WorldUI.Cm(lineMetres), new Vector3(localPos.x, localPos.y, -0.008f + localPos.z), new Vector2(width, lineMetres * 3f), TextAlignmentOptions.Center, c);
            if (bold) t.fontStyle = FontStyles.Bold;
            return t;
        }
    }
}
