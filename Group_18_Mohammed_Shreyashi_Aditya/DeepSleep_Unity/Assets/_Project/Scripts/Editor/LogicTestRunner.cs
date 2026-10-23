using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>DeepSleep > Run Logic Tests: runs the edit-mode NUnit tests (no play mode) and writes Docs/Reports/logic_tests.md.</summary>
    public static class LogicTestRunner
    {
        public const string ReportPath = "Assets/_Project/Docs/Reports/logic_tests.md";

        class Callbacks : ICallbacks
        {
            readonly StringBuilder _sb = new StringBuilder();
            int _pass, _fail;
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;
                bool ok = result.TestStatus == TestStatus.Passed;
                if (ok) _pass++; else _fail++;
                _sb.AppendLine($"- [{(ok ? "PASS" : "FAIL")}] {result.Test.FullName}" + (ok ? "" : " - " + result.Message?.Replace("\n", " ")));
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                var head = new StringBuilder();
                head.AppendLine("# Edit-mode logic tests"); head.AppendLine();
                head.AppendLine($"Date: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                head.AppendLine($"Result: {_pass} passed, {_fail} failed"); head.AppendLine();
                File.WriteAllText(ReportPath, head.ToString() + _sb);
                File.WriteAllText(ReportPath + ".done", _fail == 0 ? "OK" : "FAIL");
                Debug.Log($"[DeepSleep] Logic tests finished: {_pass} passed, {_fail} failed");
            }
        }

        [MenuItem("DeepSleep/Run Logic Tests")]
        public static void Run()
        {
            if (File.Exists(ReportPath + ".done")) File.Delete(ReportPath + ".done");
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }
    }
}
