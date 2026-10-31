using System;
using UnityEngine;
using Acrophobia.SignalProcessing;

namespace Acrophobia.GazeGeometry
{
    /// <summary>
    /// Probes for the nearest unguarded platform edge and, inside the near-edge zone,
    /// computes an aggregate Behavioural Hesitation Index (0-100) from gaze avoidance,
    /// locomotion slowness and tremor intensity.
    ///
    /// The probe is a horizontal fan of rays from the feet. For each ray it finds where
    /// the floor stops (or drops away) and whether a railing guards that path. The
    /// nearest unguarded drop across the fan is the reported distance.
    ///
    /// Cost matters here more than anywhere else in the project. The original
    /// implementation ran the full search every frame and re-cast the barrier ray once
    /// per step, reaching roughly 1,760 raycasts per frame against layer mask ~0 - which
    /// at a 72 FPS floor is not affordable on mobile hardware. Four changes bring that
    /// down by well over an order of magnitude, three of them without altering a single
    /// reported value:
    ///
    ///   1. One barrier ray per fan ray instead of one per step. The old code cast the
    ///      same ray repeatedly with a growing length; casting it once at full length
    ///      and reading hit.distance gives the identical answer for up to 1/80th of the
    ///      work. Exact.
    ///   2. Search-limit pruning. Only the nearest edge is reported, so no ray needs to
    ///      look further out than the best distance already found. Exact.
    ///   3. Coarse-then-refine beyond the near-edge zone. Fine steps are kept inside
    ///      the zone where the Hesitation Index is actually computed, and the long tail
    ///      out to maximum range is walked coarsely then bisected back to fine
    ///      resolution. This is the one approximation: a gap narrower than the coarse
    ///      step, more than the fine range away, can be missed. Such a gap is not a
    ///      fall hazard and is not what the measure is for.
    ///   4. Fixed-rate probing rather than per frame. Edge distance is a spatial
    ///      quantity changing at walking pace - at 0.8 m/s, 15 Hz still resolves every
    ///      5 cm of approach. Gaze, tremor and hesitation continue to update every
    ///      frame; only the geometric probe is rate-limited.
    ///
    /// The layer mask is also configurable now. Leaving it at Everything makes each ray
    /// test the full city, and narrowing it to just platform and railing geometry is the
    /// single biggest remaining win available here.
    /// </summary>
    public class EdgeProximityTracker : MonoBehaviour
    {
        [Tooltip("Maximum horizontal probe distance in metres to search for platform edges.")]
        [SerializeField, Range(1f, 6f)] private float _maxProbeDistance = 4.0f;

        [Tooltip("Fine horizontal step in metres, used inside the fine probe range.")]
        [SerializeField, Range(0.02f, 0.2f)] private float _probeStepDistance = 0.05f;

        [Tooltip("Coarse horizontal step in metres, used beyond the fine probe range before bisection.")]
        [SerializeField, Range(0.1f, 0.6f)] private float _coarseStepDistance = 0.25f;

        [Tooltip("Range in metres probed at fine resolution. Should comfortably exceed the near-edge threshold.")]
        [SerializeField, Range(0.3f, 2.5f)] private float _fineProbeRange = 1.0f;

        [Tooltip("Distance threshold within which edge hesitation is actively monitored.")]
        [SerializeField] private float _nearEdgeThreshold = 0.6f;

        [Tooltip("Maximum vertical drop still considered solid floor before classifying as open air.")]
        [SerializeField, Range(0.3f, 3.0f)] private float _edgeDropThreshold = 0.8f;

        [Tooltip("Angular fan in degrees (+/- half-angle) for the forward edge probes.")]
        [SerializeField, Range(15f, 90f)] private float _probeFanAngle = 60f;

        [Tooltip("Number of radial rays in the forward probe fan.")]
        [SerializeField, Range(5, 21)] private int _probeRayCount = 11;

        [Tooltip("Geometry the probe tests against. Narrowing this to platform and railing layers is a large performance win.")]
        [SerializeField] private LayerMask _probeLayerMask = ~0;

