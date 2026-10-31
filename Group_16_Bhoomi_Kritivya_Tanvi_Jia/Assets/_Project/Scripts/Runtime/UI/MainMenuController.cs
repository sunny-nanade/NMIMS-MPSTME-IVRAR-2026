using System;
using System.Collections.Generic;
using RescueSim.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RescueSim.UI
{
    /// <summary>One selectable stage card on the main menu.</summary>
    [Serializable]
    public class StageCardView
    {
        public MissionDefinition mission;
        public Button button;
        public TMP_Text status;
        public StarRating stars;
        public GameObject lockOverlay;
        public Image background;
    }

    /// <summary>Main menu: campaign title, stage cards (locked/unlocked), progress, controls help, settings and demo options.</summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] SceneTransitions transitions;
        [SerializeField] VRPanel mainPanel;
        [SerializeField] SettingsPanel settingsPanel;
        [SerializeField] VRPanel helpPanel;
        [SerializeField] List<StageCardView> cards = new List<StageCardView>();
        [SerializeField] Button helpButton;
        [SerializeField] Button helpBackButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] Button triageButton;
        [SerializeField] Button resetButton;
        [SerializeField] Toggle demoToggle;
        [SerializeField] TMP_Text progressText;
        [SerializeField] Image progressFill;
        [SerializeField] TMP_Text messageText;

        static readonly Color CardUnlocked = new Color(0.14f, 0.17f, 0.23f, 1f);
        static readonly Color CardLocked = new Color(0.09f, 0.1f, 0.12f, 1f);

        public IReadOnlyList<StageCardView> Cards => cards;
        public string Message => messageText != null ? messageText.text : "";

        void OnEnable()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                int stage = i;
                cards[i].button?.onClick.AddListener(() => SelectStage(stage));
            }
            helpButton?.onClick.AddListener(OpenHelp);
            helpBackButton?.onClick.AddListener(CloseHelp);
            settingsButton?.onClick.AddListener(OpenSettings);
            quitButton?.onClick.AddListener(Quit);
            triageButton?.onClick.AddListener(OpenTriage);
            resetButton?.onClick.AddListener(ResetProgress);
            demoToggle?.onValueChanged.AddListener(OnDemoToggled);
            settingsPanel?.BackButton?.onClick.AddListener(CloseSettings);
            CampaignProgress.Changed += Refresh;
        }

        void OnDisable()
        {
            foreach (var c in cards) c.button?.onClick.RemoveAllListeners();
            helpButton?.onClick.RemoveListener(OpenHelp);
            helpBackButton?.onClick.RemoveListener(CloseHelp);
            settingsButton?.onClick.RemoveListener(OpenSettings);
            quitButton?.onClick.RemoveListener(Quit);
            triageButton?.onClick.RemoveListener(OpenTriage);
            resetButton?.onClick.RemoveListener(ResetProgress);
            demoToggle?.onValueChanged.RemoveListener(OnDemoToggled);
            settingsPanel?.BackButton?.onClick.RemoveListener(CloseSettings);
            CampaignProgress.Changed -= Refresh;
        }

        void Start()
        {
            settingsPanel?.Hide();
            helpPanel?.Hide();
            demoToggle?.SetIsOnWithoutNotify(CampaignProgress.UnlockAll);
            Refresh();
            mainPanel?.ShowInFrontOf(head);
        }

        /// <summary>Updates card states, stars and the progress indicator from saved progress.</summary>
        public void Refresh()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                bool unlocked = CampaignProgress.IsUnlocked(i);
                if (c.button != null) c.button.interactable = true; // stays clickable so a locked tap can explain itself
                if (c.lockOverlay != null) c.lockOverlay.SetActive(!unlocked);
                if (c.background != null) c.background.color = unlocked ? CardUnlocked : CardLocked;
                int stars = CampaignProgress.BestStars(i);
                c.stars?.SetStars(stars);
                if (c.status != null)
                {
                    if (!unlocked) { c.status.text = $"LOCKED  -  complete Stage {i} first"; c.status.color = UIFactory.TextDim; }
                    else if (CampaignProgress.IsCompleted(i)) { c.status.text = $"BEST {CampaignProgress.BestScore(i)}/100  -  PLAY AGAIN"; c.status.color = UIFactory.Good; }
                    else { c.status.text = "SELECT TO PLAY"; c.status.color = UIFactory.Accent; }
                }
            }
            if (progressText != null)
                progressText.text = $"CAMPAIGN PROGRESS   {CampaignProgress.CompletedCount()}/{StageCatalog.Count} stages   -   {CampaignProgress.TotalStars()}/{CampaignProgress.MaxStars} stars" +
                                    (CampaignProgress.UnlockAll ? "   (demo: all stages unlocked)" : "");
            if (progressFill != null) progressFill.fillAmount = (float)CampaignProgress.CompletedCount() / StageCatalog.Count;
            demoToggle?.SetIsOnWithoutNotify(CampaignProgress.UnlockAll);
        }

        /// <summary>Loads the stage if it is unlocked. Returns false (and explains why) for locked or invalid stages.</summary>
        public bool SelectStage(int stage)
        {
            if (!StageCatalog.IsValid(stage)) { Debug.LogError($"[MainMenu] Invalid stage index {stage}."); return false; }
            if (!CampaignProgress.IsUnlocked(stage))
            {
                SetMessage($"Stage {stage + 1} is locked. Complete Stage {stage} first, or enable Demo mode below.");
                return false;
            }
            SetMessage("");
            transitions?.Load(StageCatalog.SceneFor(stage));
            return true;
        }

        void SetMessage(string m) { if (messageText != null) messageText.text = m; }
        void OnDemoToggled(bool on) { CampaignProgress.UnlockAll = on; }
        void ResetProgress() { CampaignProgress.ResetAll(); SetMessage("Progress reset."); }
        void OpenHelp() { mainPanel.Hide(); helpPanel.ShowInFrontOf(head); }
        void CloseHelp() { helpPanel.Hide(); mainPanel.ShowInFrontOf(head); }
        void OpenSettings() { mainPanel.Hide(); settingsPanel.ShowInFrontOf(head); }
        void CloseSettings() { settingsPanel.Hide(); mainPanel.ShowInFrontOf(head); }
        void Quit() => transitions?.Quit();
        void OpenTriage() => transitions?.Load(StageCatalog.TriageScene);
    }
}
