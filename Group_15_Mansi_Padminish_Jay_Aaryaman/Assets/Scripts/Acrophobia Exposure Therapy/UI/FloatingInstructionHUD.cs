using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Acrophobia.SignalProcessing;
using Acrophobia.GazeGeometry;

namespace Acrophobia.UI
{
    /// <summary>
    /// World-space floating HUD that guides the player through the acrophobia exposure session.
    /// Implements smooth lazy-follow behavior (1.8m in front of eyes, Y = -0.15m below eye level)
    /// using Slerp and Lerp for nausea-free visual stabilization.
    /// Features:
    /// - Start screen state locked until player presses [Space] or [A].
    /// - Title, Tier Banner, Step-by-Step Instruction text.
    /// - 30-Second Countdown progress bar.
    /// - Real-time Status Indicator ("Steady" / "High Hesitation Detected").
    /// </summary>
    public class FloatingInstructionHUD : MonoBehaviour
    {
        [Header("Lazy-Follow Configuration")]
        [Tooltip("Transform representing the player's head (CenterEyeAnchor).")]
        [SerializeField] private Transform _centerEyeAnchor;

        [Tooltip("Target distance in meters in front of the player's eyes.")]
        [SerializeField, Range(0.8f, 3.0f)] private float _followDistance = 1.3f;

        [Tooltip("Vertical offset relative to eye level (negative is slightly below eye level).")]
        [SerializeField, Range(-0.5f, 0.5f)] private float _verticalOffset = -0.15f;

        [Tooltip("Upward pitch tilt angle in degrees.")]
        [SerializeField, Range(-20f, 20f)] private float _upwardTiltAngle = 10.0f;

        [Tooltip("Position lerp smoothing speed.")]
        [SerializeField, Range(1.0f, 10.0f)] private float _positionFollowSpeed = 3.5f;

        [Tooltip("Rotation slerp smoothing speed.")]
        [SerializeField, Range(1.0f, 10.0f)] private float _rotationFollowSpeed = 4.0f;

