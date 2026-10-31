using System;
using UnityEngine;

namespace Acrophobia.Locomotion
{
    /// <summary>
    /// Keeps the participant standing on the platform. Replaces PlayerGravityFall.
    ///
    /// The brief forbids falling outright: "NEVER implement falling, fall damage, or any
    /// drop the user does not initiate." PlayerGravityFall did the opposite - gravity at
    /// -9.81, terminal velocity -50 m/s, a fall blackout below Y=15 and a respawn. It
    /// also provided thumbstick and WASD smooth locomotion, against "Teleport only. No
    /// smooth locomotion."
    ///
    /// Both are gone. What that component did that was genuinely needed is kept:
    /// grounding the capsule under the tracked head, and stopping the participant from
    /// leaning or stepping through railings.
    ///
    /// Walking off an edge therefore cannot drop the participant. Instead the virtual
    /// body is held at the last solid ground: the rig is counter-offset so the tracked
    /// head can keep moving in the real room while the virtual position stays on the
    /// platform. That is the right trade for exposure therapy - a participant who is
    /// afraid of heights must be able to stand at an edge and look down knowing with
    /// certainty that nothing will make them fall, because the whole therapeutic
    /// contract rests on that certainty.
    ///
    /// Horizontal travel is physical room-scale only. Deliberate travel belongs to the
    /// teleport system; this component never moves the participant on its own.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGroundingController : MonoBehaviour
    {
        [Tooltip("Maximum ground search distance beneath the tracked feet.")]
        [SerializeField, Range(0.1f, 2f)] private float _groundCheckDistance = 0.6f;

        [Tooltip("Radius of the sphere cast used to find ground under the feet.")]
        [SerializeField, Range(0.05f, 0.5f)] private float _groundProbeRadius = 0.20f;

        [Tooltip("Collision capsule radius for the participant's body.")]
        [SerializeField, Range(0.1f, 0.5f)] private float _bodyRadius = 0.22f;

        [Tooltip("Stop the participant stepping or leaning through railings.")]
        [SerializeField] private bool _enableBodyAntiClip = true;

        [Tooltip("Hold the virtual body on solid ground when the tracked head moves over open air.")]
        [SerializeField] private bool _enableEdgeContainment = true;

        [Tooltip("Consecutive frames the floor must be missing before containment acts.")]
        [SerializeField, Range(1, 15)] private int _framesBeforeContainment = 5;

        [Tooltip("Largest rig correction containment may apply in one frame, in metres.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _maxContainmentCorrection = 0.08f;

        [Tooltip("Geometry treated as standable floor and as barriers.")]
        [SerializeField] private LayerMask _groundLayerMask = ~0;

        [Tooltip("How quickly the body settles onto a new floor height, in metres per second.")]
        [SerializeField, Range(0.5f, 20f)] private float _floorSettleSpeed = 6f;

        [Header("Thumbstick Locomotion")]
        [Tooltip("Enable continuous thumbstick movement and rotation so physical walking is not required.")]
        [SerializeField] private bool _enableThumbstickLocomotion = true;

        [Tooltip("Walking speed in metres per second.")]
        [SerializeField, Range(0.5f, 5.0f)] private float _moveSpeed = 2.2f;

        [Tooltip("Turning speed in degrees per second.")]
        [SerializeField, Range(30f, 180f)] private float _turnSpeed = 80.0f;

        [Tooltip("Deadzone for thumbstick input.")]
        [SerializeField, Range(0.05f, 0.5f)] private float _thumbstickDeadzone = 0.18f;

        [SerializeField] private Transform _centerEyeAnchor;

        private const int OverlapBufferSize = 8;

        private readonly Collider[] _overlapBuffer = new Collider[OverlapBufferSize];
        private CharacterController _characterController;
        private float _currentFloorY;
        private bool _hasFloor;
        private Vector3 _lastSafeHeadPosition;
        private bool _hasSafePosition;

        /// <summary>
        /// True while solid ground is present beneath the tracked feet.
        /// </summary>
        public bool IsGrounded => _hasFloor;

        /// <summary>
        /// World Y of the floor the participant is standing on.
        /// </summary>
        public float CurrentFloorY => _currentFloorY;

        /// <summary>
        /// Fired when edge containment begins holding the participant back from open air.
        /// </summary>
        public event Action OnEdgeContainmentEngaged;

        /// <summary>
        /// Fired when the participant returns over solid ground.
        /// </summary>
        public event Action OnEdgeContainmentReleased;

        private bool _containmentEngaged;
        private int _framesWithoutFloor;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();

            _characterController.radius = _bodyRadius;
            _characterController.height = 1.8f;
            _characterController.center = new Vector3(0f, 0.9f, 0f);
            _characterController.skinWidth = 0.05f;
            _characterController.stepOffset = 0.3f;
            _characterController.minMoveDistance = 0f;

            // No gravity is applied anywhere in this component, so the controller is
            // never used to simulate a fall. It exists for collision only.

            ResolveReferences();
        }

        public void ResolveReferences()
        {
            if (_centerEyeAnchor == null)
            {
                Transform found = transform.Find("TrackingSpace/CenterEyeAnchor");
                if (found != null)
                {
                    _centerEyeAnchor = found;
                }
                else
                {
                    Camera cam = Camera.main;
                    if (cam != null) _centerEyeAnchor = cam.transform;
                }
            }
        }

        private void Update()
        {
            if (_centerEyeAnchor == null)
            {
                ResolveReferences();
                if (_centerEyeAnchor == null) return;
            }

            ProcessThumbstickLocomotion();
            SyncCapsuleToHead();
            UpdateFloorBeneathFeet();

            if (_enableEdgeContainment)
            {
                ApplyEdgeContainment();
            }

            if (_enableBodyAntiClip)
            {
                ApplyBodyAntiClip();
            }

            SettleOntoFloor();
        }

        /// <summary>
        /// Reads thumbstick and keyboard inputs to provide smooth continuous translation and rotation.
        /// Eliminates the need for physical room-scale walking.
        /// </summary>
        private void ProcessThumbstickLocomotion()
        {
            if (!_enableThumbstickLocomotion || _characterController == null || _centerEyeAnchor == null)
            {
                return;
            }

            // 1. Read translation inputs (Left Thumbstick / WASD)
            Vector2 moveInput = Vector2.zero;

            try
            {
                moveInput = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            if (moveInput.sqrMagnitude < 0.01f)
            {
                var gp = UnityEngine.InputSystem.Gamepad.current;
                if (gp != null)
                {
                    Vector2 stick = gp.leftStick.ReadValue();
                    if (stick.sqrMagnitude > moveInput.sqrMagnitude) moveInput = stick;
                }

                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    float x = 0f;
                    float y = 0f;
                    if (kb.wKey.isPressed) y += 1f;
                    if (kb.sKey.isPressed) y -= 1f;
                    if (kb.aKey.isPressed) x -= 1f;
                    if (kb.dKey.isPressed) x += 1f;
                    Vector2 kbVec = new Vector2(x, y);
                    if (kbVec.sqrMagnitude > moveInput.sqrMagnitude) moveInput = kbVec.normalized;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (moveInput.sqrMagnitude < 0.01f)
            {
                try
                {
                    float h = Input.GetAxisRaw("Horizontal");
                    float v = Input.GetAxisRaw("Vertical");
                    Vector2 leg = new Vector2(h, v);
                    if (leg.sqrMagnitude > moveInput.sqrMagnitude) moveInput = leg.normalized;
                }
                catch (Exception) { }
            }
#endif

            if (moveInput.magnitude > _thumbstickDeadzone)
            {
                Vector3 headFwd = _centerEyeAnchor.forward;
                headFwd.y = 0f;
                headFwd.Normalize();

                Vector3 headRight = _centerEyeAnchor.right;
                headRight.y = 0f;
                headRight.Normalize();

                Vector3 direction = (headFwd * moveInput.y + headRight * moveInput.x).normalized;
                Vector3 displacement = direction * _moveSpeed * Time.deltaTime;
                _characterController.Move(displacement);
            }

            // 2. Read turn inputs (Right Thumbstick / Arrow Keys / Q-E)
            Vector2 turnInput = Vector2.zero;

            try
            {
                turnInput = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            }
            catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
            if (Mathf.Abs(turnInput.x) < 0.01f)
            {
                var gp = UnityEngine.InputSystem.Gamepad.current;
                if (gp != null)
                {
                    Vector2 stick = gp.rightStick.ReadValue();
                    if (Mathf.Abs(stick.x) > Mathf.Abs(turnInput.x)) turnInput = stick;
                }

                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    float rx = 0f;
                    if (kb.qKey.isPressed || kb.leftArrowKey.isPressed) rx -= 1f;
                    if (kb.eKey.isPressed || kb.rightArrowKey.isPressed) rx += 1f;
                    if (Mathf.Abs(rx) > Mathf.Abs(turnInput.x)) turnInput = new Vector2(rx, 0f);
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Abs(turnInput.x) < 0.01f)
            {
                try
                {
                    if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow)) turnInput.x -= 1f;
                    if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightArrow)) turnInput.x += 1f;
                }
                catch (Exception) { }
            }
#endif

            if (Mathf.Abs(turnInput.x) > _thumbstickDeadzone)
            {
                float rotAngle = turnInput.x * _turnSpeed * Time.deltaTime;
                transform.RotateAround(_centerEyeAnchor.position, Vector3.up, rotAngle);
            }
        }

