using System.Collections;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Root component of PF_PlayerRig. Knows the head and hand anchors, places the player on the floor and
    /// guarantees one rig per session (additive scenes carry duplicates that are removed at runtime).
    /// The camera height is never offset in code on a headset: the Floor tracking origin supplies real eye height.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class PlayerRig : MonoBehaviour
    {
        public static PlayerRig Instance { get; private set; }

        [SerializeField] Transform head;
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;
        [SerializeField] float eyeHeight = DS.EYE_HEIGHT;

        public Transform Head { get { if (head == null) Resolve(); return head; } }
        public Transform LeftHand { get { if (leftHand == null) Resolve(); return leftHand; } }
        public Transform RightHand { get { if (rightHand == null) Resolve(); return rightHand; } }
        public float ConfiguredEyeHeight => eyeHeight;
        /// <summary>Height of the camera above the rig root (the floor).</summary>
        public float CameraHeight => Head != null ? head.position.y - transform.position.y : 0f;

        public static event System.Action<PlayerRig> RigReady;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // additive scene carried its own rig: remove the duplicate
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            StartCoroutine(ConfigureTracking());
            RigReady?.Invoke(this);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Resolve()
        {
            var cr = GetComponent<OVRCameraRig>();
            if (cr == null) cr = GetComponentInChildren<OVRCameraRig>(true);
            if (cr != null)
            {
                if (head == null) head = cr.centerEyeAnchor;
                if (leftHand == null) leftHand = cr.leftHandAnchor;
                if (rightHand == null) rightHand = cr.rightHandAnchor;
            }
        }

        IEnumerator ConfigureTracking()
        {
            for (int i = 0; i < 120 && OVRManager.instance == null; i++) yield return null;
            if (OVRManager.instance != null)
                OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        }

        /// <summary>Place the player so the head stands over the given floor point (only X/Z/yaw change; Y stays on the floor).</summary>
        public void PlaceAt(Vector3 floorPoint, float yawDegrees)
        {
            var cc = GetComponent<CharacterController>();
            bool wasOn = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.position = new Vector3(floorPoint.x, floorPoint.y, floorPoint.z);
            if (head != null)
            {
                Vector3 off = head.position - transform.position;
                off.y = 0f;
                transform.position -= off;
            }
            if (cc != null) cc.enabled = wasOn;
        }

        public void PlaceAt(Transform spawn) { PlaceAt(spawn.position, spawn.eulerAngles.y); }

        /// <summary>Move the head XZ to a point without touching height or yaw.</summary>
        public void TeleportHeadTo(Vector3 floorPoint)
        {
            var cc = GetComponent<CharacterController>();
            bool wasOn = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            Vector3 off = head.position - transform.position; off.y = 0f;
            transform.position = new Vector3(floorPoint.x - off.x, transform.position.y, floorPoint.z - off.z);
            if (cc != null) cc.enabled = wasOn;
        }

        public void RotateAroundHead(float degrees)
        {
            if (head == null) { transform.Rotate(0f, degrees, 0f); return; }
            transform.RotateAround(head.position, Vector3.up, degrees);
        }
    }
}