        [Header("UI Elements (Auto-Generated if unassigned)")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _mainPanel;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _tierBannerText;
        [SerializeField] private TextMeshProUGUI _instructionText;
        [SerializeField] private TextMeshProUGUI _countdownText;
        [SerializeField] private Image _progressBarFill;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private Image _statusIcon;

        [Header("Distress Telemetry Integration")]
        [SerializeField] private Acrophobia.Telemetry.SudsPromptController _sudsPrompt;
        [SerializeField] private TextMeshProUGUI _distressPromptHint;
        private Coroutine _distressBannerCoroutine;

        public enum HUDDisplayMode
        {
            TaskInstructions = 0,
            DistressPrompt = 1,
            Hidden = 2
        }

        [Header("Interchangeable HUD System")]
        [SerializeField] private HUDDisplayMode _currentDisplayMode = HUDDisplayMode.TaskInstructions;

        [Header("Runtime State")]
        [SerializeField] private bool _hasSessionStarted = false;
        [SerializeField] private int _currentTier = 1;
        [SerializeField] private float _currentProgressSeconds = 0f;
        [SerializeField] private float _targetProgressSeconds = 30f;
        [SerializeField] private bool _isHesitating = false;

        // Visual Colors - High-contrast VR-optimized palette
        private static readonly Color ColorSteady = new Color(0.00f, 0.90f, 0.46f, 1f);      // Emerald Green (#00E676)
        private static readonly Color ColorHesitation = new Color(0.95f, 0.61f, 0.07f, 1f);   // Orange (#F39C12)
        private static readonly Color ColorAwaiting = new Color(0.40f, 0.78f, 1.00f, 1f);     // High-contrast Sky Blue (#66C7FF)
        private static readonly Color ColorComplete = new Color(0.00f, 0.85f, 1.00f, 1f);     // Cyan (#00E5FF)
        private static readonly Color ColorCyanAccent = new Color(0.00f, 0.85f, 1.00f, 1f);   // Bright Neon Cyan
        private static readonly Color ColorDarkBg = new Color(0.03f, 0.05f, 0.08f, 0.98f);    // Solid high-contrast dark slate
        private static readonly Color ColorTroughBg = new Color(0.10f, 0.13f, 0.18f, 0.98f);  // Progress trough
        private static readonly Color ColorBorderCyan = new Color(0.00f, 0.85f, 1.00f, 0.85f);// Outline border

        // Connected telemetry (optional auto-binding)
        private EdgeProximityTracker _edgeTracker;
        private PosturalSwayAnalyzer _swayAnalyzer;

        public bool HasSessionStarted => _hasSessionStarted;
        public int CurrentTier => _currentTier;
        public float ProgressSeconds => _currentProgressSeconds;
        public float TargetProgressSeconds => _targetProgressSeconds;
        public bool IsHesitating => _isHesitating;
        public bool ExternalFsmControlled { get; set; } = false;
        public HUDDisplayMode CurrentDisplayMode => _currentDisplayMode;

        public event Action OnSessionStarted;
        public event Action OnExposureCompleted;
        public event Action<HUDDisplayMode> OnHUDModeChanged;

        private void Awake()
        {
            ResolveTarget();
            EnsureUIHierarchy();
            TryBindSudsPrompt();
            ShowStartScreen();
        }

        private void Start()
        {
            ResolveTarget();
            SnapToTargetPosition();
            TryBindTelemetry();
            TryBindSudsPrompt();
            ApplyDisplayMode(_currentDisplayMode);
        }

        private void OnDestroy()
        {
            if (_sudsPrompt != null)
            {
                _sudsPrompt.OnSudsSubmitted -= HandleSudsSubmitted;
                _sudsPrompt.OnPromptOpened -= HandleSudsOpened;
                _sudsPrompt.OnPromptDismissed -= HandleSudsDismissed;
            }
        }

        private void Update()
        {
            // 1. Check interchangeable HUD switch button input ([Y] / [B] / [Tab] / [H])
            CheckHUDToggleInput();

            // 2. Smooth lazy-follow movement
            UpdateLazyFollow();

            // 3. Start screen initiation check
            if (!_hasSessionStarted)
            {
                CheckStartInput();
            }
            else
            {
                // 4. Autonomous telemetry polling if present
                PollTelemetry();
            }
        }

        /// <summary>
        /// Smoothly glides the HUD to maintain 1.3m distance and -0.15m vertical offset
        /// relative to the player's head, facing the camera with a 10-degree upward tilt.
        /// </summary>
        private void UpdateLazyFollow()
        {
            if (_centerEyeAnchor == null) return;

            Vector3 headPos = _centerEyeAnchor.position;
            Vector3 forward = _centerEyeAnchor.forward;

            // Project forward onto horizontal plane to keep HUD upright
            Vector3 forwardHoriz = new Vector3(forward.x, 0f, forward.z);
            if (forwardHoriz.sqrMagnitude < 0.01f) forwardHoriz = forward;
            forwardHoriz.Normalize();

            Vector3 targetPosition = headPos + forwardHoriz * _followDistance + Vector3.up * _verticalOffset;
            Vector3 lookDirection = transform.position - headPos;

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion baseRotation = Quaternion.LookRotation(lookDirection);
                Quaternion tilt = Quaternion.Euler(-_upwardTiltAngle, 0f, 0f);
                Quaternion targetRotation = baseRotation * tilt;
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * _rotationFollowSpeed);
            }

            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * _positionFollowSpeed);
        }

        /// <summary>
        /// Instantly places the HUD at its ideal target position without smoothing.
        /// Useful during initialization and scene load.
        /// </summary>
        public void SnapToTargetPosition()
        {
            if (_centerEyeAnchor == null) return;

            Vector3 headPos = _centerEyeAnchor.position;
            Vector3 forwardHoriz = new Vector3(_centerEyeAnchor.forward.x, 0f, _centerEyeAnchor.forward.z).normalized;
            if (forwardHoriz.sqrMagnitude < 0.01f) forwardHoriz = Vector3.forward;

            transform.position = headPos + forwardHoriz * _followDistance + Vector3.up * _verticalOffset;
            Vector3 lookDirection = transform.position - headPos;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion baseRotation = Quaternion.LookRotation(lookDirection);
                Quaternion tilt = Quaternion.Euler(-_upwardTiltAngle, 0f, 0f);
                transform.rotation = baseRotation * tilt;
            }
        }

        /// <summary>
        /// Displays the initial locked Start Screen state.
        /// </summary>
        public void ShowStartScreen()
        {
            _hasSessionStarted = false;
            _currentProgressSeconds = 0f;

            if (_titleText != null) _titleText.text = "[ TIER 1: ENCLOSED LEDGE ]";
            if (_tierBannerText != null) _tierBannerText.text = "[ TIER 1: ENCLOSED LEDGE ]";
            if (_instructionText != null)
            {
                _instructionText.text = "Stand comfortably on the platform.\nPress [Space] or [A] to Start.";
            }

            if (_countdownText != null) _countdownText.text = "Calm Exposure: 0.0s / 30.0s";
            if (_progressBarFill != null) _progressBarFill.fillAmount = 0f;

            UpdateStatusDisplay(false, "STATUS: AWAITING START", ColorAwaiting);
        }

        /// <summary>
        /// Initiates the exposure session and unlocks Tier 1 guidance.
        /// </summary>
        public void StartSession()
        {
            if (_hasSessionStarted) return;

            _hasSessionStarted = true;
            _currentTier = 1;
            SetTier(1, "ENCLOSED LEDGE");
            SetInstruction("Take deep breaths. Look down past the railing at the street below.");
            UpdateProgress(0f, 30f);
            SetStatus(false, "STATUS: STEADY");

            OnSessionStarted?.Invoke();
        }

        /// <summary>
        /// Sets the current exposure tier banner.
        /// </summary>
        public void SetTier(int tierNumber, string tierName)
        {
            _currentTier = tierNumber;
            if (_tierBannerText != null)
            {
                _tierBannerText.text = $"[ TIER {tierNumber}: {tierName.ToUpperInvariant()} ]";
            }
            if (_titleText != null)
            {
                _titleText.text = $"[ TIER {tierNumber}: {tierName.ToUpperInvariant()} ]";
            }
        }

        /// <summary>
        /// Updates the main dynamic step-by-step guidance instruction.
        /// </summary>
        public void SetInstruction(string instruction)
        {
            if (_instructionText != null)
            {
                _instructionText.text = instruction;
            }
        }

        /// <summary>
        /// Updates the 30-second progress bar and text counter.
        /// </summary>
        public void UpdateProgress(float currentSeconds, float targetSeconds = 30f)
        {
            _currentProgressSeconds = Mathf.Clamp(currentSeconds, 0f, targetSeconds);
            _targetProgressSeconds = targetSeconds;

            float fill = _targetProgressSeconds > 0f ? _currentProgressSeconds / _targetProgressSeconds : 0f;
            if (_progressBarFill != null)
            {
                _progressBarFill.fillAmount = fill;
            }

            if (_countdownText != null)
            {
                if (_currentProgressSeconds >= _targetProgressSeconds)
                {
                    _countdownText.text = $"Exposure Complete! ({_targetProgressSeconds:F1}s / {_targetProgressSeconds:F1}s)";
                    UpdateStatusDisplay(false, "STATUS: TIER COMPLETE", ColorComplete);
                    OnExposureCompleted?.Invoke();
                }
                else
                {
                    _countdownText.text = $"Calm Exposure: {_currentProgressSeconds:F1}s / {_targetProgressSeconds:F1}s";
                }
            }
        }

        /// <summary>
        /// Overload accepting normalized progress (0..1), current seconds, and target seconds.
        /// Matches FloatingInstructionHUD.UpdateProgress(calmTimer / 30.0f, calmTimer, 30.0f).
        /// </summary>
        public void UpdateProgress(float progressNormalized, float currentSeconds, float targetSeconds)
        {
            UpdateProgress(currentSeconds, targetSeconds);
        }

        /// <summary>
        /// Updates the real-time Status Indicator between Steady (green) and Hesitation Detected (orange).
        /// </summary>
        public void SetStatus(bool isHesitating, string customStatus = null)
        {
            _isHesitating = isHesitating;
            string label = customStatus ?? (isHesitating ? "HESITATION DETECTED" : "STATUS: STEADY");
            Color color = isHesitating ? ColorHesitation : ColorSteady;

            UpdateStatusDisplay(isHesitating, label, color);
        }

        /// <summary>
        /// Updates the real-time Status Indicator with explicit label and color.
        /// </summary>
        public void SetStatus(bool isHesitating, string customStatus, Color customColor)
        {
            _isHesitating = isHesitating;
            UpdateStatusDisplay(isHesitating, customStatus, customColor);
        }

        private void UpdateStatusDisplay(bool isHesitating, string label, Color color)
        {
            if (_statusText != null)
            {
                _statusText.text = label;
                _statusText.color = color;
            }

            if (_statusIcon != null)
            {
                _statusIcon.color = color;
            }
        }

        private void CheckStartInput()
        {
            bool startTriggered = false;

            // 1. Keyboard Input via New Input System
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                {
                    startTriggered = true;
                }
            }
