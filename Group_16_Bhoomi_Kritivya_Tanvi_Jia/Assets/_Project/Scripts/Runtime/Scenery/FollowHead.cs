using UnityEngine;

namespace RescueSim.World
{
    /// <summary>Keeps an effect (rain, dust) centred over the player so a small particle volume covers the whole play space.</summary>
    public class FollowHead : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] Vector3 offset = new Vector3(0f, 6f, 0f);

        public void Configure(Transform h, Vector3 o) { head = h; offset = o; }

        void LateUpdate()
        {
            if (head == null) return;
            transform.position = new Vector3(head.position.x, offset.y, head.position.z) + new Vector3(offset.x, 0f, offset.z);
        }
    }
}
