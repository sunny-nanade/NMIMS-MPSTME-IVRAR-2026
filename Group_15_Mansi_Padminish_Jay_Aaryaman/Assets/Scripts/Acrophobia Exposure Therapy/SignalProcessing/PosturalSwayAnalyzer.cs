using System;
using UnityEngine;

namespace Acrophobia.SignalProcessing
{
    /// <summary>
    /// Analyzes postural sway dynamics in the horizontal plane (0.2 Hz to 1.0 Hz band)
    /// to detect center-of-mass sway velocity and behavioral freezing
    /// (sustained rigid immobility indicating acute acrophobic fear/hesitation).
    /// </summary>
    [RequireComponent(typeof(HeadPoseSampler))]
    public class PosturalSwayAnalyzer : MonoBehaviour
    {
        [Header("Sway Filter Bandwidth (0.2 Hz - 1.0 Hz)")]
        [Tooltip("Low cutoff frequency in Hz for horizontal postural sway.")]
        [SerializeField, Range(0.05f, 0.5f)] private float _swayLowCutoffHz = 0.2f;

        [Tooltip("High cutoff frequency in Hz for horizontal postural sway.")]
        [SerializeField, Range(0.5f, 2.5f)] private float _swayHighCutoffHz = 1.0f;

        [Header("Center-of-Mass Window Settings")]
        [Tooltip("Time window in seconds used to evaluate center-of-mass path length and wander.")]
        [SerializeField, Range(1.0f, 5.0f)] private float _wanderWindowSeconds = 2.5f;

        [Header("Postural Freezing Thresholds")]
        [Tooltip("Maximum velocity magnitude (m/s) considered near-zero / frozen.")]
        [SerializeField, Range(0.005f, 0.05f)] private float _freezeVelocityThreshold = 0.018f;

        [Tooltip("Minimum continuous duration (seconds) under the freeze velocity threshold to trigger freezing.")]
        [SerializeField, Range(0.5f, 4.0f)] private float _freezeDurationThreshold = 1.5f;

        [Tooltip("Velocity magnitude required to break out of the frozen state.")]
        [SerializeField, Range(0.02f, 0.1f)] private float _unfreezeVelocityThreshold = 0.035f;

        [Header("Runtime Metrics")]
        [SerializeField, ReadOnlyInspector] private float _currentSwayVelocity;
        [SerializeField, ReadOnlyInspector] private float _centerOfMassWander;
        [SerializeField, ReadOnlyInspector] private bool _isPosturalFrozen;
        [SerializeField, ReadOnlyInspector] private float _currentFreezeDuration;
        [SerializeField, ReadOnlyInspector] private Vector2 _horizontalDisplacement;

        private HeadPoseSampler _sampler;
        private TremorBandFilter _tremorFilter;

        private TremorBandFilter.BiquadFilter _swayFilterX;
        private TremorBandFilter.BiquadFilter _swayFilterZ;
        private float _lastSampleRate = -1f;

        // Rolling buffer for horizontal position samples to compute wander
        private const int MaxWanderSamples = 256;
        private readonly Vector2[] _horizontalPositions = new Vector2[MaxWanderSamples];
        private int _posBufferHead = 0;
        private int _posBufferCount = 0;

        private Vector2 _baselineHorizontalPosition;
        private bool _hasBaseline = false;

        /// <summary>
        /// Filtered horizontal center-of-mass sway velocity in the 0.2 Hz - 1.0 Hz band (m/s).
        /// </summary>
        public float HorizontalSwayVelocity { get; private set; }

        /// <summary>
        /// Alias for HorizontalSwayVelocity.
        /// </summary>
        public float SwayInstability => HorizontalSwayVelocity;

        /// <summary>
        /// Total horizontal path length / wander of the center of mass over the rolling window (meters).
        /// </summary>
        public float CenterOfMassWander => _centerOfMassWander;

        /// <summary>
        /// True if the user is currently exhibiting acute postural freezing (sustained rigid immobility).
        /// </summary>
        public bool IsPosturalFrozen => _isPosturalFrozen;

        /// <summary>
        /// Current duration in seconds spent in the near-zero velocity frozen condition.
        /// </summary>
        public float CurrentFreezeDuration => _currentFreezeDuration;

