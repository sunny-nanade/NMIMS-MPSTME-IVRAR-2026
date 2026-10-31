using System.Collections.Generic;
using UnityEngine;
using Group10Gestures.Core;

namespace Group10Gestures.Providers
{
    /// <summary>
    /// Tracks hands using Meta XR SDK (OVRHand / OVRSkeleton) or OpenXR XR Hands,
    /// mapping hardware bone transforms to the standardized 21 MediaPipe landmark indices.
    /// Compatible with Meta XR Simulator, Quest 3, and Quest 3S.
    /// </summary>
    public class MetaXRHandProvider : MonoBehaviour, IHandProvider
    {
        public string ProviderName => "Meta XR Hand Tracking";

        [SerializeField] private GestureConfig _config;

        [Header("Meta OVR References (Optional / Auto-discovered)")]
        [SerializeField] private OVRHand _leftOVRHand;
        [SerializeField] private OVRHand _rightOVRHand;
        [SerializeField] private OVRSkeleton _leftOVRSkeleton;
        [SerializeField] private OVRSkeleton _rightOVRSkeleton;

        private HandData _leftHand = new HandData(Handedness.Left);
        private HandData _rightHand = new HandData(Handedness.Right);

        // Pre-mapped bone ID lookup for MediaPipe 21 indices
        private static readonly OVRSkeleton.BoneId[] s_MediaPipeToOvrBone = new OVRSkeleton.BoneId[21]
        {
            OVRSkeleton.BoneId.Hand_WristRoot,  // 0: Wrist
            // Meta bone transforms sit at the START of each bone, so the thumb is shifted one step toward the tip:
            // Thumb1 = metacarpal start (CMC), Thumb2 = proximal start (MCP), Thumb3 = distal start (IP), ThumbTip = tip.
            OVRSkeleton.BoneId.Hand_Thumb1,      // 1: Thumb CMC
            OVRSkeleton.BoneId.Hand_Thumb2,      // 2: Thumb MCP
            OVRSkeleton.BoneId.Hand_Thumb3,      // 3: Thumb IP
            OVRSkeleton.BoneId.Hand_ThumbTip,    // 4: Thumb Tip (needed for an accurate thumb-index pinch distance)
            OVRSkeleton.BoneId.Hand_Index1,      // 5: Index MCP
            OVRSkeleton.BoneId.Hand_Index2,      // 6: Index PIP
            OVRSkeleton.BoneId.Hand_Index3,      // 7: Index DIP
            OVRSkeleton.BoneId.Hand_IndexTip,    // 8: Index Tip
            OVRSkeleton.BoneId.Hand_Middle1,     // 9: Middle MCP
            OVRSkeleton.BoneId.Hand_Middle2,     // 10: Middle PIP
            OVRSkeleton.BoneId.Hand_Middle3,     // 11: Middle DIP
            OVRSkeleton.BoneId.Hand_MiddleTip,   // 12: Middle Tip
            OVRSkeleton.BoneId.Hand_Ring1,       // 13: Ring MCP
            OVRSkeleton.BoneId.Hand_Ring2,       // 14: Ring PIP
            OVRSkeleton.BoneId.Hand_Ring3,       // 15: Ring DIP
            OVRSkeleton.BoneId.Hand_RingTip,     // 16: Ring Tip
            OVRSkeleton.BoneId.Hand_Pinky1,      // 17: Pinky MCP
            OVRSkeleton.BoneId.Hand_Pinky2,      // 18: Pinky PIP
            OVRSkeleton.BoneId.Hand_Pinky3,      // 19: Pinky DIP
            OVRSkeleton.BoneId.Hand_PinkyTip     // 20: Pinky Tip
        };

        private Dictionary<OVRSkeleton.BoneId, Transform> _leftBoneMap = new Dictionary<OVRSkeleton.BoneId, Transform>();
        private Dictionary<OVRSkeleton.BoneId, Transform> _rightBoneMap = new Dictionary<OVRSkeleton.BoneId, Transform>();

        public void Initialize(GestureConfig config)
        {
            _config = config;
            DiscoverOvrComponents();
        }

