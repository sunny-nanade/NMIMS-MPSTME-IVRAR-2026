namespace RescueSim.Hands
{
    /// <summary>
    /// Makes carrying with a bare hand comfortable and hard to drop by accident. While a casualty is being carried with this
    /// hand, the hold stays asserted even if the fingers relax. The player puts the casualty down with a deliberate NEW
    /// pinch (open the hand first, then pinch again). Pure logic, unit-tested.
    /// </summary>
    public sealed class CarryLatch
    {
        public float minCarrySeconds = 0.8f;       // a pinch right after lifting is ignored
        public float openSeconds = 0.25f;          // the hand must have been open this long before a new pinch counts
        public float cooldownSeconds = 0.8f;       // after a release the hold is suppressed (no instant re-grab)

        float _carryTime, _openTime, _cooldown;
        bool _wasCarrying;

        public bool Latched { get; private set; }
        public bool ReleaseRequested { get; private set; }
        public bool InCooldown => _cooldown > 0f;

        public void Reset() { Latched = false; ReleaseRequested = false; _carryTime = _openTime = _cooldown = 0f; _wasCarrying = false; _hasBeenOpen = _needOpen = false; }

        /// <param name="carryingWithThisHand">The casualty being carried is held by this hand's interactor.</param>
        /// <param name="pinching">Current pinch state of this hand.</param>
        /// <param name="pinchPressed">The pinch started this frame.</param>
        /// <returns>True when the hand should assert "select held" to the interactor.</returns>
        public bool Update(bool carryingWithThisHand, bool pinching, bool pinchPressed, float dt)
        {
            ReleaseRequested = false;
            if (_cooldown > 0f) _cooldown -= dt;

            if (carryingWithThisHand)
            {
                if (!_wasCarrying) { Latched = true; _carryTime = 0f; _openTime = 0f; }
                _carryTime += dt;
                _openTime = pinching ? 0f : _openTime + dt;
                if (Latched && _carryTime >= minCarrySeconds && _openTime >= 0f && pinchPressed && _hasBeenOpen)
                {
                    Latched = false; ReleaseRequested = true; _cooldown = cooldownSeconds; _needOpen = true;
                }
                if (!pinching && _openTime >= openSeconds) _hasBeenOpen = true;
            }
            else
            {
                if (_wasCarrying && Latched) Latched = false;           // carry ended some other way
                _hasBeenOpen = false;
            }
            _wasCarrying = carryingWithThisHand;
            if (!carryingWithThisHand) _hasBeenOpen = false;

            if (_needOpen) { if (!pinching) _needOpen = false; return false; }   // the release pinch must end before a new hold counts
            if (_cooldown > 0f) return false;
            return Latched || pinching;
        }

        bool _hasBeenOpen, _needOpen;
    }
}