        [Tooltip("How many times per second the geometric probe runs. Gaze and tremor still update every frame.")]
        [SerializeField, Range(2f, 72f)] private float _probeRateHz = 15f;

        [SerializeField, Range(0f, 1f)] private float _weightGazeAvoidance = 0.40f;
        [SerializeField, Range(0f, 1f)] private float _weightLocomotionSlowness = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _weightTremor = 0.25f;

        [Tooltip("Locomotion speed in m/s below which the user counts as hesitating.")]
        [SerializeField, Range(0.2f, 1.5f)] private float _normalWalkSpeed = 0.8f;

        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private GazeAvoidanceDetector _gazeDetector;
        [SerializeField] private TremorBandFilter _tremorFilter;
        [SerializeField] private HeadPoseSampler _headPoseSampler;

        [SerializeField] private float _distanceToEdge = 999f;
        [SerializeField] private bool _isNearEdge;
        [SerializeField] private float _hesitationIndex;
        [SerializeField] private float _currentSpeed;
        [SerializeField] private float _currentTremor;
        [SerializeField] private float _currentGazeAngle;
        [SerializeField] private float _currentAvoidanceRatio;

        private float _probeAccumulator;
        private float _probeInterval;

        // Diagnostic: raycasts issued by the most recent probe.
        private int _lastProbeRaycastCount;

        /// <summary>
        /// Distance in metres to the nearest unguarded platform edge.
        /// </summary>
        public float DistanceToEdge => _distanceToEdge;

        /// <summary>
        /// True if the user is inside the near-edge hesitation zone.
        /// </summary>
        public bool IsNearEdge => _isNearEdge;

        /// <summary>
        /// Aggregate Behavioural Hesitation Index (0 to 100).
        /// </summary>
        public float HesitationIndex => _hesitationIndex;

        /// <summary>
        /// Gaze angle in degrees relative to straight down, relayed from GazeAvoidanceDetector.
        /// </summary>
        public float GazeAngleWithDown => _gazeDetector != null ? _gazeDetector.GazeAngleWithDown : _currentGazeAngle;

        /// <summary>
        /// Fraction of time the user avoids looking down while near the edge.
        /// </summary>
        public float GazeAvoidanceRatio => _gazeDetector != null ? _gazeDetector.GazeAvoidanceRatio : _currentAvoidanceRatio;

        /// <summary>
        /// Current head locomotion speed in m/s.
        /// </summary>
        public float LocomotionSpeed => _currentSpeed;

        /// <summary>
        /// Current normalised tremor intensity (0 to 1).
        /// </summary>
        public float TremorIntensity => _currentTremor;

        /// <summary>
        /// Raycasts issued by the most recent geometric probe. Exposed for profiling.
        /// </summary>
        public int LastProbeRaycastCount => _lastProbeRaycastCount;

        public event Action<float> OnHesitationIndexUpdated;

        public event Action<float> OnEdgeProximityUpdated;

        private void Awake()
        {
            ResolveReferences();
            _probeInterval = 1f / Mathf.Max(1f, _probeRateHz);
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (_centerEyeAnchor == null)
            {
                ResolveReferences();
                if (_centerEyeAnchor == null) return;
            }

            // The geometric probe is rate-limited; everything downstream of it is not.
            _probeAccumulator += Time.deltaTime;
            if (_probeAccumulator >= _probeInterval)
            {
                _probeAccumulator -= _probeInterval;
                if (_probeAccumulator > _probeInterval)
                {
                    // Long hitch: resync rather than running several probes back to back.
                    _probeAccumulator = 0f;
                }

                _distanceToEdge = ComputeDistanceToNearestUnguardedEdge();
                _isNearEdge = _distanceToEdge < _nearEdgeThreshold;

                OnEdgeProximityUpdated?.Invoke(_distanceToEdge);
            }

            if (_gazeDetector != null)
            {
                _gazeDetector.IsNearEdge = _isNearEdge;
                _currentGazeAngle = _gazeDetector.GazeAngleWithDown;
                _currentAvoidanceRatio = _gazeDetector.GazeAvoidanceRatio;
            }

            _currentSpeed = _headPoseSampler != null ? _headPoseSampler.LatestSpeed : 0f;
            _currentTremor = _tremorFilter != null ? _tremorFilter.TremorIntensity : 0f;

            _hesitationIndex = _isNearEdge
                ? ComputeHesitationIndex(_currentAvoidanceRatio, _currentSpeed, _currentTremor, _distanceToEdge)
                : 0f;

            OnHesitationIndexUpdated?.Invoke(_hesitationIndex);
        }

