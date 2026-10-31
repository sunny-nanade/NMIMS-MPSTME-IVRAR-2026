using System.Collections;
using UnityEngine;

namespace CSER
{
    /// Chapter 1 exit: touch the access badge to the reader. Opens once the incident is escalated and the infected
    /// laptop is clean; the next chapter then starts by itself. Also completes "pick up your badge" on the first grab.
    public class BadgeReader : MonoBehaviour
    {
        public Hinge door;
        public Renderer led;
        public Material ledIdle, ledGranted, ledDenied;
        public float advanceDelay = 4f;
        float deniedCooldown;
        bool open, advancing;

        void Start() => GrabItem.AnyGrabbed += OnGrab;
        void OnDestroy() => GrabItem.AnyGrabbed -= OnGrab;

        void OnGrab(GrabItem item)
        {
            if (item.kind == ItemKind.Badge) GameManager.I.Complete(TaskId.SOC_Badge);
        }

        void Update()
        {
            if (deniedCooldown > 0 && (deniedCooldown -= Time.deltaTime) <= 0 && !open) led.sharedMaterial = ledIdle;
        }

        void OnTriggerEnter(Collider other) => Touch(other);
        void OnTriggerStay(Collider other) => Touch(other);

        void Touch(Collider other)
        {
            var item = other.GetComponentInParent<GrabItem>();
            if (!item || item.kind != ItemKind.Badge || open || deniedCooldown > 0) return;
            TryAccess();
        }

        /// Badge touched to the reader (physically in VR, or via P at the door on the keyboard).
        public bool TryAccess()
        {
            if (open) return true;
            var gm = GameManager.I;
            if (gm.State != GameState.Playing) return false;
            if (gm.IsDone(TaskId.SOC_Escalate) && gm.IsDone(TaskId.SOC_Malware))
            {
                open = true;
                led.sharedMaterial = ledGranted;
                Sfx.PlayAt(Sfx.I ? Sfx.I.confirm : null, transform.position);
                Sfx.Haptic(0.8f, 0.2f);
                door.Open();
                GameUI.I?.Caption("Access granted - Floor 3", Palette.Green, 3f);
                gm.Complete(TaskId.SOC_Exit);
                if (!advancing) StartCoroutine(AdvanceRoutine());
                return true;
            }
            if (deniedCooldown <= 0)
            {
                led.sharedMaterial = ledDenied;
                deniedCooldown = 2.5f;
                Sfx.PlayAt(Sfx.I ? Sfx.I.error : null, transform.position);
                GameUI.I?.Caption(gm.IsDone(TaskId.SOC_Escalate)
                    ? "Access denied: the infected laptop is still on the network. Clean it first."
                    : "Access denied: declare the incident on your console first.", Palette.Red, 4f);
            }
            return false;
        }

        IEnumerator AdvanceRoutine()
        {
            advancing = true;
            yield return new WaitForSeconds(advanceDelay);
            int next = GameManager.I.chapter + 1;
            if (next <= GameData.Chapters.Length) SceneFlow.LoadChapter(next);
        }

        public void DebugSwipe()
        {
            var badge = GameObject.Find("AccessBadge");
            if (badge) Touch(badge.GetComponentInChildren<Collider>());
        }
    }
}
