using System;
using UnityEngine;
using UnityEngine.UI;

namespace Acrophobia.Locomotion
{
    /// <summary>
    /// Teleport-only locomotion with a comfort blink. The brief allows no smooth
    /// locomotion, so this is the sole means of deliberate travel.
    ///
    /// Hold the thumbstick (or Tab) to aim: an arc is traced from the controller and the
    /// landing point is validated. Release to travel. A short fade to black covers the
    /// transition, which is what actually prevents the vestibular mismatch that smooth
    /// locomotion causes - with instantaneous movement behind a blink there is no
    /// optical flow to conflict with the inner ear, so a persistent peripheral vignette
    /// is unnecessary here.
    ///
    /// A destination is only valid on near-level ground inside the platform. Teleport
    /// can therefore never be used to put the participant over open air, which keeps the
    /// no-falling guarantee intact rather than routing around it.
    /// </summary>
    public class TeleportLocomotion : MonoBehaviour
    {
        private const int ArcSampleCount = 24;

        [SerializeField, Range(2f, 20f)] private float _maxRange = 8f;

        [Tooltip("Maximum floor slope in degrees that still counts as a valid landing.")]
        [SerializeField, Range(5f, 60f)] private float _maxLandingSlope = 30f;

        [Tooltip("Clearance needed above a landing point for the participant to stand.")]
        [SerializeField, Range(0.5f, 2.5f)] private float _requiredClearance = 1.7f;

        [SerializeField, Range(0.02f, 0.4f)] private float _fadeDuration = 0.12f;

        [SerializeField] private LayerMask _groundLayerMask = ~0;

        [SerializeField] private Transform _rig;
        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private Transform _aimSource;
        [SerializeField] private LineRenderer _arcRenderer;
        [SerializeField] private GameObject _landingMarker;

        private readonly Vector3[] _arcPoints = new Vector3[ArcSampleCount];

        private bool _isAiming;
        private bool _hasValidTarget;
        private Vector3 _targetPosition;
        private Canvas _fadeCanvas;
        private Image _fadeImage;
        private float _fadeAlpha;
        private bool _isTeleporting;

        public bool IsAiming => _isAiming;

        public bool HasValidTarget => _hasValidTarget;

        public event Action<Vector3> OnTeleported;

        private void Awake()
        {
            ResolveReferences();
            BuildFadeOverlay();
        }

        public void ResolveReferences()
        {
            if (_rig == null) _rig = transform;

            if (_centerEyeAnchor == null)
            {
                Transform found = _rig.Find("TrackingSpace/CenterEyeAnchor");
                if (found != null) _centerEyeAnchor = found;
                else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
            }

            if (_aimSource == null)
            {
                Transform hand = _rig.Find("TrackingSpace/RightHandAnchor");
                _aimSource = hand != null ? hand : _centerEyeAnchor;
            }
        }

        private void Update()
        {
            if (_isTeleporting || _aimSource == null)
            {
                return;
            }

            bool aimHeld = ReadAimHeld();

            if (aimHeld)
            {
                _isAiming = true;
                UpdateAim();
            }
            else if (_isAiming)
            {
                _isAiming = false;
                SetArcVisible(false);

                if (_hasValidTarget)
                {
                    StartCoroutine(TeleportWithBlink(_targetPosition));
                }
            }
        }

        private bool ReadAimHeld()
        {
            try
            {
                Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                if (leftStick.magnitude > 0.5f || rightStick.magnitude > 0.5f) return true;
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.tabKey.isPressed) return true;
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKey(KeyCode.Tab)) return true;
            }
            catch (Exception) { }
#endif