        /// <summary>
        /// Keeps the collision capsule under the tracked head rather than under the rig
        /// origin.
        ///
        /// Room-scale means the head wanders away from the rig, so a capsule pinned to
        /// the rig sits somewhere the participant is not. Anti-clip was testing an
        /// overlap at the head and then resolving it at the rig, which is why leaning
        /// and stepping through walls still worked. PlayerGravityFall did this sync and
        /// it was lost when that component was replaced.
        /// </summary>
        private void SyncCapsuleToHead()
        {
            if (_characterController == null || _centerEyeAnchor == null) return;

            Vector3 headLocal = transform.InverseTransformPoint(_centerEyeAnchor.position);
            _characterController.center = new Vector3(headLocal.x, 0.9f, headLocal.z);
        }

        /// <summary>
        /// Finds the floor directly beneath the tracked head's ground projection. Uses a
        /// sphere cast so a foot half over an edge still finds the platform.
        /// </summary>
        private void UpdateFloorBeneathFeet()
        {
            Vector3 headPos = _centerEyeAnchor.position;
            Vector3 origin = new Vector3(headPos.x, transform.position.y + 0.4f, headPos.z);

            // NonAlloc is not needed: the single-hit overload does not allocate, unlike
            // the SphereCastAll the previous implementation used every frame.
            if (Physics.SphereCast(origin, _groundProbeRadius, Vector3.down,
                    out RaycastHit hit, _groundCheckDistance + 0.4f,
                    _groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                _currentFloorY = hit.point.y;
                _hasFloor = true;

                _lastSafeHeadPosition = new Vector3(headPos.x, headPos.y, headPos.z);
                _hasSafePosition = true;

                if (_containmentEngaged)
                {
                    _containmentEngaged = false;
                    OnEdgeContainmentReleased?.Invoke();
                }
            }
            else
            {
                _hasFloor = false;
            }
        }

        /// <summary>
        /// Holds the virtual body over solid ground. When the tracked head passes beyond
        /// the platform the rig is counter-offset by the overshoot, so the participant's
        /// virtual position stays put instead of walking into open air.
        ///
        /// This is the mechanism that makes falling impossible rather than merely
        /// unlikely.
        /// </summary>
        private void ApplyEdgeContainment()
        {
            if (_hasFloor || !_hasSafePosition)
            {
                _framesWithoutFloor = 0;
                return;
            }

            // Debounce. A sphere cast at a platform boundary flickers between hit and
            // miss on consecutive frames, and correcting on a single miss made the rig
            // creep continuously - which also fed phantom head movement into the retreat
            // detector. Require the floor to be genuinely gone before touching the rig.
            _framesWithoutFloor++;
            if (_framesWithoutFloor < _framesBeforeContainment)
            {
                return;
            }

            Vector3 headPos = _centerEyeAnchor.position;

            Vector3 overshoot = new Vector3(
                headPos.x - _lastSafeHeadPosition.x,
                0f,
                headPos.z - _lastSafeHeadPosition.z);

            float overshootDistance = overshoot.magnitude;

            if (overshootDistance < 0.0005f)
            {
                return;
            }

            // Clamp so a bad floor reading cannot fling the rig across the scene. An
            // earlier version applied the raw offset every frame the floor was missing
            // and never re-anchored, so the correction accumulated and walked the rig
            // several metres off its spawn.
            if (overshootDistance > _maxContainmentCorrection)
            {
                overshoot = overshoot * (_maxContainmentCorrection / overshootDistance);
            }

            // Move the rig back by the overshoot: the real head keeps going, the virtual
            // body does not.
            transform.position -= overshoot;

            // Re-anchor to where the head now sits. This makes the correction idempotent:
            // if the participant stops moving, the next frame computes a zero overshoot
            // instead of re-applying this one.
            _lastSafeHeadPosition = new Vector3(
                _centerEyeAnchor.position.x,
                _lastSafeHeadPosition.y,
                _centerEyeAnchor.position.z);

            if (!_containmentEngaged)
            {
                _containmentEngaged = true;
                OnEdgeContainmentEngaged?.Invoke();
            }
        }

        /// <summary>
        /// Pushes the rig out of any barrier the participant's body is intersecting, so
        /// leaning through a railing is not possible.
        /// </summary>
        private void ApplyBodyAntiClip()
        {
            Vector3 headPos = _centerEyeAnchor.position;
            Vector3 bodyCentre = new Vector3(headPos.x, _currentFloorY + 0.9f, headPos.z);

            Vector3 capsuleBottom = bodyCentre + Vector3.down * 0.35f;
            Vector3 capsuleTop = bodyCentre + Vector3.up * 0.35f;

            // Preallocated buffer: the NonAlloc overload keeps this off the GC, unlike
            // the SphereCastAll the previous implementation ran every frame.
            int count = Physics.OverlapCapsuleNonAlloc(
                capsuleBottom, capsuleTop, _bodyRadius, _overlapBuffer,
                _groundLayerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider other = _overlapBuffer[i];
                if (other == null)
                {
                    continue;
                }

                // Never resolve against our own rig colliders.
                if (other == _characterController || other.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!Physics.ComputePenetration(
                        _characterController, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance))
                {
                    continue;
                }

                // Horizontal only. Vertical correction is the floor settle's job, and
                // letting a penetration push the rig downward could read as a drop.
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.000001f)
                {
                    transform.position += direction.normalized * distance;
                }
            }
        }

        /// <summary>
        /// Eases the rig to the floor height. No acceleration, no velocity accumulation -
        /// a bounded settle, so a lower floor can never become a fall.
        /// </summary>
        private void SettleOntoFloor()
        {
            if (!_hasFloor)
            {
                return;
            }

            Vector3 pos = transform.position;
            float delta = _currentFloorY - pos.y;

            if (Mathf.Abs(delta) < 0.0005f)
            {
                return;
            }

            float maxStep = _floorSettleSpeed * Time.deltaTime;
            pos.y += Mathf.Clamp(delta, -maxStep, maxStep);
            transform.position = pos;
        }
    }
}
