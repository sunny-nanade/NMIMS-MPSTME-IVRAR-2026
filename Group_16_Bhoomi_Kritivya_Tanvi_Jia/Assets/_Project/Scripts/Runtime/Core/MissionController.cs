using System;
using System.Collections.Generic;
using RescueSim.Hazards;
using RescueSim.Player;
using RescueSim.Survivors;
using UnityEngine;

namespace RescueSim.Core
{
    /// <summary>
    /// Owns the mission lifecycle: briefing, active play, pause, completion and failure.
    /// Hazards and survivors are wired in through serialized references (no singletons).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MissionController : MonoBehaviour
    {
        [SerializeField] MissionDefinition mission;
        [SerializeField] PlayerVitals vitals;
        [SerializeField] List<Hazard> hazards = new List<Hazard>();
        [SerializeField] List<Survivor> survivors = new List<Survivor>();

        readonly HashSet<Hazard> _neutralized = new HashSet<Hazard>();
        MissionState _statePausedFrom;

        public MissionDefinition Mission => mission;
        public MissionState State { get; private set; } = MissionState.Briefing;
        public ObjectiveTracker Objectives { get; private set; }
        public float TimeRemaining { get; private set; }
        public int Mistakes { get; private set; }
        public int SurvivorsTotal => survivors.Count;
        public int SurvivorsFound { get; private set; }
        public int SurvivorsRescued { get; private set; }
        public int SurvivorsLost { get; private set; }
        public int HazardsTotal => hazards.Count;
        public int HazardsNeutralized => _neutralized.Count;
        public ScoreResult? LastResult { get; private set; }
        public string FailureReason { get; private set; }
        public PlayerVitals Vitals => vitals;
        public IReadOnlyList<Hazard> Hazards => hazards;
        public IReadOnlyList<Survivor> Survivors => survivors;

        public event Action<MissionState> StateChanged;
        public event Action<string, FeedbackKind> Feedback;
        public event Action<ScoreResult> ResultsReady;

        void Awake()
        {
            if (mission == null)
            {
                Debug.LogError("[MissionController] No MissionDefinition assigned.", this);
                enabled = false;
                return;
            }
            Objectives = new ObjectiveTracker(mission.Objectives);
            TimeRemaining = mission.timeLimitSeconds;
            Time.timeScale = 1f;
        }

        void OnEnable()
        {
            if (Objectives == null) return;
            foreach (var h in hazards) if (h != null) h.Neutralized += OnHazardNeutralized;
            foreach (var s in survivors)
            {
                if (s == null) continue;
                s.Found += OnSurvivorFound;
                s.Rescued += OnSurvivorRescued;
                s.Lost += OnSurvivorLost;
                s.CarryStarted += OnCarryStarted;
                s.CarryEnded += OnCarryEnded;
            }
            if (vitals != null) vitals.Overcome += OnPlayerOvercome;
        }

        void OnDisable()
        {
            foreach (var h in hazards) if (h != null) h.Neutralized -= OnHazardNeutralized;
            foreach (var s in survivors)
            {
                if (s == null) continue;
                s.Found -= OnSurvivorFound;
                s.Rescued -= OnSurvivorRescued;
                s.Lost -= OnSurvivorLost;
                s.CarryStarted -= OnCarryStarted;
                s.CarryEnded -= OnCarryEnded;
            }
            if (vitals != null) vitals.Overcome -= OnPlayerOvercome;
        }

        void Start()
        {
            UpdateSurvivorProgress();
            SetState(MissionState.Briefing);
        }

        void Update()
        {
            if (State != MissionState.Active) return;
            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            if (TimeRemaining <= 0f) Fail("Time ran out before everyone was evacuated.");
        }

        // ---- Setup helpers (used by tests and the scene builder) -----------------------------

        public void Configure(MissionDefinition def, PlayerVitals v, List<Hazard> hz, List<Survivor> sv)
        {
            mission = def; vitals = v; hazards = hz ?? new List<Hazard>(); survivors = sv ?? new List<Survivor>();
        }

        // ---- Lifecycle -----------------------------------------------------------------------

        public void BeginMission()
        {
            if (State != MissionState.Briefing) return;
            if (vitals != null) { vitals.ResetVitals(); vitals.RecoveryPerSecond = mission.exposureRecoveryPerSecond; vitals.IsTracking = true; }
            SetState(MissionState.Active);
            RaiseFeedback(mission.usesExposure ? "Mission started. Work the checklist on your wrist." : "Mission started. Follow the glowing markers.", FeedbackKind.Info);
        }

        public void Pause()
        {
            if (State != MissionState.Active) return;
            _statePausedFrom = State;
            Time.timeScale = 0f;
            SetState(MissionState.Paused);
        }

        public void Resume()
        {
            if (State != MissionState.Paused) return;
            Time.timeScale = 1f;
            SetState(_statePausedFrom);
        }

        public void ReportMistake(string reason)
        {
            if (State != MissionState.Active) return;
            Mistakes++;
            RaiseFeedback(reason, FeedbackKind.Mistake);
        }

        public void Complete()
        {
            if (State != MissionState.Active) return;
            Finish(true, null);
        }

        public void Fail(string reason)
        {
            if (State != MissionState.Active) return;
            Finish(false, reason);
        }

        public ScoreResult BuildScore(bool completed)
        {
            var input = new ScoreInput
            {
                completed = completed,
                timeRemaining = TimeRemaining,
                timeLimit = mission.timeLimitSeconds,
                survivorsRescued = SurvivorsRescued,
                survivorsTotal = SurvivorsTotal,
                hazardsNeutralized = HazardsNeutralized,
                hazardsTotal = HazardsTotal,
                peakExposure = vitals != null ? vitals.PeakExposure : 0f,
                mistakes = Mistakes
            };
            return ScoreCalculator.Calculate(input, mission.scoring);
        }

        void Finish(bool completed, string reason)
        {
            FailureReason = reason;
            if (vitals != null) vitals.IsTracking = false;
            LastResult = BuildScore(completed);
            Time.timeScale = 1f;
            SetState(completed ? MissionState.Completed : MissionState.Failed);
            RaiseFeedback(completed ? "Mission complete." : (reason ?? "Mission failed."), completed ? FeedbackKind.Success : FeedbackKind.Warning);
            ResultsReady?.Invoke(LastResult.Value);
        }

        void SetState(MissionState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        void RaiseFeedback(string message, FeedbackKind kind) => Feedback?.Invoke(message, kind);

        // ---- Hazards -------------------------------------------------------------------------

        void OnHazardNeutralized(Hazard h)
        {
            if (!_neutralized.Add(h)) return;
            if (!string.IsNullOrEmpty(h.ObjectiveId) && AllHazardsForObjectiveDone(h.ObjectiveId))
                Objectives.Complete(h.ObjectiveId);
            RaiseFeedback($"{h.DisplayName} neutralised.", FeedbackKind.Success);
            CheckCompletion();
        }

        bool AllHazardsForObjectiveDone(string objectiveId)
        {
            foreach (var other in hazards)
                if (other != null && other.ObjectiveId == objectiveId && other.IsActive) return false;
            return true;
        }

        /// <summary>Called by non-hazard systems (e.g. the passage clearer) to complete an objective.</summary>
        public void CompleteObjective(string id, string message = null)
        {
            if (State != MissionState.Active) return;
            if (Objectives.Complete(id) && !string.IsNullOrEmpty(message))
                RaiseFeedback(message, FeedbackKind.Success);
            CheckCompletion();
        }

        // ---- Survivors -----------------------------------------------------------------------

        void OnSurvivorFound(Survivor s)
        {
            SurvivorsFound++;
            UpdateSurvivorProgress();
            RaiseFeedback($"Survivor located ({SurvivorsFound}/{SurvivorsTotal}). Hold GRIP on them to check, keep holding to lift, then carry them to the safe zone.", FeedbackKind.Info);
            if (SurvivorsFound >= SurvivorsTotal) Objectives.Complete(ObjectiveIds.Locate);
        }

        void OnSurvivorRescued(Survivor s)
        {
            SurvivorsRescued++;
            UpdateSurvivorProgress();
            RaiseFeedback($"{s.Name} delivered to safety - evacuated {SurvivorsRescued}/{SurvivorsTotal}.", FeedbackKind.Success);
            CheckCompletion();
        }

        void OnCarryStarted(Survivor s) =>
            RaiseFeedback($"Carrying {s.Name}. Take them to the safe zone and release GRIP there.", FeedbackKind.Info);

        void OnCarryEnded(Survivor s, bool delivered)
        {
            // Delivery itself is reported by OnSurvivorRescued (counted once); a drop elsewhere earns nothing.
            if (!delivered && s.State == SurvivorState.Released)
                RaiseFeedback($"{s.Name} put down outside the safe zone. Pick them up again and carry them in.", FeedbackKind.Warning);
        }

        void OnSurvivorLost(Survivor s)
        {
            SurvivorsLost++;
            UpdateSurvivorProgress();
            RaiseFeedback("A survivor succumbed to the conditions. Keep them out of smoke and fumes.", FeedbackKind.Warning);
            CheckCompletion();
        }

        void UpdateSurvivorProgress()
        {
            if (Objectives == null) return;
            Objectives.SetProgress(ObjectiveIds.Locate, $"{SurvivorsFound}/{SurvivorsTotal}");
            Objectives.SetProgress(ObjectiveIds.Evacuate, $"{SurvivorsRescued}/{SurvivorsTotal}");
        }

        void OnPlayerOvercome() => Fail("You were overcome by smoke and fumes. Isolate hazards and avoid hazard zones.");

        // ---- Completion ----------------------------------------------------------------------

        void CheckCompletion()
        {
            if (State != MissionState.Active) return;
            bool allResolved = SurvivorsTotal > 0 && SurvivorsRescued + SurvivorsLost >= SurvivorsTotal;
            if (allResolved)
            {
                if (SurvivorsRescued > 0) Objectives.Complete(ObjectiveIds.Evacuate);
                else Objectives.Fail(ObjectiveIds.Evacuate);
            }
            if (allResolved && SurvivorsRescued > 0 && RequiredHazardObjectivesDone())
                Complete();
        }

        bool RequiredHazardObjectivesDone()
        {
            foreach (var o in Objectives.Objectives)
            {
                if (!o.Required) continue;
                if (o.Id == ObjectiveIds.Evacuate || o.Id == ObjectiveIds.Locate) continue;
                if (o.Status != ObjectiveStatus.Complete) return false;
            }
            return true;
        }
    }
}
