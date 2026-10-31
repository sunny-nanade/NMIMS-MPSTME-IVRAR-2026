using System;
using RescueSim.Survivors;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace RescueSim.Hands
{
    /// <summary>
    /// Controller-free hand input layered on top of the existing XR rig. It does NOT add a second set of interactors: the same
    /// Near-Far interactors that controllers use are moved to the tracked hand and their select/UI inputs are OR-ed with a
    /// pinch (via the XRI reader "bypass"). So a casualty is always owned by exactly one interactor, hands and controllers can
    /// never both fire, and switching modes cannot duplicate or detach a carry.
    ///
    ///  - Pinch (thumb + index) = select / grab / UI click, at the pinch point (near) or along the aim ray (far).
    ///  - Carrying with a hand latches: relax the fingers and keep walking; pinch again to put the casualty down.
    ///  - Left hand pinch-drag when nothing is hovered = walk (forward/back) and snap-turn (sideways).
    ///  - Controllers win whenever they are tracked; when a hand is lost mid-carry the casualty is held for a hand-over grace.
    /// This runs on a headset only with OpenXR hand tracking enabled; in the Editor it is idle unless a source is injected.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class HandTrackingInput : MonoBehaviour
    {
        [SerializeField] bool locomotionWithLeftHand = true;
        [SerializeField, Range(0.05f, 0.3f)] float stickRadius = 0.12f;
        [SerializeField, Range(0f, 0.08f)] float stickDeadzone = 0.025f;

        sealed class Side
        {
            public bool right;
            public Transform controller;           // "Left/Right Controller"
            public NearFarInteractor nearFar;
            public XRPokeInteractor poke;
            public TrackedPoseDriver driver;
            public GameObject[] controllerVisuals = Array.Empty<GameObject>();
            public readonly PinchDetector pinch = new PinchDetector();
            public readonly CarryLatch latch = new CarryLatch();
            public bool active;                    // hand drives this side right now
            public bool wasActive;
            public bool driverWasEnabled;
            public bool hold;                      // value asserted to the interactor this frame
            public bool holdPrev;
            public bool carryLatched;
            public bool locomoting;
            public Vector3 stickAnchor;
            public HandSample sample;
            public HandVisual visual;
            public Vector2 move, turn;
            public InputDevice device;
            public bool interactModeAtPress;
            public float lostTime;
        }

        const float LostGrace = 1.0f;
        readonly Side _l = new Side { right = false };
        readonly Side _r = new Side { right = true };
        XROrigin _origin;
        Transform _head;
        IHandSource _source;
        HandButton _selectL, _selectR, _uiL, _uiR;
        HandVector _moveBypass, _turnBypass;
        ContinuousMoveProvider[] _moves;
        SnapTurnProvider[] _snaps;
        ContinuousTurnProvider[] _turns;
        bool _wired;
        float _controllerCheckTimer;

        public IHandSource Source { get => _source; set { _source = value; } }
        public bool LeftHandActive => _l.active;
        public bool RightHandActive => _r.active;
        public bool RightLatched => _r.carryLatched;
        public bool LeftLatched => _l.carryLatched;
        public bool RightPinching => _r.pinch.Pinching;
        public bool LeftPinching => _l.pinch.Pinching;
        public NearFarInteractor RightInteractor => _r.nearFar;
        public NearFarInteractor LeftInteractor => _l.nearFar;
        /// <summary>Test hook: treat controllers as not tracked (otherwise read from the XR input devices).</summary>
        public bool? ControllersTrackedOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;   // later scene loads need the component too
            Attach();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Attach();

        static void Attach()
        {
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null || origin.GetComponent<HandTrackingInput>() != null) return;
            origin.gameObject.AddComponent<HandTrackingInput>();
        }

        void Awake()
        {
            _origin = GetComponent<XROrigin>();
        }

        void Start()
        {
            Wire();
#if RESCUESIM_XR_HANDS
            if (_source == null) _source = new XRHandsSource(_origin);
#endif
        }

        void Wire()
        {
            if (_wired) return;
            if (_origin == null) _origin = GetComponent<XROrigin>();
            _head = _origin != null && _origin.Camera != null ? _origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
            Find(_l, "Left Controller", XRNode.LeftHand);
            Find(_r, "Right Controller", XRNode.RightHand);

            _selectL = Make(_l, false); _selectR = Make(_r, false);
            _uiL = Make(_l, true); _uiR = Make(_r, true);

            _moves = GetComponentsInChildren<ContinuousMoveProvider>(true);
            _snaps = GetComponentsInChildren<SnapTurnProvider>(true);
            _turns = GetComponentsInChildren<ContinuousTurnProvider>(true);
            _moveBypass = new HandVector(this, false);
            _turnBypass = new HandVector(this, true);
            foreach (var m in _moves) { m.leftHandMoveInput.bypass = new HandVectorReader(m.leftHandMoveInput, _moveBypass, true); }
            foreach (var s in _snaps) { s.rightHandTurnInput.bypass = new HandVectorReader(s.rightHandTurnInput, _turnBypass, true); }
            foreach (var t in _turns) { t.rightHandTurnInput.bypass = new HandVectorReader(t.rightHandTurnInput, _turnBypass, true); }
            _wired = _l.nearFar != null || _r.nearFar != null;
        }

        void Find(Side s, string controllerName, XRNode node)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == controllerName) { s.controller = t; break; }
            if (s.controller == null) return;
            s.nearFar = s.controller.GetComponentInChildren<NearFarInteractor>(true);
            // Casualties use trigger colliders (so they never block the player or the NavMesh agent). The near caster ignores
            // triggers by default, which made them impossible to hover and grab with hands or controllers; let it see them.
            if (s.nearFar != null && s.nearFar.nearInteractionCaster is SphereInteractionCaster sphere)
                sphere.physicsTriggerInteraction = QueryTriggerInteraction.Collide;
            s.poke = s.controller.GetComponentInChildren<XRPokeInteractor>(true);
            s.driver = s.controller.GetComponent<TrackedPoseDriver>();
            s.device = InputDevices.GetDeviceAtXRNode(node);
            var vis = new System.Collections.Generic.List<GameObject>();
            foreach (Transform c in s.controller)
                if (c.name.IndexOf("Visual", StringComparison.OrdinalIgnoreCase) >= 0) vis.Add(c.gameObject);
            s.controllerVisuals = vis.ToArray();
        }

        HandButton Make(Side s, bool ui)
        {
            if (s.nearFar == null) return null;
            var reader = ui ? s.nearFar.uiPressInput : s.nearFar.selectInput;
            var b = new HandButton(reader, () => s.hold, () => s.hold && !s.holdPrev, () => !s.hold && s.holdPrev);
            reader.bypass = b;
            return b;
        }

        void OnDestroy()
        {
            foreach (var s in new[] { _l, _r })
            {
                if (s.nearFar != null) { s.nearFar.selectInput.bypass = null; s.nearFar.uiPressInput.bypass = null; }
                if (s.visual != null) s.visual.Dispose();
            }
            if (_moves != null) foreach (var m in _moves) if (m != null) m.leftHandMoveInput.bypass = null;
            if (_snaps != null) foreach (var m in _snaps) if (m != null) m.rightHandTurnInput.bypass = null;
            if (_turns != null) foreach (var m in _turns) if (m != null) m.rightHandTurnInput.bypass = null;
        }

        void Update()
        {
            if (!_wired) { Wire(); if (!_wired) return; }
            if (_source == null || !_source.Available) { Deactivate(_l); Deactivate(_r); return; }
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>One input step. Public so tests can drive it deterministically.</summary>
        public void Tick(float dt)
        {
            if (!_wired) Wire();
            if (!_wired || _source == null) return;
            Step(_l, dt);
            Step(_r, dt);
        }

        bool ControllerTracked(Side s)
        {
            if (ControllersTrackedOverride.HasValue) return ControllersTrackedOverride.Value;
            if (!s.device.isValid) s.device = InputDevices.GetDeviceAtXRNode(s.right ? XRNode.RightHand : XRNode.LeftHand);
            return s.device.isValid && s.device.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t;
        }

        void Step(Side s, float dt)
        {
            if (s.nearFar == null) return;
            bool have = _source.TryGetHand(s.right, out s.sample);
            bool controller = ControllerTracked(s);

            s.pinch.Update(have, have ? s.sample.PinchDistance : 1f, dt);

            // Mode: a tracked controller always wins; otherwise a tracked hand drives the side. A hand that briefly loses tracking
            // while pinching or carrying keeps the side for LostGrace seconds so one dropped frame is not a drop.
            bool carryingMine = Survivor.Carrying != null && Survivor.Carrying.Owner == (IXRSelectInteractor)s.nearFar;
            s.lostTime = have ? 0f : s.lostTime + dt;
            bool wantActive = !controller && (have || (s.lostTime < LostGrace && (s.pinch.Pinching || carryingMine)));
            if (s.active && !wantActive && carryingMine)
                Survivor.Carrying.SuspendOwner(s.nearFar);         // hand lost / controller took over: keep the casualty, wait for the next owner
            SetActive(s, wantActive);

            // Latch + hold.
            s.holdPrev = s.hold;
            if (!s.active) { s.hold = false; s.latch.Reset(); s.carryLatched = false; s.locomoting = false; s.move = s.turn = Vector2.zero; return; }

            bool interactMode = true;
            if (s.right == false && locomotionWithLeftHand)
            {
                if (s.pinch.PressedThisFrame) s.interactModeAtPress = s.nearFar.hasHover || s.nearFar.hasSelection;
                interactMode = s.pinch.Pinching ? s.interactModeAtPress : true;
            }

            if (interactMode)
            {
                s.hold = s.latch.Update(carryingMine, s.pinch.Pinching, s.pinch.PressedThisFrame, dt);
                s.carryLatched = s.latch.Latched;
                s.locomoting = false; s.move = s.turn = Vector2.zero;
            }
            else
            {
                s.hold = false; s.latch.Reset(); s.carryLatched = false;
                StepStick(s);
            }

            if (have) PoseController(s);
            if (s.visual != null) s.visual.Update(s.sample, s.pinch.Pinching);
        }

        void StepStick(Side s)
        {
            s.locomoting = s.pinch.Pinching;
            if (!s.locomoting || _head == null) { s.move = s.turn = Vector2.zero; return; }
            Quaternion yaw = Quaternion.Euler(0, _head.eulerAngles.y, 0);
            Vector3 p = yaw * Vector3.zero + Quaternion.Inverse(yaw) * s.sample.PinchPoint;
            if (s.pinch.PressedThisFrame) s.stickAnchor = p;
            Vector3 d = p - s.stickAnchor;
            Vector2 raw = new Vector2(d.x, d.z);
            float mag = raw.magnitude;
            if (mag < stickDeadzone) { s.move = s.turn = Vector2.zero; return; }
            Vector2 v = raw / Mathf.Max(stickRadius, 0.01f);
            v = Vector2.ClampMagnitude(v, 1f);
            s.move = new Vector2(0f, v.y);        // forward / back
            s.turn = new Vector2(v.x, 0f);        // sideways drag = snap turn
        }

        void PoseController(Side s)
        {
            if (s.controller == null || _head == null) return;
            Vector3 pinchPoint = s.sample.PinchPoint;
            Vector3 shoulder = _head.position + Vector3.down * 0.18f + Quaternion.Euler(0, _head.eulerAngles.y, 0) * new Vector3(s.right ? 0.17f : -0.17f, 0f, 0f);
            Vector3 aim = pinchPoint - shoulder;
            if (aim.sqrMagnitude < 1e-4f) aim = _head.forward;
            s.controller.SetPositionAndRotation(pinchPoint, Quaternion.LookRotation(aim.normalized, Vector3.up));
            if (s.poke != null && s.sample.indexTip != Vector3.zero) s.poke.transform.position = s.sample.indexTip;
        }

        void SetActive(Side s, bool on)
        {
            if (s.active == on) return;
            s.active = on;
            if (on)
            {
                if (s.driver != null) { s.driverWasEnabled = s.driver.enabled; s.driver.enabled = false; }
                foreach (var v in s.controllerVisuals) if (v != null) v.SetActive(false);
                if (s.visual == null) s.visual = new HandVisual(s.right, transform);
                s.visual.SetVisible(true);
            }
            else
            {
                if (s.driver != null && s.driverWasEnabled) s.driver.enabled = true;
                foreach (var v in s.controllerVisuals) if (v != null) v.SetActive(true);
                if (s.visual != null) s.visual.SetVisible(false);
                s.pinch.Reset();
            }
        }

        void Deactivate(Side s) { if (s.active) SetActive(s, false); s.hold = false; }

        // ---- XRI reader bypasses -----------------------------------------------------------------------------------------

        /// <summary>Button reader: the original (controller) input OR the hand's hold, so only one interactor is ever involved.</summary>
        sealed class HandButton : IXRInputButtonReader
        {
            readonly XRInputButtonReader _raw;
            readonly Func<bool> _held, _pressed, _released;
            public HandButton(XRInputButtonReader raw, Func<bool> held, Func<bool> pressed, Func<bool> released)
            { _raw = raw; _held = held; _pressed = pressed; _released = released; }
            public bool ReadIsPerformed() => _raw.ReadIsPerformed() || _held();
            public bool ReadWasPerformedThisFrame() => _raw.ReadWasPerformedThisFrame() || _pressed();
            public bool ReadWasCompletedThisFrame() => _raw.ReadWasCompletedThisFrame() || _released();
            public float ReadValue() => Mathf.Max(_raw.ReadValue(), _held() ? 1f : 0f);
            public bool TryReadValue(out float value) { value = ReadValue(); return true; }
        }

        /// <summary>Hand-drawn stick value for locomotion (zero when the left hand is not in locomotion mode).</summary>
        sealed class HandVector
        {
            readonly HandTrackingInput _o; readonly bool _turn;
            public HandVector(HandTrackingInput o, bool turn) { _o = o; _turn = turn; }
            public Vector2 Value => _turn ? _o._l.turn : _o._l.move;
        }

        sealed class HandVectorReader : IXRInputValueReader<Vector2>
        {
            readonly XRInputValueReader<Vector2> _raw; readonly HandVector _hand;
            public HandVectorReader(XRInputValueReader<Vector2> raw, HandVector hand, bool _) { _raw = raw; _hand = hand; }
            public Vector2 ReadValue() { var r = _raw.ReadValue(); var h = _hand.Value; return r.sqrMagnitude >= h.sqrMagnitude ? r : h; }
            public bool TryReadValue(out Vector2 value) { value = ReadValue(); return true; }
        }
    }

    /// <summary>Simple tracked-hand drawing: one small sphere per joint (no skinned mesh, no extra shader needed on Quest).</summary>
    sealed class HandVisual
    {
        readonly GameObject _root;
        readonly Transform[] _joints = new Transform[26];
        readonly Renderer _pinchGlow;
        static Material _mat;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        public HandVisual(bool right, Transform parent)
        {
            _root = new GameObject(right ? "Tracked Right Hand" : "Tracked Left Hand");
            _root.transform.SetParent(parent, false);
            var mat = Material();
            for (int i = 0; i < _joints.Length; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                UnityEngine.Object.Destroy(s.GetComponent<Collider>());
                s.name = "J" + i;
                s.transform.SetParent(_root.transform, false);
                s.transform.localScale = Vector3.one * 0.012f;
                var r = s.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                _joints[i] = s.transform;
            }
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.name = "PinchGlow"; g.transform.SetParent(_root.transform, false); g.transform.localScale = Vector3.one * 0.03f;
            _pinchGlow = g.GetComponent<Renderer>(); _pinchGlow.sharedMaterial = mat; _pinchGlow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _root.SetActive(false);
        }

        static Material Material()
        {
            if (_mat != null) return _mat;
            var sh = Resources.Load<Material>("Hands/Mat_TrackedHand");
            if (sh != null) { _mat = sh; return _mat; }
            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            _mat.SetColor(ColorId, new Color(0.9f, 0.78f, 0.65f));
            return _mat;
        }

        public void SetVisible(bool v) { _root.SetActive(v); }

        public void Update(HandSample s, bool pinching)
        {
            if (!_root.activeSelf) return;
            if (s.joints != null)
                for (int i = 0; i < _joints.Length && i + 1 < s.joints.Length; i++)
                    _joints[i].position = s.joints[i + 1];
            _pinchGlow.transform.position = s.PinchPoint;
            _pinchGlow.enabled = pinching;
        }

        public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root); }
    }
}
