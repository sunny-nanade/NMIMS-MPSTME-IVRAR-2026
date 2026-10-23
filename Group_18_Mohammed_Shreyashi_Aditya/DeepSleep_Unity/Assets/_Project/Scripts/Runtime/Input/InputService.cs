using UnityEngine;
using UnityEngine.InputSystem;

namespace Fractured_Final
{
    /// <summary>
    /// The ONLY script that reads OVRInput or the Input System. Everything else asks this service.
    /// Sources: Headset (OVRInput), EditorFallback (keyboard + mouse), Scripted (PlaythroughBot / tests).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class InputService : MonoBehaviour
    {
        public enum Source { Headset, EditorFallback, Scripted }

        public class HandState
        {
            public Vector2 stick;
            public bool trigger, grip;
            public bool triggerPrev, gripPrev;
            public Vector3 handPos, rayOrigin, rayDir = Vector3.forward;
            public Quaternion handRot = Quaternion.identity;
        }

        static InputService _instance;
        public static bool Exists => _instance != null;
        public static InputService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<InputService>();
                    if (_instance == null)
                    {
                        var go = new GameObject("InputService");
                        _instance = go.AddComponent<InputService>();
                    }
                }
                return _instance;
            }
        }

        [SerializeField] Source source = Source.Headset;
        [SerializeField] float stickDeadzone = 0.15f;
        [SerializeField] float fallbackReach = 1.8f;
        [SerializeField] float fallbackRestDistance = 0.35f;
        [SerializeField] float fallbackLookSpeed = 0.12f;

        readonly HandState[] _hands = { new HandState(), new HandState() };
        bool _menu, _menuPrev;
        bool _comboHeld, _comboPrev;
        bool _forceScripted;

        Vector2 _fbMove, _fbLook;
        bool _fbLookHeld;

        public Source CurrentSource => source;
        public bool IsFallback => source == Source.EditorFallback;
        public bool IsScripted => source == Source.Scripted;

        /// <summary>Typed characters (Editor / PC keyboard). Backspace and enter arrive as control characters.</summary>
        public event System.Action<char> TextTyped;
        bool _textHooked;

        void HookText()
        {
            if (_textHooked || Keyboard.current == null) return;
            Keyboard.current.onTextInput += c => TextTyped?.Invoke(c);
            _textHooked = true;
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            source = DetectSource();
        }

        Source DetectSource()
        {
            if (_forceScripted) return Source.Scripted;
            bool hmd = false;
            try { hmd = OVRManager.isHmdPresent; } catch { }
            if (!hmd && !Application.isEditor && UnityEngine.XR.XRSettings.isDeviceActive) hmd = true;
            if (hmd) return Source.Headset;
            return Source.EditorFallback;
        }

        // ------------------------------------------------------------------ queries
        public bool Trigger(Hand h) => _hands[(int)h].trigger;
        public bool TriggerDown(Hand h) { var s = _hands[(int)h]; return s.trigger && !s.triggerPrev; }
        public bool TriggerUp(Hand h) { var s = _hands[(int)h]; return !s.trigger && s.triggerPrev; }
        public bool Grip(Hand h) => _hands[(int)h].grip;
        public bool GripDown(Hand h) { var s = _hands[(int)h]; return s.grip && !s.gripPrev; }
        public bool GripUp(Hand h) { var s = _hands[(int)h]; return !s.grip && s.gripPrev; }
        public Vector2 Thumbstick(Hand h) => _hands[(int)h].stick;
        public bool MenuPressed => _menu && !_menuPrev;
        public bool MenuHeld => _menu;
        public Vector3 RayOrigin(Hand h) => _hands[(int)h].rayOrigin;
        public Vector3 RayDirection(Hand h) => _hands[(int)h].rayDir;
        public Vector3 HandPosition(Hand h) => _hands[(int)h].handPos;
        public Quaternion HandRotation(Hand h) => _hands[(int)h].handRot;
        /// <summary>Both grips held and menu pressed (Editor: F10).</summary>
        public bool ExperimenterComboPressed => _comboHeld && !_comboPrev;
        float _holdDist = -1f;
        /// <summary>Fallback only: keep the virtual hand at a fixed distance along the ray while holding an object.</summary>
        public void SetFallbackHold(float distance) { _holdDist = distance; }

        /// <summary>Controller vibration (headset only).</summary>
        public void Vibrate(Hand h, float amplitude, float seconds)
        {
            if (source != Source.Headset) return;
            var c = h == Hand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            OVRInput.SetControllerVibration(1f, Mathf.Clamp01(amplitude), c);
            StartCoroutine(StopVibe(c, seconds));
        }

        System.Collections.IEnumerator StopVibe(OVRInput.Controller c, float s)
        {
            yield return new WaitForSecondsRealtime(s);
            OVRInput.SetControllerVibration(0, 0, c);
        }

        public Vector2 FallbackMove => _fbMove;
        public Vector2 FallbackLook => _fbLook;
        public bool FallbackLookHeld => _fbLookHeld;

        // ------------------------------------------------------------------ scripted control (bot / tests)
        public void SetScripted(bool on)
        {
            _forceScripted = on;
            source = on ? Source.Scripted : DetectSource();
        }

        public void ScriptedSetHand(Hand h, Vector2 stick, bool trigger, bool grip, Vector3 handPos, Vector3 rayOrigin, Vector3 rayDir)
        {
            var s = _hands[(int)h];
            s.stick = stick; s.trigger = trigger; s.grip = grip;
            s.handPos = handPos; s.rayOrigin = rayOrigin; s.rayDir = rayDir.normalized;
        }

        public void ScriptedSetMenu(bool menu) { _menu = menu; }
        public void ScriptedSetLook(Vector2 delta) { _fbLook = delta; }
        public void ScriptedSetMove(Vector2 move) { _fbMove = move; }
        public void ScriptedSetCombo(bool held) { _comboHeld = held; }

        // ------------------------------------------------------------------ per-frame
        void Update()
        {
            HookText();
            for (int i = 0; i < 2; i++) { _hands[i].triggerPrev = _hands[i].trigger; _hands[i].gripPrev = _hands[i].grip; }
            _menuPrev = _menu;
            _comboPrev = _comboHeld;

            switch (source)
            {
                case Source.Headset: ReadHeadset(); break;
                case Source.EditorFallback: ReadFallback(); break;
                case Source.Scripted:
                    // state is written by the bot during its own Update; edges derive from the prev copies above
                    _fbLook = Vector2.zero;
                    break;
            }
        }

        void ReadHeadset()
        {
            var l = _hands[0]; var r = _hands[1];
            l.stick = ApplyDead(OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch));
            r.stick = ApplyDead(OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch));
            l.trigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
            r.trigger = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
            l.grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            r.grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            _menu = OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch);
            _comboHeld = l.grip && r.grip && _menu;

            var rig = PlayerRig.Instance;
            if (rig != null)
            {
                SetPoseFromAnchor(l, rig.LeftHand);
                SetPoseFromAnchor(r, rig.RightHand);
            }
        }

        static void SetPoseFromAnchor(HandState s, Transform t)
        {
            if (t == null) return;
            s.handPos = t.position; s.handRot = t.rotation;
            s.rayOrigin = t.position; s.rayDir = t.forward;
        }

        Vector2 ApplyDead(Vector2 v) => v.magnitude < stickDeadzone ? Vector2.zero : v;

        void ReadFallback()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            var l = _hands[0]; var r = _hands[1];
            if (kb == null || mouse == null) return;

            _fbMove = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            l.stick = _fbMove;
            float turn = (kb.rKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0);
            r.stick = new Vector2(turn, kb.eKey.isPressed ? 1f : 0f);

            _fbLookHeld = mouse.middleButton.isPressed || kb.leftAltKey.isPressed;
            Vector2 look = Vector2.zero;
            if (_fbLookHeld) look += mouse.delta.ReadValue() * fallbackLookSpeed;
            look.x += ((kb.rightArrowKey.isPressed ? 1 : 0) - (kb.leftArrowKey.isPressed ? 1 : 0)) * 1.2f;
            look.y += ((kb.upArrowKey.isPressed ? 1 : 0) - (kb.downArrowKey.isPressed ? 1 : 0)) * 1.2f;
            _fbLook = look;

            r.trigger = mouse.leftButton.isPressed;
            r.grip = mouse.rightButton.isPressed;
            l.trigger = false;
            l.grip = kb.gKey.isPressed;
            _menu = kb.fKey.isPressed;
            _comboHeld = kb.f10Key.isPressed;

            var cam = Camera.main;
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                r.rayOrigin = ray.origin; r.rayDir = ray.direction;
                Vector3 hp = ray.origin + ray.direction * fallbackRestDistance;
                if ((r.trigger || r.grip) && Physics.Raycast(ray, out var hit, fallbackReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    hp = hit.point - ray.direction * 0.01f;
                if (r.grip && _holdDist > 0f) hp = ray.origin + ray.direction * _holdDist;
                r.handPos = hp;
                r.handRot = Quaternion.LookRotation(ray.direction);
                var ct = cam.transform;
                l.handPos = ct.position + ct.forward * 0.35f - ct.right * 0.25f - ct.up * 0.2f;
                l.handRot = ct.rotation;
                l.rayOrigin = l.handPos; l.rayDir = ct.forward;
            }
        }
    }
}
