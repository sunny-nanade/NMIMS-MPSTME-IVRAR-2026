using System;
using UnityEngine;
using Acrophobia.UI;
using Acrophobia.ProceduralEnvironment;
using Acrophobia.SignalProcessing;
using Acrophobia.GazeGeometry;

namespace Acrophobia.AdaptiveFSM
{
    /// <summary>
    /// Adaptive Exposure Finite State Machine for Acrophobia Virtual Reality Therapy.
    /// 
    /// Coordinates real-time biometric and behavioral telemetry:
    /// - TremorIntensity from TremorBandFilter (micro-tremor envelope 8-12 Hz)
    /// - HesitationIndex from EdgeProximityTracker (edge proximity, speed, gaze avoidance)
    /// - Behavioral Freezing from PosturalSwayAnalyzer (acute postural immobility)
    /// - Downward gaze angles from GazeAvoidanceDetector
    /// 
    /// Adapts the 5-Tier dynamic physical platform (DynamicCatwalkController),
    /// floating HUD instructions and countdown (FloatingInstructionHUD),
    /// and immersive audio cues (ExposureAudioManager).
    /// </summary>
    public class AdaptiveExposureFSM : MonoBehaviour
    {
        public enum ExposureState
        {
            AwaitingStart,
            Exposing,
            SafetyRecovery,
            Completed
        }

        [Header("System References")]
        [SerializeField] private FloatingInstructionHUD _hud;
        [SerializeField] private DynamicCatwalkController _catwalkController;
        [SerializeField] private ExposureAudioManager _audioManager;

        [Header("Telemetry References")]
        [SerializeField] private TremorBandFilter _tremorFilter;
        [SerializeField] private PosturalSwayAnalyzer _swayAnalyzer;
        [SerializeField] private EdgeProximityTracker _edgeTracker;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;

        [Header("Adaptive Thresholds")]
        [Tooltip("Maximum micro-tremor intensity (0..1) to count as STEADY.")]
        [SerializeField, Range(0.1f, 0.5f)] private float _steadyTremorThreshold = 0.25f;

        [Tooltip("Maximum Behavioral Hesitation Index (0..100) to count as STEADY.")]
        [SerializeField, Range(15f, 60f)] private float _steadyHesitationThreshold = 35.0f;

        [Tooltip("Tremor threshold that triggers an emergency safety intervention.")]
        [SerializeField, Range(0.5f, 0.95f)] private float _panicTremorThreshold = 0.70f;

        [Tooltip("Seconds required in steady calm state to complete each tier.")]
        [SerializeField] private float _targetTierCalmSeconds = 30.0f;

#pragma warning disable 0414
        [Tooltip("Decay multiplier per second applied to calm timer when hesitation/tremor is detected.")]
        [SerializeField, Range(0.1f, 2.0f)] private float _calmDecayRate = 0.5f;
#pragma warning restore 0414

        [Tooltip("Seconds of steady calm required to recover from a safety intervention.")]
        [SerializeField] private float _safetyRecoverySeconds = 5.0f;

        [Header("Runtime Telemetry Monitor")]
        [SerializeField] private ExposureState _currentState = ExposureState.AwaitingStart;
        [SerializeField] private int _currentTier = 1;
        [SerializeField] private float _calmTimer = 0f;
        [SerializeField] private float _recoveryTimer = 0f;
        [SerializeField] private bool _isCurrentlySteady = true;
        private bool _wasOfferingSupport = false;

        [Tooltip("Begin the session automatically after this long. The opening ritual is " +
                 "the introduction, so there is nothing for a start prompt to add, and a " +
                 "keypress cannot reach Unity while the XR Simulator window has focus.")]
        [SerializeField, Range(0f, 10f)] private float _autoStartSeconds = 1.0f;

        private float _awaitingTimer;
#pragma warning disable 0414
        [SerializeField] private bool _hasJerkedThisTier = false;
#pragma warning restore 0414

        // Current telemetry cache
        [SerializeField] private float _liveTremor = 0f;
        [SerializeField] private float _liveHesitation = 0f;
        [SerializeField] private bool _liveFrozen = false;
        [SerializeField] private float _liveGazeAvoidance = 0f;

        // Visual Palette matching FloatingInstructionHUD
        private static readonly Color ColorSteady = new Color(0.00f, 0.90f, 0.46f, 1f);      // Emerald Green (#00E676)
        private static readonly Color ColorHesitation = new Color(0.95f, 0.61f, 0.07f, 1f);   // Orange/Amber (#F39C12)
        private static readonly Color ColorSafety = new Color(1.00f, 0.24f, 0.00f, 1f);       // Bright Safety Alert (#FF3D00)
        private static readonly Color ColorComplete = new Color(0.00f, 0.82f, 0.83f, 1f);     // Cyan (#00D2D3)

