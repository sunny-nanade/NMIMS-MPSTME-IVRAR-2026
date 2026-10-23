using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Frame-rate and memory capture to profiler_log.csv in persistentDataPath, one row per second while enabled
    /// (experimenter menu "Profiler", or ProfilerCapture.Enabled = true). The frame budget is EDITOR_FRAME_BUDGET (11 ms) in the Editor and 1 / TARGET_FPS on a device.
    /// </summary>
    public class ProfilerCapture : MonoBehaviour
    {
        public static ProfilerCapture Instance { get; private set; }
        public static bool Enabled { get; set; }
        public static string FileNameOverride;

        public const string Header = "time_s,participant_id,session_id,stage,scene,block,fps,frame_ms_mean,frame_ms_max,over_budget_pct,budget_ms,managed_mb,allocated_mb,gc_collections";

        [SerializeField] float intervalSeconds = 1f;
        [SerializeField] int flushEveryRows = 10;

        public string FilePath => Path.Combine(Application.persistentDataPath, FileNameOverride ?? "profiler_log.csv");
        public int RowsWritten { get; private set; }
        public float LastFps { get; private set; }
        public float BudgetMs => Application.isEditor ? DS.EDITOR_FRAME_BUDGET_MS : 1000f / DS.TARGET_FPS;

        readonly StringBuilder _buffer = new StringBuilder();
        int _bufferedRows;
        float _t0, _acc; int _frames; float _max; int _over;
        int _lastGc;
        bool _headerChecked;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _t0 = Time.unscaledTime;
        }

        void Update()
        {
            if (!Enabled) { if (_bufferedRows > 0) Flush(); _acc = 0; _frames = 0; _max = 0; _over = 0; return; }
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            _acc += dt; _frames++; if (dt > _max) _max = dt;
            if (dt * 1000f > BudgetMs) _over++;
            if (_acc < intervalSeconds) return;
            WriteRow();
            _acc = 0; _frames = 0; _max = 0; _over = 0;
        }

        void WriteRow()
        {
            float meanMs = _acc / Mathf.Max(1, _frames) * 1000f;
            LastFps = _frames / Mathf.Max(0.0001f, _acc);
            var sm = SessionManager.Instance;
            int gc = System.GC.CollectionCount(0);
            var sb = _buffer;
            sb.Append((Time.unscaledTime - _t0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(StudyConfig.Data.participant_id).Append(',')
              .Append(sm != null ? sm.SessionId : "").Append(',')
              .Append(sm != null ? sm.Current.ToString() : "").Append(',')
              .Append(SceneFlow.Section).Append(',')
              .Append(sm != null ? sm.CurrentBlock : 0).Append(',')
              .Append(LastFps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(meanMs.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append((_max * 1000f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append((100f * _over / Mathf.Max(1, _frames)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(BudgetMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append((System.GC.GetTotalMemory(false) / 1048576f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append((UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(gc - _lastGc).AppendLine();
            _lastGc = gc;
            _bufferedRows++; RowsWritten++;
            if (_bufferedRows >= flushEveryRows) Flush();
        }

        public void Flush()
        {
            if (_bufferedRows == 0) return;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                if (!_headerChecked) { _headerChecked = true; if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + "\n"); }
                File.AppendAllText(FilePath, _buffer.ToString());
            }
            catch (System.Exception e) { DS.Warn("Profiler log write failed: " + e.Message); }
            _buffer.Clear(); _bufferedRows = 0;
        }

        void OnDisable() { Flush(); }
        void OnApplicationQuit() { Flush(); }
        void OnDestroy() { Flush(); if (Instance == this) Instance = null; }
    }
}
