using UnityEngine;

namespace CSER
{
    /// Keeps the player's head out of walls and furniture. The XR Simulator (and real room-scale walking) moves the
    /// headset inside the tracking space, which the Meta locomotor's capsule does not push back, so the player could
    /// walk through everything and out of the room (black screen). Each frame an invisible trigger capsule from knee
    /// height up to the head is depenetrated from the solid colliders under the "Environment" and "Props" roots (walls,
    /// desks, bins, boxes, shredder) and the whole rig is shifted by the correction. Items you can pick up never block.
    public class PlayerBlocker : MonoBehaviour
    {
        public float radius = 0.22f;
        [Tooltip("Bottom of the body capsule above the floor (its rounded end reaches radius lower): low enough for bins and boxes")]
        public float kneeHeight = 0.3f;

        OVRCameraRig rig;
        CapsuleCollider probe;
        Transform env, props;
        readonly Collider[] hits = new Collider[64];

        void Start()
        {
            rig = GetComponent<OVRCameraRig>();
            var go = new GameObject("PlayerBlockerProbe") { layer = 2 };
            probe = go.AddComponent<CapsuleCollider>();
            probe.isTrigger = true;
            probe.radius = radius;
            probe.direction = 1;
            var e = GameObject.Find("Environment");
            env = e ? e.transform : null;
            var pr = GameObject.Find("Props");
            props = pr ? pr.transform : null;
        }

        /// Solid room geometry: walls, furniture and fixed props. Never the rig, triggers, pick-up items or loose
        /// physics bodies (a dropped paper must not shove the player around).
        bool Blocks(Collider c)
        {
            if (!c || c.isTrigger || c.transform.IsChildOf(rig.transform)) return false;
            if (!c.transform.IsChildOf(env) && !(props && c.transform.IsChildOf(props))) return false;
            if (c is MeshCollider mc && !mc.convex) return false;
            if (c.GetComponentInParent<GrabItem>()) return false;
            return !(c.attachedRigidbody && !c.attachedRigidbody.isKinematic);
        }

        void LateUpdate()
        {
            if (!rig || !probe || !env || !rig.centerEyeAnchor) return;
            Vector3 head = rig.centerEyeAnchor.position;
            float floor = rig.transform.position.y;

            // safety net: thrown far outside the building -> back to the start pose
            var sp = rig.GetComponent<StartPose>();
            if (sp && (head.y < floor - 2f || new Vector2(head.x - sp.position.x, head.z - sp.position.z).magnitude > 12f))
            {
                rig.transform.SetPositionAndRotation(sp.position, Quaternion.Euler(0, sp.yaw, 0));
                return;
            }

            float bottom = floor + kneeHeight, top = Mathf.Max(bottom + 0.3f, head.y - 0.1f);
            probe.height = top - bottom + radius * 2f;
            probe.transform.position = new Vector3(head.x, (top + bottom) * 0.5f, head.z);
            probe.transform.rotation = Quaternion.identity;

            Vector3 push = Vector3.zero;
            for (int iter = 0; iter < 4; iter++)
            {
                Vector3 p = probe.transform.position + push;
                var up = Vector3.up * (probe.height * 0.5f - radius);
                int n = Physics.OverlapCapsuleNonAlloc(p + up, p - up, radius, hits, ~0, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i];
                    if (!Blocks(c)) continue;
                    if (Physics.ComputePenetration(probe, p, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out var dist))
                    {
                        dir.y = 0;
                        if (dir.sqrMagnitude < 1e-6f) continue;
                        push += dir.normalized * (dist + 0.005f);
                        moved = true;
                        p = probe.transform.position + push;
                    }
                }
                if (!moved) break;
            }
            if (push.sqrMagnitude > 1e-8f && !DebugNoPush) rig.transform.position += push;
            ComfortCheck();
        }

        [Tooltip("Dim the view (never full black) with a 'step back' hint while the eyes are inside geometry")]
        public bool comfortFade = true;
        [Tooltip("Eye sphere radius for the comfort check; small so standing next to a wall never triggers it")]
        public float eyeRadius = 0.06f;
        float insideFor, hintCooldown;
        public bool Dimmed => insideFor > 0.1f;
        /// Test switch: off lets the head walk into walls (comfort-fade checks).
        public static bool DebugNoPush;

        /// Last-resort safety net: if real-world walking still puts the eyes into a full-height wall or door the
        /// push-back can't resolve, the view dims after a few frames. Furniture, props and items never dim the view:
        /// leaning over a desk, a bin or a box to interact with it must stay clear.
        void ComfortCheck()
        {
            if (!comfortFade || !Fader.I) return;
            Vector3 eye = rig.centerEyeAnchor.position;
            bool inside = false;
            int n = Physics.OverlapSphereNonAlloc(eye, eyeRadius, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i];
                if (!Blocks(c) || !c.transform.IsChildOf(env) || c.bounds.size.y < 2f) continue;
                inside = true;
                break;
            }
            insideFor = inside ? insideFor + Time.unscaledDeltaTime : 0;
            bool dim = insideFor > 0.1f;
            Fader.I.SetComfort(dim ? 0.7f : 0f);
            if (hintCooldown > 0) hintCooldown -= Time.unscaledDeltaTime;
            if (dim && hintCooldown <= 0)
            {
                hintCooldown = 6f;
                GameUI.I?.Caption("Step back - you're inside a wall", Palette.Ink);
            }
        }
    }
}