            return false;
        }

        /// <summary>
        /// Traces a ballistic arc and validates where it lands.
        /// </summary>
        private void UpdateAim()
        {
            Vector3 origin = _aimSource.position;
            Vector3 velocity = _aimSource.forward * _maxRange;

            _hasValidTarget = false;
            float dt = 0.06f;
            Vector3 point = origin;
            int filled = 1;
            _arcPoints[0] = origin;

            for (int i = 1; i < ArcSampleCount; i++)
            {
                Vector3 next = point + velocity * dt + 0.5f * Physics.gravity * dt * dt;
                velocity += Physics.gravity * dt;

                if (Physics.Linecast(point, next, out RaycastHit hit,
                        _groundLayerMask, QueryTriggerInteraction.Ignore))
                {
                    _arcPoints[i] = hit.point;
                    filled = i + 1;
                    EvaluateLanding(hit);
                    break;
                }

                _arcPoints[i] = next;
                filled = i + 1;
                point = next;
            }

            if (_arcRenderer != null)
            {
                _arcRenderer.positionCount = filled;
                _arcRenderer.SetPositions(_arcPoints);
                _arcRenderer.enabled = true;
            }

            if (_landingMarker != null)
            {
                _landingMarker.SetActive(_hasValidTarget);
                if (_hasValidTarget) _landingMarker.transform.position = _targetPosition;
            }
        }

        /// <summary>
        /// A landing is valid only on near-level ground with headroom. This is what stops
        /// teleport being a way around the no-falling guarantee.
        /// </summary>
        private void EvaluateLanding(RaycastHit hit)
        {
            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > _maxLandingSlope)
            {
                return;
            }

            Vector3 standPoint = hit.point + Vector3.up * 0.05f;
            if (Physics.Raycast(standPoint, Vector3.up, _requiredClearance,
                    _groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            _targetPosition = hit.point;
            _hasValidTarget = true;
        }

        /// <summary>
        /// Runs an action at the midpoint of a comfort blink, so another system can
        /// reposition the participant with no optical flow. The height hierarchy uses
        /// this: a 90 metre vertical jump applied in the open would read as a drop,
        /// which the brief forbids, but behind a blink there is nothing to perceive.
        /// </summary>
        public void BlinkAction(Action midpoint)
        {
            if (_isTeleporting)
            {
                // Already mid-blink: apply immediately rather than queueing a second fade.
                midpoint?.Invoke();
                return;
            }

            StartCoroutine(BlinkRoutine(midpoint));
        }

        private System.Collections.IEnumerator BlinkRoutine(Action midpoint)
        {
            _isTeleporting = true;

            yield return FadeTo(1f);
            midpoint?.Invoke();
            yield return FadeTo(0f);

            _isTeleporting = false;
        }

        private System.Collections.IEnumerator TeleportWithBlink(Vector3 destination)
        {
            _isTeleporting = true;

            yield return FadeTo(1f);

            // Move the rig so the participant's head lands over the destination, rather
            // than the rig origin, which would offset them by their room-scale position.
            if (_rig != null && _centerEyeAnchor != null)
            {
                Vector3 headOffset = _centerEyeAnchor.position - _rig.position;
                headOffset.y = 0f;
                _rig.position = destination - headOffset;
            }
            else if (_rig != null)
            {
                _rig.position = destination;
            }

            OnTeleported?.Invoke(destination);

            yield return FadeTo(0f);

            _isTeleporting = false;
        }

        private System.Collections.IEnumerator FadeTo(float target)
        {
            if (_fadeImage == null)
            {
                yield break;
            }

            float start = _fadeAlpha;
            float elapsed = 0f;

            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _fadeAlpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / _fadeDuration));
                ApplyFade();
                yield return null;
            }

            _fadeAlpha = target;
            ApplyFade();
        }

        private void ApplyFade()
        {
            Color c = _fadeImage.color;
            c.a = _fadeAlpha;
            _fadeImage.color = c;
            _fadeCanvas.enabled = _fadeAlpha > 0.001f;
        }

        private void SetArcVisible(bool visible)
        {
            if (_arcRenderer != null) _arcRenderer.enabled = visible;
            if (_landingMarker != null) _landingMarker.SetActive(false);
        }

        /// <summary>
        /// Screen-space overlay used for the comfort blink. Built at runtime so the
        /// component needs no prefab wiring.
        /// </summary>
        private void BuildFadeOverlay()
        {
            var canvasObj = new GameObject("TeleportFadeCanvas");
            canvasObj.transform.SetParent(transform, false);

            _fadeCanvas = canvasObj.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = 999;
            _fadeCanvas.enabled = false;

            var imageObj = new GameObject("FadeImage");
            imageObj.transform.SetParent(canvasObj.transform, false);

            _fadeImage = imageObj.AddComponent<Image>();
            _fadeImage.color = new Color(0f, 0f, 0f, 0f);

            var rect = _fadeImage.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
