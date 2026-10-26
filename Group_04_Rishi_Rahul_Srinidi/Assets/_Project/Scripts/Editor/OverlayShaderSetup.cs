using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace CSER
{
    /// OverlayUI finds the TextMeshPro overlay shaders by name at runtime, so they must survive shader stripping in
    /// player builds. Adds them to Graphics Settings > Always Included Shaders on load and again before every build
    /// (the UI overlay shader itself lives in Resources and is included automatically).
    [InitializeOnLoad]
    class OverlayShaderSetup : IPreprocessBuildWithReport
    {
        // exact shader names (no " - " before Overlay: a wrong name here once left every popup blank on Quest)
        static readonly string[] Names = { "TextMeshPro/Mobile/Distance Field Overlay", "TextMeshPro/Distance Field Overlay" };

        static OverlayShaderSetup() => EditorApplication.delayCall += Ensure;

        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report) => Ensure();

        [MenuItem("CSER/Debug/Ensure Overlay Shaders")]
        public static void Ensure()
        {
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = gs ? new SerializedObject(gs) : new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("GraphicsSettings"));
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;
            bool changed = false;
            foreach (var n in Names)
            {
                var shader = Shader.Find(n);
                if (!shader) { Debug.LogError("[OverlayShaderSetup] shader not found: " + n); continue; }
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log("[OverlayShaderSetup] added TextMeshPro overlay shaders to Always Included Shaders");
            }
        }
    }
}
