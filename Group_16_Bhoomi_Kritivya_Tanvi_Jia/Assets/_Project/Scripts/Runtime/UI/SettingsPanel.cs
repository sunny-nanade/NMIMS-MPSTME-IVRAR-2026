using RescueSim.Player;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RescueSim.UI
{
    /// <summary>Comfort and audio settings. Builds its own widgets; values persist through <see cref="GameSettings"/>.</summary>
    public class SettingsPanel : VRPanel
    {
        [SerializeField] Toggle snapToggle;
        [SerializeField] Toggle vignetteToggle;
        [SerializeField] Slider speedSlider;
        [SerializeField] Slider volumeSlider;
        [SerializeField] TMP_Text speedLabel;
        [SerializeField] TMP_Text volumeLabel;
        [SerializeField] Button backButton;

        public Button BackButton => backButton;

        /// <summary>Creates the widget layout under <paramref name="canvas"/> (1000x760 px) and returns the panel component.</summary>
        public static SettingsPanel Build(Canvas canvas)
        {
            var panel = canvas.gameObject.AddComponent<SettingsPanel>();
            var t = canvas.transform;
            UIFactory.AddBackground(t, UIFactory.Panel);
            UIFactory.AddText(t, "SETTINGS", 54f, new Vector2(0, -30), new Vector2(900, 70), TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            UIFactory.AddText(t, "Comfort and audio. Changes apply immediately and are saved.", 28f, new Vector2(0, -100), new Vector2(900, 50), TextAlignmentOptions.Center, UIFactory.TextDim);

            panel.snapToggle = UIFactory.AddToggle(t, "Snap turning (off = smooth turning)", new Vector2(0, -170), new Vector2(860, 70), GameSettings.SnapTurn, v => GameSettings.SnapTurn = v);
            panel.vignetteToggle = UIFactory.AddToggle(t, "Comfort vignette while moving", new Vector2(0, -255), new Vector2(860, 70), GameSettings.ComfortVignette, v => GameSettings.ComfortVignette = v);

            panel.speedLabel = UIFactory.AddText(t, "", 32f, new Vector2(0, -350), new Vector2(860, 44), TextAlignmentOptions.Left);
            panel.speedSlider = UIFactory.AddSlider(t, new Vector2(0, -400), new Vector2(860, 60), GameSettings.MinMoveSpeed, GameSettings.MaxMoveSpeed, GameSettings.MoveSpeed,
                v => { GameSettings.MoveSpeed = v; panel.RefreshLabels(); });

            panel.volumeLabel = UIFactory.AddText(t, "", 32f, new Vector2(0, -490), new Vector2(860, 44), TextAlignmentOptions.Left);
            panel.volumeSlider = UIFactory.AddSlider(t, new Vector2(0, -540), new Vector2(860, 60), 0f, 1f, GameSettings.Volume,
                v => { GameSettings.Volume = v; panel.RefreshLabels(); });

            UIFactory.AddButton(t, "Reset", new Vector2(-220, -650), new Vector2(300, 80), () => { GameSettings.ResetToDefaults(); panel.SyncFromSettings(); }, UIFactory.Button);
            panel.backButton = UIFactory.AddButton(t, "Back", new Vector2(220, -650), new Vector2(300, 80), null, UIFactory.AccentDim);
            panel.RefreshLabels();
            return panel;
        }

        public void SyncFromSettings()
        {
            snapToggle?.SetIsOnWithoutNotify(GameSettings.SnapTurn);
            vignetteToggle?.SetIsOnWithoutNotify(GameSettings.ComfortVignette);
            speedSlider?.SetValueWithoutNotify(GameSettings.MoveSpeed);
            volumeSlider?.SetValueWithoutNotify(GameSettings.Volume);
            RefreshLabels();
        }

        void OnEnable() => SyncFromSettings();

        void RefreshLabels()
        {
            if (speedLabel != null) speedLabel.text = $"Walk speed: {GameSettings.MoveSpeed:0.0} m/s";
            if (volumeLabel != null) volumeLabel.text = $"Master volume: {Mathf.RoundToInt(GameSettings.Volume * 100f)}%";
        }
    }
}