        // Tier Instruction Descriptions
        private static readonly string[] TierNames = new string[]
        {
            "ENCLOSED LEDGE",
            "OPEN EDGE",
            "CRANE SUSPENSION",
            "WIDTH CONTRACTION",
            "RAZOR PLANK & GLASS FLOOR"
        };

        private static readonly string[] TierInstructions = new string[]
        {
            "Take deep breaths. Look down past the railing at the street below.",
            "Railing lowered. Step to the edge and look down at the street.",
            "Step onto the suspended crane platform. Maintain steady balance.",
            "Platform width is narrowing. Keep your footing steady.",
            "Narrow glass plank. Look straight down through the glass."
        };

        // Events
        public event Action<int> OnTierAdvanced;
        public event Action<int> OnTierSteppedBack;
        public event Action OnSafetyInterventionTriggered;
        public event Action OnSessionCompleted;
        public event Action OnSessionEndedByUser;

        public ExposureState CurrentState => _currentState;
        public int CurrentTier => _currentTier;
        public float CalmTimer => _calmTimer;
        public float TargetTierCalmSeconds => _targetTierCalmSeconds;
        public bool IsCurrentlySteady => _isCurrentlySteady;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();

            if (_hud != null)
            {
                _hud.ExternalFsmControlled = true;
                _hud.OnSessionStarted += HandleHudSessionStarted;
            }

            // Sync initial state
            _currentState = ExposureState.AwaitingStart;
            _currentTier = 1;
            _calmTimer = 0f;
            _hasJerkedThisTier = false;

            if (_catwalkController != null)
            {
                _catwalkController.SetTier(1, true);
            }
        }

        private void OnDestroy()
        {
            if (_hud != null)
            {
                _hud.OnSessionStarted -= HandleHudSessionStarted;
            }
        }

        private void Update()
        {
            PollTelemetry();

            switch (_currentState)
            {
                case ExposureState.AwaitingStart:
                    UpdateAwaitingStart();
                    break;

                case ExposureState.Exposing:
                    UpdateExposing();
                    break;

                case ExposureState.SafetyRecovery:
                    UpdateSafetyRecovery();
                    break;

                case ExposureState.Completed:
                    UpdateCompleted();
                    break;
            }

            HandleParticipantControls();
        }

        /// <summary>
        /// Automatically locates all connected subsystems and sensors if unassigned.
        /// </summary>
        public void ResolveReferences()
        {
            if (_hud == null) _hud = FindAnyObjectByType<FloatingInstructionHUD>();
            if (_catwalkController == null) _catwalkController = FindAnyObjectByType<DynamicCatwalkController>();
            if (_audioManager == null) _audioManager = FindAnyObjectByType<ExposureAudioManager>();

            if (_tremorFilter == null) _tremorFilter = FindAnyObjectByType<TremorBandFilter>();
            if (_swayAnalyzer == null) _swayAnalyzer = FindAnyObjectByType<PosturalSwayAnalyzer>();
            if (_edgeTracker == null) _edgeTracker = FindAnyObjectByType<EdgeProximityTracker>();
            if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();
        }

        private void PollTelemetry()
        {
            _liveTremor = _tremorFilter != null ? _tremorFilter.TremorIntensity : 0f;
            _liveHesitation = _edgeTracker != null ? _edgeTracker.HesitationIndex : 0f;
            _liveFrozen = _swayAnalyzer != null && _swayAnalyzer.IsPosturalFrozen;
            _liveGazeAvoidance = _gazeDetector != null ? _gazeDetector.GazeAvoidanceRatio : 0f;
        }

        private void HandleHudSessionStarted()
        {
            if (_currentState == ExposureState.AwaitingStart)
            {
                StartExposureSession();
            }
        }

        /// <summary>
        /// Begins the exposure protocol at Tier 1.
        /// </summary>
        public void StartExposureSession()
        {
            _currentState = ExposureState.Exposing;
            _currentTier = 1;
            _calmTimer = 0f;
            _hasJerkedThisTier = false;

            if (_catwalkController != null)
            {
                _catwalkController.SetTier(1);
            }

            if (_audioManager != null)
            {
                _audioManager.StartAmbientLoops();
                _audioManager.SetTierWindIntensity(1);
            }

            ApplyTierHUD(_currentTier);
        }

        private void UpdateAwaitingStart()
        {
            // If the user presses space, enter, or VR button, start the session
            bool startPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame ||
                 UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame))
            {
                startPressed = true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    startPressed = true;
                }
            }
            catch (Exception) { }
