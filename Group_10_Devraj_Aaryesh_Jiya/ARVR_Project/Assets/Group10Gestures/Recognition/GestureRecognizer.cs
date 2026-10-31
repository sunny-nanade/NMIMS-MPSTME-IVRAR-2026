using System;
using UnityEngine;
using Group10Gestures.Core;
using Group10Gestures.Filtering;

namespace Group10Gestures.Recognition
{
    /// <summary>
    /// Pure mathematical geometric gesture recognizer.
    /// Operates on 21 3D hand joints normalized by anatomical palm size (Wrist to Middle-MCP).
    /// Uses hysteresis thresholds and N-frame debouncing to provide rock-solid, flicker-free gesture labels.
    /// Recognizes: OpenPalm, Fist, Pinch, Point, Peace, ThumbsUp, None.
    /// Detects two-hand BothOpen held for 0.6s with 1.0s cooldown to switch modes.
    /// </summary>
    public class GestureRecognizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GestureConfig _config;
        [SerializeField] private HandFilter _handFilter;

        // Current debounced results
        public GestureResult LeftResult { get; private set; }
        public GestureResult RightResult { get; private set; }

        // Both-Open Mode Toggle State
        public float BothOpenProgress { get; private set; } // 0.0 to 1.0 for UI dwell ring
        public event Action OnBothHandsOpenTriggered;

        private float _bothOpenTimer = 0f;
        private float _lastModeSwitchTime = -100f;

        // Debounce tracking per hand
        private GestureType _leftCandidate = GestureType.None;
        private int _leftCandidateFrames = 0;
        private GestureType _leftCommitted = GestureType.None;

        private GestureType _rightCandidate = GestureType.None;
        private int _rightCandidateFrames = 0;
        private GestureType _rightCommitted = GestureType.None;

