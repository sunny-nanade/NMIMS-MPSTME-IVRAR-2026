using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// Handles exporting, packaging, and formatting Quest 2 telemetry sessions
    /// into research-ready CSV and JSON formats for external analysis (Pandas, R, SPSS).
    /// </summary>
    public static class TelemetryExporter
    {
        public const string DefaultTelemetryFolderName = "Telemetry";
        public const string DefaultExportsFolderName = "TelemetryExports";

        [Serializable]
        public class SessionExportMetadata
        {
            public string sessionId;
            public string participantId;
            public string deviceModel;
            public string operatingSystem;
            public string unityVersion;
            public string startTimeUtc;
            public string endTimeUtc;
            public float sessionDurationSeconds;
            public int sampleCount;
            public int highestTierReached;
            public float maxHeightAboveGroundM;
            public float maxPlanarDistanceM;
            public float totalDownwardGazeSeconds;
            public int retreatEventsCount;
            public int sudsResponseCount;
            public float initialSudsRating = -1f;
            public float finalSudsRating = -1f;
            public float peakTremorIntensity;
            public float meanTremorIntensity;
            public float meanGazeAvoidanceRatio;
            public TierDwellMetric[] tierDwells;
            public SudsEventSummary[] sudsEvents;
        }

        [Serializable]
        public class TierDwellMetric
        {
            public int tier;
            public float dwellSeconds;
        }

        [Serializable]
        public class SudsEventSummary
        {
            public float sessionTime;
            public int tier;
            public string promptContext;
            public int rating;
        }

        /// <summary>
        /// Gets the primary root directory where telemetry files are recorded.
        /// </summary>
        public static string GetTelemetryRootDirectory()
        {
            return Path.Combine(Application.persistentDataPath, DefaultTelemetryFolderName);
        }

        /// <summary>
        /// Gets the destination directory where finalized export bundles are stored.
        /// </summary>
        public static string GetExportDirectory()
        {
            string dir = Path.Combine(Application.persistentDataPath, DefaultExportsFolderName);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        /// <summary>
        /// Exports a complete session summary as structured JSON to the export directory.
        /// </summary>
        public static string ExportSessionSummaryJson(SessionExportMetadata metadata)
        {
            if (metadata == null) return null;

            string exportDir = GetExportDirectory();
            string filename = $"session_{metadata.sessionId}_summary.json";
            string filePath = Path.Combine(exportDir, filename);

            try
            {
                string json = JsonUtility.ToJson(metadata, true);
                File.WriteAllText(filePath, json, Encoding.UTF8);
                Debug.Log($"[TelemetryExporter] Exported session summary JSON to: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryExporter] Failed to export JSON summary: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Copies session samples and event CSVs to an export target destination,
        /// ensuring clean file naming and directory creation.
        /// </summary>
        public static bool ExportSessionFilesToDirectory(string sessionId, string targetDirectory, out List<string> exportedFiles)
        {
            exportedFiles = new List<string>();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(targetDirectory))
            {
                return false;
            }

            try
            {
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                string sourceDir = GetTelemetryRootDirectory();
                string[] matchingFiles = Directory.GetFiles(sourceDir, $"session_{sessionId}*.*");

                foreach (string src in matchingFiles)
                {
                    string dest = Path.Combine(targetDirectory, Path.GetFileName(src));
                    File.Copy(src, dest, true);
                    exportedFiles.Add(dest);
                }

                // Also check export directory for summary JSON
                string exportDir = GetExportDirectory();
                string[] matchingExports = Directory.GetFiles(exportDir, $"session_{sessionId}*.*");
                foreach (string src in matchingExports)
                {
                    string dest = Path.Combine(targetDirectory, Path.GetFileName(src));
                    if (!File.Exists(dest) || src != dest)
                    {
                        File.Copy(src, dest, true);
                        if (!exportedFiles.Contains(dest)) exportedFiles.Add(dest);
                    }
                }

                return exportedFiles.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TelemetryExporter] Error exporting session files: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads all existing session IDs from the telemetry recording directory.
        /// </summary>
        public static List<string> DiscoverRecordedSessions()
        {
            var sessions = new HashSet<string>();
            string root = GetTelemetryRootDirectory();
            if (!Directory.Exists(root)) return new List<string>();

            string[] files = Directory.GetFiles(root, "session_*.*");
            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                // format: session_20261020_180000_samples
                string[] parts = name.Split('_');
                if (parts.Length >= 3 && parts[0] == "session")
                {
                    string id = $"{parts[1]}_{parts[2]}";
                    sessions.Add(id);
                }
            }

            var list = new List<string>(sessions);
            list.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal)); // descending (newest first)
            return list;
        }
    }
}
