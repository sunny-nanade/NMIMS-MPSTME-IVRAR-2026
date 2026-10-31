using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RescueSim.Triage
{
    /// <summary>CSV serialisation for telemetry: raw per-casualty records, derived summary and confusion matrix are separate files.</summary>
    public static class TriageCsv
    {
        public static readonly string[] RecordColumns =
        {
            "session_id", "data_provenance", "participant_id", "protocol_perfusion", "casualty_id",
            "ground_truth", "assigned", "is_correct", "true_red_under_triaged", "expected_branch",
            "assessment_start_utc", "assessment_end_utc", "assessment_start_s", "assessment_end_s", "assessment_latency_s", "latency_valid",
            "hazard_level", "inputs_examined", "airway_repositioned",
            "obs_can_walk", "obs_breathing", "obs_breathing_after_airway", "obs_resp_rate", "obs_cap_refill_s", "obs_radial_pulse", "obs_follows_commands"
        };

        public static readonly string[] SummaryColumns =
        {
            "session_id", "data_provenance", "participant_id", "protocol_perfusion", "casualties_total", "casualties_classified", "session_complete",
            "correct", "accuracy", "true_red_total", "true_red_under_triaged", "under_triage_rate",
            "mean_latency_s", "median_latency_s", "min_latency_s", "max_latency_s", "invalid_latency_count"
        };

        public const string NotAvailable = "NA";

        /// <summary>RFC 4180 quoting, plus a guard against spreadsheet formula injection (cells starting with = + - @).</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@') s = "'" + s;
            bool quote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!quote) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static string D(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "true" : "false";
        static string B(bool? v) => v.HasValue ? B(v.Value) : "";
        static string Iso(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        public static string Row(TriageRecord r)
        {
            var f = new[]
            {
                r.sessionId, r.provenance.ToString(), r.participantId, r.protocol.ToString(), r.casualtyId,
                r.groundTruth.ToString(), r.assigned.ToString(), B(r.isCorrect), B(r.trueRedUnderTriaged), r.expectedBranch.ToString(),
                Iso(r.startUtc), Iso(r.endUtc), D(r.startSeconds), D(r.endSeconds), D(r.latencySeconds), B(r.latencyValid),
                r.hazardLevel.ToString(CultureInfo.InvariantCulture), r.inputsExamined, B(r.airwayRepositioned),
                B(r.observedCanWalk), B(r.observedBreathing), B(r.observedBreathingAfterAirway),
                r.observedRespiratoryRate.HasValue ? r.observedRespiratoryRate.Value.ToString(CultureInfo.InvariantCulture) : "",
                r.observedCapillaryRefill.HasValue ? D(r.observedCapillaryRefill.Value) : "",
                B(r.observedRadialPulse), B(r.observedFollowsCommands)
            };
            var sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Escape(f[i])); }
            return sb.ToString();
        }

        public static string RecordsToCsv(IReadOnlyList<TriageRecord> records)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", RecordColumns)).Append('\n');
            foreach (var r in records) sb.Append(Row(r)).Append('\n');
            return sb.ToString();
        }

        public static string SummaryToCsv(TriageSession session, TriageSummary s)
        {
            var f = new[]
            {
                session.SessionId, session.Provenance.ToString(), session.ParticipantId, session.Protocol.ToString(),
                s.total.ToString(CultureInfo.InvariantCulture), s.classified.ToString(CultureInfo.InvariantCulture), B(s.isComplete),
                s.correct.ToString(CultureInfo.InvariantCulture), s.hasAccuracy ? D(s.accuracy) : NotAvailable,
                s.trueRedTotal.ToString(CultureInfo.InvariantCulture), s.trueRedUnderTriaged.ToString(CultureInfo.InvariantCulture),
                s.hasUnderTriageRate ? D(s.underTriageRate) : NotAvailable,
                s.hasLatency ? D(s.meanLatency) : NotAvailable, s.hasLatency ? D(s.medianLatency) : NotAvailable,
                s.hasLatency ? D(s.minLatency) : NotAvailable, s.hasLatency ? D(s.maxLatency) : NotAvailable,
                s.invalidLatencyCount.ToString(CultureInfo.InvariantCulture)
            };
            var sb = new StringBuilder();
            sb.Append(string.Join(",", SummaryColumns)).Append('\n');
            for (int i = 0; i < f.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Escape(f[i])); }
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>4x4 confusion matrix: rows = ground truth, columns = assigned (Green, Yellow, Red, Black).</summary>
        public static string ConfusionToCsv(TriageSummary s)
        {
            string[] names = { "Green", "Yellow", "Red", "Black" };
            var sb = new StringBuilder();
            sb.Append("ground_truth\\assigned,").Append(string.Join(",", names)).Append('\n');
            for (int i = 0; i < 4; i++)
            {
                sb.Append(names[i]);
                for (int j = 0; j < 4; j++) sb.Append(',').Append(s.confusion[i, j].ToString(CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
