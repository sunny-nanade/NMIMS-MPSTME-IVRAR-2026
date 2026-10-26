using UnityEngine;

namespace CSER
{
    /// Chapter 3: the attacker's implant in the rack. Inspect it (ray), pull its network cable, then it can be bagged.
    public class RogueImplant : MonoBehaviour
    {
        public Renderer led;
        public Material ledOn, ledOff;
        public Transform socket;          // where the cable plugs into the implant
        public Transform plug;            // grabbable plug at the end of the cable
        public Transform cableStart;      // switch port the cable comes from
        public LineRenderer cable;
        public GameObject[] enableWhenUnplugged; // the implant's ISDK grab interactions
        public float unplugDistance = 0.12f;

        bool unplugged;
        Rigidbody body;

        void Start()
        {
            body = GetComponent<Rigidbody>();
            foreach (var g in enableWhenUnplugged) if (g) g.SetActive(false);
            if (body) body.isKinematic = true;
        }

        public void Inspect()
        {
            if (GameManager.I.Complete(TaskId.SRV_Find))
                GameUI.I?.Caption("Unknown device: Raspberry Pi, no asset tag, cable into switch port 14. This is the implant!", Palette.Ink, 6f);
        }

        void Update()
        {
            if (led && !unplugged) led.sharedMaterial = Mathf.Repeat(Time.time, 0.6f) < 0.3f ? ledOn : ledOff;
            UpdateCable();
            // once pulled out and let go, the plug drops and dangles on its cable
            if (unplugged && plug && plug.TryGetComponent<Rigidbody>(out var prb) && prb.isKinematic
                && plug.TryGetComponent<GrabItem>(out var pi) && !pi.IsHeld)
                prb.isKinematic = false;
            if (unplugged || !plug || !socket) return;
            if (Vector3.Distance(plug.position, socket.position) > unplugDistance)
            {
                unplugged = true;
                if (!GameManager.I.IsDone(TaskId.SRV_Find)) Inspect();
                led.sharedMaterial = ledOff;
                Sfx.PlayAt(Sfx.I ? Sfx.I.drop : null, socket.position);
                foreach (var g in enableWhenUnplugged) if (g) g.SetActive(true);
                if (body) body.isKinematic = false;
                GameManager.I.Complete(TaskId.SRV_Unplug);
            }
        }

        /// Sagging cable between the switch port and the plug.
        void UpdateCable()
        {
            if (!cable || !cableStart || !plug) return;
            const int n = 16;
            cable.positionCount = n;
            var a = cableStart.position;
            var b = plug.position;
            float sag = Mathf.Clamp(Vector3.Distance(a, b) * 0.25f, 0.03f, 0.35f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                cable.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4 * t * (1 - t)));
            }
        }

        public void DebugUnplug()
        {
            if (plug) plug.position = socket.position + Vector3.forward * 0.3f;
        }
    }
}
