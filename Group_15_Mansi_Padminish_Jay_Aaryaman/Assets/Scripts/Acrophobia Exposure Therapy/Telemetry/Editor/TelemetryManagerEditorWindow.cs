using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Acrophobia.Telemetry.Editor
{
    /// <summary>
    /// Unity Editor Window for viewing, exporting, and analyzing Meta Quest 2 telemetry sessions.
    /// Provides one-click access to persistent telemetry storage, ADB pull from connected Quest 2 devices,
    /// and export to JSON/CSV for data science pipelines.
    /// </summary>
    public class TelemetryManagerEditorWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private List<string> _discoveredSessionIds = new List<string>();
        private int _selectedSessionIndex = -1;
        private string _statusMessage = "";
        private MessageType _statusType = MessageType.Info;
        private string _targetExportFolder = "";

        [MenuItem("Tools/Acrophobia/Telemetry & Export Manager", false, 100)]
        public static void Open()
        {
            var window = GetWindow<TelemetryManagerEditorWindow>("Quest Telemetry Manager");
            window.minSize = new Vector2(650, 520);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshSessions();
            if (string.IsNullOrEmpty(_targetExportFolder))
            {
                _targetExportFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Acrophobia_Telemetry_Exports"
                );
            }
        }

        private void RefreshSessions()
        {
            _discoveredSessionIds = TelemetryExporter.DiscoverRecordedSessions();
            if (_selectedSessionIndex >= _discoveredSessionIds.Count)
            {
                _selectedSessionIndex = _discoveredSessionIds.Count - 1;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            DrawHeader();
            EditorGUILayout.Space(8);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawSessionOverview();
            EditorGUILayout.Space(12);

            DrawExportTools();
            EditorGUILayout.Space(12);

            DrawAdbQuestSection();
            EditorGUILayout.Space(12);

            DrawPythonAnalysisSection();

            EditorGUILayout.EndScrollView();

            DrawStatusBar();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Meta Quest 2 Telemetry & Export Manager", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Collect, package, and export bio-adaptive exposure signals, head tremor, gaze avoidance, and controller telemetry for research analysis.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawSessionOverview()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Recorded Sessions ({_discoveredSessionIds.Count})", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                {
                    RefreshSessions();
                    SetStatus("Refreshed session list.", MessageType.Info);
                }
                if (GUILayout.Button("Open Folder", GUILayout.Width(100)))
                {
                    OpenFolder(TelemetryExporter.GetTelemetryRootDirectory());
                }
                EditorGUILayout.EndHorizontal();

                if (_discoveredSessionIds.Count == 0)
                {
                    EditorGUILayout.HelpBox($"No local session files found in:\n{TelemetryExporter.GetTelemetryRootDirectory()}\nRun a session in Editor PlayMode or pull logs from Quest 2 via ADB below.", MessageType.Info);
                    return;
                }

                EditorGUILayout.Space(4);
                string[] sessionLabels = _discoveredSessionIds.ToArray();
                int newIndex = EditorGUILayout.Popup("Select Session:", _selectedSessionIndex < 0 ? 0 : _selectedSessionIndex, sessionLabels);
                if (newIndex != _selectedSessionIndex)
                {
                    _selectedSessionIndex = newIndex;
                }

                if (_selectedSessionIndex >= 0 && _selectedSessionIndex < _discoveredSessionIds.Count)
                {
                    string sessionId = _discoveredSessionIds[_selectedSessionIndex];
                    DrawSelectedSessionCard(sessionId);
                }
            }
        }

        private void DrawSelectedSessionCard(string sessionId)
        {
            string root = TelemetryExporter.GetTelemetryRootDirectory();
            string samplesCsv = Path.Combine(root, $"session_{sessionId}_samples.csv");
            string eventsCsv = Path.Combine(root, $"session_{sessionId}_events.csv");
            string summaryJson = Path.Combine(TelemetryExporter.GetExportDirectory(), $"session_{sessionId}_summary.json");

            bool hasSamples = File.Exists(samplesCsv);
            bool hasEvents = File.Exists(eventsCsv);
            bool hasSummary = File.Exists(summaryJson);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Session Details: {sessionId}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Samples File: {(hasSamples ? "Found (" + new FileInfo(samplesCsv).Length / 1024 + " KB)" : "Not Found")}");
                EditorGUILayout.LabelField($"Events File: {(hasEvents ? "Found" : "Not Found")}");
                EditorGUILayout.LabelField($"Summary JSON: {(hasSummary ? "Found" : "Not Generated Yet")}");

                if (hasSummary)
                {
                    try
                    {
                        string json = File.ReadAllText(summaryJson);
                        var meta = JsonUtility.FromJson<TelemetryExporter.SessionExportMetadata>(json);
                        if (meta != null)
                        {
                            EditorGUILayout.LabelField($"Participant: {meta.participantId} | Duration: {meta.sessionDurationSeconds:F1}s | Samples: {meta.sampleCount}");
                            EditorGUILayout.LabelField($"Max Height: {meta.maxHeightAboveGroundM:F1}m (Tier {meta.highestTierReached}) | Peak Tremor: {meta.peakTremorIntensity:F3}");
                            if (meta.initialSudsRating >= 0 && meta.finalSudsRating >= 0)
                            {
                                float sudsDelta = meta.finalSudsRating - meta.initialSudsRating;
                                EditorGUILayout.LabelField($"SUDS Rating: {meta.initialSudsRating} -> {meta.finalSudsRating} (Delta: {sudsDelta:+0;-0;0})");
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
        }

        private void DrawExportTools()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Export for Data Analysis (Pandas, R, Excel)", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                _targetExportFolder = EditorGUILayout.TextField("Export Target Path:", _targetExportFolder);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string folder = EditorUtility.OpenFolderPanel("Select Export Directory", _targetExportFolder, "");
                    if (!string.IsNullOrEmpty(folder)) _targetExportFolder = folder;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Export Selected Session to Folder", GUILayout.Height(28)))
                {
                    if (_selectedSessionIndex >= 0 && _selectedSessionIndex < _discoveredSessionIds.Count)
                    {
                        string id = _discoveredSessionIds[_selectedSessionIndex];
                        if (TelemetryExporter.ExportSessionFilesToDirectory(id, _targetExportFolder, out var files))
                        {
                            SetStatus($"Exported {files.Count} files for session {id} to {_targetExportFolder}", MessageType.Info);
                            OpenFolder(_targetExportFolder);
                        }
                        else
                        {
                            SetStatus("Failed to export files. Verify session files exist.", MessageType.Error);
                        }
                    }
                }

                if (GUILayout.Button("Open Export Directory", GUILayout.Height(28), GUILayout.Width(160)))
                {
                    OpenFolder(_targetExportFolder);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawAdbQuestSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Meta Quest 2 ADB Log Retrieval", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("When running on a standalone Quest 2 headset via USB/AirLink, telemetry is stored inside Android app storage.", EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Check Connected Quest Devices (ADB)", GUILayout.Height(26)))
                {
                    RunAdbCommand("devices");
                }

                if (GUILayout.Button("Pull Telemetry from Quest 2 to PC", GUILayout.Height(26)))
                {
                    PullTelemetryFromQuest();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPythonAnalysisSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Automated Python Analysis", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Runs the statistical analysis pipeline (Tremor power spectrum, Gaze avoidance index, and SUDS desensitization plots).", EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("Run Python Analysis on Selected Session", GUILayout.Height(28)))
                {
                    if (_selectedSessionIndex >= 0 && _selectedSessionIndex < _discoveredSessionIds.Count)
                    {
                        RunPythonAnalysis(_discoveredSessionIds[_selectedSessionIndex]);
                    }
                    else
                    {
                        SetStatus("Please select a session first.", MessageType.Warning);
                    }
                }
            }
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        private void SetStatus(string msg, MessageType type)
        {
            _statusMessage = msg;
            _statusType = type;
            Repaint();
        }

        private void OpenFolder(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        private string GetAdbPath()
        {
            try
            {
                string sdkRoot = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath;
                if (!string.IsNullOrEmpty(sdkRoot))
                {
                    string candidate = Path.Combine(sdkRoot, "platform-tools", "adb.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception) { }
            return "adb";
        }

        private void RunAdbCommand(string args)
        {
            try
            {
                string adbExe = GetAdbPath();
                var psi = new ProcessStartInfo(adbExe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(3000);
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    SetStatus(!string.IsNullOrEmpty(output) ? output : error, MessageType.Info);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"ADB execution error: {ex.Message}. Make sure Android SDK Platform Tools (adb.exe) is on system PATH.", MessageType.Warning);
            }
        }

        private void PullTelemetryFromQuest()
        {
            string packageName = Application.identifier;
            string remotePath = $"/sdcard/Android/data/{packageName}/files/Telemetry/";
            string localPath = TelemetryExporter.GetTelemetryRootDirectory();

            try
            {
                var psi = new ProcessStartInfo("adb", $"pull \"{remotePath}\" \"{localPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(8000);
                    string output = p.StandardOutput.ReadToEnd();
                    RefreshSessions();
                    SetStatus($"ADB Pull completed:\n{output}", MessageType.Info);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"ADB Pull failed: {ex.Message}", MessageType.Error);
            }
        }

        private void RunPythonAnalysis(string sessionId)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string scriptPath = Path.Combine(projectRoot, "Tools", "TelemetryAnalysis", "analyze_telemetry.py");

            if (!File.Exists(scriptPath))
            {
                SetStatus($"Analysis script not found at: {scriptPath}", MessageType.Error);
                return;
            }

            string root = TelemetryExporter.GetTelemetryRootDirectory();
            string samplesCsv = Path.Combine(root, $"session_{sessionId}_samples.csv");
            string exportDir = _targetExportFolder;

            try
            {
                var psi = new ProcessStartInfo("python", $"\"{scriptPath}\" --samples \"{samplesCsv}\" --output \"{exportDir}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(10000);
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    Debug.Log($"[Telemetry Analysis Output]\n{output}\n{error}");
                    SetStatus($"Analysis completed! Check Console and {exportDir}", MessageType.Info);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Python analysis error: {ex.Message}", MessageType.Error);
            }
        }
    }
}
