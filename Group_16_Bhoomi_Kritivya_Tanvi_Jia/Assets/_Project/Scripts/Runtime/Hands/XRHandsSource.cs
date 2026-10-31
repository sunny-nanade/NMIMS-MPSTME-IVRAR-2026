#if RESCUESIM_XR_HANDS
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace RescueSim.Hands
{
    /// <summary>Reads tracked hands from the XR Hands subsystem (OpenXR hand tracking on Quest). Joint poses are converted to world space.</summary>
    public sealed class XRHandsSource : IHandSource
    {
        readonly XROrigin _origin;
        readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem _sub;
        readonly Vector3[] _left = new Vector3[XRHandJointID.EndMarker.ToIndex()];
        readonly Vector3[] _right = new Vector3[XRHandJointID.EndMarker.ToIndex()];

        public XRHandsSource(XROrigin origin) { _origin = origin; }

        public string Name => "OpenXR hand tracking";

        public bool Available
        {
            get { Refresh(); return _sub != null; }
        }

        void Refresh()
        {
            if (_sub != null && _sub.running) return;
            _sub = null;
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (var s in _subsystems) if (s.running) { _sub = s; break; }
        }

        public bool TryGetHand(bool right, out HandSample sample)
        {
            sample = default;
            Refresh();
            if (_sub == null) return false;
            var hand = right ? _sub.rightHand : _sub.leftHand;
            if (!hand.isTracked) return false;

            Transform space = _origin != null && _origin.CameraFloorOffsetObject != null ? _origin.CameraFloorOffsetObject.transform
                            : _origin != null ? _origin.transform : null;
            if (space == null) return false;

            var joints = right ? _right : _left;
            for (int i = XRHandJointID.BeginMarker.ToIndex(); i < XRHandJointID.EndMarker.ToIndex(); i++)
            {
                var j = hand.GetJoint(XRHandJointIDUtility.FromIndex(i));
                joints[i] = j.TryGetPose(out Pose p) ? space.TransformPoint(p.position) : Vector3.zero;
            }

            sample.tracked = true;
            sample.thumbTip = joints[XRHandJointID.ThumbTip.ToIndex()];
            sample.indexTip = joints[XRHandJointID.IndexTip.ToIndex()];
            sample.wrist = joints[XRHandJointID.Wrist.ToIndex()];
            sample.palm = joints[XRHandJointID.Palm.ToIndex()];
            sample.joints = joints;
            return sample.thumbTip != Vector3.zero && sample.indexTip != Vector3.zero;
        }
    }
}
#endif
