using UnityEngine;

namespace Group10Gestures.Core
{
    /// <summary>
    /// Central ScriptableObject holding all configurable parameters for
    /// hand tracking, filtering, gesture recognition, drawing, and UI.
    /// </summary>
    [CreateAssetMenu(fileName = "GestureConfig", menuName = "Group10/GestureConfig", order = 1)]
    public class GestureConfig : ScriptableObject
    {
        [Header("Webcam & UDP Settings")]
        [Tooltip("Local UDP port to receive MediaPipe JSON packets on.")]
        public int udpPort = 5005;

        [Tooltip("Timeout in seconds before marking webcam tracking as lost.")]
        public float trackingTimeoutSec = 0.5f;

        [Tooltip("Webcam coordinate scaling factor in Unity world space.")]
        public Vector3 webcamWorldScale = new Vector3(1.2f, 0.9f, 1.0f);

        [Tooltip("Webcam tracking center offset relative to camera/origin.")]
        public Vector3 webcamWorldOffset = new Vector3(0f, 0f, 0.7f);

        [Header("One Euro Filter (Smoothing)")]
        [Tooltip("Minimum cutoff frequency in Hz. Lower values reduce jitter when still.")]
        public float filterMinCutoff = 1.0f;

        [Tooltip("Beta coefficient for speed adaptation. Higher values reduce lag during fast motion.")]
        public float filterBeta = 0.005f;

        [Tooltip("Cutoff frequency for the derivative.")]
        public float filterDCutoff = 1.0f;

        [Header("Gesture Recognition Thresholds (Normalized by Palm Size)")]
        [Tooltip("Pinch enter threshold (distance between thumb and index tip / palm size).")]
        [Range(0.1f, 0.6f)]
        public float pinchEnterThreshold = 0.35f;

        [Tooltip("Pinch exit threshold (hysteresis boundary to prevent flicker).")]
        [Range(0.2f, 0.8f)]
        public float pinchExitThreshold = 0.50f;

        [Tooltip("Ratio of fingertip-to-wrist distance over MCP-to-wrist distance to consider finger curled.")]
        [Range(0.4f, 1.0f)]
        public float fingerCurlThreshold = 0.85f;

        [Tooltip("Ratio of fingertip-to-wrist distance over MCP-to-wrist distance to consider finger extended.")]
        [Range(0.9f, 1.6f)]
        public float fingerExtendThreshold = 1.15f;

        [Tooltip("Index-to-middle fingertip distance ratio for Peace/V gesture spread.")]
        [Range(0.15f, 0.6f)]
        public float peaceSpreadThreshold = 0.25f;

        [Tooltip("Number of consecutive frames required to debounce gesture state transitions.")]
        [Range(1, 10)]
        public int debounceFrames = 3;

        [Header("Two-Hand & Mode Switching")]
        [Tooltip("Continuous hold time required with both hands open to trigger mode change (seconds).")]
        public float bothOpenHoldDuration = 0.6f;

        [Tooltip("Cooldown period after mode switch to prevent flicker (seconds).")]
        public float modeSwitchCooldown = 1.0f;

        [Header("Drawing & Interaction")]
        [Tooltip("Grid snapping interval in meters.")]
        public float gridSnapSize = 0.25f;

        [Tooltip("Dwell ring hold time for button activation (seconds).")]
        public float dwellActivationTime = 0.8f;

        [Tooltip("Minimum extrusion height in meters.")]
        public float minExtrudeHeight = 0.2f;

        [Tooltip("Maximum extrusion height in meters.")]
        public float maxExtrudeHeight = 4.0f;
    }
}
