using UnityEngine;

namespace RescueSim.Hands
{
    /// <summary>
    /// Turns the thumb-index gap into a stable "pinching" state: two thresholds (hysteresis), a short release debounce so a
    /// jittery fingertip cannot drop a casualty, and a tracking-loss grace so one lost frame is not a release.
    /// Pure logic, no Unity objects, so it is unit-tested directly.
    /// </summary>
    public sealed class PinchDetector
    {
        public float onDistance = 0.025f;          // thumb and index tips closer than this start a pinch
        public float offDistance = 0.050f;         // and must be farther than this to end it
        public float releaseDebounce = 0.12f;      // ...for at least this long
        public float trackingLossGrace = 0.35f;    // tracking can drop this long without ending the pinch

        float _offTime, _lostTime;

        public bool Pinching { get; private set; }
        public bool PressedThisFrame { get; private set; }
        public bool ReleasedThisFrame { get; private set; }

        public void Reset() { Pinching = false; PressedThisFrame = ReleasedThisFrame = false; _offTime = _lostTime = 0f; }

        public void Update(bool tracked, float distance, float dt)
        {
            PressedThisFrame = ReleasedThisFrame = false;
            bool was = Pinching;

            if (!tracked)
            {
                _lostTime += dt;
                if (Pinching && _lostTime >= trackingLossGrace) Pinching = false;
            }
            else
            {
                _lostTime = 0f;
                if (distance <= onDistance) { Pinching = true; _offTime = 0f; }
                else if (Pinching && distance >= offDistance)
                {
                    _offTime += dt;
                    if (_offTime >= releaseDebounce) { Pinching = false; _offTime = 0f; }
                }
                else if (Pinching) _offTime = 0f;      // in the hysteresis band: stay pinched
            }

            PressedThisFrame = Pinching && !was;
            ReleasedThisFrame = !Pinching && was;
        }
    }
}
