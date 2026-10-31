using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Acrophobia.GazeGeometry;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// World-space prompt that asks the participant for a SUDS rating (Subjective Units
    /// of Distress, 0-100) and reports it to the telemetry logger.
    ///
    /// SUDS is the only subjective measure in the data set and the one every objective
    /// signal gets read against, so the interaction has to be cheap to perform while
    /// standing somewhere frightening: one axis to set the number, one button to confirm.
    ///
    /// Two design rules from the project brief are load-bearing here:
    ///
    ///   - The prompt is always dismissable. It never traps the participant and never
    ///     waits on an answer, because a modal prompt would take away the reachable
    ///     "step back" and "end session" controls while it was up.
    ///   - Answering it does not advance anything. It records a number and closes.
    ///     Progression stays user-initiated elsewhere.
    ///
    /// Input is read from OVRInput, the Input System and the legacy Input Manager, each
    /// guarded independently. That redundancy is deliberate: the project was previously
    /// unable to run because a single unguarded legacy Input call threw on every frame
    /// when Active Input Handling was set to the new system only.
    /// </summary>
    public class SudsPromptController : MonoBehaviour
    {
        private const int ScaleMinimum = 0;
        private const int ScaleMaximum = 100;

        [SerializeField, Range(1, 25)] private int _step = 5;

        [Tooltip("Value the dial starts at each time the prompt opens.")]
        [SerializeField, Range(0, 100)] private int _initialValue = 0;

        [Tooltip("Metres in front of the eyes the panel sits.")]
        [SerializeField, Range(0.6f, 2.5f)] private float _followDistance = 1.1f;

        [SerializeField, Range(-0.5f, 0.5f)] private float _verticalOffset = 0.15f;

        [SerializeField, Range(1f, 12f)] private float _positionFollowSpeed = 6f;

        [SerializeField, Range(1f, 12f)] private float _rotationFollowSpeed = 7f;

        [Tooltip("Axis magnitude needed before a thumbstick nudge registers.")]
        [SerializeField, Range(0.1f, 0.9f)] private float _axisDeadzone = 0.45f;

        [Tooltip("Delay before a held direction starts repeating.")]
        [SerializeField, Range(0.1f, 1f)] private float _repeatDelaySeconds = 0.35f;

        [Tooltip("Interval between steps while a direction is held.")]
        [SerializeField, Range(0.03f, 0.5f)] private float _repeatIntervalSeconds = 0.12f;

        [Tooltip("Close on its own after this long with no answer, recorded as a non-response. " +
                 "0 disables the timeout.")]
        [SerializeField, Range(0f, 120f)] private float _noResponseTimeoutSeconds = 25f;

        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private SessionTelemetryLogger _logger;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;

        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private TextMeshProUGUI _questionText;
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private TextMeshProUGUI _hintText;
        [SerializeField] private TextMeshProUGUI _anchorLowText;
        [SerializeField] private TextMeshProUGUI _anchorHighText;
        [SerializeField] private RectTransform _scaleFill;

        private bool _isOpen;
        private float _openSince;
        private int _currentValue;
        private string _context = "unspecified";
        private float _heldDirection;
        private float _heldTimer;
        private float _nextRepeatAt;
        private Coroutine _hapticCoroutine;

        private static readonly Color ColorPanel = new Color(0.03f, 0.05f, 0.08f, 0.98f);
        private static readonly Color ColorAccent = new Color(0.00f, 0.85f, 1.00f, 1f);
        private static readonly Color ColorText = new Color(0.95f, 0.98f, 1.00f, 1f);
        private static readonly Color ColorMuted = new Color(0.70f, 0.80f, 0.90f, 1f);
        private static readonly Color ColorTrough = new Color(0.10f, 0.13f, 0.18f, 0.98f);

        // Distress Severity Colors (SUDS clinical gradient)
        private static readonly Color ColorMild = new Color(0.00f, 0.90f, 0.46f, 1f);     // Emerald (0-30)
        private static readonly Color ColorModerate = new Color(0.95f, 0.61f, 0.07f, 1f); // Orange (35-65)
        private static readonly Color ColorSevere = new Color(1.00f, 0.28f, 0.34f, 1f);   // Crimson/Red (70-100)

        public bool IsOpen => _isOpen;

        public int CurrentValue => _currentValue;

        /// <summary>
        /// Fired with the confirmed rating and the context string it was requested under.
        /// </summary>
        public event Action<int, string> OnSudsSubmitted;

        public event Action<string> OnPromptDismissed;

        public event Action<string> OnPromptOpened;

        private void Awake()
        {
            _isOpen = false;
            ResolveReferences();
            EnsureUIHierarchy();
            ApplyVisibility(false);
        }

        private void Start()
        {
            _isOpen = false;
            ApplyVisibility(false);
        }

        private void OnDisable()
        {
            StopHaptics();
        }

        public void ResolveReferences()
        {
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();
            if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();

            if (_centerEyeAnchor == null && _gazeDetector != null)
            {
                _centerEyeAnchor = _gazeDetector.CenterEyeAnchor;
            }

            if (_centerEyeAnchor == null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    _centerEyeAnchor = cam.transform;
                }
            }
        }

        /// <summary>
        /// Opens the prompt. <paramref name="context"/> is written into the telemetry row,
        /// so pass a stable literal such as "tier_entry" or "pre_session".
        /// </summary>
        public void Show(string context)
        {
            ResolveReferences();
            EnsureUIHierarchy();

            _context = string.IsNullOrEmpty(context) ? "unspecified" : context;
            _currentValue = Mathf.Clamp(_initialValue, ScaleMinimum, ScaleMaximum);
            _isOpen = true;
            _openSince = Time.time;
            _heldDirection = 0f;
            _heldTimer = 0f;

            SnapToFrontOfHead();
            RefreshDisplay();
            ApplyVisibility(true);

            TriggerHapticTick();

            Debug.Log($"[SUDS] Prompt opened (context: {_context})");
            OnPromptOpened?.Invoke(_context);
        }

        /// <summary>
        /// Closes without recording anything. Always available to the participant.
        /// </summary>
        public void Dismiss()
        {
            if (!_isOpen)
            {
                return;
            }

            _isOpen = false;
            ApplyVisibility(false);
            StopHaptics();

            Debug.Log($"[SUDS] Prompt dismissed (context: {_context})");
            _logger?.RecordEvent("suds_dismissed", 0, _context, 0f);
            OnPromptDismissed?.Invoke(_context);
        }

        /// <summary>
        /// Records the current value and closes.
        /// </summary>
        public void Submit()
        {
            if (!_isOpen)
            {
                return;
            }

            int value = Mathf.Clamp(_currentValue, ScaleMinimum, ScaleMaximum);

            _isOpen = false;
            ApplyVisibility(false);

            TriggerHapticConfirm();

            _logger?.RecordSudsResponse(value, _context);
            OnSudsSubmitted?.Invoke(value, _context);
        }

        public void SetValue(int value)
        {
            _currentValue = Mathf.Clamp(value, ScaleMinimum, ScaleMaximum);
            RefreshDisplay();
        }

        private void Update()
        {
            if (!_isOpen)
            {
                // Allow participant to summon distress rating on demand anytime
                if (CheckOpenTrigger())
                {
                    Show("self_report");
                }
                return;
            }

            // Without this the prompt could hang in front of the participant forever:
            // confirm is a keypress, and a keypress never arrives while the XR Simulator
            // window holds focus. A non-response is also honest data - it is recorded as
            // a skip rather than guessed at.
            if (_noResponseTimeoutSeconds > 0f &&
                Time.time - _openSince >= _noResponseTimeoutSeconds)
            {
                _isOpen = false;
                ApplyVisibility(false);
                StopHaptics();
                _logger?.RecordEvent("suds_no_response", 0, _context, 0f);
                OnPromptDismissed?.Invoke(_context);
                return;
            }

            UpdateFollow();
            UpdateInput();
        }

        private void UpdateFollow()
        {
            if (_centerEyeAnchor == null)
            {
                return;
            }

            Vector3 target = ComputeTargetPosition();
            Quaternion targetRotation = ComputeTargetRotation();

            transform.position = Vector3.Lerp(
                transform.position, target, Time.deltaTime * _positionFollowSpeed);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRotation, Time.deltaTime * _rotationFollowSpeed);
        }

        private Vector3 ComputeTargetPosition()
        {
            Vector3 forward = _centerEyeAnchor.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            return _centerEyeAnchor.position
                   + forward * _followDistance
                   + Vector3.up * _verticalOffset;
        }

        private Quaternion ComputeTargetRotation()
        {
            Vector3 forward = _centerEyeAnchor != null ? _centerEyeAnchor.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void SnapToFrontOfHead()
        {
            if (_centerEyeAnchor == null)
            {
                return;
            }

            transform.position = ComputeTargetPosition();
            transform.rotation = ComputeTargetRotation();
        }

        private bool CheckOpenTrigger()
        {
            // Meta Quest Touch Controller [Y] or [X] buttons, or thumbstick click
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.Four) ||
                    OVRInput.GetDown(OVRInput.RawButton.Y) ||
                    OVRInput.GetDown(OVRInput.Button.Three) ||
                    OVRInput.GetDown(OVRInput.RawButton.X) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick) ||
                    OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))
                {
                    return true;
                }
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                // [M] (Menu) or [U] (SUDS) or [Tab] or [R] (Rate Distress) or [I]
                if (kb != null && (kb.mKey.wasPressedThisFrame ||
                                   kb.uKey.wasPressedThisFrame ||
                                   kb.tabKey.wasPressedThisFrame ||
                                   kb.rKey.wasPressedThisFrame ||
                                   kb.iKey.wasPressedThisFrame))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.M) ||
                    Input.GetKeyDown(KeyCode.U) ||
                    Input.GetKeyDown(KeyCode.Tab) ||
                    Input.GetKeyDown(KeyCode.R) ||
                    Input.GetKeyDown(KeyCode.I))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

            return false;
        }

        private void UpdateInput()
        {
            if (WasConfirmPressed())
            {
                Submit();
                return;
            }

            if (WasDismissPressed())
            {
                Dismiss();
                return;
            }

            // Direct numeric shortcut keys (0-9) for rapid desktop / simulation testing
            if (TryReadNumericShortcut(out int numericVal))
            {
                SetValue(numericVal);
                TriggerHapticTick();
                return;
            }

            float axis = ReadDirectionAxis();
            int direction = 0;

            if (axis > _axisDeadzone) direction = 1;
            else if (axis < -_axisDeadzone) direction = -1;

            if (direction == 0)
            {
                _heldDirection = 0f;
                _heldTimer = 0f;
                return;
            }

            if (Mathf.Approximately(_heldDirection, 0f) || (int)_heldDirection != direction)
            {
                // Fresh press: step immediately, then wait out the repeat delay.
                _heldDirection = direction;
                _heldTimer = 0f;
                _nextRepeatAt = _repeatDelaySeconds;
                Nudge(direction);
                return;
            }

            _heldTimer += Time.deltaTime;
            if (_heldTimer >= _nextRepeatAt)
            {
                _nextRepeatAt = _heldTimer + _repeatIntervalSeconds;
                Nudge(direction);
            }
        }

        private void Nudge(int direction)
        {
            int next = Mathf.Clamp(_currentValue + direction * _step, ScaleMinimum, ScaleMaximum);
            if (next == _currentValue)
            {
                return;
            }

            _currentValue = next;
            RefreshDisplay();
            TriggerHapticTick();
        }

        private float ReadDirectionAxis()
        {
            float axis = 0f;

            try
            {
                // Primary Thumbstick: check both horizontal (right/left) and vertical (up/down)
                Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                if (Mathf.Abs(stick.x) > Mathf.Abs(axis)) axis = stick.x;
                if (Mathf.Abs(stick.y) > Mathf.Abs(axis)) axis = stick.y;

                // Secondary Thumbstick
                Vector2 secondary = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                if (Mathf.Abs(secondary.x) > Mathf.Abs(axis)) axis = secondary.x;
                if (Mathf.Abs(secondary.y) > Mathf.Abs(axis)) axis = secondary.y;

                // D-Pad / Buttons
                if (OVRInput.Get(OVRInput.Button.DpadUp) || OVRInput.Get(OVRInput.Button.DpadRight)) axis = 1f;
                else if (OVRInput.Get(OVRInput.Button.DpadDown) || OVRInput.Get(OVRInput.Button.DpadLeft)) axis = -1f;
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    // Use arrow keys and +/- keys only; WASD is preserved for player locomotion
                    if (kb.rightArrowKey.isPressed || kb.upArrowKey.isPressed ||
                        kb.numpadPlusKey.isPressed || kb.equalsKey.isPressed || kb.pageUpKey.isPressed)
                    {
                        axis = 1f;
                    }
                    else if (kb.leftArrowKey.isPressed || kb.downArrowKey.isPressed ||
                             kb.numpadMinusKey.isPressed || kb.minusKey.isPressed || kb.pageDownKey.isPressed)
                    {
                        axis = -1f;
                    }
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow) ||
                    Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus) || Input.GetKey(KeyCode.PageUp))
                {
                    axis = 1f;
                }
                else if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.DownArrow) ||
                         Input.GetKey(KeyCode.KeypadMinus) || Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.PageDown))
                {
                    axis = -1f;
                }
            }
            catch (Exception) { }
