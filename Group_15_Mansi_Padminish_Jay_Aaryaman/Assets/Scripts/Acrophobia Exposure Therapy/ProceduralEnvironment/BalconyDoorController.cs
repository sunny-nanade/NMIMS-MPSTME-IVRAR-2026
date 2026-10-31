using System;
using UnityEngine;
using Acrophobia.AdaptiveFSM;
using Acrophobia.Telemetry;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// The balcony door. The single gate between the safe room and the exposure.
    ///
    /// This exists to make the first sight of height a deliberate act. The participant
    /// wakes facing away from it, has to turn, approach, and open it. Nothing pushes them
    /// through and nothing opens on their behalf, which is what the brief means by
    /// user-initiated progression - and it is also how graded exposure is supposed to
    /// work: approach is the participant's decision, every time.
    ///
    /// Opening lifts the audio muffling, so the city arrives by sound before the drop is
    /// even looked at. That reveal does more than any amount of shader work.
    ///
    /// The door can be closed again from outside. Retreating into the room and shutting
    /// it is a legitimate, recorded avoidance behaviour, not a failure state.
    /// </summary>
    public class BalconyDoorController : MonoBehaviour
    {
        [Tooltip("The door panel that swings. Rotated about its hinge edge.")]
        [SerializeField] private Transform _doorPanel;

        [Tooltip("Degrees the door swings when open.")]
        [SerializeField, Range(45f, 120f)] private float _openAngle = 95f;

        [Tooltip("Seconds the swing takes.")]
        [SerializeField, Range(0.3f, 4f)] private float _swingSeconds = 1.4f;

        [Tooltip("How close the participant must be to operate the door, in metres.")]
        [SerializeField, Range(0.5f, 3f)] private float _reachDistance = 1.3f;

        [Tooltip("Seconds the ambience takes to cross between indoors and outdoors.")]
        [SerializeField, Range(0.2f, 4f)] private float _audioBlendSeconds = 1.6f;

        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private ExposureAudioManager _audioManager;
        [SerializeField] private SessionTelemetryLogger _logger;

        private bool _isOpen;
        private bool _isSwinging;
        private float _indoorAmount = 1f;
        private float _targetIndoorAmount = 1f;
        private Quaternion _closedRotation;
        private Quaternion _openRotation;
        private bool _capturedRotations;

        public bool IsOpen => _isOpen;

        /// <summary>
        /// 1 while sealed indoors, 0 once fully open. Drives the ambience blend.
        /// </summary>
        public float IndoorAmount => _indoorAmount;

        public bool IsWithinReach => ComputeDistanceToDoor() <= _reachDistance;

        public event Action OnDoorOpened;

        public event Action OnDoorClosed;

        private void Awake()
        {
            ResolveReferences();
            CaptureRotations();
        }

        private void Start()
        {
            // Begin sealed, so the reveal has something to reveal.
            _indoorAmount = 1f;
            _targetIndoorAmount = 1f;
            _audioManager?.SetIndoorAttenuation(1f);
        }

        public void ResolveReferences()
        {
            if (_audioManager == null) _audioManager = FindAnyObjectByType<ExposureAudioManager>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();

            if (_doorPanel == null)
            {
                Transform found = transform.Find("Door_Panel");
                if (found != null) _doorPanel = found;
            }

            if (_centerEyeAnchor == null)
            {
                var found = GameObject.Find("CenterEyeAnchor");
                if (found != null) _centerEyeAnchor = found.transform;
                else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
            }
        }

        private void CaptureRotations()
        {
            if (_capturedRotations || _doorPanel == null)
            {
                return;
            }

            _closedRotation = transform.localRotation;
            _openRotation = _closedRotation * Quaternion.Euler(0f, -_openAngle, 0f);
            _capturedRotations = true;
        }

        private void Update()
        {
            BlendAmbience();

            if (_isSwinging || _doorPanel == null)
            {
                return;
            }

            if (!IsWithinReach)
            {
                return;
            }

            if (WasInteractPressed())
            {
                if (_isOpen) Close();
                else Open();
            }
        }

        /// <summary>
        /// The ambience is eased rather than switched, so the city arrives instead of
        /// appearing. An instant cut reads as a bug; a 1.6 second swell reads as a door.
        /// </summary>
        private void BlendAmbience()
        {
            if (Mathf.Approximately(_indoorAmount, _targetIndoorAmount))
            {
                return;
            }

            float step = Time.deltaTime / Mathf.Max(0.01f, _audioBlendSeconds);
            _indoorAmount = Mathf.MoveTowards(_indoorAmount, _targetIndoorAmount, step);
            _audioManager?.SetIndoorAttenuation(_indoorAmount);
        }

        public void Open()
        {
            if (_isOpen || _isSwinging)
            {
                return;
            }

            CaptureRotations();
            _isOpen = true;
            _targetIndoorAmount = 0f;

            _logger?.RecordEvent("balcony_door_opened", 0, "participant_initiated", 0f);
            OnDoorOpened?.Invoke();

            StartCoroutine(Swing(_openRotation));
        }

        public void Close()
        {
            if (!_isOpen || _isSwinging)
            {
                return;
            }

            CaptureRotations();
            _isOpen = false;
            _targetIndoorAmount = 1f;

            // Shutting the door on the drop is avoidance worth measuring, not a failure.
            _logger?.RecordEvent("balcony_door_closed", 0, "participant_initiated", 0f);
            OnDoorClosed?.Invoke();

            StartCoroutine(Swing(_closedRotation));
        }

        private System.Collections.IEnumerator Swing(Quaternion target)
        {
            _isSwinging = true;

            Quaternion from = transform.localRotation;
            float elapsed = 0f;

            while (elapsed < _swingSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _swingSeconds));
                transform.localRotation = Quaternion.Slerp(from, target, t);
                yield return null;
            }

            transform.localRotation = target;
            _isSwinging = false;
        }

        private float ComputeDistanceToDoor()
        {
            if (_centerEyeAnchor == null)
            {
                return float.MaxValue;
            }

            Vector3 doorPos = _doorPanel != null ? _doorPanel.position : transform.position;
            Vector3 a = _centerEyeAnchor.position;

            float dx = a.x - doorPos.x;
            float dz = a.z - doorPos.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private bool WasInteractPressed()
        {
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
                    OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger) ||
                    OVRInput.GetDown(OVRInput.Button.One))
                {
                    return true;
                }
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space))
                {
                    return true;
                }
            }
            catch (Exception) { }
#endif

            return false;
        }
    }
}
