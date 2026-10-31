using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Anything the player can pick up. Wraps the ISDK Grabbable(s) set up by the Quick Actions grab wizards.
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(100)]   // hold pose runs after KeyboardGrab / ISDK have placed the item this frame
    public class GrabItem : MonoBehaviour
    {
        public enum HoldPose { Free, Inspect, Read }

        public ItemKind kind;
        [Tooltip("While held: Free = as grabbed, Inspect = top face turned to the eyes, Read = document upright facing the eyes at reading distance")]
        public HoldPose holdPose = HoldPose.Free;
        public string displayName = "Item";
        [TextArea] public string caption;
        [TextArea] public string wrongZoneHint;

        public static event Action<GrabItem> AnyGrabbed;
        public static event Action<GrabItem> AnyStored;

        public bool IsHeld { get; private set; }
        public bool Consumed { get; private set; }
        /// Drop zones only react to items the player has actually handled.
        public bool EverHeld { get; set; }
        /// Strapped to the player's wrist (the flashlight): counts as "in hand" for the inventory.
        public bool Mounted { get; set; }
        public bool Stored { get; private set; }
        /// Held by the keyboard grab (KeyboardGrab) rather than an ISDK hand.
        public bool ForceHeld { get; set; }
        /// Resting inside a container (evidence box / bag): stays visible and no longer triggers drop zones.
        public bool Deposited { get; private set; }
        /// Everything except fixed cables can go in the inventory.
        public bool CanStore => kind != ItemKind.Plug && !Consumed;

        Grabbable[] grabbables;
        Rigidbody rb;
        Vector3 homePos;
        Quaternion homeRot;
        float rejectCooldown, settleFor;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grabbables = GetComponentsInChildren<Grabbable>(true);
            homePos = transform.position;
            homeRot = transform.rotation;
        }

        void Update()
        {
            bool handHeld = false;
            foreach (var g in grabbables)
                if (g && g.SelectingPointsCount > 0) { handHeld = true; break; }
            // a real hand took an item the inventory put in the hand: the hand owns it now, so letting go drops it
            if (handHeld) ForceHeld = false;
            bool held = ForceHeld || handHeld;

            if (held != IsHeld)
            {
                IsHeld = held;
                if (held)
                {
                    EverHeld = true;
                    Sfx.Play(Sfx.I ? Sfx.I.pickup : null, 0.5f);
                    Sfx.Haptic();
                    if (!string.IsNullOrEmpty(caption)) GameUI.I?.Caption(caption, Palette.Ink);
                    AnyGrabbed?.Invoke(this);
                }
                else settleFor = 2f;
            }

            if (rejectCooldown > 0) rejectCooldown -= Time.deltaTime;
            // released items fall and rest: never leave one frozen in mid-air (the grab lock can leave it kinematic)
            bool free = EverHeld && !held && !Mounted && !Stored && !Consumed && !Deposited && kind != ItemKind.Plug;
            if (free && rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.WakeUp();
            }
            // a body that was kinematic while carried is still asleep when the grab lock hands it back to physics,
            // so it would hang in the air: keep it awake until it has had time to land
            if (settleFor > 0)
            {
                settleFor -= Time.deltaTime;
                if (free && !rb.isKinematic && rb.IsSleeping()) rb.WakeUp();
            }
            // never lose a puzzle item behind furniture or through the floor
            if (transform.position.y < -1f || transform.position.sqrMagnitude > 100f) ResetHome();
        }

        static Transform eyes;
        Quaternion poseRot;
        bool posing;

        /// Hold pose on top of the ISDK grab (which recomputes the hand-relative pose every frame, so this never
        /// accumulates): the item's +Y face (where its text / label is) turns to the eyes, its +Z edge points up,
        /// so documents are upright, never mirrored, the same for either hand. Smoothed to remove hand jitter.
        void LateUpdate()
        {
            bool active = holdPose != HoldPose.Free && IsHeld && !Consumed;
            if (!active) { posing = false; return; }
            if (!eyes) { var cam = Camera.main; if (!cam) return; eyes = cam.transform; }
            Vector3 p = transform.position;
            Vector3 toEye = eyes.position - p;
            if (holdPose == HoldPose.Read)
            {
                // comfortable reading distance, without leaving the hand far behind
                float dist = toEye.magnitude;
                float want = Mathf.Clamp(dist, 0.3f, 0.5f);
                if (dist > 1e-3f && !Mathf.Approximately(dist, want)) p = eyes.position - toEye / dist * want;
                toEye = eyes.position - p;
            }
            if (toEye.sqrMagnitude < 1e-6f) return;
            Vector3 up = Vector3.ProjectOnPlane(eyes.up, toEye);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, toEye);
            var target = Quaternion.LookRotation(up.normalized, toEye.normalized);
            if (!posing) poseRot = transform.rotation;
            poseRot = Quaternion.Slerp(poseRot, target, 1 - Mathf.Exp(-14f * Time.deltaTime));
            posing = true;
            transform.SetPositionAndRotation(p, poseRot);
        }

        /// Puts the item into the inventory: it disappears from the world until it is taken out again.
        public void Store()
        {
            if (Stored) return;
            AnyStored?.Invoke(this);
            IsHeld = false;
            Mounted = false;
            Stored = true;
            gameObject.SetActive(false);
        }

        /// Brings a stored (or discarded) item back into the world at the given pose.
        public void Restore(Vector3 pos, Quaternion rot)
        {
            Stored = false;
            gameObject.SetActive(true);
            EverHeld = true;
            Teleport(pos, rot);
        }

        /// Sets the item down inside a container and leaves it there, visible.
        public void Deposit(Bounds zone)
        {
            if (Deposited || Consumed) return;
            Deposited = true;
            ForceHeld = false;
            var p = transform.position;
            var e = zone.extents * 0.6f;
            p = new Vector3(Mathf.Clamp(p.x, zone.center.x - e.x, zone.center.x + e.x), Mathf.Clamp(p.y, zone.center.y - e.y, zone.center.y + e.y), Mathf.Clamp(p.z, zone.center.z - e.z, zone.center.z + e.z));
            Teleport(p, transform.rotation);
            rb.isKinematic = true;
            foreach (var g in grabbables) if (g) g.enabled = false;
        }

        public void ResetHome() => Teleport(homePos, homeRot);

        /// Moves the body and the transform together (an interpolated rigidbody would otherwise snap back).
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
        }

        public bool CanBeRejected => rejectCooldown <= 0;

        /// Lifts a rejected item out of a container and sets it down just outside, on the player's side,
        /// so it can't stay trapped inside the zone and be rejected again.
        public void Eject(Bounds zone, Vector3 playerPos)
        {
            rejectCooldown = 1.5f;
            var dir = Vector3.ProjectOnPlane(playerPos - zone.center, Vector3.up);
            dir = dir.sqrMagnitude < 1e-4f ? Vector3.back : dir.normalized;
            float reach = new Vector2(zone.extents.x, zone.extents.z).magnitude + 0.12f;
            Teleport(zone.center + dir * reach + Vector3.up * (zone.extents.y + 0.05f), transform.rotation);
        }

        public void Consume(Transform into = null, float duration = 0.6f, float sink = 0.25f)
        {
            if (Consumed) return;
            Consumed = true;
            foreach (var g in grabbables) if (g) g.enabled = false;
            StartCoroutine(ConsumeRoutine(into, duration, sink));
        }

        IEnumerator ConsumeRoutine(Transform into, float duration, float sink)
        {
            rb.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Vector3 start = transform.position;
            Vector3 end = into ? into.position : start;
            Quaternion startRot = transform.rotation;
            Quaternion endRot = into ? into.rotation : startRot;
            for (float t = 0; t < 1; t += Time.deltaTime / duration)
            {
                float e = t * t * (3 - 2 * t);
                transform.SetPositionAndRotation(Vector3.Lerp(start, end, e), Quaternion.Slerp(startRot, endRot, e));
                yield return null;
            }
            Vector3 p = transform.position;
            for (float t = 0; t < 1; t += Time.deltaTime / duration)
            {
                transform.position = p + Vector3.down * sink * t;
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
