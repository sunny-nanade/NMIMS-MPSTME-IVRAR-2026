using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Group10Gestures.Core;
using Group10Gestures.Providers;
using Group10Gestures.Recognition;

namespace Group10Gestures.Interaction
{
    /// <summary>
    /// Records one CSV row per usability trial (real participant data for dossier Section 4).
    ///
    /// Standard task (say it out loud to every participant):
    ///   "Draw a 4 m x 3 m room, raise its walls to 2.5 m, then paint it blue."
    ///
    /// Nothing is recorded until you press F2 (until then the overlay says PRACTICE). A recorded trial ends
    /// automatically when the finished room is painted. Keys:  F2 = start recorded trial,
    /// F3 = end and save (marked incomplete), F4 = next participant (discards an unfinished trial).
    ///
    /// Run each participant once per condition: Webcam (MediaPipe) and Meta XR (Quest hand tracking);
    /// the condition is recorded automatically from the active provider. Do not press F1 during a trial.
    ///
    /// CSV location: TrialData/g10_trials.csv next to the Assets folder in the Editor, Application.persistentDataPath in a build.
    /// Latency is measured only for the webcam provider (Python timestamp -> Unity receive, same PC clock).
    /// </summary>
    public class TrialLogger : MonoBehaviour
    {
        [Header("Task targets (for accuracy columns)")]
        [SerializeField] private float _targetAreaM2 = 12f;
        [SerializeField] private float _targetHeightM = 2.5f;

        private RoomBuilderController _builder;
        private HandProviderSelector _selector;
        private WebcamHandProvider _webcam;
        private GestureRecognizer _rec;

        private int _participantId;
        private int _trialIndex;
        private bool _active;
        private float _startTime;
        private DateTime _startUtc;
        private string _condition = "";

        private int _corners, _undos, _deleted;
        private bool _roomBuilt;
        private float _roomArea, _roomHeight;
        private float _lostTime;
        private double _lastStamp = -1.0;
        private readonly List<double> _latencies = new List<double>();
        private readonly List<float> _fps = new List<float>();

        private string _csvPath;
        private string _message = "";
        private GUIStyle _guiStyle;

        private void Start()
        {
            _participantId = PlayerPrefs.GetInt("g10_pid", 1);

            string dir = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TrialData")
                : Application.persistentDataPath;
            try { Directory.CreateDirectory(dir); } catch (Exception e) { Debug.LogWarning("[TrialLogger] " + e.Message); }
            _csvPath = Path.Combine(dir, "g10_trials.csv");
            Debug.Log("[TrialLogger] Trial CSV: " + _csvPath);
        }

        private void Subscribe()
        {
            _builder = GetComponent<RoomBuilderController>();
            if (_builder == null) return;
            _builder.CornerPlaced += OnCornerPlaced;
            _builder.CornerUndone += OnCornerUndone;
            _builder.RoomDeleted += OnRoomDeleted;
            _builder.RoomCompleted += OnRoomCompleted;
            _builder.RoomColored += OnRoomColored;
        }

        private void OnDestroy()
        {
            if (_builder == null) return;
            _builder.CornerPlaced -= OnCornerPlaced;
            _builder.CornerUndone -= OnCornerUndone;
            _builder.RoomDeleted -= OnRoomDeleted;
            _builder.RoomCompleted -= OnRoomCompleted;
            _builder.RoomColored -= OnRoomColored;
        }

        // ---------------------------------------------------------------- events from the builder
        private void OnCornerPlaced(int count)
        {
            if (_active) _corners++; // practice runs (no F2) are never recorded
        }

        private void OnCornerUndone() { if (_active) _undos++; }
        private void OnRoomDeleted() { if (_active) _deleted++; }

        private void OnRoomCompleted(Room room)
        {
            if (!_active) return;
            _roomBuilt = true;
            _roomArea = room.Area;
            _roomHeight = room.Height;
        }

        private void OnRoomColored(Room room)
        {
            if (_active && _roomBuilt) EndTrial(true);
        }

        // ---------------------------------------------------------------- trial lifecycle
        private void BeginTrial()
        {
            _active = true;
            _startTime = Time.realtimeSinceStartup;
            _startUtc = DateTime.UtcNow;
            _condition = _selector != null ? _selector.ProviderName : "unknown";
            _corners = _undos = _deleted = 0;
            _roomBuilt = false;
            _roomArea = _roomHeight = 0f;
            _lostTime = 0f;
            _lastStamp = -1.0;
            _latencies.Clear();
            _fps.Clear();
            _message = "Trial running...";
        }

