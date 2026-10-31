using UnityEditor;
using UnityEngine;

namespace RescueSim.EditorTools
{
    /// <summary>Development aid: appends Editor errors/exceptions (with stack traces) to Builds/editor_errors.txt so build and test runs can be audited from outside the Editor.</summary>
    [InitializeOnLoad]
    static class ErrorLogToFile
    {
        const string Path = "Builds/editor_errors.txt";
        static ErrorLogToFile()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            try
            {
                System.IO.Directory.CreateDirectory("Builds");
                System.IO.File.AppendAllText(Path, $"[{System.DateTime.Now:HH:mm:ss}] {type}: {condition}\n{stackTrace}\n");
            }
            catch { }
        }
    }
}
