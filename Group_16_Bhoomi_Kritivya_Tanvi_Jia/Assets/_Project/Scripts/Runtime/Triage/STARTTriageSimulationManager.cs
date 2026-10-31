using System;
using System.Collections.Generic;
using UnityEngine;

namespace RescueSim.Triage
{
    /// <summary>
    /// Central coordinator of the START triage exercise: owns the TriageSession, receives selections / examinations / tags
    /// from the XR UI or desktop input, updates casualty visuals, raises events for the UI and exports telemetry on completion.
    /// Educational prototype: not clinically validated.
    /// </summary>
    public class STARTTriageSimulationManager : MonoBehaviour
    {
        [SerializeField] List<CasualtyProfile> casualties = new List<CasualtyProfile>();
        [SerializeField] Transform head;
        [SerializeField] PerfusionCriterion perfusion = PerfusionCriterion.CapillaryRefill;
        [SerializeField] TriageTelemetryLogger logger;
        [SerializeField] TriageHazardEscalation hazard;
        [SerializeField] bool showCorrectness = true;
        [Tooltip("The player must be within this many metres of a casualty to select them. 0 disables the check.")]
        [SerializeField, Min(0f)] float maxSelectDistance = 8f;

        public event Action SessionStarted;
        public event Action SessionReset;
        public event Action<CasualtyProfile> CasualtySelected;
        public event Action<ExamKind, string> ExamPerformed;
        public event Action<TriageRecord, string> CasualtyTagged;
        public event Action<TriageSummary> SessionCompleted;
        public event Action<string> Message;

        TriageSession _session;
        CasualtyProfile _active;

        public TriageSession Session { get { EnsureSession(); return _session; } }
        public TriageSummary Summary { get; private set; }
        public CasualtyProfile Active => _active;
        public IReadOnlyList<CasualtyProfile> Casualties => casualties;
        public PerfusionCriterion Perfusion => perfusion;
        public TriageTelemetryLogger Logger => logger;
        public int HazardLevel => hazard != null ? hazard.Level : 0;
        public bool ShowCorrectness { get => showCorrectness; set => showCorrectness = value; }
        public bool IsRunning => _session != null && _session.State == TriageSession.SessionState.Running;
        public bool IsComplete => _session != null && _session.State == TriageSession.SessionState.Complete;

        public void Configure(List<CasualtyProfile> list, Transform headTransform, TriageTelemetryLogger log, TriageHazardEscalation haz, PerfusionCriterion criterion)
        {
            casualties = list; head = headTransform; logger = log; hazard = haz; perfusion = criterion; _session = null; EnsureSession();
        }

        void Awake() { EnsureSession(); }

        void EnsureSession()
        {
            if (_session != null || casualties == null || casualties.Count == 0) return;
            var cases = new List<CasualtyCase>();
            foreach (var c in casualties) if (c != null) cases.Add(c.ToCase());
            _session = new TriageSession(cases, perfusion, () => Time.realtimeSinceStartupAsDouble, null, () => HazardLevel);
        }

        CasualtyProfile Profile(string id) => casualties.Find(c => c != null && c.Id == id);

        void Say(string m) { Message?.Invoke(m); }

        public bool SetParticipant(string participantId, DataProvenance provenance)
        {
            EnsureSession();
            if (_session == null) return false;
            if (provenance != DataProvenance.SoftwareTest && participantId == "TEST") { Say("Choose a participant id (P01...) for pilot or human data."); return false; }
            if (!_session.SetParticipant(participantId, provenance)) { Say("Invalid participant id."); return false; }
            return true;
        }

        public bool BeginSession()
        {
            EnsureSession();
            if (_session == null) { Say("No casualties in the scene."); return false; }
            if (!_session.Start()) return false;
            Summary = null; _active = null;
            foreach (var c in casualties) c?.ResetVisual();
            hazard?.BeginRun();
            SessionStarted?.Invoke();
            Say("Session " + _session.SessionId + " started. Select a casualty.");
            return true;
        }

        public bool SelectCasualty(CasualtyProfile c)
        {
            EnsureSession();
            if (c == null || _session == null) return false;
            if (!IsRunning) { Say("Press Begin to start the exercise."); return false; }
            if (_session.IsTagged(c.Id)) { Say(c.Id + " is already tagged."); return false; }
            if (head != null && maxSelectDistance > 0f && Vector3.Distance(head.position, c.transform.position) > maxSelectDistance)
            { Say("Move closer to " + c.Id + " to examine them."); return false; }
            if (!_session.BeginAssessment(c.Id)) return false;
            if (_active != null) _active.SetSelected(false);
            _active = c; c.SetSelected(true);
            CasualtySelected?.Invoke(c);
            return true;
        }

        public bool SelectCasualtyById(string id) { var p = Profile(id); return p != null && SelectCasualty(p); }

        /// <summary>Selects the next untagged casualty after the current one (desktop shortcut).</summary>
        public bool SelectNext()
        {
            if (casualties.Count == 0) return false;
            int start = _active != null ? casualties.IndexOf(_active) : -1;
            for (int i = 1; i <= casualties.Count; i++)
            {
                var c = casualties[(start + i + casualties.Count) % casualties.Count];
                if (c != null && _session != null && !_session.IsTagged(c.Id)) return SelectCasualty(c);
            }
            return false;
        }

        public string Examine(ExamKind kind)
        {
            EnsureSession();
            string text = _session?.Examine(kind);
            if (text == null) { Say("Select a casualty first."); return null; }
            ExamPerformed?.Invoke(kind, text);
            return text;
        }

        public TriageRecord Assign(TriageCategory category)
        {
            EnsureSession();
            if (_session == null || _active == null) { Say("Select a casualty first."); return null; }
            var profile = _active;
            var rec = _session.Assign(category);
            if (rec == null) return null;
            profile.SetTag(category); profile.SetSelected(false); _active = null;
            string reason = STARTEvaluator.Evaluate(profile.Vitals, perfusion, true).reason;
            string fb = !showCorrectness
                ? profile.Id + " tagged " + category + "."
                : rec.isCorrect
                    ? profile.Id + ": " + category + " is correct. " + reason
                    : profile.Id + ": tagged " + category + ", START says " + rec.groundTruth + ". " + reason;
            CasualtyTagged?.Invoke(rec, fb);
            if (_session.State == TriageSession.SessionState.Complete) Finish();
            return rec;
        }

        void Finish()
        {
            hazard?.StopRun();
            Summary = TriageMetrics.Compute(_session.Records, _session.Cases.Count);
            logger?.Export(_session);
            SessionCompleted?.Invoke(Summary);
        }

        /// <summary>Exports a partly finished session (session_complete = false) so no data is lost, then clears everything.</summary>
        public void Restart()
        {
            EnsureSession();
            if (_session == null) return;
            if (IsRunning && _session.TaggedCount > 0) logger?.Export(_session);
            _session.Reset();
            Summary = null; _active = null;
            foreach (var c in casualties) c?.ResetVisual();
            hazard?.ResetRun();
            SessionReset?.Invoke();
        }

        void OnApplicationQuit() { if (IsRunning && _session.TaggedCount > 0) logger?.Export(_session); }
    }
}
