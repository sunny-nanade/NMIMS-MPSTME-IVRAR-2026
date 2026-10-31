using System;
using System.Collections.Generic;
using UnityEngine;

namespace Acrophobia.GazeGeometry
{
    /// <summary>
    /// Tracks head gaze direction relative to the downward vertical vector:
    ///   theta_gaze = arccos( (v_forward . v_down) / (|v_forward| * |v_down|) )
    /// Classifies looking down into the street intersection as theta_gaze <= 45 degrees.
    /// Maintains a sliding 5-second window calculating GazeAvoidanceRatio:
    /// the fraction of time the user actively avoids looking down while near an open edge.
    /// </summary>
    public class GazeAvoidanceDetector : MonoBehaviour
    {
        [Header("Target Tracking")]
        [Tooltip("Transform representing the player's head / gaze origin (CenterEyeAnchor).")]
        [SerializeField] private Transform _centerEyeAnchor;

        [Header("Gaze Thresholds")]
        [Tooltip("Maximum angle in degrees with Vector3.down to classify gaze as looking down.")]
        [SerializeField, Range(15f, 75f)] private float _lookingDownAngleThreshold = 45.0f;

        [Tooltip("Sliding window duration in seconds for computing avoidance ratio.")]
        [SerializeField, Range(1f, 10f)] private float _slidingWindowSeconds = 5.0f;

        [Header("Runtime Metrics")]
        [SerializeField] private float _currentGazeAngle;
        [SerializeField] private bool _isLookingDown;
        [SerializeField] private float _currentAvoidanceRatio;
        [SerializeField] private bool _isNearEdge;

        private struct GazeSample
        {
            public float time;
            public float dt;
            public bool isLookingDown;
            public bool isNearEdge;

            public GazeSample(float time, float dt, bool isLookingDown, bool isNearEdge)
            {
                this.time = time;
                this.dt = dt;
                this.isLookingDown = isLookingDown;
                this.isNearEdge = isNearEdge;
            }
        }

        private readonly Queue<GazeSample> _sampleHistory = new Queue<GazeSample>();
        private float _windowNearEdgeDuration = 0f;
        private float _windowAvoidedDuration = 0f;
        private float _totalWindowDuration = 0f;
        private float _simulatedTime = 0f;

        /// <summary>
        /// Gaze angle in degrees relative to the downward vertical vector (0 deg = looking straight down).
        /// </summary>
        public float GazeAngleWithDown => _currentGazeAngle;

        /// <summary>
        /// True if the user is currently looking down into the street (gaze angle <= 45 degrees).
        /// </summary>
        public bool IsLookingDown => _isLookingDown;

        /// <summary>
        /// Fraction of time (0.0 to 1.0) over the sliding 5-second window where the user
        /// actively avoids looking down while near an open edge.
        /// </summary>
        public float GazeAvoidanceRatio => _currentAvoidanceRatio;

        /// <summary>
        /// Whether the user is currently within the active edge proximity zone.
        /// Can be set by EdgeProximityTracker or external proximity sensors.
        /// </summary>
        public bool IsNearEdge
        {
            get => _isNearEdge;
            set => _isNearEdge = value;
        }

        /// <summary>
        /// Target transform being tracked.
        /// </summary>
        public Transform CenterEyeAnchor
        {
            get => _centerEyeAnchor;
            set => _centerEyeAnchor = value;
        }

        /// <summary>
        /// Event invoked whenever the gaze angle with the downward vector is computed.
        /// </summary>
        public event Action<float> OnGazeAngleUpdated;

        /// <summary>
        /// Event invoked whenever the 5-second sliding avoidance ratio is updated.
        /// </summary>
        public event Action<float> OnAvoidanceRatioUpdated;

        private void Awake()
        {
            ResolveTarget();
        }

        private void Start()
        {
            ResolveTarget();
        }

        private void Update()
        {
            if (_centerEyeAnchor == null)
            {
                ResolveTarget();
                if (_centerEyeAnchor == null) return;
            }

            ProcessGaze(Time.deltaTime);
        }

