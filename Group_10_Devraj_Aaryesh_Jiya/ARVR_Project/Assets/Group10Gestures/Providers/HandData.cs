using UnityEngine;
using Group10Gestures.Core;

namespace Group10Gestures.Providers
{
    /// <summary>
    /// Represents 21 3D joints of a tracked hand in MediaPipe landmark order:
    /// 0: Wrist
    /// 1-4: Thumb (CMC, MCP, IP, TIP)
    /// 5-8: Index (MCP, PIP, DIP, TIP)
    /// 9-12: Middle (MCP, PIP, DIP, TIP)
    /// 13-16: Ring (MCP, PIP, DIP, TIP)
    /// 17-20: Pinky (MCP, PIP, DIP, TIP)
    /// </summary>
    [System.Serializable]
    public class HandData
    {
        public Handedness Handedness;
        public bool IsTracked;
        public Vector3[] Joints = new Vector3[21];
        public double Timestamp;

        public HandData(Handedness handedness)
        {
            Handedness = handedness;
            IsTracked = false;
            for (int i = 0; i < 21; i++)
            {
                Joints[i] = Vector3.zero;
            }
            Timestamp = 0;
        }

        public void CopyFrom(HandData other)
        {
            Handedness = other.Handedness;
            IsTracked = other.IsTracked;
            Timestamp = other.Timestamp;
            for (int i = 0; i < 21; i++)
            {
                Joints[i] = other.Joints[i];
            }
        }
    }
}
