using System.Collections;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// SCN_MedicalBay start. R010 + H001 on arrival, H002 after 20 s, R011 when the player is near the tray,
    /// R012 five seconds later if the cup is still on the tray. Advances to EarCheck once the cup was grabbed AND one teleport happened.
    /// </summary>
    public class AwakeningController : MonoBehaviour
    {
        [SerializeField] Grabbable cup;
        [SerializeField] Transform tray;
        [SerializeField] float h002DelaySeconds = 20f;
        [SerializeField] float nearTrayMeters = 1.2f;
        [SerializeField] float r012DelaySeconds = 5f;

        public bool CupGrabbed { get; private set; }
        public bool Completed { get; private set; }
        public bool Started => _started;
        public Grabbable Cup { get => cup; set => cup = value; }
        public Transform Tray { get => tray; set => tray = value; }

        float _t0 = float.MaxValue;
        bool _started;
        bool _h002, _r011, _r012;
        float _r011Time;
        PlayerLocomotion _loco;

        void Start()
        {
            var sm = SessionManager.Instance;
            if (sm != null && sm.Current < Stage.Awakening) sm.EnsureAtLeast(Stage.Awakening);
            if (cup != null) cup.Grabbed += OnGrabbed;
            StyleCup();
            StartCoroutine(Begin());
        }

        /// <summary>The mission briefing comes first (once per session); Rose and HALCYON start when it is gone.</summary>
        IEnumerator Begin()
        {
            var sm = SessionManager.Instance;
            if (sm != null && sm.Current == Stage.Awakening && ObjectiveScreen.Pending(sm.SessionId))
            {
                var screen = ObjectiveScreen.Create(transform);
                yield return screen.Show(sm.SessionId);
                yield return new WaitForSeconds(0.5f);
            }
            _t0 = Time.time;
            _started = true;
            VoiceBus.Request("R010");
            VoiceBus.Request("H001");
        }

        /// <summary>A real mug shape in a bright colour, so the cup is easy to see against the tray.</summary>
        void StyleCup()
        {
            if (cup == null) return;
            var mf = cup.GetComponent<MeshFilter>(); var mr = cup.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) return;
            mf.sharedMesh = MeshFactory.Cup();
            var c = new Color(0.05f, 0.85f, 0.95f);
            mr.sharedMaterial = MeshFactory.Lit(c, 0.7f, 0f, c * 0.35f);
            var cap = cup.GetComponent<CapsuleCollider>();
            if (cap != null) { cap.direction = 1; cap.radius = 0.048f; cap.height = 0.1f; cap.center = Vector3.zero; }
            foreach (Transform child in cup.transform) child.gameObject.SetActive(false); // any old child meshes
        }

        void OnDestroy() { if (cup != null) cup.Grabbed -= OnGrabbed; }

        void OnGrabbed(Grabbable g, Hand h)
        {
            if (CupGrabbed) return;
            CupGrabbed = true;
            DS.Log("Awakening: cup picked up");
            SfxEvents.Raise(SfxEvent.Grab, g.transform.position);
        }

        void Update()
        {
            if (Completed || !_started) return;
            var rig = PlayerRig.Instance;
            if (rig == null || rig.Head == null) return;
            if (_loco == null) _loco = rig.GetComponent<PlayerLocomotion>();

            if (!_h002 && Time.time - _t0 >= h002DelaySeconds) { _h002 = true; VoiceBus.Request("H002"); }

            if (!_r011 && tray != null && !CupGrabbed)
            {
                Vector3 d = rig.Head.position - tray.position; d.y = 0;
                if (d.magnitude <= nearTrayMeters) { _r011 = true; _r011Time = Time.time; VoiceBus.Request("R011"); }
            }
            if (_r011 && !_r012 && !CupGrabbed && Time.time - _r011Time >= r012DelaySeconds) { _r012 = true; VoiceBus.Request("R012"); }

            if (CupGrabbed && _loco != null && _loco.TeleportCount >= 1)
            {
                Completed = true;
                DS.Log("Awakening complete");
                var sm = SessionManager.Instance;
                if (sm != null) sm.TryAdvance(Stage.EarCheck);
            }
        }
    }
}