#endif
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One) ||
                    OVRInput.GetDown(OVRInput.RawButton.A) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
                {
                    startPressed = true;
                }
            }
            catch (Exception) { }

            // The session also starts on its own. Requiring a keypress stranded the
            // whole opening - ritual, calibration, baseline rating - behind an input
            // that never arrives while the simulator window holds focus.
            _awaitingTimer += Time.deltaTime;
            if (_autoStartSeconds > 0f && _awaitingTimer >= _autoStartSeconds)
            {
                startPressed = true;
            }

            if (startPressed)
            {
                if (_hud != null) _hud.StartSession();
                StartExposureSession();
            }
        }

        private void UpdateExposing()
        {
            // 1. Distress support. This used to call TriggerSafetyIntervention(), which
            // reverted the platform to tier 1 on its own. That was wrong twice over: an
            // automatic revert is an un-requested change to the height hierarchy, which
            // the brief forbids as much as auto-advancing; and postural stillness is the
            // goal of the exercise, not evidence of panic. A participant standing
            // motionless at an edge looking down is succeeding, and the freeze detector
            // cannot tell that apart from fear-immobility.
            //
            // So nothing is changed for them. High distress surfaces an offer, and the
            // participant decides whether to step back or stop.
            bool highDistress = _liveTremor > _panicTremorThreshold;
            if (highDistress != _wasOfferingSupport)
            {
                _wasOfferingSupport = highDistress;

                if (highDistress)
                {
                    if (_hud != null)
                    {
                        _hud.SetInstruction("Breathe slowly. You can step back or end the session whenever you want.");
                        _hud.SetStatus(true, "HIGH AROUSAL", ColorSafety);
                    }

                    OnSafetyInterventionTriggered?.Invoke();
                }
                else
                {
                    ApplyTierHUD(_currentTier);
                }
            }

            // 2. Steady / Hesitation evaluation. Display and telemetry only - it no
            // longer accumulates toward anything. The brief requires that nothing
            // auto-advances the height hierarchy and that there are no timers, so the
            // calm timer, the countdown and the automatic progression are all gone.
            // The participant advances when they choose to, via RequestAdvanceTier().
            _isCurrentlySteady = (_liveTremor < _steadyTremorThreshold) && (_liveHesitation < _steadyHesitationThreshold);

            if (_hud != null)
            {
                if (_isCurrentlySteady)
                {
                    _hud.SetStatus(false, "STATUS: STEADY", ColorSteady);
                }
                else
                {
                    _hud.SetStatus(true, "TAKE YOUR TIME", ColorHesitation);
                }
            }

            // No stability perturbation. The cable jerk that used to fire at 15s in
            // tiers 3-5 was a drop the participant did not initiate, which the brief
            // forbids outright.
        }

        /// <summary>
        /// Advances one tier. The only route to a higher tier, and only ever called in
        /// response to a deliberate participant action.
        /// </summary>
        public void RequestAdvanceTier()
        {
            if (_currentState != ExposureState.Exposing)
            {
                return;
            }

            AdvanceToNextTier();
        }

        /// <summary>
        /// Steps back one tier at the participant's request. The brief requires a
        /// reachable "step back" control at all times, and retreat must be as available
        /// as approach.
        /// </summary>
        public void RequestStepBack()
        {
            if (_currentState != ExposureState.Exposing && _currentState != ExposureState.SafetyRecovery)
            {
                return;
            }

            if (_currentTier <= 1)
            {
                if (_hud != null)
                {
                    _hud.SetInstruction("You are at the lowest level. You can end the session at any time.");
                }

                return;
            }

            _currentTier--;
            _currentState = ExposureState.Exposing;

            if (_catwalkController != null)
            {
                _catwalkController.SetTier(_currentTier);
            }

            if (_audioManager != null)
            {
                _audioManager.SetTierWindIntensity(_currentTier);
            }

            ApplyTierHUD(_currentTier);
            OnTierSteppedBack?.Invoke(_currentTier);
        }

        /// <summary>
        /// Ends the session immediately at the participant's request.
        /// </summary>
        public void RequestEndSession()
        {
            if (_currentState == ExposureState.Completed)
            {
                return;
            }

            _currentState = ExposureState.Completed;

            if (_hud != null)
            {
                _hud.SetInstruction("Session ended. Well done for the exposure you completed.");
                _hud.SetStatus(false, "STATUS: ENDED", ColorComplete);
            }

            OnSessionEndedByUser?.Invoke();
            OnSessionCompleted?.Invoke();
        }

        private void AdvanceToNextTier()
        {
            _calmTimer = 0f;
            _hasJerkedThisTier = false;

            // Play completion chime
            if (_audioManager != null)
            {
                _audioManager.PlayChime();
            }

            if (_currentTier < 5)
            {
                _currentTier++;
                if (_catwalkController != null)
                {
                    _catwalkController.SetTier(_currentTier);
                }
                if (_audioManager != null)
                {
                    _audioManager.SetTierWindIntensity(_currentTier);
                }

                ApplyTierHUD(_currentTier);
                OnTierAdvanced?.Invoke(_currentTier);
            }
            else
            {
                // Reaching the top tier does NOT end the session. It used to set
                // Completed, which made RequestStepBack return early - so the moment a
                // participant reached tier 5 the step-back control went dead, leaving
                // them stuck at 104 m with only "end session" available. That breaks the
                // rule that step-back stays reachable at all times.
                //
                // Ending is the participant's decision, via RequestEndSession().
                if (_hud != null)
                {
                    _hud.SetTier(5, TierNames[4]);
                    _hud.SetInstruction("Highest level reached. Stay as long as you want, step back down, or end the session.");
                    _hud.SetStatus(false, "STATUS: TOP LEVEL", ColorComplete);
                }
            }
        }

        private void TriggerSafetyIntervention()
        {
            _currentState = ExposureState.SafetyRecovery;
            _recoveryTimer = 0f;

            // 1. Revert environment immediately to Tier 1 safety
            if (_catwalkController != null)
            {
                _catwalkController.SetTier(1);
            }

            // 2. Play warning alert
            if (_audioManager != null)
            {
                _audioManager.PlayWarningBeep();
            }

            // 3. Update HUD with safety guidance
            if (_hud != null)
            {
                _hud.SetInstruction("SAFETY RECOVERY ACTIVATED - Focus on slow, deep breaths.\nRailing raised.");
                _hud.SetStatus(true, "SAFETY INTERVENTION", ColorSafety);
            }

            OnSafetyInterventionTriggered?.Invoke();
        }

        private void UpdateSafetyRecovery()
        {
            // Evaluate whether user is calm enough to resume
            bool isRecovering = (_liveTremor < (_steadyTremorThreshold + 0.05f)) && !_liveFrozen;

            if (isRecovering)
            {
                _recoveryTimer += Time.deltaTime;
                if (_hud != null)
                {
                    _hud.SetStatus(false, $"RECOVERING: {_recoveryTimer:F0}s / {_safetyRecoverySeconds:F0}s", ColorSteady);
                }

                if (_recoveryTimer >= _safetyRecoverySeconds)
                {
                    // Resume current tier
                    _currentState = ExposureState.Exposing;
                    _calmTimer = 0f;
                    _hasJerkedThisTier = false;

                    if (_catwalkController != null)
                    {
                        _catwalkController.SetTier(_currentTier);
                    }
                    ApplyTierHUD(_currentTier);
                }
            }
            else
            {
                _recoveryTimer = Mathf.Max(0f, _recoveryTimer - Time.deltaTime);
                if (_hud != null)
                {
                    _hud.SetStatus(true, "HIGH ANXIETY - BREATHE SLOWLY", ColorSafety);
                }
            }
        }

        private void UpdateCompleted()
        {
            // Maintain completion display
            // No progress bar: there is nothing counting down.
        }

        private void ApplyTierHUD(int tier)
        {
            if (_hud == null) return;

            int index = Mathf.Clamp(tier - 1, 0, TierNames.Length - 1);
            _hud.SetTier(tier, TierNames[index]);
            _hud.SetInstruction(TierInstructions[index]);
            _hud.SetStatus(false, "STATUS: STEADY", ColorSteady);
        }

        /// <summary>
        /// Participant controls. These are the only route through the hierarchy: advance,
        /// step back, and end. All three stay reachable in every exposure state, which is
        /// what the brief requires. The old cable-jerk key is gone with the jerk itself.
        /// </summary>
        private void HandleParticipantControls()
        {
            bool advance = false;
            bool stepBack = false;
            bool endSession = false;

            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One)) advance = true;
                if (OVRInput.GetDown(OVRInput.Button.Two)) stepBack = true;
                if (OVRInput.GetDown(OVRInput.Button.Start)) endSession = true;
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (kb.upArrowKey.wasPressedThisFrame) advance = true;
                    if (kb.downArrowKey.wasPressedThisFrame) stepBack = true;
                    if (kb.escapeKey.wasPressedThisFrame) endSession = true;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.UpArrow)) advance = true;
                if (Input.GetKeyDown(KeyCode.DownArrow)) stepBack = true;
                if (Input.GetKeyDown(KeyCode.Escape)) endSession = true;
            }
            catch (Exception) { }
#endif

            if (endSession) RequestEndSession();
            else if (advance) RequestAdvanceTier();
            else if (stepBack) RequestStepBack();
        }
    }
}
