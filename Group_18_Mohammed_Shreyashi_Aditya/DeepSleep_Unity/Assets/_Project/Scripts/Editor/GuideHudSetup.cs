using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Creates Resources/M_Hologram (HologramPanel shader) so the shader is always included in a build.</summary>
    public static class GuideHudSetup
    {
        const string Path = "Assets/_Project/Resources/M_Hologram.mat";

        [MenuItem("DeepSleep/Setup/Create hologram material")]
        public static string CreateMaterial()
        {
            var sh = Shader.Find("Fractured_Final/HologramPanel");
            if (sh == null) return "HologramPanel shader not found (compile error?)";
            var m = AssetDatabase.LoadAssetAtPath<Material>(Path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, Path); }
            m.shader = sh;
            m.SetColor("_Color", new Color(0.25f, 0.85f, 1f, 1f));
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return "M_Hologram ready";
        }
    }
}