        /// <summary>
        /// Sweeps a horizontal fan from the feet and returns the distance to the nearest
        /// unguarded drop, or the maximum probe distance if none is found.
        /// </summary>
        public float ComputeDistanceToNearestUnguardedEdge()
        {
            _lastProbeRaycastCount = 0;

            Transform headTransform = _centerEyeAnchor != null ? _centerEyeAnchor : transform;
            Vector3 headPos = headTransform.position;
            int mask = _probeLayerMask.value;

            // Establish the floor the user is standing on. Everything else is measured
            // as a drop relative to this.
            float floorY = headPos.y - 1.6f;
            _lastProbeRaycastCount++;
            if (Physics.Raycast(headPos + Vector3.up * 0.1f, Vector3.down,
                    out RaycastHit groundHit, 3.0f, mask, QueryTriggerInteraction.Ignore))
            {
                floorY = groundHit.point.y;
            }

            Vector3 feetOrigin = new Vector3(headPos.x, floorY + 0.05f, headPos.z);
            Vector3 barrierOrigin = feetOrigin + Vector3.up * 0.7f;

            Vector3 forward = headTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            int rayCount = Mathf.Max(3, _probeRayCount);
            float startAngle = -_probeFanAngle;
            float angleStep = (_probeFanAngle * 2f) / (rayCount - 1);

            float minEdgeDistance = _maxProbeDistance;

            for (int r = 0; r < rayCount; r++)
            {
                // Pruning: a ray cannot improve the answer by looking beyond the best
                // distance already found, so cap its search there.
                float searchLimit = minEdgeDistance;
                if (searchLimit <= _probeStepDistance)
                {
                    break;
                }

                float angle = startAngle + r * angleStep;
                Vector3 probeDir = Quaternion.Euler(0f, angle, 0f) * forward;

                // One barrier ray at full search length, instead of one per step. If a
                // railing blocks the path, nothing beyond it counts as reachable.
                _lastProbeRaycastCount++;
                if (Physics.Raycast(barrierOrigin, probeDir, out RaycastHit barrierHit,
                        searchLimit, mask, QueryTriggerInteraction.Ignore))
                {
                    searchLimit = barrierHit.distance;
                    if (searchLimit <= _probeStepDistance)
                    {
                        continue;
                    }
                }

                float found = FindEdgeAlongRay(feetOrigin, probeDir, floorY, searchLimit, mask);
                if (found >= 0f && found < minEdgeDistance)
                {
                    minEdgeDistance = found;
                }
            }

            return minEdgeDistance;
        }