        /// <summary>
        /// Explicitly processes gaze orientation with a given delta time.
        /// Called automatically during Update, or manually for testing/simulation.
        /// </summary>
        public void ProcessGaze(float dt)
        {
            if (dt <= 0f) return;

            Vector3 forward = _centerEyeAnchor != null ? _centerEyeAnchor.forward : transform.forward;
            Vector3 down = Vector3.down;

            // theta_gaze = arccos( (v_forward . v_down) / (|v_forward| * |v_down|) )
            float dot = Vector3.Dot(forward.normalized, down);
            dot = Mathf.Clamp(dot, -1f, 1f);
            float thetaRad = Mathf.Acos(dot);
            _currentGazeAngle = thetaRad * Mathf.Rad2Deg;

            // Classify looking down: theta_gaze <= 45 degrees
            _isLookingDown = _currentGazeAngle <= _lookingDownAngleThreshold;
            bool isAvoiding = !_isLookingDown;

            _simulatedTime += dt;
            float now = _simulatedTime;
            GazeSample newSample = new GazeSample(now, dt, _isLookingDown, _isNearEdge);
            _sampleHistory.Enqueue(newSample);

            _totalWindowDuration += dt;
            if (_isNearEdge)
            {
                _windowNearEdgeDuration += dt;
                if (isAvoiding)
                {
                    _windowAvoidedDuration += dt;
                }
            }
            else
            {
                // If not near edge, still track general non-looking-down time for baseline
                if (isAvoiding)
                {
                    _windowAvoidedDuration += dt;
                }
            }

            // Prune samples older than sliding window duration (5.0s)
            float cutoffTime = now - _slidingWindowSeconds;
            while (_sampleHistory.Count > 0 && _sampleHistory.Peek().time < cutoffTime)
            {
                GazeSample old = _sampleHistory.Dequeue();
                _totalWindowDuration = Mathf.Max(0f, _totalWindowDuration - old.dt);

                if (old.isNearEdge)
                {
                    _windowNearEdgeDuration = Mathf.Max(0f, _windowNearEdgeDuration - old.dt);
                    if (!old.isLookingDown)
                    {
                        _windowAvoidedDuration = Mathf.Max(0f, _windowAvoidedDuration - old.dt);
                    }
                }
                else
                {
                    if (!old.isLookingDown)
                    {
                        _windowAvoidedDuration = Mathf.Max(0f, _windowAvoidedDuration - old.dt);
                    }
                }
            }

            // Calculate GazeAvoidanceRatio
            // If near edge during the window, ratio is relative to time spent near edge
            if (_windowNearEdgeDuration > 0.1f)
            {
                _currentAvoidanceRatio = Mathf.Clamp01(_windowAvoidedDuration / _windowNearEdgeDuration);
            }
            else if (_totalWindowDuration > 0.1f)
            {
                // If user is currently near edge but just arrived, or general context
                _currentAvoidanceRatio = _isNearEdge 
                    ? Mathf.Clamp01(_windowAvoidedDuration / _totalWindowDuration)
                    : 0f;
            }
            else
            {
                _currentAvoidanceRatio = isAvoiding && _isNearEdge ? 1.0f : 0.0f;
            }

            OnGazeAngleUpdated?.Invoke(_currentGazeAngle);
            OnAvoidanceRatioUpdated?.Invoke(_currentAvoidanceRatio);
        }

        private void ResolveTarget()
        {
            if (_centerEyeAnchor != null) return;

            if (name.Contains("CenterEyeAnchor") || GetComponent<Camera>() != null)
            {
                _centerEyeAnchor = transform;
                return;
            }

            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye != null)
            {
                _centerEyeAnchor = centerEye.transform;
                return;
            }

            if (Camera.main != null)
            {
                _centerEyeAnchor = Camera.main.transform;
                return;
            }

            _centerEyeAnchor = transform;
        }

        /// <summary>
        /// Resets the sliding window history.
        /// </summary>
        public void ResetWindow()
        {
            _sampleHistory.Clear();
            _windowNearEdgeDuration = 0f;
            _windowAvoidedDuration = 0f;
            _totalWindowDuration = 0f;
            _currentAvoidanceRatio = 0f;
            _simulatedTime = 0f;
        }
    }
}
