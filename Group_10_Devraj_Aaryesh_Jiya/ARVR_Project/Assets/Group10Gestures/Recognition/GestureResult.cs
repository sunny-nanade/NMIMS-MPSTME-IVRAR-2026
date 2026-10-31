namespace Group10Gestures.Recognition
{
    /// <summary>
    /// Encapsulates the output of the gesture recognition pipeline for a single hand.
    /// </summary>
    [System.Serializable]
    public struct GestureResult
    {
        public GestureType Gesture;
        public float Confidence;      // 0.0 to 1.0
        public float PinchDistance;    // Normalized thumb-index distance
        public float PalmSize;         // Wrist to Middle MCP in meters
        public bool IsTracked;

        public static GestureResult Empty => new GestureResult
        {
            Gesture = GestureType.None,
            Confidence = 0.0f,
            PinchDistance = 1.0f,
            PalmSize = 0.1f,
            IsTracked = false
        };
    }
}
