using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Group10Gestures.Core;
using Group10Gestures.Providers;
using Group10Gestures.Recognition;

namespace Group10Gestures.UI
{
    /// <summary>
    /// Live Heads-Up Display (HUD) displaying:
    /// - Active Provider (XR / Webcam UDP:5005)
    /// - Live Gesture Label & Confidence for Left and Right hands
    /// - Current Mode (EDIT / COLOR)
    /// - FPS counter & Tracking Latency (ms)
    /// - BothOpen 0.6s hold progress bar
    /// - Controls cheat-sheet
    /// Supports automatic procedural canvas generation for instant zero-setup operation.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandProviderSelector _providerSelector;
        [SerializeField] private GestureRecognizer _gestureRecognizer;
        [SerializeField] private WebcamHandProvider _webcamProvider;

        [Header("UI Text Displays (Auto-generated if empty)")]
        [SerializeField] private TMP_Text _headerText;
        [SerializeField] private TMP_Text _providerText;
        [SerializeField] private TMP_Text _modeText;
        [SerializeField] private TMP_Text _leftHandText;
        [SerializeField] private TMP_Text _rightHandText;
        [SerializeField] private TMP_Text _telemetryText;
        [SerializeField] private TMP_Text _helpText;
        [SerializeField] private Slider _bothOpenBar;

        private float _fpsAccumulator = 0f;
        private int _fpsFrames = 0;
        private float _currentFps = 60f;
        private float _fpsTimer = 0f;

        private string _currentModeName = "EDIT MODE";

        private void Start()
        {
            if (_providerSelector == null) _providerSelector = FindAnyObjectByType<HandProviderSelector>();
            if (_gestureRecognizer == null) _gestureRecognizer = FindAnyObjectByType<GestureRecognizer>();
            if (_webcamProvider == null) _webcamProvider = FindAnyObjectByType<WebcamHandProvider>();

            EnsureCanvasAndElementsExist();
        }

        public void SetModeName(string modeName)
        {
            _currentModeName = modeName;
        }

        private void EnsureCanvasAndElementsExist()
        {
            if (_headerText != null && _providerText != null) return;

            Canvas canvas = GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("Group10_HUDCanvas");
                canvasObj.transform.SetParent(transform, false);
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Top-Left Info Panel
            GameObject panel = new GameObject("InfoPanel");
            panel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(25f, -25f);
            panelRect.sizeDelta = new Vector2(440f, 320f);

            Image panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.10f, 0.14f, 0.88f);

            // Title Header
            _headerText = CreateTmpText(panel.transform, "IVRAR 2026 - Group 10", 22, FontStyles.Bold, new Color(0.2f, 0.85f, 1.0f), 15f, -15f);

            // Mode Label
            _modeText = CreateTmpText(panel.transform, "MODE: EDIT", 18, FontStyles.Bold, new Color(1.0f, 0.85f, 0.0f), 15f, -50f);

            // Active Provider
            _providerText = CreateTmpText(panel.transform, "Provider: Detecting...", 16, FontStyles.Normal, Color.white, 15f, -80f);

            // Left Hand
            _leftHandText = CreateTmpText(panel.transform, "Left: [Not Tracked]", 16, FontStyles.Normal, new Color(0.0f, 0.9f, 1.0f), 15f, -115f);

            // Right Hand
            _rightHandText = CreateTmpText(panel.transform, "Right: [Not Tracked]", 16, FontStyles.Normal, new Color(0.1f, 1.0f, 0.4f), 15f, -145f);

            // Telemetry (FPS, Latency)
            _telemetryText = CreateTmpText(panel.transform, "FPS: 60 | Latency: 0 ms", 15, FontStyles.Normal, new Color(0.7f, 0.7f, 0.8f), 15f, -180f);

            // BothOpen Progress Bar Label
            CreateTmpText(panel.transform, "Both-Hands Open (Mode Switch):", 13, FontStyles.Italic, new Color(0.8f, 0.8f, 0.8f), 15f, -210f);

            // BothOpen Slider / Progress Bar
            GameObject sliderObj = new GameObject("BothOpenProgress");
            sliderObj.transform.SetParent(panel.transform, false);
            RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 1f);
            sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = new Vector2(15f, -235f);
            sliderRect.sizeDelta = new Vector2(400f, 18f);

            _bothOpenBar = sliderObj.AddComponent<Slider>();
            _bothOpenBar.minValue = 0f;
            _bothOpenBar.maxValue = 1f;
            _bothOpenBar.interactable = false;

            // Background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(sliderObj.transform, false);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = Vector2.zero;

            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.color = new Color(0.2f, 0.9f, 1.0f);
            _bothOpenBar.fillRect = fillRect;

            // Shortcut Hint text at bottom of panel
            _helpText = CreateTmpText(panel.transform, "[F1] Toggle Provider | Both Open 0.6s: Switch Mode", 12, FontStyles.Normal, new Color(0.6f, 0.8f, 0.9f), 15f, -270f);
        }

        private TextMeshProUGUI CreateTmpText(Transform parent, string text, float fontSize, FontStyles style, Color color, float x, float y)
        {
            GameObject textObj = new GameObject("Text_" + text.Replace(" ", "_"));
            textObj.transform.SetParent(parent, false);
            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(420f, fontSize + 8f);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            return tmp;
        }

        private void Update()
        {
            UpdateFps();

            // Active Provider
            if (_providerText != null && _providerSelector != null)
            {
                _providerText.text = $"Provider: <b>{_providerSelector.ProviderName}</b>";
            }

            // Current Mode
            if (_modeText != null)
            {
                _modeText.text = $"MODE: <b>{_currentModeName}</b>";
            }

            // Left Hand Recognition
            if (_leftHandText != null && _gestureRecognizer != null)
            {
                GestureResult l = _gestureRecognizer.LeftResult;
                if (l.IsTracked)
                {
                    _leftHandText.text = $"Left:  <b>{l.Gesture}</b> ({l.Confidence * 100f:F0}%) | Pinch: {l.PinchDistance:F2}";
                }
                else
                {
                    _leftHandText.text = "Left:  <color=#888888>[Not Tracked]</color>";
                }
            }

            // Right Hand Recognition
            if (_rightHandText != null && _gestureRecognizer != null)
            {
                GestureResult r = _gestureRecognizer.RightResult;
                if (r.IsTracked)
                {
                    _rightHandText.text = $"Right: <b>{r.Gesture}</b> ({r.Confidence * 100f:F0}%) | Pinch: {r.PinchDistance:F2}";
                }
                else
                {
                    _rightHandText.text = "Right: <color=#888888>[Not Tracked]</color>";
                }
            }

            // Telemetry (FPS & Latency)
            if (_telemetryText != null)
            {
                double latency = _webcamProvider != null ? _webcamProvider.LatencyMs : 0;
                _telemetryText.text = $"FPS: {_currentFps:F1} | Latency: {latency:F1} ms";
            }

            // BothOpen progress bar
            if (_bothOpenBar != null && _gestureRecognizer != null)
            {
                _bothOpenBar.value = _gestureRecognizer.BothOpenProgress;
            }
        }

        private void UpdateFps()
        {
            _fpsFrames++;
            _fpsAccumulator += Time.unscaledDeltaTime;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer >= 0.5f)
            {
                _currentFps = _fpsFrames / _fpsAccumulator;
                _fpsFrames = 0;
                _fpsAccumulator = 0f;
                _fpsTimer = 0f;
            }
        }
    }
}
