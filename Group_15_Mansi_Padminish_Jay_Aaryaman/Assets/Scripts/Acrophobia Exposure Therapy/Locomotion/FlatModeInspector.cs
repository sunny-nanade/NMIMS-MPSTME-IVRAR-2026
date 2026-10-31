using System;
using UnityEngine;
using Acrophobia.ProceduralEnvironment;

namespace Acrophobia.Locomotion
{
    /// <summary>
    /// Mouse-and-keyboard camera control for evaluating the scene without a headset.
    ///
    /// The XR Simulator composites its own synthetic environment into its preview
    /// window, which cannot be switched off from inside the app. During testing that
    /// made it impossible to tell our geometry from Meta's: their bedroom, their glass
    /// door and a portrait of a stranger kept appearing over ours, and several rounds
    /// were spent diagnosing rendering problems that turned out to be theirs.
    ///
    /// With the simulator runtime disabled, the scene renders straight to Unity's Game
    /// view with nothing composited over it. This supplies the movement that would
    /// otherwise be missing, so the room, the lighting, the heights and the ritual can
    /// all be judged on their own.
    ///
    /// Strictly a development aid. It disables itself whenever an XR display is active,
    /// so it can never interfere with a headset build, and it deliberately bypasses the
    /// grounding and containment systems - it is a free camera, not a participant.
    /// </summary>
    public class FlatModeInspector : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 10f)] private float _moveSpeed = 2.2f;
        [SerializeField, Range(1f, 20f)] private float _sprintSpeed = 6f;
        [SerializeField, Range(0.5f, 8f)] private float _lookSensitivity = 2.5f;

        [Tooltip("Eye height above the floor, matching a standing adult.")]
        [SerializeField, Range(1.2f, 2f)] private float _eyeHeight = 1.65f;

        [Tooltip("Hold right mouse button to look, instead of always looking.")]
        [SerializeField] private bool _requireRightMouseToLook = true;

        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private bool _active;

        public bool IsActive => _active;

        private void Start()
        {
            // Only take over when there is genuinely no headset driving the view.
            _active = !UnityEngine.XR.XRSettings.isDeviceActive;

            if (!_active)
            {
                enabled = false;
                return;
            }

            _camera = GetComponentInChildren<Camera>();
            if (_camera == null) _camera = Camera.main;

            if (_camera != null)
            {
                Vector3 e = _camera.transform.eulerAngles;
                _yaw = e.y;
                _pitch = 0f;

                // Stand the camera at eye height above the rig, since no tracking will.
                Vector3 p = _camera.transform.position;
                p.y = transform.position.y + _eyeHeight;
                _camera.transform.position = p;
            }

            Debug.Log("[FlatModeInspector] No XR display active - flat inspection mode on. " +
                      "WASD to move, Q/E down/up, right-drag to look, Shift to sprint.", this);
        }

        private void Update()
        {
            if (!_active || _camera == null) return;

            UpdateLook();
            UpdateMove();
        }

        private void UpdateLook()
        {
            bool looking = !_requireRightMouseToLook || ReadRightMouseHeld();
            if (!looking) return;

            Vector2 delta = ReadMouseDelta();
            _yaw += delta.x * _lookSensitivity * 0.1f;
            _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity * 0.1f, -89f, 89f);

            _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void UpdateMove()
        {
            Vector3 input = ReadMoveInput();
            if (input.sqrMagnitude < 0.0001f) return;

            float speed = ReadSprintHeld() ? _sprintSpeed : _moveSpeed;

            // Horizontal movement follows where you are looking, flattened, so the
            // camera does not sink or rise when walking while looking down.
            Vector3 forward = _camera.transform.forward; forward.y = 0f; forward.Normalize();
            Vector3 right = _camera.transform.right; right.y = 0f; right.Normalize();

            Vector3 move = forward * input.z + right * input.x + Vector3.up * input.y;
            _camera.transform.position += move * speed * Time.deltaTime;
        }

        private static Vector2 ReadMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m != null) return m.delta.ReadValue();
            }
            catch (Exception) { }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { return new Vector2(Input.GetAxis("Mouse X") * 10f, Input.GetAxis("Mouse Y") * 10f); }
            catch (Exception) { }
#endif
            return Vector2.zero;
        }

        private static bool ReadRightMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m != null) return m.rightButton.isPressed;
            }
            catch (Exception) { }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { return Input.GetMouseButton(1); } catch (Exception) { }
#endif
            return false;
        }

        private static bool ReadSprintHeld()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                var k = UnityEngine.InputSystem.Keyboard.current;
                if (k != null) return k.leftShiftKey.isPressed;
            }
            catch (Exception) { }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { return Input.GetKey(KeyCode.LeftShift); } catch (Exception) { }
#endif
            return false;
        }

        private static Vector3 ReadMoveInput()
        {
            Vector3 v = Vector3.zero;

#if ENABLE_INPUT_SYSTEM
            try
            {
                var k = UnityEngine.InputSystem.Keyboard.current;
                if (k != null)
                {
                    if (k.wKey.isPressed) v.z += 1f;
                    if (k.sKey.isPressed) v.z -= 1f;
                    if (k.dKey.isPressed) v.x += 1f;
                    if (k.aKey.isPressed) v.x -= 1f;
                    if (k.eKey.isPressed) v.y += 1f;
                    if (k.qKey.isPressed) v.y -= 1f;
                    return v;
                }
            }
            catch (Exception) { }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (Input.GetKey(KeyCode.W)) v.z += 1f;
                if (Input.GetKey(KeyCode.S)) v.z -= 1f;
                if (Input.GetKey(KeyCode.D)) v.x += 1f;
                if (Input.GetKey(KeyCode.A)) v.x -= 1f;
                if (Input.GetKey(KeyCode.E)) v.y += 1f;
                if (Input.GetKey(KeyCode.Q)) v.y -= 1f;
            }
            catch (Exception) { }
#endif
            return v;
        }
    }
}
