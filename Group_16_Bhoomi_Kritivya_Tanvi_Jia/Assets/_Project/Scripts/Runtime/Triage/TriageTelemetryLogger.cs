using System;
using System.IO;
using UnityEngine;

namespace RescueSim.Triage
{
    /// <summary>
    /// Exports a finished (or abandoned) triage session as three CSV files:
    /// {session}_records.csv (raw, one row per casualty), {session}_summary.csv (derived metrics), {session}_confusion.csv.
    /// Files go to Application.persistentDataPath/TriageTelemetry unless a directory override is set.
    /// Rows hold only an anonymous participant id (P01, P02... or TEST): no names or contact details.
    /// </summary>
    public class TriageTelemetryLogger : MonoBehaviour
    {
        [SerializeField] string folderName = "TriageTelemetry";
        [Tooltip("Optional absolute directory (used by tests). Empty = persistent data path.")]
        [SerializeField] string directoryOverride = "";

        public string LastRecordsPath { get; private set; }
        public string LastSummaryPath { get; private set; }
        public string LastConfusionPath { get; private set; }
        public string LastError { get; private set; }
        public int ExportCount { get; private set; }

        public void SetDirectoryOverride(string dir) { directoryOverride = dir; }

        public string ResolvedDirectory => string.IsNullOrEmpty(directoryOverride) ? Path.Combine(Application.persistentDataPath, folderName) : directoryOverride;

        public bool Export(TriageSession session)
        {
            LastError = null;
            if (!WriteSession(session, ResolvedDirectory, out var rec, out var sum, out var conf, out var err)) { LastError = err; Debug.LogError("[TriageTelemetry] " + err); return false; }
            LastRecordsPath = rec; LastSummaryPath = sum; LastConfusionPath = conf; ExportCount++;
            return true;
        }

        /// <summary>File-writing core, usable without a scene (tests, tools).</summary>
        public static bool WriteSession(TriageSession session, string directory, out string recordsPath, out string summaryPath, out string confusionPath, out string error)
        {
            recordsPath = summaryPath = confusionPath = null; error = null;
            if (session == null) { error = "No session."; return false; }
            try
            {
                Directory.CreateDirectory(directory);
                string safe = Sanitize(session.SessionId);
                var summary = TriageMetrics.Compute(session.Records, session.Cases.Count);
                recordsPath = Path.Combine(directory, safe + "_records.csv");
                summaryPath = Path.Combine(directory, safe + "_summary.csv");
                confusionPath = Path.Combine(directory, safe + "_confusion.csv");
                File.WriteAllText(recordsPath, TriageCsv.RecordsToCsv(session.Records));
                File.WriteAllText(summaryPath, TriageCsv.SummaryToCsv(session, summary));
                File.WriteAllText(confusionPath, TriageCsv.ConfusionToCsv(summary));
                return true;
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return false; }
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
