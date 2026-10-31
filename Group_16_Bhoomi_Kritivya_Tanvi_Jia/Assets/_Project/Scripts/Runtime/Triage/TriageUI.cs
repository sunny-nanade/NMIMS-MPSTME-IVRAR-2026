using System.Globalization;
using RescueSim.Core;
using RescueSim.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RescueSim.Triage
{
    /// <summary>World-space UI for the triage exercise: start panel, assessment panel (examinations + four tags) and results panel.</summary>
    public class TriageUI : MonoBehaviour
    {
        [SerializeField] STARTTriageSimulationManager manager;
        [SerializeField] Transform head;
        [SerializeField] SceneTransitions transitions;
        [SerializeField] VRPanel startPanel, assessPanel, resultsPanel;
        [Header("Start panel")]
        [SerializeField] Button beginButton, participantButton, provenanceButton;
        [SerializeField] TMP_Text participantLabel, provenanceLabel, startMessage;
        [SerializeField] TMP_Text protocolText;
        [SerializeField] Toggle feedbackToggle;
        [Header("Assessment panel")]
        [SerializeField] TMP_Text headerText, descriptionText, feedbackText, progressText;
        [SerializeField] Button[] examButtons = new Button[6];
        [SerializeField] TMP_Text[] examResults = new TMP_Text[6];
        [SerializeField] Button[] tagButtons = new Button[4];   // Green, Yellow, Red, Black
        [Header("Results panel")]
        [SerializeField] TMP_Text summaryText, pathText;
        [SerializeField] Button restartButton, menuButton;

        int _participantIndex;           // 0 = TEST, 1..20 = P01..P20
        DataProvenance _provenance = DataProvenance.SoftwareTest;
        const int ParticipantMax = 20;

        string ParticipantId => _participantIndex == 0 ? "TEST" : "P" + _participantIndex.ToString("00", CultureInfo.InvariantCulture);

        public bool IsStartVisible => startPanel != null && startPanel.IsVisible;
        public bool IsAssessVisible => assessPanel != null && assessPanel.IsVisible;
        public bool IsResultsVisible => resultsPanel != null && resultsPanel.IsVisible;
        public string FeedbackMessage => feedbackText != null ? feedbackText.text : "";
        public string SummaryMessage => summaryText != null ? summaryText.text : "";

        void OnEnable()
        {
            beginButton?.onClick.AddListener(OnBegin);
            participantButton?.onClick.AddListener(CycleParticipant);
            provenanceButton?.onClick.AddListener(CycleProvenance);
            feedbackToggle?.onValueChanged.AddListener(OnFeedbackToggled);
            for (int i = 0; i < examButtons.Length; i++) { int k = i; examButtons[i]?.onClick.AddListener(() => manager.Examine((ExamKind)k)); }
            for (int i = 0; i < tagButtons.Length; i++) { int c = i + 1; tagButtons[i]?.onClick.AddListener(() => manager.Assign((TriageCategory)c)); }
            restartButton?.onClick.AddListener(OnRestart);
            menuButton?.onClick.AddListener(OnMenu);
            if (manager == null) return;
            manager.SessionStarted += OnSessionStarted; manager.SessionReset += OnSessionReset;
            manager.CasualtySelected += OnSelected; manager.ExamPerformed += OnExam;
            manager.CasualtyTagged += OnTagged; manager.SessionCompleted += OnCompleted; manager.Message += OnMessage;
        }

        void OnDisable()
        {
            beginButton?.onClick.RemoveAllListeners(); participantButton?.onClick.RemoveAllListeners(); provenanceButton?.onClick.RemoveAllListeners();
            feedbackToggle?.onValueChanged.RemoveAllListeners();
            foreach (var b in examButtons) b?.onClick.RemoveAllListeners();
            foreach (var b in tagButtons) b?.onClick.RemoveAllListeners();
            restartButton?.onClick.RemoveAllListeners(); menuButton?.onClick.RemoveAllListeners();
            if (manager == null) return;
            manager.SessionStarted -= OnSessionStarted; manager.SessionReset -= OnSessionReset;
            manager.CasualtySelected -= OnSelected; manager.ExamPerformed -= OnExam;
            manager.CasualtyTagged -= OnTagged; manager.SessionCompleted -= OnCompleted; manager.Message -= OnMessage;
        }

        void Start()
        {
            feedbackToggle?.SetIsOnWithoutNotify(manager != null && manager.ShowCorrectness);
            if (manager != null)
            {
                for (int i = 0; i < examButtons.Length; i++)
                {
                    var t = examButtons[i] != null ? examButtons[i].GetComponentInChildren<TMP_Text>() : null;
                    if (t != null) t.text = STARTEvaluator.Label((ExamKind)i, manager.Perfusion);
                }
                if (protocolText != null)
                    protocolText.text = "Protocol: START adult, perfusion check = " + (manager.Perfusion == PerfusionCriterion.CapillaryRefill ? "capillary refill > 2 s" : "radial pulse") + ". Training exercise, not clinically validated.";
            }
            ShowStart();
        }

        void Update()
        {
            if (manager == null || !manager.IsRunning || progressText == null) return;
            var s = manager.Session;
            int sec = Mathf.FloorToInt((float)s.ElapsedSeconds);
            progressText.text = $"Tagged {s.TaggedCount}/{s.Cases.Count}    Time {sec / 60:00}:{sec % 60:00}    Hazard level {manager.HazardLevel}";
        }

        // ---- panels ----
        void ShowStart()
        {
            startPanel?.ShowInFrontOf(head); assessPanel?.Hide(); resultsPanel?.Hide();
            RefreshStartLabels();
            if (startMessage != null) startMessage.text = "";
        }

        void RefreshStartLabels()
        {
            if (participantLabel != null) participantLabel.text = "Participant: " + ParticipantId;
            if (provenanceLabel != null) provenanceLabel.text = "Data source: " + _provenance;
        }

        void CycleParticipant() { _participantIndex = (_participantIndex + 1) % (ParticipantMax + 1); RefreshStartLabels(); }
        void CycleProvenance() { _provenance = (DataProvenance)(((int)_provenance + 1) % 3); RefreshStartLabels(); }
        void OnFeedbackToggled(bool on) { if (manager != null) manager.ShowCorrectness = on; }

        void OnBegin()
        {
            if (!manager.SetParticipant(ParticipantId, _provenance)) return;
            manager.BeginSession();
        }

        void OnRestart() { manager.Restart(); }
        void OnMenu() { if (transitions != null) transitions.Load(StageCatalog.MenuScene); }

        void OnSessionStarted()
        {
            startPanel?.Hide(); resultsPanel?.Hide();
            ClearExamTexts();
            if (headerText != null) headerText.text = "Select a casualty";
            if (descriptionText != null) descriptionText.text = "Point at a casualty and press the trigger (or press Tab on the keyboard).";
            if (feedbackText != null) feedbackText.text = "";
            assessPanel?.ShowInFrontOf(head);
        }

        void OnSessionReset() { ShowStart(); }

        void ClearExamTexts() { foreach (var t in examResults) if (t != null) t.text = "-"; }

        void OnSelected(CasualtyProfile c)
        {
            if (headerText != null) headerText.text = "Assessing " + c.Id;
            if (descriptionText != null) descriptionText.text = "Run the START checks below, then assign a tag. Findings appear only after you check them.";
            ClearExamTexts();
            var s = manager.Session;
            for (int i = 0; i < examResults.Length; i++)
                if (examResults[i] != null && s.HasExamined(c.Id, (ExamKind)i))
                    examResults[i].text = STARTEvaluator.Describe((ExamKind)i, c.Vitals, manager.Perfusion);
            assessPanel?.ShowInFrontOf(head);
        }

        void OnExam(ExamKind kind, string text) { int i = (int)kind; if (i < examResults.Length && examResults[i] != null) examResults[i].text = text; }

        void OnTagged(TriageRecord rec, string feedback)
        {
            if (feedbackText != null) { feedbackText.text = feedback; feedbackText.color = !manager.ShowCorrectness ? UIFactory.TextMain : rec.isCorrect ? UIFactory.Good : UIFactory.Bad; }
            if (headerText != null) headerText.text = "Select the next casualty";
            if (descriptionText != null) descriptionText.text = "";
            ClearExamTexts();
        }

        void OnMessage(string m)
        {
            if (IsStartVisible && startMessage != null) { startMessage.text = m; return; }
            if (feedbackText != null) { feedbackText.text = m; feedbackText.color = UIFactory.TextDim; }
        }

        void OnCompleted(TriageSummary s)
        {
            assessPanel?.Hide();
            if (summaryText != null) summaryText.text = FormatSummary(manager.Session, s);
            if (pathText != null)
            {
                var l = manager.Logger;
                pathText.text = l == null ? "Telemetry logger not connected." :
                    string.IsNullOrEmpty(l.LastError) ? "Telemetry saved:\n" + l.LastRecordsPath : "Telemetry export failed: " + l.LastError;
            }
            resultsPanel?.ShowInFrontOf(head);
        }

        public static string FormatSummary(TriageSession session, TriageSummary s)
        {
            var ci = CultureInfo.InvariantCulture;
            string acc = s.hasAccuracy ? $"{s.accuracy * 100:0}%  ({s.correct}/{s.classified})" : "n/a";
            string under = s.hasUnderTriageRate ? $"{s.underTriageRate * 100:0}%  ({s.trueRedUnderTriaged}/{s.trueRedTotal} true Red)" : "n/a (no true Red casualties)";
            string lat = s.hasLatency ? $"{s.meanLatency.ToString("0.0", ci)} s mean   (median {s.medianLatency.ToString("0.0", ci)}, range {s.minLatency.ToString("0.0", ci)}-{s.maxLatency.ToString("0.0", ci)})" : "n/a";
            string perf = session.Protocol == PerfusionCriterion.CapillaryRefill ? "capillary refill > 2 s" : "radial pulse";
            return $"<b>Tagged</b>  {s.classified}/{s.total}\n<b>Accuracy</b>  {acc}\n<b>Under-triage</b>  {under}\n<b>Assessment time</b>  {lat}\n\n" +
                   $"Protocol: START, perfusion = {perf}\nSource: {session.Provenance}   Participant: {session.ParticipantId}\nSession: {session.SessionId}";
        }
    }
}
