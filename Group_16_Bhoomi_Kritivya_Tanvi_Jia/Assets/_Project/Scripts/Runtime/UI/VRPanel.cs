using UnityEngine;

namespace RescueSim.UI
{
    /// <summary>A world-space menu panel that appears in front of the player and stays put (never head-locked).</summary>
    public class VRPanel : MonoBehaviour
    {
        [SerializeField, Min(0.6f)] float distance = 1.7f;
        [SerializeField] float heightOffset = -0.05f;

        const float TrackedMinHeight = 0.5f;   // a tracked headset is never lower than this above its parent
        const float FallbackEyeHeight = 1.6f;
        const float TrackingWaitSeconds = 20f;

        Transform _head;
        float _waitLeft;

        public bool IsVisible => gameObject.activeSelf;

        static bool IsTracked(Transform head) => head.localPosition.y > TrackedMinHeight;

        public void ShowInFrontOf(Transform head)
        {
            _head = head;
            if (head != null)
            {
                Place(head);
                // The first frames after a scene load report the head at the origin; re-place once tracking arrives.
                _waitLeft = IsTracked(head) ? 0f : TrackingWaitSeconds;
            }
            gameObject.SetActive(true);
        }

        void Place(Transform head)
        {
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            float eyeY = head.position.y;
            if (!IsTracked(head)) eyeY = head.root.position.y + FallbackEyeHeight;
            Vector3 pos = head.position + fwd * distance;
            pos.y = eyeY + heightOffset;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        }

        void Update()
        {
            if (_waitLeft <= 0f || _head == null) return;
            _waitLeft -= Time.unscaledDeltaTime;
            if (IsTracked(_head)) { Place(_head); _waitLeft = 0f; }
        }

        public void Hide() { _waitLeft = 0f; gameObject.SetActive(false); }
    }
}