        /// <summary>
        /// Horizontal displacement (X, Z in meters) relative to recent baseline position.
        /// </summary>
        public Vector2 HorizontalDisplacement => _horizontalDisplacement;

        /// <summary>
        /// Event fired with updated tremor intensity (0.0 to 1.0 range).
        /// </summary>
        public event Action<float> OnTremorUpdated;

        /// <summary>
        /// Event fired when acute behavioral freezing (sustained rigid immobility) is detected.
        /// </summary>
        public event Action OnPosturalFreezeDetected;

        /// <summary>
        /// Event fired when the user breaks out of the frozen state and resumes movement.
        /// </summary>
        public event Action OnPosturalFreezeEnded;

        private void Awake()
        {
            if (_sampler == null)
            {
                _sampler = GetComponent<HeadPoseSampler>();
                if (_sampler == null) _sampler = FindAnyObjectByType<HeadPoseSampler>();
            }

            if (_tremorFilter == null)
            {
                _tremorFilter = GetComponent<TremorBandFilter>();
                if (_tremorFilter == null) _tremorFilter = FindAnyObjectByType<TremorBandFilter>();
            }

            Initialize(_sampler, _tremorFilter);
        }

        private void OnEnable()
        {
            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
                _sampler.OnSampleRecorded += HandleSampleRecorded;
            }
            if (_tremorFilter != null)
            {
                _tremorFilter.OnTremorUpdated -= HandleTremorUpdated;
                _tremorFilter.OnTremorUpdated += HandleTremorUpdated;
            }
        }

