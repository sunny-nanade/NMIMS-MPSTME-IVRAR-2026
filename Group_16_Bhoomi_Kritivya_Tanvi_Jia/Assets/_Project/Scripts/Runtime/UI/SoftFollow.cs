using UnityEngine;

namespace RescueSim.UI
{
    /// <summary>Gently follows the head at an offset, lagging behind quick turns so it never feels glued to the face.</summary>
    public class SoftFollow : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] Vector3 offset = new Vector3(0f, -0.38f, 1.5f);
        [SerializeField, Min(0.5f)] float positionSmoothing = 4f;
        [SerializeField, Min(0.5f)] float rotationSmoothing = 5f;

        public void SetHead(Transform h) => head = h;

        void LateUpdate()
        {
            if (head == null) return;
            float dt = Time.unscaledDeltaTime;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) return;
            fwd.Normalize();
            Quaternion yaw = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 target = head.position + yaw * offset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-positionSmoothing * dt));
            Vector3 away = transform.position - head.position; away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(away, Vector3.up), 1f - Mathf.Exp(-rotationSmoothing * dt));
        }
    }
}
