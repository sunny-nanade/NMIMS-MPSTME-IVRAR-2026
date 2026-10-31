using System;
using UnityEngine;
using Acrophobia.GazeGeometry;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// Measures the participant's standing eye height at session start.
    ///
    /// Height accuracy is the therapeutic mechanism in this project, so every height
    /// figure in the telemetry has to be interpretable per participant - a reported
    /// 104 m means something different to someone whose eyes sit at 1.55 m than to
    /// someone at 1.90 m.
    ///
    /// The second job matters more than the measurement itself: it validates that the
    /// tracking origin really is floor level. With OVRManager set to FloorLevel the
    /// camera's height above the rig IS eye height above the physical floor, so a
    /// plausible reading (roughly 1.2-2.2 m) confirms the whole height chain. A reading
    /// near zero means the origin is eye level instead, which would silently make every
    /// height in the scene and in the telemetry wrong while still looking fine in the
    /// headset. That failure is invisible without this check.
    ///
    /// Calibration is explicitly user-initiated and can be cancelled at any point.
    /// Nothing in the exposure progression depends on it.
    /// </summary>
    public class EyeHeightCalibrator : MonoBehaviour
    {
        private const int MaxSamples = 256;

        [SerializeField, Range(0.5f, 5f)] private float _calibrationSeconds = 1.5f;

        [Tooltip("Samples per second taken while calibrating.")]
        [SerializeField, Range(10f, 90f)] private float _sampleRateHz = 30f;

        [Tooltip("Calibration is rejected if head height varies more than this during the window.")]
        [SerializeField, Range(0.01f, 0.3f)] private float _maxHeightVariance = 0.06f;

        [Tooltip("Lower bound for a believable standing eye height, in metres.")]
        [SerializeField] private float _plausibleMinimum = 1.20f;

        [Tooltip("Upper bound for a believable standing eye height, in metres.")]
        [SerializeField] private float _plausibleMaximum = 2.20f;

        [SerializeField] private Transform _headTransform;
        [SerializeField] private Transform _trackingOriginTransform;
        [SerializeField] private SessionTelemetryLogger _logger;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;

        private readonly float[] _samples = new float[MaxSamples];
        private int _sampleCount;
        private float _sampleAccumulator;
        private float _sampleInterval;
        private float _elapsed;
        private bool _isCalibrating;

        private float _calibratedEyeHeight = -1f;

        public bool IsCalibrating => _isCalibrating;

        public bool IsCalibrated => _calibratedEyeHeight > 0f;

        /// <summary>
        /// Measured eye height above the tracking floor in metres, or -1 if not yet measured.
        /// </summary>
        public float CalibratedEyeHeight => _calibratedEyeHeight;

        public float CalibrationProgress =>
            _isCalibrating && _calibrationSeconds > 0f ? Mathf.Clamp01(_elapsed / _calibrationSeconds) : 0f;

        public event Action<float> OnCalibrationComplete;

        public event Action<string> OnCalibrationFailed;

        public event Action OnCalibrationStarted;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();
            if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();

            if (_headTransform == null && _gazeDetector != null)
            {
                _headTransform = _gazeDetector.CenterEyeAnchor;
            }

            if (_headTransform == null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    _headTransform = cam.transform;
                }
            }

            if (_trackingOriginTransform == null && _headTransform != null)
            {
                // Walk up to the rig root. With OVRCameraRig the hierarchy is
                // rig -> TrackingSpace -> CenterEyeAnchor, so two levels up is the rig.
                Transform t = _headTransform.parent;
                if (t != null && t.parent != null)
                {
                    _trackingOriginTransform = t.parent;
                }
                else if (t != null)
                {
                    _trackingOriginTransform = t;
                }
            }
        }

        /// <summary>
        /// Starts a calibration window. Ask the participant to stand still and look ahead.
        /// </summary>
        public void BeginCalibration()
        {
            ResolveReferences();

            if (_headTransform == null)
            {
                Fail("no_head_transform");
                return;
            }

            _isCalibrating = true;
            _sampleCount = 0;
            _elapsed = 0f;
            _sampleAccumulator = 0f;
            _sampleInterval = 1f / Mathf.Max(1f, _sampleRateHz);

            OnCalibrationStarted?.Invoke();
        }

        /// <summary>
        /// Abandons an in-progress calibration without recording anything.
        /// </summary>
        public void CancelCalibration()
        {
            if (!_isCalibrating)
            {
                return;
            }

            _isCalibrating = false;
            _sampleCount = 0;
            OnCalibrationFailed?.Invoke("cancelled_by_user");
        }

        private void Update()
        {
            if (!_isCalibrating)
            {
                return;
            }

            if (_headTransform == null)
            {
                Fail("head_transform_lost");
                return;
            }

            float dt = Time.deltaTime;
            _elapsed += dt;
            _sampleAccumulator += dt;

            if (_sampleAccumulator >= _sampleInterval)
            {
                _sampleAccumulator -= _sampleInterval;
                if (_sampleAccumulator > _sampleInterval)
                {
                    _sampleAccumulator = 0f;
                }

                if (_sampleCount < MaxSamples)
                {
                    _samples[_sampleCount++] = MeasureCurrentEyeHeight();
                }
            }

            if (_elapsed >= _calibrationSeconds)
            {
                CompleteCalibration();
            }
        }

        /// <summary>
        /// Eye height above the tracking floor. Measured in the rig's local space so a
        /// rooftop at Y=104 does not leak into the figure.
        /// </summary>
        private float MeasureCurrentEyeHeight()
        {
            if (_trackingOriginTransform != null)
            {
                return _trackingOriginTransform.InverseTransformPoint(_headTransform.position).y;
            }

            return _headTransform.localPosition.y;
        }

        private void CompleteCalibration()
        {
            _isCalibrating = false;

            if (_sampleCount < 3)
            {
                Fail("too_few_samples");
                return;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            float sum = 0f;

            for (int i = 0; i < _sampleCount; i++)
            {
                float v = _samples[i];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float spread = max - min;
            if (spread > _maxHeightVariance)
            {
                // Moving, crouching, or mid-step. A bad measurement is worse than none,
                // because it would be recorded as fact against every height in the session.
                Fail("too_much_movement");
                return;
            }

            float mean = sum / _sampleCount;

            if (mean < _plausibleMinimum || mean > _plausibleMaximum)
            {
                Debug.LogWarning(
                    "[EyeHeightCalibrator] Measured eye height " + mean.ToString("F3") +
                    " m is outside the plausible range " + _plausibleMinimum.ToString("F2") +
                    "-" + _plausibleMaximum.ToString("F2") + " m. The usual cause is " +
                    "OVRManager tracking origin being set to Eye Level rather than Floor Level, " +
                    "which would make every height reading in the scene and in the telemetry " +
                    "wrong. Check OVRManager > Tracking Origin Type before trusting this session.",
                    this);

                _logger?.RecordEvent("eye_height_implausible", 0, "metres", mean);
            }

            _calibratedEyeHeight = mean;

            _logger?.RecordEyeHeightCalibration(mean);
            _logger?.RecordEvent("eye_height_spread", 0, "metres", spread);

            Debug.Log(
                "[EyeHeightCalibrator] Eye height " + mean.ToString("F3") + " m " +
                "(spread " + spread.ToString("F3") + " m over " + _sampleCount + " samples).",
                this);

            OnCalibrationComplete?.Invoke(mean);
        }

        private void Fail(string reason)
        {
            _isCalibrating = false;
            _sampleCount = 0;

            Debug.LogWarning("[EyeHeightCalibrator] Calibration failed: " + reason, this);
            _logger?.RecordEvent("eye_height_calibration_failed", 0, reason, 0f);

            OnCalibrationFailed?.Invoke(reason);
        }
    }
}