        private void OnDisable()
        {
            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
            }
            if (_tremorFilter != null)
            {
                _tremorFilter.OnTremorUpdated -= HandleTremorUpdated;
            }
        }

        /// <summary>
        /// Explicitly initializes or binds the sway analyzer to sampler and tremor filter.
        /// Useful for testing, dynamic setup, or runtime re-binding.
        /// </summary>
        public void Initialize(HeadPoseSampler sampler, TremorBandFilter tremorFilter = null)
        {
            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
            }
            if (_tremorFilter != null)
            {
                _tremorFilter.OnTremorUpdated -= HandleTremorUpdated;
            }

            _sampler = sampler;
            _tremorFilter = tremorFilter;

            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
                _sampler.OnSampleRecorded += HandleSampleRecorded;
            }
            if (_tremorFilter != null)
            {
                _tremorFilter.OnTremorUpdated -= HandleTremorUpdated;
                _tremorFilter.OnTremorUpdated += HandleTremorUpdated;
            }

            InitializeSwayFilters();
        }

        private void InitializeSwayFilters()
        {
            float sampleRate = _sampler != null ? _sampler.SampleRate : (Time.fixedDeltaTime > 0 ? 1f / Time.fixedDeltaTime : 90f);
            _lastSampleRate = sampleRate;

            _swayFilterX = new TremorBandFilter.BiquadFilter();
            _swayFilterZ = new TremorBandFilter.BiquadFilter();

            UpdateFilterCoefficients(sampleRate);
        }

        private void UpdateFilterCoefficients(float sampleRate)
        {
            float fLow = Mathf.Max(0.05f, _swayLowCutoffHz);
            float fHigh = Mathf.Max(fLow + 0.1f, _swayHighCutoffHz);

            _swayFilterX.ConfigureBandpass(fLow, fHigh, sampleRate);
            _swayFilterZ.ConfigureBandpass(fLow, fHigh, sampleRate);
        }

        private void HandleTremorUpdated(float tremorIntensity)
        {
            OnTremorUpdated?.Invoke(tremorIntensity);
        }

        private void HandleSampleRecorded(Vector3 position, Vector3 velocity, float speed, float dt)
        {
            ProcessKinematicSample(position, velocity, speed, dt);
        }

        /// <summary>
        /// Processes a kinematic sample to compute horizontal sway velocity and detect behavioral freezing.
        /// </summary>
        public void ProcessKinematicSample(Vector3 position, Vector3 velocity, float speed, float dt)
        {
            if (dt <= 0f) return;

            // Same guard as TremorBandFilter. This also subscribes to the sampler's
            // event, and a subscriber that throws can stop the rest of the invocation
            // list from running - so the tremor crash was very likely taking sway down
            // with it.
            if (_swayFilterX == null || _swayFilterZ == null)
            {
                InitializeSwayFilters();
            }

            // Recalculate filter coefficients if sample rate changed
            float currentSampleRate = 1f / dt;
            if (Mathf.Abs(currentSampleRate - _lastSampleRate) > 1.0f)
            {
                _lastSampleRate = currentSampleRate;
                UpdateFilterCoefficients(currentSampleRate);
            }

            // 1. Process Low-Frequency Horizontal Postural Sway (0.2 Hz to 1.0 Hz)
            float swayX = _swayFilterX.Process(velocity.x);
            float swayZ = _swayFilterZ.Process(velocity.z);
            HorizontalSwayVelocity = Mathf.Sqrt(swayX * swayX + swayZ * swayZ);
            _currentSwayVelocity = HorizontalSwayVelocity;

            // 2. Track Horizontal Center-of-Mass Wander over rolling window
            Vector2 horizPos = new Vector2(position.x, position.z);
            UpdateWanderTracking(horizPos, dt);

            // 3. Detect Behavioral Freezing (sustained rigid immobility)
            UpdateFreezingDetection(speed, dt);
        }

        private void UpdateWanderTracking(Vector2 horizPos, float dt)
        {
            if (!_hasBaseline)
            {
                _baselineHorizontalPosition = horizPos;
                _hasBaseline = true;
            }
            else
            {
                // Slow baseline drift tracking (re-centering over ~10 seconds)
                _baselineHorizontalPosition = Vector2.Lerp(_baselineHorizontalPosition, horizPos, dt * 0.1f);
            }

            _horizontalDisplacement = horizPos - _baselineHorizontalPosition;

            // Store position in circular buffer
            _horizontalPositions[_posBufferHead] = horizPos;
            _posBufferHead = (_posBufferHead + 1) % MaxWanderSamples;
            if (_posBufferCount < MaxWanderSamples) _posBufferCount++;

            // Calculate cumulative path length over the configured wander window
            int windowSampleCount = Mathf.Min(_posBufferCount, Mathf.RoundToInt(_wanderWindowSeconds / dt));
            if (windowSampleCount < 2)
            {
                _centerOfMassWander = 0f;
                return;
            }

            float cumulativeDistance = 0f;
            int currentIndex = (_posBufferHead - 1 + MaxWanderSamples) % MaxWanderSamples;

            for (int i = 0; i < windowSampleCount - 1; i++)
            {
                int prevIndex = (currentIndex - 1 + MaxWanderSamples) % MaxWanderSamples;
                cumulativeDistance += Vector2.Distance(_horizontalPositions[currentIndex], _horizontalPositions[prevIndex]);
                currentIndex = prevIndex;
            }

            _centerOfMassWander = cumulativeDistance;
        }

        private void UpdateFreezingDetection(float speed, float dt)
        {
            if (!_isPosturalFrozen)
            {
                if (speed <= _freezeVelocityThreshold)
                {
                    _currentFreezeDuration += dt;
                    if (_currentFreezeDuration >= _freezeDurationThreshold)
                    {
                        _isPosturalFrozen = true;
                        OnPosturalFreezeDetected?.Invoke();
                    }
                }
                else
                {
                    _currentFreezeDuration = Mathf.Max(0f, _currentFreezeDuration - dt * 2f);
                }
            }
            else
            {
                // User is in frozen state
                if (speed >= _unfreezeVelocityThreshold)
                {
                    _isPosturalFrozen = false;
                    _currentFreezeDuration = 0f;
                    OnPosturalFreezeEnded?.Invoke();
                }
                else
                {
                    _currentFreezeDuration += dt;
                }
            }
        }

        /// <summary>
        /// Manually resets the center-of-mass wander baseline and freezing timer.
        /// </summary>
        public void ResetBaseline()
        {
            _hasBaseline = false;
            _posBufferCount = 0;
            _posBufferHead = 0;
            _currentFreezeDuration = 0f;
            _isPosturalFrozen = false;
        }
    }
}
