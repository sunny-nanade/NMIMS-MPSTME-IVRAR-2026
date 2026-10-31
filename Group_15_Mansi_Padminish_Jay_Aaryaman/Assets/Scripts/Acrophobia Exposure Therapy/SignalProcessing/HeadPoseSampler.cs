using System;
using UnityEngine;

namespace Acrophobia.SignalProcessing
{
    /// <summary>
    /// Samples head pose kinematics at fixed intervals (targeting 90 Hz / fixedDeltaTime),
    /// calculates linear velocity, and maintains a rolling circular buffer of the last 128
    /// kinematic velocity magnitude samples.
    /// Attach to CenterEyeAnchor (or Camera.main).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HeadPoseSampler : MonoBehaviour
    {
        public const int BufferCapacity = 128;

        [Header("Target Tracking")]
        [Tooltip("Transform to track. Defaults to this transform (CenterEyeAnchor) or Camera.main if null.")]
        [SerializeField] private Transform _targetTransform;

        [Header("Runtime Metrics")]
        [SerializeField, ReadOnlyInspector] private float _currentSpeed;
        [SerializeField, ReadOnlyInspector] private Vector3 _currentVelocity;

        // Circular buffer storing the last 128 velocity magnitudes
        private readonly float[] _velocityBuffer = new float[BufferCapacity];
        private int _bufferHead = 0;
        private int _totalSamplesRecorded = 0;

        private Vector3 _previousPosition;
        private bool _isFirstSample = true;

        /// <summary>
        /// Latest calculated linear velocity vector in world coordinates (m/s).
        /// </summary>
        public Vector3 LatestVelocity => _currentVelocity;

        /// <summary>
        /// Latest calculated linear speed / velocity magnitude (m/s).
        /// </summary>
        public float LatestSpeed => _currentSpeed;

        /// <summary>
        /// Current sampled position of the head in world coordinates.
        /// </summary>
        public Vector3 LatestPosition => _targetTransform != null ? _targetTransform.position : transform.position;

        /// <summary>
        /// Effective sampling rate in Hz based on fixedDeltaTime.
        /// </summary>
        public float SampleRate => Time.fixedDeltaTime > 0f ? 1f / Time.fixedDeltaTime : 90f;

        /// <summary>
        /// Number of valid samples recorded so far (capped at BufferCapacity).
        /// </summary>
        public int AvailableSamples => Mathf.Min(_totalSamplesRecorded, BufferCapacity);

        /// <summary>
        /// Direct access to the internal rolling circular buffer of 128 velocity magnitude samples.
        /// </summary>
        public float[] VelocityBuffer => _velocityBuffer;

        /// <summary>
        /// Capacity of the circular buffer (128).
        /// </summary>
        public int Capacity => BufferCapacity;

        /// <summary>
        /// Target transform being tracked.
        /// </summary>
        public Transform TargetTransform
        {
            get => _targetTransform;
            set
            {
                _targetTransform = value;
                _isFirstSample = true;
            }
        }

        /// <summary>
        /// Event invoked whenever a new kinematic sample is computed.
        /// Arguments: (position, velocity, speed, dt).
        /// </summary>
        public event Action<Vector3, Vector3, float, float> OnSampleRecorded;

        private void Awake()
        {
            ResolveTarget();
        }

        private void Start()
        {
            ResolveTarget();
            if (_targetTransform != null)
            {
                _previousPosition = _targetTransform.position;
                _isFirstSample = false;
            }
        }

        private void OnEnable()
        {
            _isFirstSample = true;
        }

        private void FixedUpdate()
        {
            if (_targetTransform == null)
            {
                ResolveTarget();
                if (_targetTransform == null) return;
            }

            ManualSample(_targetTransform.position, Time.fixedDeltaTime);
        }

        /// <summary>
        /// Explicitly processes a sample position with a given delta time.
        /// Called automatically during FixedUpdate, or manually for testing/simulation.
        /// </summary>
        public void ManualSample(Vector3 currentPos, float dt)
        {
            if (dt <= 0f) return;

            if (_isFirstSample)
            {
                _previousPosition = currentPos;
                _isFirstSample = false;
                _currentVelocity = Vector3.zero;
                _currentSpeed = 0f;
                RecordSample(_currentSpeed);
                OnSampleRecorded?.Invoke(currentPos, _currentVelocity, _currentSpeed, dt);
                return;
            }

            // Calculate instantaneous linear velocity at 90 Hz / fixedDeltaTime
            _currentVelocity = (currentPos - _previousPosition) / dt;
            _currentSpeed = _currentVelocity.magnitude;
            _previousPosition = currentPos;

            // Record to circular buffer
            RecordSample(_currentSpeed);

            // Notify listeners
            OnSampleRecorded?.Invoke(currentPos, _currentVelocity, _currentSpeed, dt);
        }

        private void RecordSample(float speed)
        {
            _velocityBuffer[_bufferHead] = speed;
            _bufferHead = (_bufferHead + 1) % BufferCapacity;
            _totalSamplesRecorded++;
        }

        /// <summary>
        /// Copies the stored velocity magnitude samples into an output array in chronological order
        /// (from oldest to newest recorded sample).
        /// </summary>
        /// <param name="destination">Destination array. Must have length of at least BufferCapacity.</param>
        /// <returns>Number of valid samples copied.</returns>
        public int CopySamplesChronological(float[] destination)
        {
            if (destination == null || destination.Length < BufferCapacity)
            {
                throw new ArgumentException($"Destination array must have length of at least {BufferCapacity}.");
            }

            int count = AvailableSamples;
            if (count == 0) return 0;

            if (count < BufferCapacity)
            {
                Array.Copy(_velocityBuffer, 0, destination, 0, count);
            }
            else
            {
                int tailLength = BufferCapacity - _bufferHead;
                Array.Copy(_velocityBuffer, _bufferHead, destination, 0, tailLength);
                if (_bufferHead > 0)
                {
                    Array.Copy(_velocityBuffer, 0, destination, tailLength, _bufferHead);
                }
            }

            return count;
        }

        /// <summary>
        /// Gets a new array containing the latest velocity magnitude samples in chronological order.
        /// </summary>
        public float[] GetVelocityMagnitudeSamples()
        {
            int count = AvailableSamples;
            float[] result = new float[count];
            if (count == 0) return result;

            if (count < BufferCapacity)
            {
                Array.Copy(_velocityBuffer, 0, result, 0, count);
            }
            else
            {
                int tailLength = BufferCapacity - _bufferHead;
                Array.Copy(_velocityBuffer, _bufferHead, result, 0, tailLength);
                if (_bufferHead > 0)
                {
                    Array.Copy(_velocityBuffer, 0, result, tailLength, _bufferHead);
                }
            }

            return result;
        }

        private void ResolveTarget()
        {
            if (_targetTransform != null) return;

            // 1. If this component is attached to CenterEyeAnchor or Main Camera, use self
            if (name.Contains("CenterEyeAnchor") || GetComponent<Camera>() != null)
            {
                _targetTransform = transform;
                return;
            }

            // 2. Try to find CenterEyeAnchor on OVRCameraRig
            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye != null)
            {
                _targetTransform = centerEye.transform;
                return;
            }

            // 3. Fallback to Camera.main
            if (Camera.main != null)
            {
                _targetTransform = Camera.main.transform;
                return;
            }

            // 4. Fallback to own transform
            _targetTransform = transform;
        }
    }

    /// <summary>
    /// Simple attribute to display fields as read-only in the Unity Inspector.
    /// </summary>
    public class ReadOnlyInspectorAttribute : PropertyAttribute { }
}
