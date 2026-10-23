using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    /// <summary>Debug helper: writes the active scene hierarchy with components to a text file.</summary>
    public static class SceneDump
    {
        public static void Dump(string outPath, int maxDepth = 3)
        {
            var sb = new StringBuilder();
            foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects()) Rec(r.transform, 0, maxDepth, sb);
            File.WriteAllText(outPath, sb.ToString());
        }

        static void Rec(Transform t, int d, int max, StringBuilder sb)
        {
            sb.Append(new string(' ', d * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)").Append("  [");
            var comps = t.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) { sb.Append("MISSING,"); continue; }
                sb.Append(comps[i].GetType().Name).Append(i < comps.Length - 1 ? "," : "");
            }
            sb.Append("] pos=").Append(t.position.ToString("F2")).AppendLine();
            if (d < max) foreach (Transform c in t) Rec(c, d + 1, max, sb);
        }
    }
}
