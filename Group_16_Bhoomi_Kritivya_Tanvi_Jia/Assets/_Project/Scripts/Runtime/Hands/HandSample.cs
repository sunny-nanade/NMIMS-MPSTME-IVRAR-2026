using UnityEngine;

namespace RescueSim.Hands
{
    /// <summary>One frame of tracked-hand data in world space (what the input layer needs; independent of the XR Hands package).</summary>
    public struct HandSample
    {
        public bool tracked;
        public Vector3 thumbTip, indexTip, wrist, palm;
        /// <summary>Optional: all 26 joint positions in world space (for drawing the hand). May be null.</summary>
        public Vector3[] joints;

        public Vector3 PinchPoint => (thumbTip + indexTip) * 0.5f;
        public float PinchDistance => Vector3.Distance(thumbTip, indexTip);
    }

    /// <summary>Where hand data comes from: the headset's XR Hands subsystem at runtime, or a scripted source in tests.</summary>
    public interface IHandSource
    {
        string Name { get; }
        /// <summary>False when the runtime can never provide hands (no subsystem), so the input layer stays idle.</summary>
        bool Available { get; }
        bool TryGetHand(bool right, out HandSample sample);
    }

    /// <summary>
    /// Scripted hand data for EditMode/PlayMode tests and Editor development. This is SIMULATED input: it exercises the same
    /// pinch, carry and hand-over code as real tracking but says nothing about how real hand tracking feels on a headset.
    /// </summary>
    public sealed class SimulatedHandSource : IHandSource
    {
        readonly HandSample[] _hands = new HandSample[2];
        public string Name => "Simulated";
        public bool Available => true;

        public bool TryGetHand(bool right, out HandSample sample)
        {
            sample = _hands[right ? 1 : 0];
            return sample.tracked;
        }

        /// <summary>Places a hand so the pinch point is at <paramref name="pinchPoint"/> with the given thumb-index gap (metres).</summary>
        public void Set(bool right, Vector3 pinchPoint, float pinchGap, bool tracked = true)
        {
            var s = new HandSample
            {
                tracked = tracked,
                thumbTip = pinchPoint + new Vector3(0f, -pinchGap * 0.5f, 0f),
                indexTip = pinchPoint + new Vector3(0f, pinchGap * 0.5f, 0f),
                wrist = pinchPoint + new Vector3(0f, 0f, -0.18f),
                palm = pinchPoint + new Vector3(0f, 0f, -0.08f),
            };
            _hands[right ? 1 : 0] = s;
        }

        public void Lose(bool right) { var s = _hands[right ? 1 : 0]; s.tracked = false; _hands[right ? 1 : 0] = s; }
    }
}
