using System;
using UnityEngine;
using Acrophobia.Telemetry;

namespace Acrophobia.AdaptiveFSM
{
    /// <summary>
    /// Orchestrates the session: calibrate, then ask for a distress rating at the moments
    /// that matter.
    ///
    /// The telemetry components existed and were individually verified, but nothing ever
    /// called them, so a real session produced a CSV with no SUDS rows and no eye-height
    /// row - two named spec requirements silently absent. This is the piece that invokes
    /// them.
    ///
    /// SUDS is requested at four points: before exposure begins (baseline), on entering a
    /// tier, on stepping back, and at the end. Baseline before anything else matters most,
    /// because every later rating is only interpretable relative to it.
    ///
    /// Nothing here gates or advances. The prompt is dismissable, a dismissal is recorded
    /// as a skip rather than invented as a number, and the participant's tier is never
    /// changed by this component.
    /// </summary>
    public class SessionFlowController : MonoBehaviour
    {
        [Tooltip("Seconds to wait after a tier change before asking, so the participant can look around first.")]
        [SerializeField, Range(0f, 15f)] private float _sudsDelayAfterTierChange = 4f;

        [Tooltip("Run eye-height calibration when the session starts.")]
        [SerializeField] private bool _calibrateOnSessionStart = true;

        [Tooltip("Ask for a baseline rating before exposure begins.")]
        [SerializeField] private bool _askBaselineSuds = true;

        [SerializeField] private AdaptiveExposureFSM _fsm;
        [SerializeField] private SudsPromptController _suds;
        [SerializeField] private EyeHeightCalibrator _calibrator;
        [SerializeField] private SessionTelemetryLogger _logger;

        private AdaptiveExposureFSM.ExposureState _lastState = AdaptiveExposureFSM.ExposureState.AwaitingStart;
        private bool _sessionStartHandled;
        private float _pendingSudsTimer = -1f;
        private string _pendingSudsContext;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced += HandleTierAdvanced;
                _fsm.OnTierSteppedBack += HandleTierSteppedBack;
                _fsm.OnSessionCompleted += HandleSessionEnded;
            }

            if (_calibrator != null)
            {
                _calibrator.OnCalibrationComplete += HandleCalibrationComplete;
                _calibrator.OnCalibrationFailed += HandleCalibrationFailed;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= HandleTierAdvanced;
                _fsm.OnTierSteppedBack -= HandleTierSteppedBack;
                _fsm.OnSessionCompleted -= HandleSessionEnded;
            }

            if (_calibrator != null)
            {
                _calibrator.OnCalibrationComplete -= HandleCalibrationComplete;
                _calibrator.OnCalibrationFailed -= HandleCalibrationFailed;
            }
        }

        public void ResolveReferences()
        {
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();
            if (_suds == null) _suds = FindAnyObjectByType<SudsPromptController>();
            if (_calibrator == null) _calibrator = FindAnyObjectByType<EyeHeightCalibrator>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();
        }

        private void Update()
        {
            if (_fsm == null)
            {
                return;
            }

            // The FSM has no "session started" event, so the transition out of
            // AwaitingStart is detected here rather than adding coupling to it.
            var state = _fsm.CurrentState;
            if (state != _lastState)
            {
                if (_lastState == AdaptiveExposureFSM.ExposureState.AwaitingStart
                    && state == AdaptiveExposureFSM.ExposureState.Exposing
                    && !_sessionStartHandled)
                {
                    _sessionStartHandled = true;
                    HandleSessionStart();
                }

                _lastState = state;
            }

            if (_pendingSudsTimer > 0f)
            {
                _pendingSudsTimer -= Time.deltaTime;
                if (_pendingSudsTimer <= 0f)
                {
                    _pendingSudsTimer = -1f;
                    ShowSuds(_pendingSudsContext);
                }
            }
        }

        private void HandleSessionStart()
        {
            if (_calibrateOnSessionStart && _calibrator != null && !_calibrator.IsCalibrated)
            {
                // Calibration first: it needs a couple of still seconds, and the baseline
                // rating is more meaningful once the participant has settled.
                _calibrator.BeginCalibration();
                return;
            }

            if (_askBaselineSuds)
            {
                ShowSuds("baseline");
            }
        }

        private void HandleCalibrationComplete(float eyeHeight)
        {
            if (_askBaselineSuds)
            {
                ShowSuds("baseline");
            }
        }

        private void HandleCalibrationFailed(string reason)
        {
            // Calibration is not a gate. A failure is recorded by the calibrator itself;
            // the session continues so a bad measurement never blocks exposure.
            if (_askBaselineSuds)
            {
                ShowSuds("baseline");
            }
        }

        private void HandleTierAdvanced(int tier)
        {
            QueueSuds("tier_entry");
        }

        private void HandleTierSteppedBack(int tier)
        {
            QueueSuds("after_step_back");
        }

        private void HandleSessionEnded()
        {
            // Asked immediately: delaying risks the participant removing the headset first.
            ShowSuds("session_end");
        }

        /// <summary>
        /// Schedules a rating request, giving the participant a moment to take in the new
        /// height before being asked about it.
        /// </summary>
        private void QueueSuds(string context)
        {
            _pendingSudsContext = context;
            _pendingSudsTimer = Mathf.Max(0.01f, _sudsDelayAfterTierChange);
        }

        private void ShowSuds(string context)
        {
            if (_suds == null)
            {
                _logger?.RecordEvent("suds_prompt_unavailable", 0, context, 0f);
                return;
            }

            if (_suds.IsOpen)
            {
                return;
            }

            _suds.Show(context);
        }
    }
}
