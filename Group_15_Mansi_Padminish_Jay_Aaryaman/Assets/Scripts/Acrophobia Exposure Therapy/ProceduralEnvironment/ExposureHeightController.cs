using System;
using UnityEngine;
using Acrophobia.AdaptiveFSM;
using Acrophobia.Locomotion;
using Acrophobia.Telemetry;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Gives the five exposure tiers five actual altitudes.
    ///
    /// The scene was authored with every tier at the same place: the tower spans y -6 to
    /// 110, the platform sits at 103.7-105.4, and the rig starts at 104.17. So tier 1 was
    /// already the roof of a thirty-storey building, and the tiers only varied railing
    /// height, platform width, glass floor and crane suspension. For an acrophobia
    /// hierarchy that is the wrong graded variable - the first exposure was close to the
    /// worst one available, leaving nowhere to escalate and no gentle entry point.
    ///
    /// This moves the participant and the platform they stand on together, so the
    /// hierarchy climbs in real metres and ends on the original rooftop geometry. The
    /// tower keeps extending above at the lower tiers, which reads as a ledge partway up
    /// a skyscraper.
    ///
    /// Height changes are applied at the midpoint of a comfort blink. A 90 metre vertical
    /// move applied in the open would be perceived as a drop, which the brief forbids
    /// outright; behind a blink there is no optical flow and nothing to perceive. The
    /// change is instant, never animated.
    ///
    /// Nothing here chooses a tier. It only follows the tier the participant selected.
    /// </summary>
    public class ExposureHeightController : MonoBehaviour
    {
        [Tooltip("Altitude in metres for tiers 1-5. Must be ascending.")]
        [SerializeField]
        private float[] _tierHeights = { 3f, 12f, 30f, 60f, 104f };

        [Tooltip("Altitude the scene geometry was authored at. Offsets are measured from this.")]
        [SerializeField] private float _authoredHeight = 104.17f;

        [Tooltip("The apartment interior. Hidden on the top tier, where there is no safe room.")]
        [SerializeField] private Transform _apartmentRoot;

        [Tooltip("Hide the apartment on the highest tier, leaving the open terrace.")]
        [SerializeField] private bool _hideApartmentAtTopTier = true;

        [Tooltip("Root of the platform the participant stands on.")]
        [SerializeField] private Transform _platformRoot;

        [Tooltip("The camera rig.")]
        [SerializeField] private Transform _playerRig;

        [SerializeField] private AdaptiveExposureFSM _fsm;
        [SerializeField] private TeleportLocomotion _teleport;
        [SerializeField] private SessionTelemetryLogger _logger;

        [Tooltip("Apply tier 1's height when the scene starts.")]
        [SerializeField] private bool _applyOnStart = true;

        private Vector3 _platformOrigin;
        private Vector3 _rigOrigin;
        private bool _capturedOrigins;
        private int _appliedTier = -1;

        private int TopTier => _tierHeights != null && _tierHeights.Length > 0 ? _tierHeights.Length : 5;

        /// <summary>
        /// Altitude in metres of the tier currently applied, or -1 before the first apply.
        /// </summary>
        public float CurrentHeight => _appliedTier >= 1 ? HeightForTier(_appliedTier) : -1f;

        public int AppliedTier => _appliedTier;

        public event Action<int, float> OnHeightApplied;

        private void Awake()
        {
            ResolveReferences();
            CaptureOrigins();
        }

        private void OnEnable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced += HandleTierChanged;
                _fsm.OnTierSteppedBack += HandleTierChanged;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= HandleTierChanged;
                _fsm.OnTierSteppedBack -= HandleTierChanged;
            }
        }

        private void Start()
        {
            ResolveReferences();
            CaptureOrigins();

            if (_applyOnStart)
            {
                // Instant, with no blink: the session has not begun, so there is nothing
                // to hide and no perceived motion to avoid.
                ApplyTierImmediate(_fsm != null ? _fsm.CurrentTier : 1);
            }
        }

        public void ResolveReferences()
        {
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();
            if (_teleport == null) _teleport = FindAnyObjectByType<TeleportLocomotion>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();

            if (_playerRig == null && _teleport != null)
            {
                _playerRig = _teleport.transform;
            }

            if (_playerRig == null)
            {
                var ground = FindAnyObjectByType<PlayerGroundingController>();
                if (ground != null) _playerRig = ground.transform;
            }

            if (_platformRoot == null)
            {
                var found = GameObject.Find("Player_Rooftop_Environment");
                if (found != null) _platformRoot = found.transform;
            }

            if (_apartmentRoot == null && _platformRoot != null)
            {
                _apartmentRoot = _platformRoot.Find("Apartment_Interior");
            }
        }

        /// <summary>
        /// Records where the platform and rig were authored, so every later move is an
        /// absolute offset from that rather than an accumulation.
        /// </summary>
        private void CaptureOrigins()
        {
            if (_capturedOrigins)
            {
                return;
            }

            if (_platformRoot != null) _platformOrigin = _platformRoot.position;
            if (_playerRig != null) _rigOrigin = _playerRig.position;

            _capturedOrigins = _platformRoot != null || _playerRig != null;
        }

        public float HeightForTier(int tier)
        {
            if (_tierHeights == null || _tierHeights.Length == 0)
            {
                return _authoredHeight;
            }

            int index = Mathf.Clamp(tier - 1, 0, _tierHeights.Length - 1);
            return _tierHeights[index];
        }

        /// <summary>
        /// Moves to the tier's altitude behind a comfort blink. This is the normal path
        /// once a session is running.
        /// </summary>
        public void ApplyTier(int tier)
        {
            if (tier == _appliedTier)
            {
                return;
            }

            if (_teleport != null)
            {
                int captured = tier;
                _teleport.BlinkAction(() => ApplyTierImmediate(captured));
            }
            else
            {
                ApplyTierImmediate(tier);
            }
        }

        /// <summary>
        /// Moves without a blink. Used at scene start, before the participant is looking.
        /// </summary>
        public void ApplyTierImmediate(int tier)
        {
            CaptureOrigins();

            float targetHeight = HeightForTier(tier);
            float offset = targetHeight - _authoredHeight;

            if (_platformRoot != null)
            {
                Vector3 p = _platformOrigin;
                p.y = _platformOrigin.y + offset;
                _platformRoot.position = p;
            }

            if (_playerRig != null)
            {
                Vector3 r = _rigOrigin;
                r.y = _rigOrigin.y + offset;
                _playerRig.position = r;
            }

            // Tiers 1-4 are balconies off the apartment, which is the constant safe base
            // the participant can always retreat into. Tier 5 is the open terrace, where
            // that safe box deliberately does not exist - the escalation is partly the
            // absence of somewhere to go back to.
            if (_apartmentRoot != null && _hideApartmentAtTopTier)
            {
                bool apartmentPresent = tier < TopTier;
                if (_apartmentRoot.gameObject.activeSelf != apartmentPresent)
                {
                    _apartmentRoot.gameObject.SetActive(apartmentPresent);
                }
            }

            _appliedTier = tier;

            _logger?.RecordEvent("tier_height_applied", tier, "metres", targetHeight);
            OnHeightApplied?.Invoke(tier, targetHeight);
        }

        private void HandleTierChanged(int tier)
        {
            ApplyTier(tier);
        }
    }
}