#endif

            return axis;
        }

        private bool TryReadNumericShortcut(out int numericValue)
        {
            numericValue = 0;
#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) { numericValue = 0; return true; }
                    if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) { numericValue = 10; return true; }
                    if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) { numericValue = 20; return true; }
                    if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) { numericValue = 30; return true; }
                    if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) { numericValue = 40; return true; }
                    if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) { numericValue = 50; return true; }
                    if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) { numericValue = 60; return true; }
                    if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) { numericValue = 70; return true; }
                    if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) { numericValue = 80; return true; }
                    if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) { numericValue = 90; return true; }
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) { numericValue = 0; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) { numericValue = 10; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) { numericValue = 20; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) { numericValue = 30; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) { numericValue = 40; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) { numericValue = 50; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) { numericValue = 60; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) { numericValue = 70; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) { numericValue = 80; return true; }
                if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) { numericValue = 90; return true; }
            }
            catch (Exception) { }
#endif
            return false;
        }

        private bool WasConfirmPressed()
        {
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One) ||
                    OVRInput.GetDown(OVRInput.RawButton.A) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
                    OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
                {
                    return true;
                }
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null &&
                    (kb.enterKey.wasPressedThisFrame ||
                     kb.numpadEnterKey.wasPressedThisFrame ||
                     kb.spaceKey.wasPressedThisFrame))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter) ||
                    Input.GetKeyDown(KeyCode.Space))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

            return false;
        }

        private bool WasDismissPressed()
        {
            try
            {
                // [B] to dismiss, or press [Y] / [X] again to toggle closed
                if (OVRInput.GetDown(OVRInput.Button.Two) ||
                    OVRInput.GetDown(OVRInput.RawButton.B) ||
                    OVRInput.GetDown(OVRInput.Button.Four) ||
                    OVRInput.GetDown(OVRInput.RawButton.Y) ||
                    OVRInput.GetDown(OVRInput.Button.Three) ||
                    OVRInput.GetDown(OVRInput.RawButton.X))
                {
                    return true;
                }
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                // [Esc] or toggle keys [M] / [U] / [Tab] / [R] / [I] to dismiss
                if (kb != null && (kb.escapeKey.wasPressedThisFrame ||
                                   kb.mKey.wasPressedThisFrame ||
                                   kb.uKey.wasPressedThisFrame ||
                                   kb.tabKey.wasPressedThisFrame ||
                                   kb.rKey.wasPressedThisFrame ||
                                   kb.iKey.wasPressedThisFrame))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape) ||
                    Input.GetKeyDown(KeyCode.M) ||
                    Input.GetKeyDown(KeyCode.U) ||
                    Input.GetKeyDown(KeyCode.Tab) ||
                    Input.GetKeyDown(KeyCode.R) ||
                    Input.GetKeyDown(KeyCode.I))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

            return false;
        }

        private void TriggerHapticTick()
        {
            TriggerHapticPulse(0.12f, 0.05f);
        }

        private void TriggerHapticConfirm()
        {
            TriggerHapticPulse(0.35f, 0.15f);
        }

        private void TriggerHapticPulse(float amplitude, float durationSeconds)
        {
            try
            {
                if (_hapticCoroutine != null)
                {
                    StopCoroutine(_hapticCoroutine);
                }
                _hapticCoroutine = StartCoroutine(HapticPulseRoutine(amplitude, durationSeconds));
            }
            catch (Exception) { }
        }

        private void StopHaptics()
        {
            if (_hapticCoroutine != null)
            {
                StopCoroutine(_hapticCoroutine);
                _hapticCoroutine = null;
            }

            try
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            }
            catch (Exception) { }
        }

        private System.Collections.IEnumerator HapticPulseRoutine(float amplitude, float duration)
        {
            try
            {
                OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(amplitude, amplitude, OVRInput.Controller.LTouch);
            }
            catch (Exception) { }

            yield return new WaitForSecondsRealtime(duration);

            try
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            }
            catch (Exception) { }
            _hapticCoroutine = null;
        }

        private Color GetDistressColor(int value)
        {
            if (value <= 30) return ColorMild;
            if (value <= 65) return ColorModerate;
            return ColorSevere;
        }

        private void RefreshDisplay()
        {
            Color distressColor = GetDistressColor(_currentValue);

            if (_valueText != null)
            {
                _valueText.SetText("{0}", _currentValue);
                _valueText.color = distressColor;
            }

            if (_scaleFill != null)
            {
                float t = (float)(_currentValue - ScaleMinimum) / (ScaleMaximum - ScaleMinimum);
                Vector2 max = _scaleFill.anchorMax;
                max.x = Mathf.Clamp01(t);
                _scaleFill.anchorMax = max;

                var img = _scaleFill.GetComponent<Image>();
                if (img != null) img.color = distressColor;
            }
        }

        private void ApplyVisibility(bool visible)
        {
            if (_canvas != null)
            {
                _canvas.enabled = visible;
            }

            if (_panel != null)
            {
                _panel.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Builds the panel if it has not been assigned in the inspector. Idempotent, and
        /// follows the same world-space convention as FloatingInstructionHUD: UI authored
        /// in 1000-units-per-metre then scaled by 0.001.
        /// </summary>
        public void EnsureUIHierarchy()
        {
            if (_canvas == null) _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            var canvasRect = _canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(760f, 320f);
            canvasRect.localScale = Vector3.one * 0.001f; // 0.76 m x 0.32 m

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 8f;

            if (_panel == null)
            {
                Transform existing = transform.Find("SudsPanel");
                GameObject panelObj = existing != null ? existing.gameObject : new GameObject("SudsPanel");
                panelObj.transform.SetParent(transform, false);

                _panel = panelObj.GetComponent<RectTransform>();
                if (_panel == null) _panel = panelObj.AddComponent<RectTransform>();

                _panel.anchorMin = Vector2.zero;
                _panel.anchorMax = Vector2.one;
                _panel.offsetMin = Vector2.zero;
                _panel.offsetMax = Vector2.zero;

                Image bg = panelObj.GetComponent<Image>();
                if (bg == null) bg = panelObj.AddComponent<Image>();
                bg.color = ColorPanel;

                var outline = panelObj.GetComponent<Outline>();
                if (outline == null) outline = panelObj.AddComponent<Outline>();
                outline.effectColor = new Color(0.00f, 0.85f, 1.00f, 0.85f);
                outline.effectDistance = new Vector2(3f, -3f);
            }

            _questionText = EnsureText("QuestionText",
                "Right now, how much distress do you feel?",
                42f, FontStyles.Bold, ColorText,
                new Vector2(0f, 108f), new Vector2(700f, 70f), TextAlignmentOptions.Center);

            _valueText = EnsureText("ValueText", "0",
                110f, FontStyles.Bold, ColorAccent,
                new Vector2(0f, 24f), new Vector2(400f, 130f), TextAlignmentOptions.Center);

            EnsureScaleBar();

            EnsureText("MinusSymbol", "-",
                32f, FontStyles.Bold, ColorMuted,
                new Vector2(-330f, -40f), new Vector2(40f, 36f), TextAlignmentOptions.Center);

            EnsureText("PlusSymbol", "+",
                32f, FontStyles.Bold, ColorMuted,
                new Vector2(330f, -40f), new Vector2(40f, 36f), TextAlignmentOptions.Center);

            _anchorLowText = EnsureText("AnchorLow", "0  none",
                26f, FontStyles.Normal, ColorMuted,
                new Vector2(-250f, -74f), new Vector2(220f, 34f), TextAlignmentOptions.Left);

            _anchorHighText = EnsureText("AnchorHigh", "worst imaginable  100",
                26f, FontStyles.Normal, ColorMuted,
                new Vector2(250f, -74f), new Vector2(260f, 34f), TextAlignmentOptions.Right);

            _hintText = EnsureText("HintText",
                "Thumbstick / Arrows to adjust   |   [A] / Enter to confirm\n[Y] / [X] / [U] / [Tab] to Toggle Prompt",
                20f, FontStyles.Normal, ColorMuted,
                new Vector2(0f, -122f), new Vector2(720f, 44f), TextAlignmentOptions.Center);
        }

        private void EnsureScaleBar()
        {
            Transform troughTf = _panel.Find("ScaleTrough");
            GameObject troughObj = troughTf != null ? troughTf.gameObject : new GameObject("ScaleTrough");
            troughObj.transform.SetParent(_panel, false);

            var troughRect = troughObj.GetComponent<RectTransform>();
            if (troughRect == null) troughRect = troughObj.AddComponent<RectTransform>();
            troughRect.anchoredPosition = new Vector2(0f, -40f);
            troughRect.sizeDelta = new Vector2(620f, 18f);

            Image troughImg = troughObj.GetComponent<Image>();
            if (troughImg == null) troughImg = troughObj.AddComponent<Image>();
            troughImg.color = ColorTrough;

            if (_scaleFill == null)
            {
                Transform fillTf = troughObj.transform.Find("ScaleFill");
                GameObject fillObj = fillTf != null ? fillTf.gameObject : new GameObject("ScaleFill");
                fillObj.transform.SetParent(troughObj.transform, false);

                _scaleFill = fillObj.GetComponent<RectTransform>();
                if (_scaleFill == null) _scaleFill = fillObj.AddComponent<RectTransform>();

                _scaleFill.anchorMin = Vector2.zero;
                _scaleFill.anchorMax = new Vector2(0f, 1f);
                _scaleFill.offsetMin = Vector2.zero;
                _scaleFill.offsetMax = Vector2.zero;
                _scaleFill.pivot = new Vector2(0f, 0.5f);

                Image fillImg = fillObj.GetComponent<Image>();
                if (fillImg == null) fillImg = fillObj.AddComponent<Image>();
                fillImg.color = ColorAccent;
            }
        }

        private TextMeshProUGUI EnsureText(string name, string content, float fontSize,
            FontStyles style, Color color, Vector2 anchoredPosition, Vector2 size,
            TextAlignmentOptions alignment)
        {
            Transform existing = _panel.Find(name);
            GameObject obj = existing != null ? existing.gameObject : new GameObject(name);
            obj.transform.SetParent(_panel, false);

            var rect = obj.GetComponent<RectTransform>();
            if (rect == null) rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = obj.GetComponent<TextMeshProUGUI>();
            if (text == null) text = obj.AddComponent<TextMeshProUGUI>();

            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.richText = false;

            return text;
        }
    }
}
