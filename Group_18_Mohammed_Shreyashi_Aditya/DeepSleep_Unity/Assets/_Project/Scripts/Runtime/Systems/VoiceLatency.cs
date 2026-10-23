using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Trigger-to-onset latency of every R / H / beacon voice line, on the audio clock (AudioSettings.dspTime).
    /// trigger = the moment gameplay asked for the line (VoiceBus.Request or the beacon timer), play = Play() was called,
    /// onset = first audible sample = play + output buffer latency + the part of the file's leading silence that was not skipped.
    /// queue_wait is the share of the delay spent behind a line that was already talking (not a latency bug).
    /// </summary>
    public static class VoiceLatency
    {
        public struct Row
        {
            public string id, scene, blockedBy;
            public double triggerDsp, playDsp, onsetDsp;
            public float leadSkipped, leadFile;
            public double queueWait; // time spent behind another voice line that was already talking
            public double EffectiveOnset => onsetDsp - triggerDsp - queueWait;
            public double TriggerToPlay => playDsp - triggerDsp;
            public double TriggerToOnset => onsetDsp - triggerDsp;
        }

        public static readonly List<Row> Rows = new List<Row>();
        static readonly Dictionary<string, (double dsp, string blockedBy)> Pending = new Dictionary<string, (double, string)>();

        public static void Reset() { Rows.Clear(); Pending.Clear(); LastEndDsp = 0; }

        /// <summary>Audio-clock time at which the last voice line (Rose comms, HALCYON or guidance) stopped.</summary>
        public static double LastEndDsp { get; private set; }
        public static void NoteEnded() { LastEndDsp = AudioSettings.dspTime; }

        public static void Triggered(string id, string blockedBy = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!Pending.ContainsKey(id)) Pending[id] = (AudioSettings.dspTime, blockedBy);
        }

        public static void Dropped(string id) { if (id != null) Pending.Remove(id); }

        /// <summary>Output latency of the audio device in seconds (DSP buffer length times buffer count).</summary>
        public static double OutputLatency
        {
            get { AudioSettings.GetDSPBufferSize(out int len, out int num); int sr = AudioSettings.outputSampleRate; return sr > 0 ? (double)len * num / sr : 0.0; }
        }

        public static void Played(string id, float leadSkipped)
        {
            float leadFile = leadSkipped > 0f ? leadSkipped + 0.02f : 0f; // LeadSilence keeps a 20 ms pad
            double now = AudioSettings.dspTime;
            double trig = now; string by = null;
            if (Pending.TryGetValue(id, out var p)) { trig = p.dsp; by = p.blockedBy; Pending.Remove(id); }
            double wait = by != null ? System.Math.Max(0.0, System.Math.Min(now, LastEndDsp) - trig) : 0.0;
            Rows.Add(new Row
            {
                queueWait = wait,
                id = id, scene = SceneFlow.Section, blockedBy = by,
                triggerDsp = trig, playDsp = now, onsetDsp = now + OutputLatency + System.Math.Max(0f, leadFile - leadSkipped),
                leadFile = leadFile, leadSkipped = leadSkipped,
            });
        }

        public static string ToCsv()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("id,scene,trigger_to_play_s,trigger_to_onset_s,queue_wait_s,onset_after_queue_s,queue_blocked_by,lead_in_file_s,lead_skipped_s\n");
            foreach (var r in Rows)
                sb.Append(r.id).Append(',').Append(r.scene).Append(',').Append(r.TriggerToPlay.ToString("0.000", inv)).Append(',').Append(r.TriggerToOnset.ToString("0.000", inv)).Append(',')
                  .Append(r.queueWait.ToString("0.000", inv)).Append(',').Append(r.EffectiveOnset.ToString("0.000", inv)).Append(',').Append(r.blockedBy ?? "").Append(',').Append(r.leadFile.ToString("0.000", inv)).Append(',').Append(r.leadSkipped.ToString("0.000", inv)).Append('\n');
            return sb.ToString();
        }
    }
}