#else
            try
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    startTriggered = true;
                }
            }
            catch (System.InvalidOperationException) { }
#endif

            // 2. Meta Quest Touch Controller Input
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One) ||           // A button
                    OVRInput.GetDown(OVRInput.Button.Three) ||         // X button
                    OVRInput.GetDown(OVRInput.RawButton.A) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
                    OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
                {
                    startTriggered = true;
                }
            }
            catch (System.Exception) { }

            if (startTriggered)
            {
                StartSession();
            }
        }

        private void PollTelemetry()
        {
            if (ExternalFsmControlled) return;

            if (_edgeTracker != null)
            {
                // If within edge zone and hesitation index is elevated (> 45), flag hesitation
                bool highHesitation = _edgeTracker.IsNearEdge && _edgeTracker.HesitationIndex > 45f;
                if (highHesitation != _isHesitating)
                {
                    SetStatus(highHesitation);
                }
            }
        }

        private void TryBindTelemetry()
        {
            if (_edgeTracker == null) _edgeTracker = FindAnyObjectByType<EdgeProximityTracker>();
            if (_swayAnalyzer == null) _swayAnalyzer = FindAnyObjectByType<PosturalSwayAnalyzer>();
        }

        #region Interchangeable HUD System

        private void CheckHUDToggleInput()
        {
            bool togglePressed = false;

            try
            {
                // Meta Quest Touch VR Controllers:
                // Primary/Secondary Thumbstick Click (L3 / R3) or Left Controller 'Y' button (Universal VR Menu)
                if (OVRInput.GetDown(OVRInput.Button.Four) ||          // Y button (Left Controller)
                    OVRInput.GetDown(OVRInput.RawButton.Y) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick) ||   // Left stick press
                    OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))  // Right stick press
                {
                    togglePressed = true;
                }
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            if (!togglePressed)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                // [M] (Menu), [Tab], [U] (UI), [V] (View), [I] (Info)
                // NEVER USE 'H' KEY: In Meta XR Simulator, 'H' is hardcoded to "Reset HMD Pose to Origin", which pushes the player backward!
                if (kb != null && (kb.mKey.wasPressedThisFrame ||
                                   kb.tabKey.wasPressedThisFrame ||
                                   kb.uKey.wasPressedThisFrame ||
                                   kb.vKey.wasPressedThisFrame ||
                                   kb.iKey.wasPressedThisFrame))
                {
                    togglePressed = true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (!togglePressed)
            {
                try
                {
                    if (Input.GetKeyDown(KeyCode.M) ||
                        Input.GetKeyDown(KeyCode.Tab) ||
                        Input.GetKeyDown(KeyCode.U) ||
                        Input.GetKeyDown(KeyCode.V) ||
                        Input.GetKeyDown(KeyCode.I))
                    {
                        togglePressed = true;
                    }
                }
                catch (Exception) { }
            }
#endif

            if (togglePressed)
            {
                CycleHUDDisplayMode();
            }
        }

        public void CycleHUDDisplayMode()
        {
            HUDDisplayMode nextMode;
            switch (_currentDisplayMode)
            {
                case HUDDisplayMode.TaskInstructions:
                    nextMode = HUDDisplayMode.DistressPrompt;
                    break;
                case HUDDisplayMode.DistressPrompt:
                    nextMode = HUDDisplayMode.Hidden;
                    break;
                default:
                    nextMode = HUDDisplayMode.TaskInstructions;
                    break;
            }

            ApplyDisplayMode(nextMode);
        }

        public void ApplyDisplayMode(HUDDisplayMode mode)
        {
            _currentDisplayMode = mode;
            TryBindSudsPrompt();

            switch (mode)
            {
                case HUDDisplayMode.TaskInstructions:
                    if (_canvas != null) _canvas.enabled = true;
                    if (_mainPanel != null) _mainPanel.gameObject.SetActive(true);
                    if (_sudsPrompt != null && _sudsPrompt.IsOpen) _sudsPrompt.Dismiss();
                    SnapToTargetPosition();
                    break;

                case HUDDisplayMode.DistressPrompt:
                    if (_mainPanel != null) _mainPanel.gameObject.SetActive(false);
                    if (_sudsPrompt != null)
                    {
                        if (!_sudsPrompt.IsOpen)
                        {
                            _sudsPrompt.Show("hud_switch");
                        }
                    }
                    else
                    {
                        // Fallback if SUDS controller not found: keep Task HUD active
                        if (_canvas != null) _canvas.enabled = true;
                        if (_mainPanel != null) _mainPanel.gameObject.SetActive(true);
                    }
                    break;

                case HUDDisplayMode.Hidden:
                    if (_mainPanel != null) _mainPanel.gameObject.SetActive(false);
                    if (_canvas != null) _canvas.enabled = false;
                    if (_sudsPrompt != null && _sudsPrompt.IsOpen) _sudsPrompt.Dismiss();
                    break;
            }

            OnHUDModeChanged?.Invoke(_currentDisplayMode);
        }

        private void HandleSudsOpened(string context)
        {
            // Guarantee exclusive display: hide Task HUD while SUDS prompt is active
            if (_mainPanel != null) _mainPanel.gameObject.SetActive(false);
            _currentDisplayMode = HUDDisplayMode.DistressPrompt;
        }

        private void HandleSudsDismissed(string context)
        {
            // Restore Task HUD after SUDS closes if not explicitly hidden
            if (_currentDisplayMode == HUDDisplayMode.DistressPrompt)
            {
                _currentDisplayMode = HUDDisplayMode.TaskInstructions;
                if (_mainPanel != null) _mainPanel.gameObject.SetActive(true);
                if (_canvas != null) _canvas.enabled = true;
            }
        }

        #endregion

        private void TryBindSudsPrompt()
        {
            if (_sudsPrompt == null)
            {
                _sudsPrompt = FindAnyObjectByType<Acrophobia.Telemetry.SudsPromptController>();
            }

            if (_sudsPrompt != null)
            {
                _sudsPrompt.OnSudsSubmitted -= HandleSudsSubmitted;
                _sudsPrompt.OnSudsSubmitted += HandleSudsSubmitted;
                _sudsPrompt.OnPromptOpened -= HandleSudsOpened;
                _sudsPrompt.OnPromptOpened += HandleSudsOpened;
                _sudsPrompt.OnPromptDismissed -= HandleSudsDismissed;
                _sudsPrompt.OnPromptDismissed += HandleSudsDismissed;
            }
        }

        private void HandleSudsSubmitted(int distressRating, string context)
        {
            ShowDistressLoggedConfirmation(distressRating);
        }

        /// <summary>
        /// Displays transient visual feedback on the HUD that distress was recorded.
        /// </summary>
        public void ShowDistressLoggedConfirmation(int distressValue)
        {
            if (_distressBannerCoroutine != null)
            {
                StopCoroutine(_distressBannerCoroutine);
            }
            _distressBannerCoroutine = StartCoroutine(DistressConfirmationRoutine(distressValue));
        }

        private System.Collections.IEnumerator DistressConfirmationRoutine(int distressValue)
        {
            UpdateStatusDisplay(false, $"DISTRESS: {distressValue}/100 LOGGED", ColorComplete);

            yield return new WaitForSeconds(3.5f);

            if (_hasSessionStarted)
            {
                SetStatus(_isHesitating);
            }
            else
            {
                UpdateStatusDisplay(false, "STATUS: AWAITING START", ColorAwaiting);
            }

            _distressBannerCoroutine = null;
        }

        private void ResolveTarget()
        {
            if (_centerEyeAnchor != null) return;

            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye != null) _centerEyeAnchor = centerEye.transform;
            else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
        }

        /// <summary>
        /// Builds the entire ergonomic World-Space Canvas and visual hierarchy programmatically
        /// if components are not already wired in the Inspector.
        /// Configured for 850 x 350 at 0.001 scale (0.85m x 0.35m physical) with 2px cyan outline.
        /// </summary>
        public void EnsureUIHierarchy()
        {
            // 1. Ensure Canvas
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRt = GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(860f, 380f);
            canvasRt.localScale = Vector3.one * 0.001f; // 0.86m x 0.38m physical size

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 8f;

            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null) gameObject.AddComponent<GraphicRaycaster>();

            // 2. Main Background Panel (0.86m x 0.38m solid dark slate with 3px cyan outline)
            Transform panelTrans = transform.Find("MainPanel");
            GameObject panelObj;
            if (panelTrans == null)
            {
                panelObj = new GameObject("MainPanel");
                panelObj.transform.SetParent(transform, false);
            }
            else
            {
                panelObj = panelTrans.gameObject;
            }

            _mainPanel = panelObj.GetComponent<RectTransform>();
            if (_mainPanel == null) _mainPanel = panelObj.AddComponent<RectTransform>();
            _mainPanel.anchorMin = Vector2.zero;
            _mainPanel.anchorMax = Vector2.one;
            _mainPanel.sizeDelta = Vector2.zero;

            Image bg = panelObj.GetComponent<Image>();
            if (bg == null) bg = panelObj.AddComponent<Image>();
            bg.color = ColorDarkBg;

            // 3px High-Contrast Cyan Outline
            Outline outline = panelObj.GetComponent<Outline>();
            if (outline == null) outline = panelObj.AddComponent<Outline>();
            outline.effectColor = ColorBorderCyan;
            outline.effectDistance = new Vector2(3f, -3f);

            // 3. Header / Tier Banner: Font size 34, Bold, bright neon cyan: [ TIER 1: ENCLOSED LEDGE ]
            _titleText = EnsureTextComponent(_mainPanel.transform, "TitleText", 
                "[ TIER 1: ENCLOSED LEDGE ]", 34f, FontStyles.Bold, ColorCyanAccent,
                new Vector2(0f, 125f), new Vector2(820f, 48f), TextAlignmentOptions.Center);
            _tierBannerText = _titleText;

            // 4. Instruction Text: Font size 28, Bold, crisp white, centered
            _instructionText = EnsureTextComponent(_mainPanel.transform, "InstructionText",
                "Stand comfortably on the platform.\nPress [Space] or [A] to Start.", 28f, FontStyles.Bold, Color.white,
                new Vector2(0f, 48f), new Vector2(800f, 82f), TextAlignmentOptions.Center);

            // 5. 30-Second Countdown Text: Font size 22, Bold, Calm Exposure: 0.0s / 30.0s
            _countdownText = EnsureTextComponent(_mainPanel.transform, "CountdownText",
                "Calm Exposure: 0.0s / 30.0s", 22f, FontStyles.Bold, new Color(0.85f, 0.92f, 1.00f, 1f),
                new Vector2(0f, -24f), new Vector2(720f, 30f), TextAlignmentOptions.Center);

            // 6. Progress Bar (Trough + Emerald Fill): Prominent horizontal bar (Height = 20px, Width = 720px)
            Transform troughTrans = _mainPanel.transform.Find("ProgressBarTrough");
            GameObject troughObj;
            if (troughTrans == null)
            {
                troughObj = new GameObject("ProgressBarTrough");
                troughObj.transform.SetParent(_mainPanel.transform, false);
            }
            else
            {
                troughObj = troughTrans.gameObject;
            }

            RectTransform trRt = troughObj.GetComponent<RectTransform>();
            if (trRt == null) trRt = troughObj.AddComponent<RectTransform>();
            trRt.anchoredPosition = new Vector2(0f, -56f);
            trRt.sizeDelta = new Vector2(720f, 20f);

            Image trImg = troughObj.GetComponent<Image>();
            if (trImg == null) trImg = troughObj.AddComponent<Image>();
            trImg.color = ColorTroughBg;

            Transform fillTrans = troughObj.transform.Find("ProgressBarFill");
            GameObject fillObj;
            if (fillTrans == null)
            {
                fillObj = new GameObject("ProgressBarFill");
                fillObj.transform.SetParent(troughObj.transform, false);
            }
            else
            {
                fillObj = fillTrans.gameObject;
            }

            RectTransform fillRt = fillObj.GetComponent<RectTransform>();
            if (fillRt == null) fillRt = fillObj.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;

            _progressBarFill = fillObj.GetComponent<Image>();
            if (_progressBarFill == null) _progressBarFill = fillObj.AddComponent<Image>();
            _progressBarFill.type = Image.Type.Filled;
            _progressBarFill.fillMethod = Image.FillMethod.Horizontal;
            _progressBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progressBarFill.fillAmount = 0f;
            _progressBarFill.color = ColorSteady;

            // 7. Status Indicator Pill Badge: STATUS: AWAITING START / STATUS: STEADY / HESITATION DETECTED
            Transform statusTrans = _mainPanel.transform.Find("StatusIndicator");
            GameObject statusObj;
            if (statusTrans == null)
            {
                statusObj = new GameObject("StatusIndicator");
                statusObj.transform.SetParent(_mainPanel.transform, false);
            }
            else
            {
                statusObj = statusTrans.gameObject;
            }

            RectTransform statusRt = statusObj.GetComponent<RectTransform>();
            if (statusRt == null) statusRt = statusObj.AddComponent<RectTransform>();
            statusRt.anchoredPosition = new Vector2(0f, -102f);
            statusRt.sizeDelta = new Vector2(380f, 36f);

            Image statusBg = statusObj.GetComponent<Image>();
            if (statusBg == null) statusBg = statusObj.AddComponent<Image>();
            statusBg.color = new Color(0.10f, 0.14f, 0.20f, 0.98f);

            Outline statusOutline = statusObj.GetComponent<Outline>();
            if (statusOutline == null) statusOutline = statusObj.AddComponent<Outline>();
            statusOutline.effectColor = new Color(0.00f, 0.85f, 1.00f, 0.60f);
            statusOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // Status Icon Circle
            Transform iconTrans = statusObj.transform.Find("StatusIcon");
            GameObject iconObj;
            if (iconTrans == null)
            {
                iconObj = new GameObject("StatusIcon");
                iconObj.transform.SetParent(statusObj.transform, false);
            }
            else
            {
                iconObj = iconTrans.gameObject;
            }

            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            if (iconRt == null) iconRt = iconObj.AddComponent<RectTransform>();
            iconRt.anchoredPosition = new Vector2(-130f, 0f);
            iconRt.sizeDelta = new Vector2(16f, 16f);

            _statusIcon = iconObj.GetComponent<Image>();
            if (_statusIcon == null) _statusIcon = iconObj.AddComponent<Image>();
            _statusIcon.color = ColorAwaiting;

            // Status Text
            _statusText = EnsureTextComponent(statusObj.transform, "StatusText",
                "STATUS: AWAITING START", 20f, FontStyles.Bold, ColorAwaiting,
                new Vector2(10f, 0f), new Vector2(280f, 32f), TextAlignmentOptions.Center);
            _statusText.textWrappingMode = TextWrappingModes.NoWrap;

            // 8. Interchangeable HUD Toggle Callout at bottom of panel
            _distressPromptHint = EnsureTextComponent(_mainPanel.transform, "DistressPromptHint",
                "[M] / [Y] / [Tab] : Switch Menu (Tasks | Distress Rating | Clean View)", 18f, FontStyles.Bold,
                new Color(0.40f, 0.78f, 1.00f, 1f),
                new Vector2(0f, -148f), new Vector2(720f, 28f), TextAlignmentOptions.Center);
        }

        private TextMeshProUGUI EnsureTextComponent(Transform parent, string name, string initialText,
            float fontSize, FontStyles style, Color color, Vector2 anchoredPos, Vector2 sizeDelta, TextAlignmentOptions alignment)
        {
            Transform existing = parent.Find(name);
            GameObject textObj;
            if (existing == null)
            {
                textObj = new GameObject(name);
                textObj.transform.SetParent(parent, false);
            }
            else
            {
                textObj = existing.gameObject;
            }

            RectTransform rt = textObj.GetComponent<RectTransform>();
            if (rt == null) rt = textObj.AddComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = textObj.AddComponent<TextMeshProUGUI>();

            tmp.text = initialText;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;

            return tmp;
        }
    }
}
