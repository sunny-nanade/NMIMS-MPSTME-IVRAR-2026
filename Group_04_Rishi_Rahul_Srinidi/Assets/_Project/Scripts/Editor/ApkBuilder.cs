using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CSER.EditorTools
{
    /// Builds the Quest APK from the scenes in Build Settings into the folder above the project.
    /// The result summary is written to Temp/cser_build.txt so it can be read without the editor UI.
    static class ApkBuilder
    {
        const string ProductName = "K068_K075_K081_AR VR Project_The Northwind Breach";
        // Unique id: the template default (com.UnityTechnologies.com.unity.template.urpblank) collides with
        // other builds already installed on shared lab headsets and fails with INSTALL_FAILED_UPDATE_INCOMPATIBLE.
        const string PackageId = "com.k068k075k081.arvrproject";

        [MenuItem("CSER/Build APK (Quest)")]
        static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, PackageId);
            AssetDatabase.SaveAssets();

            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ProductName + ".apk"));
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            EditorUserBuildSettings.buildAppBundle = false;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string msg = $"result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize} time={s.totalTime} path={outPath}";
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) msg += "\n" + m.content;
            Debug.Log("[ApkBuilder] " + msg);
            File.WriteAllText(Path.Combine("Temp", "cser_build.txt"), msg);
        }
    }
}