        /// <summary>
        /// Walks one ray outward looking for the first point where the floor has dropped
        /// away, returning its distance or -1 if the floor holds for the whole limit.
        ///
        /// Fine steps are used out to the fine probe range, because that is the band in
        /// which the Hesitation Index is computed and precision is therefore meaningful.
        /// Beyond it, coarse steps locate the crossing and a short bisection recovers
        /// fine resolution.
        /// </summary>
        private float FindEdgeAlongRay(Vector3 feetOrigin, Vector3 probeDir, float floorY,
            float searchLimit, int mask)
        {
            float fineLimit = Mathf.Min(searchLimit, _fineProbeRange);
            float step = Mathf.Max(0.01f, _probeStepDistance);

            for (float dist = step; dist <= fineLimit; dist += step)
            {
                if (IsDropAt(feetOrigin, probeDir, dist, floorY, mask))
                {
                    return dist;
                }
            }

            if (searchLimit <= fineLimit)
            {
                return -1f;
            }

            float coarse = Mathf.Max(step, _coarseStepDistance);
            float previous = fineLimit;

            for (float dist = fineLimit + coarse; ; dist += coarse)
            {
                bool last = dist >= searchLimit;
                if (last)
                {
                    dist = searchLimit;
                }

                if (IsDropAt(feetOrigin, probeDir, dist, floorY, mask))
                {
                    // Crossing is between previous (floor) and dist (drop). Bisect back
                    // to fine resolution.
                    float low = previous;
                    float high = dist;
                    while (high - low > step)
                    {
                        float mid = (low + high) * 0.5f;
                        if (IsDropAt(feetOrigin, probeDir, mid, floorY, mask))
                        {
                            high = mid;
                        }
                        else
                        {
                            low = mid;
                        }
                    }

                    return high;
                }

                previous = dist;

                if (last)
                {
                    break;
                }
            }

            return -1f;
        }

        /// <summary>
        /// True if the floor at this distance along the ray is missing, or has dropped
        /// further than the edge threshold below the standing floor.
        /// </summary>
        private bool IsDropAt(Vector3 feetOrigin, Vector3 probeDir, float distance,
            float floorY, int mask)
        {
            Vector3 rayStart = feetOrigin + probeDir * distance + Vector3.up * 0.3f;

            _lastProbeRaycastCount++;
            if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
                    _edgeDropThreshold + 0.3f, mask, QueryTriggerInteraction.Ignore))
            {
                // Nothing within reach below: open air.
                return true;
            }

            return (floorY - hit.point.y) > _edgeDropThreshold;
        }

        /// <summary>
        /// Combines gaze avoidance, locomotion slowness and tremor into a 0-100 index,
        /// scaled up as the user gets closer to the edge.
        /// </summary>
        public float ComputeHesitationIndex(float gazeAvoidanceRatio, float speed,
            float tremorIntensity, float distanceToEdge)
        {
            float gazeFactor = Mathf.Clamp01(gazeAvoidanceRatio);
            float slownessFactor = 1.0f - Mathf.Clamp01(speed / Mathf.Max(0.1f, _normalWalkSpeed));
            float tremorFactor = Mathf.Clamp01(tremorIntensity);

            float rawHesitation = _weightGazeAvoidance * gazeFactor +
                                  _weightLocomotionSlowness * slownessFactor +
                                  _weightTremor * tremorFactor;

            float proximityScale = Mathf.Clamp01((_nearEdgeThreshold - distanceToEdge) / _nearEdgeThreshold);
            float scaledHesitation = rawHesitation * (0.6f + 0.4f * proximityScale);

            return Mathf.Clamp(scaledHesitation * 100.0f, 0f, 100f);
        }

        private void ResolveReferences()
        {
            if (_centerEyeAnchor == null)
            {
                if (name.Contains("CenterEyeAnchor") || GetComponent<Camera>() != null)
                {
                    _centerEyeAnchor = transform;
                }
                else
                {
                    var centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) _centerEyeAnchor = centerEye.transform;
                    else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
                }
            }

            if (_gazeDetector == null)
            {
                _gazeDetector = GetComponent<GazeAvoidanceDetector>();
                if (_gazeDetector == null) _gazeDetector = FindAnyObjectByType<GazeAvoidanceDetector>();
            }

            if (_tremorFilter == null)
            {
                _tremorFilter = GetComponent<TremorBandFilter>();
                if (_tremorFilter == null) _tremorFilter = FindAnyObjectByType<TremorBandFilter>();
            }

            if (_headPoseSampler == null)
            {
                _headPoseSampler = GetComponent<HeadPoseSampler>();
                if (_headPoseSampler == null) _headPoseSampler = FindAnyObjectByType<HeadPoseSampler>();
            }
        }
    }
}
