using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace CampusQuest
{
    [RequireComponent(typeof(CharacterController))]
    public class DesktopPlayerController : MonoBehaviour
    {
        [Header("Camera")]
        public Camera view;

        [Header("Desktop")]
        public float speed = 2.5f;

        [Header("XR")]
        public bool useXRInput;
        public InputActionAsset xrActions;
        public float turnSpeed = 90f;

        CharacterController motor;
        float pitch;
        float fall;

        // XR head tracking can move the camera independently of thumbstick
        // locomotion. Route its horizontal delta through the same capsule so
        // room-scale movement cannot bypass campus walls and stair ramps.
        Transform xrCameraOffset;
        Vector3 lastTrackedHeadLocalPosition;
        bool hasLastTrackedHeadPosition;

        InputAction xrMove;
        InputAction xrTurn;

        public InteractableObject Target { get; private set; }

        void Awake()
        {
            motor = GetComponent<CharacterController>();
            motor.detectCollisions = true;
            motor.enableOverlapRecovery = true;
            xrCameraOffset = useXRInput && view != null
                ? view.transform.parent
                : null;

            if (useXRInput)
            {
                // The parent player controller is the single collision body and mover.
                // Disable the sample rig's second capsule and locomotion providers so
                // neither can move the XR Origin through walls independently.
                foreach (var controller in GetComponentsInChildren<CharacterController>(true))
                    if (controller != motor)
                        controller.enabled = false;

                foreach (var provider in GetComponentsInChildren<LocomotionProvider>(true))
                    provider.enabled = false;
            }

            if (xrActions != null)
            {
                xrMove = xrActions.FindAction(
                    "XRI Left Locomotion/Move", false);

                xrTurn = xrActions.FindAction(
                    "XRI Right Locomotion/Turn", false);
            }
        }

        void OnEnable()
        {
            if (!useXRInput)
                return;

            xrMove?.Enable();
            xrTurn?.Enable();
            hasLastTrackedHeadPosition = false;
        }

        void LateUpdate()
        {
            if (!useXRInput || !motor.enabled || view == null ||
                xrCameraOffset == null || xrCameraOffset.parent == null)
                return;

            Vector3 trackedHeadLocalPosition = view.transform.localPosition;
            if (!hasLastTrackedHeadPosition)
            {
                lastTrackedHeadLocalPosition = trackedHeadLocalPosition;
                hasLastTrackedHeadPosition = true;
                return;
            }

            // Only horizontal room-scale movement is transferred to the body.
            // Vertical head tracking remains untouched; gravity and the ramp
            // colliders determine the player's floor height.
            Vector3 trackedDelta = trackedHeadLocalPosition - lastTrackedHeadLocalPosition;
            trackedDelta.y = 0f;
            lastTrackedHeadLocalPosition = trackedHeadLocalPosition;
            if (trackedDelta.sqrMagnitude < 0.000001f)
                return;

            Transform trackingOrigin = xrCameraOffset.parent;
            Vector3 requestedWorldDelta = trackingOrigin.TransformDirection(trackedDelta);
            requestedWorldDelta.y = 0f;

            // Move the collision capsule with the headset's tracked horizontal
            // motion. CharacterController.Move is clamped by walls and stair ramps.
            motor.Move(requestedWorldDelta);

            // CameraOffset is not tracked by the headset. Counter the tracked
            // local delta there so the camera stays at the capsule's collision-
            // constrained position instead of remaining beyond the wall.
            xrCameraOffset.localPosition -= trackedDelta;
        }

        void Update()
        {
            var tm = SceneTransitionManager.Instance;
            var ui = DesktopHUD.Instance;

            if (tm == null || tm.Busy || ui == null || ui.MenuOpen)
            {
                Target = null;
                return;
            }

            Vector2 moveInput;

            if (useXRInput)
            {
                moveInput = xrMove != null
                    ? xrMove.ReadValue<Vector2>()
                    : Vector2.zero;

                // Meta XR Simulator keyboard movement is routed through this same
                // CharacterController, so W/A/S/D obey the campus collision geometry.
                if (moveInput.sqrMagnitude < 0.0001f)
                    moveInput = ReadSimulatorKeyboardMove();

                Vector2 turnInput = xrTurn != null
                    ? xrTurn.ReadValue<Vector2>()
                    : Vector2.zero;

                if (view != null)
                {
                    // Turn the body about its collision capsule. Rotating around the
                    // tracked head translates the capsule and can push it through walls.
                    transform.Rotate(0f, turnInput.x * turnSpeed * Time.deltaTime, 0f, Space.World);
                }
            }
            else
            {
                Vector2 look = DesktopInput.Look;
                transform.Rotate(0f, look.x, 0f);
                pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);

                if (view != null)
                    view.transform.localRotation =
                        Quaternion.Euler(pitch, 0f, 0f);

                moveInput = DesktopInput.Move;
            }

            if (motor.isGrounded && fall < 0f)
                fall = -2f;

            fall = Mathf.Max(fall - 18f * Time.deltaTime, -25f);

            Vector3 forward;
            Vector3 right;

            if (useXRInput && view != null)
            {
                // Move relative to the headset's horizontal facing direction. The
                // resulting displacement still goes through this player's capsule.
                forward = view.transform.forward;
                right = view.transform.right;
                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();
            }
            else
            {
                forward = transform.forward;
                right = transform.right;
            }

            Vector3 movement =
                (right * moveInput.x + forward * moveInput.y) * speed;

            motor.Move(
                (movement + Vector3.up * fall) * Time.deltaTime);

            if (transform.position.y < -8f)
                tm.Travel(tm.CurrentLevel, "start");

            Target = null;

            if (view != null &&
                Physics.Raycast(
                    view.transform.position,
                    view.transform.forward,
                    out RaycastHit hit,
                    3.5f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                Target = hit.collider.GetComponentInParent<InteractableObject>();

                // Keep keyboard interaction when running the desktop version.
                if (!useXRInput &&
                    Target != null &&
                    DesktopInput.Key("E"))
                {
                    Target.Use();
                }
            }
        }

        static Vector2 ReadSimulatorKeyboardMove()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return Vector2.zero;

            float x = (keyboard.dKey.isPressed ? 1f : 0f) -
                      (keyboard.aKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed ? 1f : 0f) -
                      (keyboard.sKey.isPressed ? 1f : 0f);
            var input = new Vector2(x, y);
            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        public void Place(Transform spawn)
        {
            motor.enabled = false;
            transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            fall = 0f;
            pitch = 0f;

            // XR tracking controls the camera rotation in headset mode.
            if (!useXRInput && view != null)
                view.transform.localRotation = Quaternion.identity;

            motor.enabled = true;
        }
    }
}
