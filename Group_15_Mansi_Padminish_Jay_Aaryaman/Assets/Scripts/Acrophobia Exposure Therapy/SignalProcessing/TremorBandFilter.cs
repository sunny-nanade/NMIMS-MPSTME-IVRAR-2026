using System;
using UnityEngine;

namespace Acrophobia.SignalProcessing
{
    /// <summary>
    /// Digital 2nd-order Butterworth bandpass filter targeting 8.0 Hz to 12.0 Hz,
    /// corresponding to the physiological micro-tremor frequency band observed during
    /// acrophobic anxiety and postural hesitation.
    /// Attenuates voluntary intentional head turns (<2.0 Hz) and sensor tracking jitter (>16.0 Hz).
    /// </summary>
    [RequireComponent(typeof(HeadPoseSampler))]
    public class TremorBandFilter : MonoBehaviour
    {
        [Header("Filter Bandwidth Configuration")]
        [Tooltip("Low cutoff frequency in Hz. Attenuates intentional head turns below this band.")]
        [SerializeField, Range(1f, 15f)] private float _lowCutoffHz = 8.0f;

        [Tooltip("High cutoff frequency in Hz. Attenuates high-frequency sensor noise above this band.")]
        [SerializeField, Range(8f, 25f)] private float _highCutoffHz = 12.0f;

        [Header("Normalization & Sensitivity")]
        [Tooltip("Baseline physiological noise floor in m/s below which tremor intensity is zero.")]
        [SerializeField, Range(0.0001f, 0.01f)] private float _noiseFloor = 0.0015f;

        [Tooltip("Maximum expected micro-tremor velocity magnitude in m/s representing 1.0 intensity.")]
        [SerializeField, Range(0.01f, 0.1f)] private float _maxTremorMagnitude = 0.035f;

        [Tooltip("Time constant in seconds for smoothing the tremor envelope.")]
        [SerializeField, Range(0.05f, 1.0f)] private float _envelopeSmoothingTime = 0.35f;

        [Header("Runtime Output")]
        [SerializeField, ReadOnlyInspector] private float _currentTremorIntensity;
        [SerializeField, ReadOnlyInspector] private float _rawTremorMagnitude;

        private HeadPoseSampler _sampler;
        private BiquadFilter _filterX;
        private BiquadFilter _filterY;
        private BiquadFilter _filterZ;

        private float _currentEnvelope = 0f;
        private float _lastSampleRate = -1f;

        /// <summary>
        /// Normalized micro-tremor intensity in the range 0.0 (calm/baseline) to 1.0 (acute anxiety/tremor).
        /// </summary>
        public float TremorIntensity { get; private set; }

        /// <summary>
        /// Instantaneous magnitude of the bandpass-filtered velocity vector (m/s).
        /// </summary>
        public float InstantaneousTremorMagnitude => _rawTremorMagnitude;

        /// <summary>
        /// Filtered 3D tremor velocity oscillation vector (m/s).
        /// </summary>
        public Vector3 FilteredTremorVector { get; private set; }

        /// <summary>
        /// Event invoked whenever a new tremor intensity value is computed.
        /// </summary>
        public event Action<float> OnTremorUpdated;

        private void Awake()
        {
            if (_sampler == null)
            {
                _sampler = GetComponent<HeadPoseSampler>();
                if (_sampler == null) _sampler = FindAnyObjectByType<HeadPoseSampler>();
            }

            Initialize(_sampler);
        }

        private void OnEnable()
        {
            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
                _sampler.OnSampleRecorded += HandleSampleRecorded;
            }
        }

