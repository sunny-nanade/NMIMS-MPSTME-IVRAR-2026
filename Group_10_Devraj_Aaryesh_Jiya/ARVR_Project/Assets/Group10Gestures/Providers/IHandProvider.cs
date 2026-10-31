using UnityEngine;
using Group10Gestures.Core;

namespace Group10Gestures.Providers
{
    /// <summary>
    /// Common interface for hand tracking data sources (Meta XR / Webcam MediaPipe).
    /// Standardizes access to 21 3D landmarks indexed identically to MediaPipe Hands.
    /// </summary>
    public interface IHandProvider
    {
        /// <summary>
        /// Human-readable provider name (e.g. "Meta XR Hand Tracking", "Webcam (UDP 5005)").
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Whether the specified hand is currently actively tracked and providing valid poses.
        /// </summary>
        bool IsTracked(Handedness handedness);

        /// <summary>
        /// Returns the array of 21 3D joint positions for the specified hand.
        /// </summary>
        Vector3[] GetJoints(Handedness handedness);

        /// <summary>
        /// Returns a specific 3D joint position (0-20) for the specified hand.
        /// </summary>
        Vector3 GetJoint(Handedness handedness, int jointIndex);

        /// <summary>
        /// Returns the hardware/packet timestamp in milliseconds for the specified hand.
        /// </summary>
        double GetTimestamp(Handedness handedness);

        /// <summary>
        /// Initializes the provider resources.
        /// </summary>
        void Initialize(GestureConfig config);

        /// <summary>
        /// Updates hand tracking state each frame.
        /// </summary>
        void UpdateProvider();

        /// <summary>
        /// Releases all sockets, threads, and XR resources.
        /// </summary>
        void Shutdown();
    }
}