        private void EndTrial(bool completed)
        {
            if (!_active) return;
            _active = false;
            _trialIndex++;

            float duration = Time.realtimeSinceStartup - _startTime;
            string latMean = "", latP95 = "";
            if (_latencies.Count > 0)
            {
                double sum = 0;
                foreach (double l in _latencies) sum += l;
                latMean = Fmt(sum / _latencies.Count);

                var sorted = new List<double>(_latencies);
                sorted.Sort();
                int i95 = Mathf.Clamp((int)Math.Ceiling(0.95 * sorted.Count) - 1, 0, sorted.Count - 1);
                latP95 = Fmt(sorted[i95]);
            }

            string fpsMean = "", fpsMin = "";
            if (_fps.Count > 0)
            {
                float fs = 0f, fm = float.MaxValue;
                foreach (float f in _fps) { fs += f; if (f < fm) fm = f; }
                fpsMean = Fmt(fs / _fps.Count);
                fpsMin = Fmt(fm);
            }

            string areaErr = "", heightErr = "";
            if (_roomBuilt)
            {
                areaErr = Fmt(Math.Abs(_roomArea - _targetAreaM2) / _targetAreaM2 * 100.0);
                heightErr = Fmt(Math.Abs(_roomHeight - _targetHeightM) / _targetHeightM * 100.0);
            }

            var sb = new StringBuilder();
            sb.Append(_participantId).Append(',')
              .Append(_trialIndex).Append(',')
              .Append(Csv(_condition)).Append(',')
              .Append(_startUtc.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(Fmt(duration)).Append(',')
              .Append(completed ? 1 : 0).Append(',')
              .Append(_corners).Append(',')
              .Append(_undos).Append(',')
              .Append(_deleted).Append(',')
              .Append(_roomBuilt ? Fmt(_roomArea) : "").Append(',')
              .Append(areaErr).Append(',')
              .Append(_roomBuilt ? Fmt(_roomHeight) : "").Append(',')
              .Append(heightErr).Append(',')
              .Append(Fmt(_lostTime)).Append(',')
              .Append(latMean).Append(',')
              .Append(latP95).Append(',')
              .Append(fpsMean).Append(',')
              .Append(fpsMin)
              .Append('\n');

            try
            {
                if (!File.Exists(_csvPath))
                {
                    File.WriteAllText(_csvPath,
                        "participant_id,trial,condition,start_utc,duration_s,completed,corners_placed,undo_count,rooms_deleted," +
                        "room_area_m2,area_error_pct,room_height_m,height_error_pct,tracking_lost_s," +
                        "mean_latency_ms,p95_latency_ms,mean_fps,min_fps\n");
                }
                File.AppendAllText(_csvPath, sb.ToString());
                _message = "Saved trial " + _trialIndex + " (" + (completed ? "complete" : "incomplete") + ", " + duration.ToString("F1") + " s)";
            }
            catch (Exception e)
            {
                _message = "CSV write failed: " + e.Message;
                Debug.LogError("[TrialLogger] " + e);
            }
            Debug.Log("[TrialLogger] " + _message);
        }

        private static string Fmt(double v) { return v.ToString("F2", CultureInfo.InvariantCulture); }
        private static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        // ---------------------------------------------------------------- per-frame sampling
        private void Update()
        {
            if (_builder == null) Subscribe();
            if (_selector == null) _selector = FindAnyObjectByType<HandProviderSelector>();
            if (_webcam == null) _webcam = FindAnyObjectByType<WebcamHandProvider>();
            if (_rec == null) _rec = FindAnyObjectByType<GestureRecognizer>();

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f2Key.wasPressedThisFrame && !_active) BeginTrial();
                if (kb.f3Key.wasPressedThisFrame && _active) EndTrial(false);
                if (kb.f4Key.wasPressedThisFrame)
                {
                    if (_active) { _active = false; } // discard a running trial without saving it
                    _participantId++;
                    _trialIndex = 0;
                    PlayerPrefs.SetInt("g10_pid", _participantId);
                    _message = "Participant " + _participantId;
                }
            }

            if (!_active) return;

            if (Time.unscaledDeltaTime > 0f) _fps.Add(1f / Time.unscaledDeltaTime);

            if (_rec != null && _builder != null && !_rec.RightResult.IsTracked) _lostTime += Time.unscaledDeltaTime;

            bool webcamActive = _selector != null && _selector.CurrentType == HandProviderSelector.ProviderType.Webcam;
            if (webcamActive && _webcam != null)
            {
                double stamp = _webcam.GetTimestamp(Handedness.Right);
                if (stamp > 0 && Math.Abs(stamp - _lastStamp) > 0.5)
                {
                    _lastStamp = stamp;
                    _latencies.Add(_webcam.LatencyMs); // one sample per new packet, not per frame
                }
            }
        }

        // ---------------------------------------------------------------- desktop overlay (top-right)
        private void OnGUI()
        {
            if (_guiStyle == null)
            {
                _guiStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperRight, wordWrap = true };
                _guiStyle.normal.textColor = new Color(1f, 0.95f, 0.6f);
            }

            string cond = _selector != null ? _selector.ProviderName : "?";
            string state = _active
                ? "RECORDING " + (Time.realtimeSinceStartup - _startTime).ToString("F1") + " s"
                : "PRACTICE (not recorded)";
            string text = "Participant " + _participantId + "  |  " + cond + "\nTrial: " + state +
                          "\n[F2] start recorded trial  [F3] end & save  [F4] next participant\n" + _message;
            GUI.Label(new Rect(Screen.width - 520f, 20f, 500f, 120f), text, _guiStyle);
        }
    }
}
