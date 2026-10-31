using System.Text;
using RescueSim.Core;
using RescueSim.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RescueSim.UI
{
    /// <summary>Coordinates briefing, pause, settings, results and campaign-complete panels with the mission state.</summary>
    public class MissionUI : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] MissionController mission;
        [SerializeField] Transform head;
        [SerializeField] SceneTransitions transitions;
        [SerializeField] string menuSceneName = "MainMenu";

        [Header("Panels")]
        [SerializeField] VRPanel briefingPanel;
        [SerializeField] VRPanel pausePanel;
        [SerializeField] SettingsPanel settingsPanel;
        [SerializeField] VRPanel resultsPanel;
        [SerializeField] VRPanel campaignPanel;

        [Header("Widgets")]
        [SerializeField] TMP_Text briefingStage;
        [SerializeField] TMP_Text briefingTitle;
        [SerializeField] TMP_Text briefingBody;
        [SerializeField] Button beginButton;
        [SerializeField] Button resumeButton;
        [SerializeField] Button pauseSettingsButton;
        [SerializeField] Button pauseRestartButton;
        [SerializeField] Button pauseMenuButton;
        [SerializeField] TMP_Text resultsTitle;
        [SerializeField] TMP_Text resultsSubtitle;
        [SerializeField] TMP_Text resultsScore;
        [SerializeField] TMP_Text resultsBody;
        [SerializeField] StarRating resultsStars;
        [SerializeField] Button replayButton;
        [SerializeField] Button nextButton;
        [SerializeField] TMP_Text nextButtonLabel;
        [SerializeField] Button resultsMenuButton;
        [SerializeField] TMP_Text campaignBody;
        [SerializeField] StarRating campaignStars;
        [SerializeField] Button campaignMenuButton;
        [SerializeField] Button campaignReplayButton;

        readonly StringBuilder _sb = new StringBuilder(512);
        VRPanel _settingsReturnTo;

        public VRPanel Briefing => briefingPanel;
        public VRPanel Pause => pausePanel;
        public VRPanel Results => resultsPanel;
        public VRPanel Campaign => campaignPanel;
        public SettingsPanel Settings => settingsPanel;
        public int LastScore100 { get; private set; }
        public int LastStars { get; private set; }
        public int StageIndex => mission != null && mission.Mission != null ? mission.Mission.stageIndex : -1;
        /// <summary>True when this scene's stage index maps to a stage in the campaign.</summary>
        public bool HasValidStage => StageCatalog.IsValid(StageIndex);

        void OnEnable()
        {
            if (mission == null) return;
            mission.StateChanged += OnStateChanged;
            beginButton?.onClick.AddListener(OnBegin);
            resumeButton?.onClick.AddListener(OnResume);
            pauseSettingsButton?.onClick.AddListener(OpenSettingsFromPause);
            pauseRestartButton?.onClick.AddListener(Restart);
            pauseMenuButton?.onClick.AddListener(GoToMenu);
            replayButton?.onClick.AddListener(Restart);
            nextButton?.onClick.AddListener(OnNext);
            resultsMenuButton?.onClick.AddListener(GoToMenu);
            campaignMenuButton?.onClick.AddListener(GoToMenu);
            campaignReplayButton?.onClick.AddListener(Restart);
            settingsPanel?.BackButton?.onClick.AddListener(CloseSettings);
        }

        void OnDisable()
        {
            if (mission != null) mission.StateChanged -= OnStateChanged;
            beginButton?.onClick.RemoveListener(OnBegin);
            resumeButton?.onClick.RemoveListener(OnResume);
            pauseSettingsButton?.onClick.RemoveListener(OpenSettingsFromPause);
            pauseRestartButton?.onClick.RemoveListener(Restart);
            pauseMenuButton?.onClick.RemoveListener(GoToMenu);
            replayButton?.onClick.RemoveListener(Restart);
            nextButton?.onClick.RemoveListener(OnNext);
            resultsMenuButton?.onClick.RemoveListener(GoToMenu);
            campaignMenuButton?.onClick.RemoveListener(GoToMenu);
            campaignReplayButton?.onClick.RemoveListener(Restart);
            settingsPanel?.BackButton?.onClick.RemoveListener(CloseSettings);
        }

        void Start()
        {
            if (mission == null) return;
            if (mission.Mission != null)
            {
                if (!HasValidStage) Debug.LogWarning($"[MissionUI] Mission '{mission.Mission.title}' has stage index {StageIndex}, which is not in the campaign. Progress will not be recorded.");
                if (briefingStage != null) briefingStage.text = HasValidStage ? $"STAGE {StageIndex + 1} OF {StageCatalog.Count}" : "MISSION BRIEFING";
                if (briefingTitle != null) briefingTitle.text = mission.Mission.title.ToUpperInvariant();
                if (briefingBody != null) briefingBody.text = mission.Mission.briefing;
            }
            HideAll();
            OnStateChanged(mission.State);
        }

        void HideAll()
        {
            briefingPanel?.Hide(); pausePanel?.Hide(); settingsPanel?.Hide(); resultsPanel?.Hide(); campaignPanel?.Hide();
        }

        void OnStateChanged(MissionState state)
        {
            switch (state)
            {
                case MissionState.Briefing:
                    HideAll(); briefingPanel?.ShowInFrontOf(head); break;
                case MissionState.Active:
                    HideAll(); break;
                case MissionState.Paused:
                    HideAll(); pausePanel?.ShowInFrontOf(head); break;
                case MissionState.Completed:
                case MissionState.Failed:
                    HideAll(); FillResults(); resultsPanel?.ShowInFrontOf(head); break;
            }
        }

        void OnBegin() => mission.BeginMission();
        void OnResume() => mission.Resume();

        /// <summary>Toggles pause from the controller menu button.</summary>
        public void TogglePause()
        {
            if (mission.State == MissionState.Active) mission.Pause();
            else if (mission.State == MissionState.Paused) { settingsPanel?.Hide(); mission.Resume(); }
        }

        void OpenSettingsFromPause()
        {
            if (settingsPanel == null) return;
            _settingsReturnTo = pausePanel;
            pausePanel.Hide();
            settingsPanel.ShowInFrontOf(head);
        }

        void CloseSettings()
        {
            settingsPanel.Hide();
            _settingsReturnTo?.ShowInFrontOf(head);
        }

        public void Restart() => transitions?.ReloadCurrent();
        public void GoToMenu() => transitions?.Load(menuSceneName);

        /// <summary>Loads the following stage, or opens the campaign-complete panel after the last stage.</summary>
        public void OnNext()
        {
            if (mission.State != MissionState.Completed || !HasValidStage) return;
            int next = StageCatalog.NextAfter(StageIndex);
            if (next < 0) { ShowCampaignComplete(); return; }
            transitions?.Load(StageCatalog.SceneFor(next));
        }

        void ShowCampaignComplete()
        {
            resultsPanel?.Hide();
            if (campaignBody != null)
            {
                _sb.Clear();
                for (int i = 0; i < StageCatalog.Count; i++)
                {
                    _sb.Append("Stage ").Append(i + 1).Append("   ");
                    _sb.Append(CampaignProgress.IsCompleted(i) ? "Score " + CampaignProgress.BestScore(i) + "/100" : "not completed");
                    _sb.Append("   ").Append(CampaignProgress.BestStars(i)).Append("/3 stars\n");
                }
                _sb.Append("\nTotal stars: ").Append(CampaignProgress.TotalStars()).Append(" / ").Append(CampaignProgress.MaxStars);
                campaignBody.text = _sb.ToString();
            }
            campaignStars?.SetStars(CampaignProgress.TotalStars() >= CampaignProgress.MaxStars - 1 ? 3 : CampaignProgress.TotalStars() >= 6 ? 2 : 1);
            campaignPanel?.ShowInFrontOf(head);
        }

        void FillResults()
        {
            if (mission.LastResult == null) return;
            var r = mission.LastResult.Value;
            bool ok = mission.State == MissionState.Completed;
            int max = mission.Mission != null ? mission.Mission.scoring.maxScore : 1000;
            LastScore100 = StageScoring.ToHundred(r.total, max);
            LastStars = StageScoring.Stars(LastScore100, ok);
            if (HasValidStage) CampaignProgress.RecordResult(StageIndex, LastScore100, LastStars, ok);

            if (resultsTitle != null)
            {
                resultsTitle.text = ok ? "MISSION COMPLETE" : "MISSION FAILED";
                resultsTitle.color = ok ? UIFactory.Good : UIFactory.Bad;
            }
            if (resultsSubtitle != null) resultsSubtitle.text = mission.Mission != null ? mission.Mission.title : "";
            resultsStars?.SetStars(LastStars);
            if (resultsScore != null) resultsScore.text = $"SCORE  <b>{LastScore100}</b> / 100";

            bool hasNext = ok && HasValidStage;
            bool isFinal = HasValidStage && StageCatalog.IsFinal(StageIndex);
            if (nextButton != null) nextButton.gameObject.SetActive(hasNext);
            if (nextButtonLabel != null) nextButtonLabel.text = isFinal ? "Finish Campaign" : "Next Stage";

            if (resultsBody == null) return;
            _sb.Clear();
            if (!ok && !string.IsNullOrEmpty(mission.FailureReason)) _sb.Append(mission.FailureReason).Append("\n\n");
            _sb.Append("People evacuated   ").Append(mission.SurvivorsRescued).Append('/').Append(mission.SurvivorsTotal).Append('\n');
            if (mission.HazardsTotal > 0)
                _sb.Append("Hazards neutralised   ").Append(mission.HazardsNeutralized).Append('/').Append(mission.HazardsTotal).Append('\n');
            _sb.Append("Objectives   ").Append(mission.Objectives.CompletedCount()).Append('/').Append(mission.Objectives.Objectives.Count).Append('\n');
            _sb.Append("Time bonus   +").Append(Mathf.RoundToInt(100f * r.timeBonus / Mathf.Max(1, max))).Append('\n');
            if (mission.Mission != null && mission.Mission.usesExposure)
                _sb.Append("Exposure penalty   -").Append(Mathf.RoundToInt(100f * r.exposurePenalty / Mathf.Max(1, max))).Append('\n');
            _sb.Append("Mistakes (").Append(mission.Mistakes).Append(")   -").Append(Mathf.RoundToInt(100f * r.mistakePenalty / Mathf.Max(1, max))).Append('\n');
            resultsBody.text = _sb.ToString();
        }
    }
}