        private void Start()
        {
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<GestureConfig>();
            }
            if (_handFilter == null)
            {
                _handFilter = FindAnyObjectByType<HandFilter>();
            }
        }

        private void Update()
        {
            if (_handFilter == null) return;

            // Recognize single hand gestures
            LeftResult = EvaluateHand(Handedness.Left, ref _leftCandidate, ref _leftCandidateFrames, ref _leftCommitted);
            RightResult = EvaluateHand(Handedness.Right, ref _rightCandidate, ref _rightCandidateFrames, ref _rightCommitted);

            // Evaluate Two-Hand Gesture (BothOpen)
            EvaluateBothOpen();
        }

        private GestureResult EvaluateHand(
            Handedness handedness,
            ref GestureType candidate,
            ref int candidateFrames,
            ref GestureType committed)
        {
            if (!_handFilter.IsTracked(handedness))
            {
                candidate = GestureType.None;
                candidateFrames = 0;
                committed = GestureType.None;
                return GestureResult.Empty;
            }

            Vector3[] joints = _handFilter.GetFilteredJoints(handedness);

            // Anatomic Palm Normalization (Wrist joint 0 to Middle MCP joint 9)
            float palmSize = Vector3.Distance(joints[0], joints[9]);
            if (palmSize < 0.02f) palmSize = 0.08f; // Guard against degenerate zero vectors

            // Finger tip to wrist distances
            float thumbTipDist = Vector3.Distance(joints[4], joints[0]);
            float indexTipDist = Vector3.Distance(joints[8], joints[0]);
            float middleTipDist = Vector3.Distance(joints[12], joints[0]);
            float ringTipDist = Vector3.Distance(joints[16], joints[0]);
            float pinkyTipDist = Vector3.Distance(joints[20], joints[0]);

            // Finger MCP to wrist distances
            float thumbMcpDist = Vector3.Distance(joints[2], joints[0]);
            float indexMcpDist = Vector3.Distance(joints[5], joints[0]);
            float middleMcpDist = Vector3.Distance(joints[9], joints[0]);
            float ringMcpDist = Vector3.Distance(joints[13], joints[0]);
            float pinkyMcpDist = Vector3.Distance(joints[17], joints[0]);

            // Normalized Pinch Distance (Thumb Tip to Index Tip)
            float pinchDist = Vector3.Distance(joints[4], joints[8]) / palmSize;

            // Finger extension states (Fingertip-to-wrist distance relative to MCP-to-wrist distance)
            float curlRatio = _config != null ? _config.fingerCurlThreshold : 0.85f;
            float extRatio = _config != null ? _config.fingerExtendThreshold : 1.15f;

            bool indexExt = indexTipDist > indexMcpDist * extRatio;
            bool indexCurled = indexTipDist < indexMcpDist * curlRatio;

            bool middleExt = middleTipDist > middleMcpDist * extRatio;
            bool middleCurled = middleTipDist < middleMcpDist * curlRatio;

            bool ringExt = ringTipDist > ringMcpDist * extRatio;
            bool ringCurled = ringTipDist < ringMcpDist * curlRatio;

            bool pinkyExt = pinkyTipDist > pinkyMcpDist * extRatio;
            bool pinkyCurled = pinkyTipDist < pinkyMcpDist * curlRatio;

            bool thumbExt = thumbTipDist > thumbMcpDist * 1.05f;
            bool thumbCurled = thumbTipDist < thumbMcpDist * 0.95f || Vector3.Distance(joints[4], joints[9]) < 0.65f * palmSize;

            // Hysteresis Pinch Check
            float pinchEnter = _config != null ? _config.pinchEnterThreshold : 0.35f;
            float pinchExit = _config != null ? _config.pinchExitThreshold : 0.50f;
            bool isPinching = committed == GestureType.Pinch ? pinchDist < pinchExit : pinchDist < pinchEnter;

            GestureType rawGesture = GestureType.None;
            float confidence = 0.5f;

            if (isPinching)
            {
                rawGesture = GestureType.Pinch;
                confidence = Mathf.Clamp01(1.0f - (pinchDist / pinchExit));
            }
            else if (indexExt && middleCurled && ringCurled && pinkyCurled)
            {
                rawGesture = GestureType.Point;
                confidence = 0.85f;
            }
            else if (indexExt && middleExt && ringCurled && pinkyCurled)
            {
                // Peace / V sign check spread between index and middle
                float spreadDist = Vector3.Distance(joints[8], joints[12]) / palmSize;
                float peaceSpread = _config != null ? _config.peaceSpreadThreshold : 0.25f;
                if (spreadDist > peaceSpread)
                {
                    rawGesture = GestureType.Peace;
                    confidence = Mathf.Clamp01(spreadDist / 0.5f);
                }
            }
            else if (indexCurled && middleCurled && ringCurled && pinkyCurled)
            {
                // Check ThumbsUp: thumb points upward relative to world or palm
                Vector3 thumbDir = (joints[4] - joints[2]).normalized;
                float dotUp = Vector3.Dot(thumbDir, Vector3.up);

                if (thumbExt && dotUp > 0.4f)
                {
                    rawGesture = GestureType.ThumbsUp;
                    confidence = Mathf.Clamp01(dotUp);
                }
                else
                {
                    rawGesture = GestureType.Fist;
                    confidence = 0.9f;
                }
            }
            else if (thumbExt && indexExt && middleExt && ringExt && pinkyExt)
            {
                rawGesture = GestureType.OpenPalm;
                confidence = 0.95f;
            }

            // N-Frame Debounce
            int debounceReq = _config != null ? _config.debounceFrames : 3;
            if (rawGesture == candidate)
            {
                candidateFrames++;
                if (candidateFrames >= debounceReq)
                {
                    committed = rawGesture;
                }
            }
            else
            {
                candidate = rawGesture;
                candidateFrames = 1;
            }

            return new GestureResult
            {
                Gesture = committed,
                Confidence = confidence,
                PinchDistance = pinchDist,
                PalmSize = palmSize,
                IsTracked = true
            };
        }

        private void EvaluateBothOpen()
        {
            float now = Time.time;
            float cooldown = _config != null ? _config.modeSwitchCooldown : 1.0f;
            float requiredHold = _config != null ? _config.bothOpenHoldDuration : 0.6f;

            bool inCooldown = now - _lastModeSwitchTime < cooldown;
            bool bothOpen = LeftResult.IsTracked && RightResult.IsTracked &&
                            LeftResult.Gesture == GestureType.OpenPalm &&
                            RightResult.Gesture == GestureType.OpenPalm;

            if (bothOpen && !inCooldown)
            {
                _bothOpenTimer += Time.deltaTime;
                BothOpenProgress = Mathf.Clamp01(_bothOpenTimer / requiredHold);

                if (_bothOpenTimer >= requiredHold)
                {
                    _lastModeSwitchTime = now;
                    _bothOpenTimer = 0f;
                    BothOpenProgress = 0f;
                    Debug.Log("[GestureRecognizer] Both hands OpenPalm held for 0.6s -> Triggering Mode Switch!");
                    OnBothHandsOpenTriggered?.Invoke();
                }
            }
            else
            {
                _bothOpenTimer = 0f;
                BothOpenProgress = inCooldown ? 0f : Mathf.MoveTowards(BothOpenProgress, 0f, Time.deltaTime * 3f);
            }
        }
    }
}
