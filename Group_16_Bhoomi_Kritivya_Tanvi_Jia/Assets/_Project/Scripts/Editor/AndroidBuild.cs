using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RescueSim.EditorTools
{
    /// <summary>
    /// Builds the Quest APK and writes an honest report (result, errors, size, duration) to Builds/android-build-report.txt.
    /// Requires the active build target to be Android (Build Profiles > Meta Quest > Switch Platform) and Android Build Support installed.
    /// </summary>
    public static class AndroidBuild
    {
        const string OutDir = "Builds/Android";
        const string ReportPath = "Builds/android-build-report.txt";

        [MenuItem("Rescue Sim/Build/Switch target to Android")]
        static void ToAndroid() => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        [MenuItem("Rescue Sim/Build/Switch target to macOS")]
        static void ToMac() => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);

        [MenuItem("Rescue Sim/Build/Android APK (Quest) - requires Android active target")]
        public static void BuildApk()
        {
            Directory.CreateDirectory(OutDir);
            PlayerSettings.productName = ProjectInfo.ProductName;
            var sb = new StringBuilder();
            sb.AppendLine($"Android build report - {DateTime.Now:u}");
            sb.AppendLine($"Unity {Application.unityVersion}");
            sb.AppendLine($"Active build target: {EditorUserBuildSettings.activeBuildTarget}");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                sb.AppendLine("NOT BUILT: active build target is not Android. Switch platform first.");
                Finish(sb);
                return;
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            sb.AppendLine("Scenes: " + string.Join(", ", scenes));
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutDir, "DisasterResponseVR_HumansHands.apk"),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            sb.AppendLine($"Result: {s.result}");
            sb.AppendLine($"Errors: {s.totalErrors}  Warnings: {s.totalWarnings}");
            sb.AppendLine($"Duration: {s.totalTime}");
            if (s.result == BuildResult.Succeeded)
                sb.AppendLine($"Output: {s.outputPath}  Size: {s.totalSize / (1024 * 1024)} MB");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        sb.AppendLine("  ERROR: " + m.content);
            Finish(sb);
        }

        static void Finish(StringBuilder sb)
        {
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log("[RescueSim] " + sb);
        }
    }
}
