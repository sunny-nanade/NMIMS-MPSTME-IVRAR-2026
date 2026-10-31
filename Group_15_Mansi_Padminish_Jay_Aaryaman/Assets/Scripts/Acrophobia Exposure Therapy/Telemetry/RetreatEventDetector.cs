using System;
using UnityEngine;
using Acrophobia.GazeGeometry;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// Detects withdrawal from the edge and reports it as a discrete retreat event.
    ///
    /// Retreat is one of the required telemetry outputs and is clinically the most
    /// interesting single behaviour in an exposure session: it marks the point where
    /// approach stopped and avoidance took over. Logging it as an event, with the
    /// distance it started from and the ground it gave up, lets a session be read as a
    /// sequence of approaches and withdrawals rather than just a distance trace.
    ///
    /// Detection is a small state machine over EdgeProximityTracker.DistanceToEdge:
    ///
    ///   Idle ---- distance &lt; engage threshold ----&gt; Engaged
    ///   Engaged -- gained >= min gain within window --&gt; retreat fired, then cooldown
    ///
    /// While engaged it tracks the closest approach, so the gain is measured from the
    /// nearest point reached rather than from wherever the participant happened to be
    /// when the window opened.
    ///
    /// Two details matter for correctness. EdgeProximityTracker reports a sentinel
    /// (999) when it finds no edge within its probe range, so raw distances are clamped
    /// before any arithmetic - otherwise stepping behind a railing would register as a
    /// 998 metre retreat. And a retreat has to happen within a time window, so slowly
    /// wandering away across a minute is not counted as withdrawal.
    ///
    /// Read-only with respect to the rest of the project: it observes the tracker and
    /// reports to the logger, and drives nothing.
    /// </summary>
    public class RetreatEventDetector : MonoBehaviour
    {
        private enum DetectorState
        {
            Idle,
            Engaged,
            Cooldown
        }

        [Tooltip("Distance to edge, in metres, below which the participant counts as engaged with the edge.")]
        [SerializeField, Range(0.2f, 3f)] private float _engageThresholdMetres = 0.9f;

        [Tooltip("Metres of ground given up, measured from the closest approach, to count as a retreat.")]
        [SerializeField, Range(0.1f, 2f)] private float _minRetreatGainMetres = 0.45f;

        [Tooltip("The gain must accumulate within this many seconds, or it is drift rather than retreat.")]
        [SerializeField, Range(0.5f, 10f)] private float _retreatWindowSeconds = 3.0f;

        [Tooltip("Seconds to wait after a retreat before another can be reported.")]
        [SerializeField, Range(0.5f, 10f)] private float _cooldownSeconds = 2.0f;

        [Tooltip("Distances above this are treated as 'no edge nearby'. Must match the tracker's probe range.")]
        [SerializeField, Range(1f, 10f)] private float _maxMeaningfulDistance = 4.0f;

        [Tooltip("Minimum head movement, in metres, required to corroborate a retreat.")]
        [SerializeField, Range(0.05f, 1f)] private float _minHeadDisplacementMetres = 0.15f;

        [SerializeField] private Transform _headTransform;
        [SerializeField] private EdgeProximityTracker _edgeTracker;
        [SerializeField] private SessionTelemetryLogger _logger;

        private DetectorState _state = DetectorState.Idle;
        private float _closestApproach;
        private Vector3 _closestApproachHeadPosition;
        private float _engagedTimer;
        private float _cooldownTimer;
        private int _retreatCount;

        public int RetreatCount => _retreatCount;

        public bool IsEngagedWithEdge => _state == DetectorState.Engaged;

        /// <summary>
        /// Closest distance to the edge reached during the current engagement, in metres.
        /// </summary>
        public float ClosestApproachMetres => _state == DetectorState.Engaged ? _closestApproach : -1f;

        /// <summary>
        /// Fired with (distance gained, distance retreated from) when a retreat is detected.
        /// </summary>
        public event Action<float, float> OnRetreatDetected;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (_edgeTracker == null) _edgeTracker = FindAnyObjectByType<EdgeProximityTracker>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();

            if (_headTransform == null)
            {
                var gaze = FindAnyObjectByType<GazeAvoidanceDetector>();
                if (gaze != null) _headTransform = gaze.CenterEyeAnchor;
                if (_headTransform == null && Camera.main != null) _headTransform = Camera.main.transform;
            }
        }

        /// <summary>
        /// Clamps the tracker's reading into a usable range. The tracker returns a large
        /// sentinel when no edge is found, which must not enter distance arithmetic.
        /// </summary>
        private float ReadClampedDistance()
        {
            if (_edgeTracker == null)
            {
                return _maxMeaningfulDistance;
            }

            float raw = _edgeTracker.DistanceToEdge;

            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw > _maxMeaningfulDistance)
            {
                return _maxMeaningfulDistance;
            }

            return raw < 0f ? 0f : raw;
        }

        private void Update()
        {
            if (_edgeTracker == null)
            {
                return;
            }

            ProcessDistance(ReadClampedDistance(), Time.deltaTime);
        }

        /// <summary>
        /// Advances the detector with one already-clamped distance reading. Separated from
        /// Update so the state machine can be driven directly, matching the pattern the
        /// other signal components use (ProcessGaze, ProcessVelocitySample,
        /// ProcessKinematicSample).
        /// </summary>
        public void ProcessDistance(float distance, float dt)
        {
            if (distance > _maxMeaningfulDistance)
            {
                distance = _maxMeaningfulDistance;
            }
            else if (distance < 0f || float.IsNaN(distance))
            {
                distance = 0f;
            }

            switch (_state)
            {
                case DetectorState.Idle:
                    if (distance < _engageThresholdMetres)
                    {
                        _state = DetectorState.Engaged;
                        _closestApproach = distance;
                        _closestApproachHeadPosition = CurrentHeadPosition();
                        _engagedTimer = 0f;
                    }
                    break;

                case DetectorState.Engaged:
                    _engagedTimer += dt;

                    if (distance < _closestApproach)
                    {
                        // Moved nearer: reset the window so the gain is always measured
                        // from the closest point actually reached.
                        _closestApproach = distance;
                        _closestApproachHeadPosition = CurrentHeadPosition();
                        _engagedTimer = 0f;
                    }

                    float gained = distance - _closestApproach;

                    // A retreat requires the participant to have actually moved. The edge
                    // probe can jump from a near reading to its no-edge-found ceiling in
                    // one tick - stepping behind a railing does it - which looked like a
                    // multi-metre withdrawal while the head sat perfectly still. Requiring
                    // corroborating head displacement removes that whole class of false
                    // positive, which was otherwise writing junk into the graded telemetry.
                    bool participantMoved = HeadDisplacementSince(_closestApproachHeadPosition)
                                            >= _minHeadDisplacementMetres;

                    if (gained >= _minRetreatGainMetres
                        && _engagedTimer <= _retreatWindowSeconds
                        && participantMoved)
                    {
                        ReportRetreat(gained, _closestApproach);
                        _state = DetectorState.Cooldown;
                        _cooldownTimer = 0f;
                    }
                    else if (_engagedTimer > _retreatWindowSeconds)
                    {
                        if (distance >= _engageThresholdMetres)
                        {
                            // Drifted away slowly. Not a retreat; just disengage.
                            _state = DetectorState.Idle;
                        }
                        else
                        {
                            // Still at the edge, just lingering. Keep watching from here.
                            _engagedTimer = 0f;
                            _closestApproach = distance;
                            _closestApproachHeadPosition = CurrentHeadPosition();
                        }
                    }
                    break;

                case DetectorState.Cooldown:
                    _cooldownTimer += dt;
                    if (_cooldownTimer >= _cooldownSeconds)
                    {
                        _state = distance < _engageThresholdMetres
                            ? DetectorState.Engaged
                            : DetectorState.Idle;
                        _closestApproach = distance;
                        _closestApproachHeadPosition = CurrentHeadPosition();
                        _engagedTimer = 0f;
                    }
                    break;
            }
        }

        private Vector3 CurrentHeadPosition()
        {
            return _headTransform != null ? _headTransform.position : Vector3.zero;
        }

        /// <summary>
        /// Horizontal head displacement from a reference point, in metres.
        /// </summary>
        private float HeadDisplacementSince(Vector3 reference)
        {
            if (_headTransform == null)
            {
                // No head reference available: do not gate on movement we cannot measure.
                return float.MaxValue;
            }

            Vector3 now = _headTransform.position;
            float dx = now.x - reference.x;
            float dz = now.z - reference.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void ReportRetreat(float distanceGained, float fromDistance)
        {
            _retreatCount++;

            _logger?.RecordRetreatEvent(distanceGained, fromDistance);
            OnRetreatDetected?.Invoke(distanceGained, fromDistance);
        }

        /// <summary>
        /// Clears detector state. Call when a new session begins so counts and the state
        /// machine do not carry across sessions.
        /// </summary>
        public void ResetDetector()
        {
            _state = DetectorState.Idle;
            _closestApproach = 0f;
            _closestApproachHeadPosition = CurrentHeadPosition();
            _engagedTimer = 0f;
            _cooldownTimer = 0f;
            _retreatCount = 0;
        }
    }
}
