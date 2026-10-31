using UnityEngine;

namespace CSER
{
    /// Keyboard grab for desktop / Simulator play: 4 picks up the item you point at (right hand first, then your gaze),
    /// 3 lets it go so it falls and rests. The ISDK hand grab keeps working alongside it.
    public class KeyboardGrab : MonoBehaviour
    {
        public float reach = 3f;

        public static KeyboardGrab I { get; private set; }
        public bool HasItem => held != null;

        void Awake() => I = this;
        void OnDestroy() { if (I == this) I = null; }

        OVRCameraRig rig;
        GrabItem held;
        Transform heldHome;

        void Update()
        {
            if (!rig) rig = FindAnyObjectByType<OVRCameraRig>();
            if (!rig || GameManager.I == null || GameManager.I.State != GameState.Playing) return;
            if (held && (!held.ForceHeld || held.Mounted || held.Stored || held.Consumed || held.Deposited || !held.gameObject.activeInHierarchy)) Forget();

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) Grab();
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Release();
#endif
        }

        void LateUpdate()
        {
            if (!held || !held.ForceHeld || held.Mounted || !rig || !rig.rightHandAnchor) return;
            // follow the hand with the item (kinematic, so nothing jitters)
            var t = rig.rightHandAnchor;
            held.transform.SetPositionAndRotation(t.position + t.forward * 0.18f, t.rotation);
        }

        GrabItem Find(Transform from)
        {
            if (!from) return null;
            if (Physics.SphereCast(from.position, 0.1f, from.forward, out var hit, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                var g = hit.collider.GetComponentInParent<GrabItem>();
                if (g && !g.Stored && !g.Consumed && !g.Deposited && g.gameObject.activeInHierarchy && g.kind != ItemKind.Plug) return g;
            }
            // nothing under the ray: the closest item within arm's reach
            GrabItem best = null; float bd = 0.8f;
            foreach (var g in FindObjectsByType<GrabItem>(FindObjectsInactive.Exclude))
            {
                if (g.Stored || g.Consumed || g.Deposited || g.Mounted || g.kind == ItemKind.Plug) continue;
                float d = Vector3.Distance(g.transform.position, from.position);
                if (d < bd) { bd = d; best = g; }
            }
            return best;
        }

        void Grab()
        {
            if (held) return;
            var g = Find(rig.rightHandAnchor) ?? Find(rig.centerEyeAnchor);
            if (!g) { GameUI.I?.Caption("Nothing to grab. Point at an item and press 4.", Palette.Ink, 2.5f); return; }
            Hold(g);
        }

        /// Puts the item in the right hand (used by the grab key and by the inventory's take-out).
        public bool Hold(GrabItem g)
        {
            if (held || !g) return false;
            held = g;
            var rb = g.GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            g.ForceHeld = true;
            g.EverHeld = true;
            return true;
        }

        void Release()
        {
            if (!held) return;
            var g = held;
            Forget();
            var rb = g.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.WakeUp();
            Sfx.Play(Sfx.I ? Sfx.I.drop : null, 0.6f);
        }

        void Forget()
        {
            if (held) held.ForceHeld = false;
            held = null;
        }
    }
}
