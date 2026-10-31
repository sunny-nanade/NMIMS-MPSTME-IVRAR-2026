using UnityEngine;

namespace RescueSim.UI
{
    /// <summary>Rotates a world-space label around Y so it faces the main camera (yaw only, no roll).</summary>
    public class FaceCamera : MonoBehaviour
    {
        Transform _cam;

        void LateUpdate()
        {
            if (_cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            Vector3 away = transform.position - _cam.position; away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