        private void OnDisable()
        {
            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
            }
        }

        /// <summary>
        /// Explicitly initializes or binds the filter to a specific sampler.
        /// Useful for testing, dynamic setup, or runtime re-binding.
        /// </summary>
        public void Initialize(HeadPoseSampler sampler)
        {
            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
            }

            _sampler = sampler;

            if (_sampler != null)
            {
                _sampler.OnSampleRecorded -= HandleSampleRecorded;
                _sampler.OnSampleRecorded += HandleSampleRecorded;
            }

            InitializeFilters();
        }

        private void InitializeFilters()
        {
            float sampleRate = _sampler != null ? _sampler.SampleRate : (Time.fixedDeltaTime > 0 ? 1f / Time.fixedDeltaTime : 90f);
            _lastSampleRate = sampleRate;

            _filterX = new BiquadFilter();
            _filterY = new BiquadFilter();
            _filterZ = new BiquadFilter();

            UpdateCoefficients(sampleRate);
        }

        private void UpdateCoefficients(float sampleRate)
        {
            float fLow = Mathf.Max(1f, _lowCutoffHz);
            float fHigh = Mathf.Max(fLow + 0.5f, _highCutoffHz);

            _filterX.ConfigureBandpass(fLow, fHigh, sampleRate);
            _filterY.ConfigureBandpass(fLow, fHigh, sampleRate);
            _filterZ.ConfigureBandpass(fLow, fHigh, sampleRate);
        }

        private void HandleSampleRecorded(Vector3 position, Vector3 velocity, float speed, float dt)
        {
            ProcessVelocitySample(velocity, dt);
        }

        /// <summary>
        /// Filters a 3D velocity vector sample through the 8.0 Hz - 12.0 Hz bandpass filter
        /// and updates the normalized TremorIntensity (0.0 to 1.0).
        /// </summary>
        public float ProcessVelocitySample(Vector3 velocity, float dt)
        {
            if (dt <= 0f) return TremorIntensity;

            // The filters are built in Awake, but this method is public and was being
            // reached before that had happened - throwing a NullReferenceException on
            // every sample, which both spammed the log and left TremorIntensity stuck at
            // zero. Since tremor feeds the hesitation index and the FSM, that silently
            // disabled a chunk of the telemetry. Build on demand instead of assuming.
            if (_filterX == null || _filterY == null || _filterZ == null)
            {
                InitializeFilters();
            }

            // Recalculate coefficients if sample rate changed dynamically
            float currentSampleRate = 1f / dt;
            if (Mathf.Abs(currentSampleRate - _lastSampleRate) > 1.0f)
            {
                _lastSampleRate = currentSampleRate;
                UpdateCoefficients(currentSampleRate);
            }

            // Filter each velocity axis through the 8.0 Hz - 12.0 Hz 2nd-order Butterworth bandpass
            float tx = _filterX.Process(velocity.x);
            float ty = _filterY.Process(velocity.y);
            float tz = _filterZ.Process(velocity.z);

            FilteredTremorVector = new Vector3(tx, ty, tz);
            _rawTremorMagnitude = FilteredTremorVector.magnitude;

            // Smooth envelope using leaky integrator (low-pass smoothing of the rectified oscillation)
            float alpha = Mathf.Clamp01(dt / Mathf.Max(0.01f, _envelopeSmoothingTime));
            _currentEnvelope = Mathf.Lerp(_currentEnvelope, _rawTremorMagnitude, alpha);

            // Normalize to 0.0 .. 1.0 range
            float normalized = Mathf.InverseLerp(_noiseFloor, _maxTremorMagnitude, _currentEnvelope);
            _currentTremorIntensity = Mathf.Clamp01(normalized);
            TremorIntensity = _currentTremorIntensity;

            OnTremorUpdated?.Invoke(TremorIntensity);
            return TremorIntensity;
        }

        /// <summary>
        /// Digital Biquad filter implementation for 2nd-order Butterworth bandpass filtering.
        /// Attenuates frequencies below fLow and above fHigh with 0 dB peak gain.
        /// </summary>
        public class BiquadFilter
        {
            private float _b0, _b1, _b2;
            private float _a1, _a2;
            private float _x1, _x2;
            private float _y1, _y2;

            /// <summary>
            /// Configures a 2nd-order Butterworth bandpass filter centered between fLow and fHigh.
            /// Uses standard bilinear transform with 0 dB peak gain.
            /// </summary>
            public void ConfigureBandpass(float fLow, float fHigh, float sampleRate)
            {
                float nyquist = sampleRate * 0.5f;
                fLow = Mathf.Clamp(fLow, 0.1f, nyquist - 1f);
                fHigh = Mathf.Clamp(fHigh, fLow + 0.1f, nyquist - 0.5f);

                // Geometric center frequency & bandwidth
                float f0 = Mathf.Sqrt(fLow * fHigh);
                float bw = fHigh - fLow;
                float q = Mathf.Max(0.1f, f0 / bw);

                float w0 = 2f * Mathf.PI * f0 / sampleRate;
                float sinW0 = Mathf.Sin(w0);
                float cosW0 = Mathf.Cos(w0);
                float alpha = sinW0 / (2f * q);

                // 2nd-order bandpass with constant skirt gain (peak gain = 0 dB)
                float b0 = alpha;
                float b1 = 0f;
                float b2 = -alpha;
                float a0 = 1f + alpha;
                float a1 = -2f * cosW0;
                float a2 = 1f - alpha;

                // Normalize coefficients by a0
                _b0 = b0 / a0;
                _b1 = b1 / a0;
                _b2 = b2 / a0;
                _a1 = a1 / a0;
                _a2 = a2 / a0;

                Reset();
            }

            public float Process(float sample)
            {
                float output = _b0 * sample + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

                _x2 = _x1;
                _x1 = sample;
                _y2 = _y1;
                _y1 = output;

                if (float.IsNaN(output) || float.IsInfinity(output))
                {
                    Reset();
                    return 0f;
                }

                return output;
            }

            public void Reset()
            {
                _x1 = _x2 = 0f;
                _y1 = _y2 = 0f;
            }
        }
    }
}
