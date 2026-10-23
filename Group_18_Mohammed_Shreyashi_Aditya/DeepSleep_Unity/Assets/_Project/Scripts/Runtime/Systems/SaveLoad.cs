using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    [Serializable]
    public class DebriefRecord
    {
        public string preference = "";        // Block 1 / Block 2 / No difference
        public string free_text = "";
        public string plaque = "";
        public float block1_time_s;
        public float block2_time_s;
    }

    [Serializable]
    public class SessionRecord
    {
        public string participant_id;
        public string session_id;
        public string order;
        public List<string> stage_history = new List<string>();
        public List<string> telemetry_rows = new List<string>();
        public DebriefRecord debrief = new DebriefRecord();
    }

    /// <summary>JSON and CSV export to persistentDataPath only.</summary>
    public class SaveLoad : MonoBehaviour
    {
        public static SaveLoad Instance { get; private set; }
        public DebriefRecord Debrief { get; private set; } = new DebriefRecord();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void ResetDebrief() { Debrief = new DebriefRecord(); }

        public string Export()
        {
            var tel = TelemetryLogger.Instance;
            string csv = tel != null ? tel.Flush() : "";
            var rec = new SessionRecord
            {
                participant_id = StudyConfig.Data.participant_id,
                session_id = SessionManager.Instance != null ? SessionManager.Instance.SessionId : "",
                order = ConditionRandomizer.OrderLabel(StudyConfig.ParticipantNumber),
                debrief = Debrief
            };
            if (SessionManager.Instance != null) rec.stage_history.AddRange(SessionManager.Instance.History);
            if (tel != null)
                foreach (var kv in tel.Rows)
                {
                    var cells = new List<string>();
                    foreach (var h in TelemetryLogger.Header) cells.Add(kv.Value.TryGetValue(h, out var v) ? v : "");
                    rec.telemetry_rows.Add(string.Join(",", cells));
                }
            string path = Path.Combine(Application.persistentDataPath, $"deepsleep_session_{rec.participant_id}_{rec.session_id}.json");
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(path, JsonUtility.ToJson(rec, true));
                DS.Log($"Exported session JSON {path} and CSV {csv}");
            }
            catch (Exception e) { DS.Error("Export failed: " + e.Message); }
            return path;
        }

        public static string Plaque(string id, float block1, float block2)
        {
            return $"MISSION LOG. Aethelgard, Deck 4 evacuation. Participant {id}. Block 1 {FormatTime(block1)}. Block 2 {FormatTime(block2)}.";
        }

        public static string FormatTime(float s) => $"{(int)(s / 60f)}:{(int)(s % 60f):00}";
    }
}