        private void Start()
        {
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<GestureConfig>();
            }
            DiscoverOvrComponents();
        }

        private void DiscoverOvrComponents()
        {
            if (_leftOVRHand == null || _rightOVRHand == null)
            {
                OVRHand[] hands = Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include);
                foreach (var h in hands)
                {
                    if (h.GetHand() == OVRPlugin.Hand.HandLeft) _leftOVRHand = h;
                    else if (h.GetHand() == OVRPlugin.Hand.HandRight) _rightOVRHand = h;
                }
            }

            if (_leftOVRSkeleton == null || _rightOVRSkeleton == null)
            {
                OVRSkeleton[] skeletons = Object.FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include);
                foreach (var s in skeletons)
                {
                    if (s.GetSkeletonType() == OVRSkeleton.SkeletonType.HandLeft) _leftOVRSkeleton = s;
                    else if (s.GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight) _rightOVRSkeleton = s;
                }
            }
        }

        private void RefreshBoneMap(OVRSkeleton skeleton, Dictionary<OVRSkeleton.BoneId, Transform> map)
        {
            if (skeleton == null || !skeleton.IsInitialized) return;
            if (map.Count > 0 && map.Count == skeleton.Bones.Count) return;

            map.Clear();
            foreach (var bone in skeleton.Bones)
            {
                if (bone != null && bone.Transform != null)
                {
                    map[bone.Id] = bone.Transform;
                }
            }
        }

        public void UpdateProvider()
        {
            DiscoverOvrComponents();

            UpdateHand(Handedness.Left, _leftOVRHand, _leftOVRSkeleton, _leftBoneMap, _leftHand);
            UpdateHand(Handedness.Right, _rightOVRHand, _rightOVRSkeleton, _rightBoneMap, _rightHand);
        }

        private void UpdateHand(
            Handedness handedness,
            OVRHand ovrHand,
            OVRSkeleton ovrSkeleton,
            Dictionary<OVRSkeleton.BoneId, Transform> boneMap,
            HandData targetData)
        {
            if (ovrHand == null || ovrSkeleton == null)
            {
                targetData.IsTracked = false;
                return;
            }

            targetData.IsTracked = ovrHand.IsTracked && ovrSkeleton.IsInitialized;
            targetData.Timestamp = Time.realtimeSinceStartupAsDouble * 1000.0;

            if (!targetData.IsTracked) return;

            RefreshBoneMap(ovrSkeleton, boneMap);

            for (int i = 0; i < 21; i++)
            {
                OVRSkeleton.BoneId boneId = s_MediaPipeToOvrBone[i];
                if (boneMap.TryGetValue(boneId, out Transform t) && t != null)
                {
                    targetData.Joints[i] = t.position;
                }
                else if (i == 4 && boneMap.TryGetValue(OVRSkeleton.BoneId.Hand_ThumbTip, out Transform thumbTip) && thumbTip != null)
                {
                    targetData.Joints[i] = thumbTip.position;
                }
                else
                {
                    // Fallback to wrist if bone not found
                    if (boneMap.TryGetValue(OVRSkeleton.BoneId.Hand_WristRoot, out Transform wrist) && wrist != null)
                    {
                        targetData.Joints[i] = wrist.position;
                    }
                }
            }
        }

        public bool IsTracked(Handedness handedness)
        {
            return handedness == Handedness.Left ? _leftHand.IsTracked : _rightHand.IsTracked;
        }

        public Vector3[] GetJoints(Handedness handedness)
        {
            return handedness == Handedness.Left ? _leftHand.Joints : _rightHand.Joints;
        }

        public Vector3 GetJoint(Handedness handedness, int jointIndex)
        {
            if (jointIndex < 0 || jointIndex >= 21) return Vector3.zero;
            return handedness == Handedness.Left ? _leftHand.Joints[jointIndex] : _rightHand.Joints[jointIndex];
        }

        public double GetTimestamp(Handedness handedness)
        {
            return handedness == Handedness.Left ? _leftHand.Timestamp : _rightHand.Timestamp;
        }

        public void Shutdown()
        {
            _leftBoneMap.Clear();
            _rightBoneMap.Clear();
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
