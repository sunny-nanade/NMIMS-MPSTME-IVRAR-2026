using System.Text;
using UnityEditor;
using UnityEngine;

namespace RescueSim.EditorTools
{
    public static class CharacterDiagnostics
    {
        [MenuItem("Rescue Sim/Characters/Dump avatar hierarchy")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach (var a in new[] { "Male_Adult_01", "Construction_Male_01" })
            {
                string path = $"{CasualtyModels.RocketboxDir}/{a}/{a}.fbx";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine("== " + a + (go == null ? " (missing)" : ""));
                if (go == null) continue;
                Walk(go.transform, 0, sb);
            }
            System.IO.Directory.CreateDirectory("Builds");
            System.IO.File.WriteAllText("Builds/RocketboxHierarchy.txt", sb.ToString());
        }

        static void Walk(Transform t, int d, StringBuilder sb)
        {
            if (d > 3) return;
            var sk = t.GetComponent<SkinnedMeshRenderer>();
            var mf = t.GetComponent<MeshFilter>();
            string info = sk != null ? $" [Skinned tris={(sk.sharedMesh != null ? sk.sharedMesh.triangles.Length / 3 : 0)} mats={string.Join("|", System.Array.ConvertAll(sk.sharedMaterials, m => m != null ? m.name : "null"))}]" : mf != null ? " [MeshFilter]" : "";
            sb.AppendLine(new string(' ', d * 2) + t.name + info + $" children={t.childCount}");
            foreach (Transform c in t) Walk(c, d + 1, sb);
        }
    }
}
