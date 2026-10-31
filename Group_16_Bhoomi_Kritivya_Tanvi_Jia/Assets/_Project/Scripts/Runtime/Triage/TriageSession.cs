using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RescueSim.Triage
{
    /// <summary>One scenario casualty: a fixed identifier and its findings.</summary>
    public sealed class CasualtyCase
    {
        public readonly string id;
        public readonly VitalSigns vitals;
        public readonly string description;
        public CasualtyCase(string id, VitalSigns vitals, string description = "") { this.id = id; this.vitals = vitals; this.description = description; }
    }

    /// <summary>The raw per-casualty telemetry row. Nullable inputs are null when the trainee never examined that sign.</summary>
    public sealed class TriageRecord
    {
        public string sessionId, participantId, casualtyId, inputsExamined;
        public DataProvenance provenance;
        public PerfusionCriterion protocol;
        public TriageCategory groundTruth, assigned;
        public TriageBranch expectedBranch;
        public bool isCorrect, trueRedUnderTriaged, latencyValid, airwayRepositioned;
        public int hazardLevel;
        public double startSeconds, endSeconds, latencySeconds;
        public DateTime startUtc, endUtc;
        public bool? observedCanWalk, observedBreathing, observedBreathingAfterAirway, observedRadialPulse, observedFollowsCommands;
        public int? observedRespiratoryRate;
        public float? observedCapillaryRefill;
    }

    /// <summary>
    /// A resettable triage exercise: pure C# (injectable clock) so it can be unit-tested without Unity.
    /// Latency = time from the first selection of a casualty to its tag.
    /// </summary>
    public sealed class TriageSession
    {
        public enum SessionState { NotStarted, Running, Complete }

        static readonly Regex ParticipantPattern = new Regex(@"^(P[0-9]{2,3}|TEST)$", RegexOptions.Compiled);
        public static bool IsValidParticipantId(string id) => id != null && ParticipantPattern.IsMatch(id);

        sealed class Attempt
        {
            public double start; public DateTime startUtc;
            public readonly List<ExamKind> examined = new List<ExamKind>();
        }

        readonly Func<double> _clock;
        readonly Func<DateTime> _utcNow;
        readonly Func<int> _hazardLevel;
        readonly List<CasualtyCase> _cases;
        readonly List<TriageRecord> _records = new List<TriageRecord>();
        readonly Dictionary<string, Attempt> _attempts = new Dictionary<string, Attempt>();
        double _sessionStart;
        string _active;
        int _sequence;

        public string SessionId { get; private set; }
        public string ParticipantId { get; private set; } = "TEST";
        public DataProvenance Provenance { get; private set; } = DataProvenance.SoftwareTest;
        public PerfusionCriterion Protocol { get; }
        public SessionState State { get; private set; } = SessionState.NotStarted;
        public IReadOnlyList<CasualtyCase> Cases => _cases;
        public IReadOnlyList<TriageRecord> Records => _records;
        public string ActiveCasualtyId => _active;
        public DateTime StartedUtc { get; private set; }
        public double ElapsedSeconds => State == SessionState.NotStarted ? 0.0 : _clock() - _sessionStart;

        public TriageSession(IEnumerable<CasualtyCase> cases, PerfusionCriterion protocol,
            Func<double> clock, Func<DateTime> utcNow = null, Func<int> hazardLevel = null)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _cases = new List<CasualtyCase>(cases);
            var seen = new HashSet<string>();
            foreach (var c in _cases)
                if (c == null || string.IsNullOrEmpty(c.id) || c.vitals == null || !seen.Add(c.id))
                    throw new ArgumentException("Casualty ids must be unique and non-empty, and vitals must be set.");
            Protocol = protocol; _clock = clock; _utcNow = utcNow ?? (() => DateTime.UtcNow); _hazardLevel = hazardLevel ?? (() => 0);
            SessionId = NewSessionId();
        }

        string NewSessionId() => "S" + _utcNow().ToString("yyyyMMdd'T'HHmmss") + "-" + (++_sequence).ToString("00");

        public bool SetParticipant(string id, DataProvenance provenance)
        {
            if (State == SessionState.Running) return false;
            if (!IsValidParticipantId(id)) return false;
            ParticipantId = id; Provenance = provenance;
            return true;
        }

        public bool Start()
        {
            if (State == SessionState.Running) return false;
            if (_cases.Count == 0) return false;
            _records.Clear(); _attempts.Clear(); _active = null;
            _sessionStart = _clock(); StartedUtc = _utcNow();
            State = SessionState.Running;
            return true;
        }

        /// <summary>Clears all data and starts a fresh, un-started session with a new id (participant settings are kept).</summary>
        public void Reset()
        {
            _records.Clear(); _attempts.Clear(); _active = null;
            State = SessionState.NotStarted;
            SessionId = NewSessionId();
        }

        public CasualtyCase Find(string id) => _cases.Find(c => c.id == id);
        public bool IsTagged(string id) => _records.Exists(r => r.casualtyId == id);
        public int TaggedCount => _records.Count;

        public bool BeginAssessment(string id)
        {
            if (State != SessionState.Running || Find(id) == null || IsTagged(id)) return false;
            if (!_attempts.ContainsKey(id)) _attempts[id] = new Attempt { start = _clock(), startUtc = _utcNow() };
            _active = id;
            return true;
        }

        /// <summary>Performs an examination on the active casualty. Returns the text to show, or null if there is no active casualty.</summary>
        public string Examine(ExamKind kind)
        {
            if (State != SessionState.Running || _active == null) return null;
            var a = _attempts[_active];
            if (!a.examined.Contains(kind)) a.examined.Add(kind);
            return STARTEvaluator.Describe(kind, Find(_active).vitals, Protocol);
        }

        public bool HasExamined(string id, ExamKind kind) => _attempts.TryGetValue(id, out var a) && a.examined.Contains(kind);

        /// <summary>Tags the active casualty. Tags are final: re-tagging is rejected.</summary>
        public TriageRecord Assign(TriageCategory category)
        {
            if (State != SessionState.Running || _active == null) return null;
            if (category == TriageCategory.None) return null;
            var c = Find(_active); var a = _attempts[_active];
            double end = _clock(); DateTime endUtc = _utcNow();
            var truth = STARTEvaluator.Evaluate(c.vitals, Protocol, true);
            var v = c.vitals;
            double latency = end - a.start;
            var r = new TriageRecord
            {
                sessionId = SessionId, participantId = ParticipantId, provenance = Provenance, protocol = Protocol,
                casualtyId = c.id, groundTruth = truth.category, expectedBranch = truth.branch, assigned = category,
                isCorrect = category == truth.category,
                trueRedUnderTriaged = truth.category == TriageCategory.Red && (category == TriageCategory.Yellow || category == TriageCategory.Green),
                startSeconds = a.start - _sessionStart, endSeconds = end - _sessionStart, startUtc = a.startUtc, endUtc = endUtc,
                latencyValid = latency >= 0.0, latencySeconds = latency >= 0.0 ? latency : 0.0,
                hazardLevel = _hazardLevel(),
                inputsExamined = string.Join("|", a.examined.ConvertAll(k => k.ToString())),
                airwayRepositioned = a.examined.Contains(ExamKind.Airway)
            };
            if (a.examined.Contains(ExamKind.Walking)) r.observedCanWalk = v.canWalk;
            if (a.examined.Contains(ExamKind.Breathing)) r.observedBreathing = v.isBreathing;
            if (a.examined.Contains(ExamKind.Airway)) r.observedBreathingAfterAirway = v.isBreathing || v.breathingAfterAirwayReposition;
            if (a.examined.Contains(ExamKind.RespiratoryRate)) r.observedRespiratoryRate = v.isBreathing ? v.respiratoryRate : 0;
            if (a.examined.Contains(ExamKind.Perfusion))
            {
                if (Protocol == PerfusionCriterion.CapillaryRefill) r.observedCapillaryRefill = v.capillaryRefillSeconds;
                else r.observedRadialPulse = v.hasRadialPulse;
            }
            if (a.examined.Contains(ExamKind.MentalStatus)) r.observedFollowsCommands = v.followsCommands;
            _records.Add(r);
            _attempts.Remove(_active);
            _active = null;
            if (_records.Count == _cases.Count) State = SessionState.Complete;
            return r;
        }
    }
}
