using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Writes deepsleep_telemetry.csv, one row per smoke-room block. Other scripts only call Log(field, value).
    /// Session-level fields (earcheck, keypad, trail, coolant) are copied into every row at flush time.
    /// </summary>
    public class TelemetryLogger : MonoBehaviour
    {
        public static TelemetryLogger Instance { get; private set; }

        public static readonly string[] Header =
        {
            "participant_id", "session_id", "condition", "block_order", "layout", "evac_time_s", "wrong_turns",
            "dead_end_entries", "backtracks", "path_length_m", "path_efficiency", "wall_contacts", "idle_time_s",
            "timeout_flag", "mean_head_turn_latency_ms", "TLX_mental", "TLX_physical", "TLX_temporal",
            "TLX_performance", "TLX_effort", "TLX_frustration", "TLX_raw_mean", "SUS_score",
            "earcheck_mean_error_deg", "earcheck_mean_rt_s", "mean_fps", "min_fps", "p1_low_fps", "timestamp",
            "keypad_time_s", "keypad_wrong_attempts", "trail_time_s", "coolant_time_s", "coolant_wrong_plugs",
            "beacon_game_time_s", "beacon_wrong_placements"
        };

        static readonly HashSet<string> SessionFields = new HashSet<string>
        {
            "earcheck_mean_error_deg", "earcheck_mean_rt_s", "keypad_time_s", "keypad_wrong_attempts",
            "trail_time_s", "coolant_time_s", "coolant_wrong_plugs", "beacon_game_time_s", "beacon_wrong_placements"
        };

        [SerializeField] string fileName = "deepsleep_telemetry.csv";

        readonly Dictionary<string, string> _session = new Dictionary<string, string>();
        readonly Dictionary<int, Dictionary<string, string>> _rows = new Dictionary<int, Dictionary<string, string>>();
        readonly HashSet<int> _flushed = new HashSet<int>();

        // fps sampling
        bool _sampling;
        readonly List<float> _frameTimes = new List<float>();

        /// <summary>Set by the playthrough bot so its rows never mix with real participant data.</summary>
        public static string FileNameOverride;
        public string FilePath => Path.Combine(Application.persistentDataPath, FileNameOverride ?? fileName);
        public IReadOnlyDictionary<int, Dictionary<string, string>> Rows => _rows;
        public IReadOnlyDictionary<string, string> SessionValues => _session;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Update()
        {
            if (_sampling && Time.unscaledDeltaTime > 0f && Time.timeScale > 0f) _frameTimes.Add(Time.unscaledDeltaTime);
        }

        public static void Log(string field, object value, int block = -1)
        {
            if (Instance == null) return;
            Instance.Set(field, value, block);
        }

        void Set(string field, object value, int block)
        {
            string v = Format(value);
            if (SessionFields.Contains(field)) { _session[field] = v; foreach (var r in _rows.Values) r[field] = v; return; }
            if (block < 0) block = SessionManager.Instance != null ? Mathf.Max(1, SessionManager.Instance.CurrentBlock) : 1;
            if (!_rows.TryGetValue(block, out var row)) row = NewRow(block);
            row[field] = v;
        }

        static string Format(object value)
        {
            switch (value)
            {
                case null: return "";
                case float f: return f.ToString("0.####", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.####", CultureInfo.InvariantCulture);
                case bool b: return b ? "1" : "0";
                case IFormattable fm: return fm.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        Dictionary<string, string> NewRow(int block)
        {
            var row = new Dictionary<string, string>();
            foreach (var h in Header) row[h] = "";
            row["participant_id"] = StudyConfig.Data.participant_id;
            row["session_id"] = SessionManager.Instance != null ? SessionManager.Instance.SessionId : "none";
            row["block_order"] = block.ToString();
            var plan = ConditionRandomizer.Plan(StudyConfig.ParticipantNumber, block);
            row["condition"] = ConditionRandomizer.Short(plan.condition);
            row["layout"] = plan.layout.ToString();
            row["timestamp"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            foreach (var kv in _session) row[kv.Key] = kv.Value;
            _rows[block] = row;
            return row;
        }

        public void BeginBlock(int block)
        {
            if (!_rows.ContainsKey(block)) NewRow(block);
            _frameTimes.Clear();
            _sampling = true;
        }

        public void EndBlock(int block)
        {
            _sampling = false;
            if (_frameTimes.Count == 0) { Set("mean_fps", 0f, block); Set("min_fps", 0f, block); Set("p1_low_fps", 0f, block); return; }
            var ft = new List<float>(_frameTimes);
            float sum = 0, max = 0;
            foreach (var f in ft) { sum += f; if (f > max) max = f; }
            float mean = ft.Count / sum;
            ft.Sort();
            int n = Mathf.Max(1, Mathf.CeilToInt(ft.Count * 0.01f));
            float worst = 0; for (int i = ft.Count - n; i < ft.Count; i++) worst += ft[i];
            worst /= n;
            Set("mean_fps", mean, block); Set("min_fps", 1f / max, block); Set("p1_low_fps", 1f / worst, block);
        }

        public void ResetAll() { _rows.Clear(); _session.Clear(); _flushed.Clear(); _frameTimes.Clear(); _sampling = false; }

        /// <summary>
        /// Write this session's rows to the CSV in persistentDataPath. Safe to call any number of times: rows of this session (same participant and session id and block)
        /// that were written earlier are replaced by their current, more complete version; rows of other sessions are never touched.
        /// </summary>
        public string Flush()
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                var lines = new List<string>();
                if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath));
                if (lines.Count == 0 || lines[0] != string.Join(",", Header)) { lines.RemoveAll(l => l == string.Join(",", Header)); lines.Insert(0, string.Join(",", Header)); }
                string session = SessionManager.Instance != null ? SessionManager.Instance.SessionId : "none";
                string pid = StudyConfig.Data.participant_id;
                foreach (var kv in new SortedDictionary<int, Dictionary<string, string>>(new Dictionary<int, Dictionary<string, string>>(_rows)))
                {
                    foreach (var kv2 in _session) kv.Value[kv2.Key] = kv2.Value;
                    string prefix = Escape(kv.Value["participant_id"]) + "," + Escape(kv.Value["session_id"]) + "," ;
                    // replace an earlier version of this row: same participant, session and block order
                    lines.RemoveAll(l => l.StartsWith(prefix) && BlockOrderOf(l) == kv.Key.ToString());
                    var cells = new List<string>();
                    foreach (var h in Header) cells.Add(Escape(kv.Value.TryGetValue(h, out var v) ? v : ""));
                    lines.Add(string.Join(",", cells));
                    _flushed.Add(kv.Key);
                }
                File.WriteAllLines(FilePath, lines);
                DS.Log("Telemetry written: " + FilePath);
            }
            catch (Exception e) { DS.Error("Telemetry flush failed: " + e.Message); }
            return FilePath;
        }

        static string BlockOrderOf(string line)
        {
            // block_order is the fourth column (participant, session, condition, block_order)
            var parts = line.Split(',');
            return parts.Length > 3 ? parts[3] : "";
        }

        static string Escape(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        /// <summary>Names of fields that are still empty in a row (used by tests and the bot).</summary>
        public List<string> EmptyFields(int block)
        {
            var list = new List<string>();
            if (!_rows.TryGetValue(block, out var row)) { list.AddRange(Header); return list; }
            foreach (var h in Header) if (string.IsNullOrEmpty(row[h])) list.Add(h);
            return list;
        }
    }
}
