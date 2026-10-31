using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace RescueSim.EditorTools
{
    /// <summary>
    /// Runs the EditMode or PlayMode suite from a menu item. The result callback is re-registered after every
    /// domain reload (PlayMode runs reload scripts) and the summary is written to Builds/TestSummary.txt in the project.
    /// </summary>
    [InitializeOnLoad]
    public static class RunTests
    {
        static TestRunnerApi _api;

        static RunTests()
        {
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new Cb());
        }

        [MenuItem("Rescue Sim/Tests/Run EditMode")] static void Edit() => Run(TestMode.EditMode);
        [MenuItem("Rescue Sim/Tests/Run PlayMode")] static void Play() => Run(TestMode.PlayMode);
        [MenuItem("Rescue Sim/Tests/Run PlayMode - Playthroughs")] static void PlayThrough() => Run(TestMode.PlayMode, @"RescueSim\.Tests\.CampaignPlaythroughTests");
        [MenuItem("Rescue Sim/Tests/Run PlayMode - Scenes and menu")] static void PlayScenes() => Run(TestMode.PlayMode, @"RescueSim\.Tests\.(CampaignSceneTests|SceneSmokeTests)");
        [MenuItem("Rescue Sim/Tests/Run PlayMode - Mission flow")] static void PlayFlow() => Run(TestMode.PlayMode, @"RescueSim\.Tests\.MissionFlowTests");

        static void Run(TestMode mode, string groupRegex = null)
        {
            File.Delete(SummaryPath());
            var f = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(groupRegex)) f.groupNames = new[] { groupRegex };
            _api.Execute(new ExecutionSettings(f));
        }

        static string SummaryPath()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "TestSummary.txt");
        }

        class Cb : ICallbacks
        {
            static readonly StringBuilder Fails = new StringBuilder();
            static void Log(string line) => File.AppendAllText(Path.Combine(Path.GetDirectoryName(SummaryPath()), "TestProgress.txt"), line + "\n");
            public void RunStarted(ITestAdaptor t) { Fails.Clear(); File.WriteAllText(Path.Combine(Path.GetDirectoryName(SummaryPath()), "TestProgress.txt"), ""); }
            public void TestStarted(ITestAdaptor t) { if (!t.HasChildren) Log("START " + t.FullName); }
            public void TestFinished(ITestResultAdaptor r)
            {
                if (!r.HasChildren) Log($"{r.TestStatus} {r.FullName} ({r.Duration:F1}s)");
                if (!r.HasChildren && r.TestStatus == TestStatus.Failed) Fails.AppendLine($"FAIL {r.FullName}: {r.Message}");
            }
            public void RunFinished(ITestResultAdaptor r)
            {
                var s = $"passed={r.PassCount} failed={r.FailCount} skipped={r.SkipCount} inconclusive={r.InconclusiveCount}\n{Fails}";
                File.WriteAllText(SummaryPath(), s);
                Debug.Log("[RunTests] " + s);
            }
        }
    }
}
