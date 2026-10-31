using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Group10Gestures.Core;
using Group10Gestures.Filtering;
using Group10Gestures.Providers;
using Group10Gestures.Recognition;
using Group10Gestures.UI;

namespace Group10Gestures.Interaction
{
    /// <summary>A finished room: floor polygon + extruded walls + colour.</summary>
    public class Room
    {
        public int Id;
        public List<Vector3> Corners;
        public float Height;
        public Color Color;
        public GameObject Root;
        public Material WallMat;
        public Material FloorMat;
        public float Area => Mathf.Abs(RoomMeshBuilder.SignedArea(Corners));
    }

    /// <summary>
    /// Interaction layer on top of the Group 10 hand pipeline (HandFilter -> GestureRecognizer).
    /// It does not modify the tracking scripts.
    ///
    /// AIMING (same in every mode): the pinch point (midpoint of thumb tip and index tip) is mapped to a point on
    /// the floor grid:  hand left/right = cursor left/right,  hand up = cursor farther away.
    /// A click uses the hand position from ~0.1 s earlier, because the hand drifts while the fingers close.
    ///
    /// EDIT  : right PINCH places a corner. Near the first corner (big green dot) the cursor snaps onto it and a pinch
    ///         closes the room. Then raise/lower the hand for wall height and PINCH to confirm.
    /// COLOR : hold the right index fingertip on a swatch to pick a colour; right PINCH paints the outlined room.
    /// VIEW  : pinch with BOTH hands and move apart/together = zoom; pinch with ONE hand and drag sideways = rotate.
    ///
    /// LEFT HAND: FIST = undo (Edit) / reset view (View), PEACE = delete last room, THUMBS-UP = close room,
    ///            POINT held 0.8 s = next mode (Edit -> Color -> View).
    /// KEYS: M next mode, C clear drawing, Space close room, R recalibrate floor, arrow keys rotate/zoom in View.
    /// </summary>
    public class RoomBuilderController : MonoBehaviour
    {
        public enum BuildState { Drawing, Extruding }
        public enum InteractionMode { Edit, Color, View }

        [Header("Hands")]
        [SerializeField] private Handedness _dominant = Handedness.Right;

        [Header("Aiming: hand -> floor plan")]
        [SerializeField] private float _eyeToFloorXR = 1.5f;
        [SerializeField] private float _eyeToFloorWebcam = 1.0f;
        [Tooltip("Metres of floor per metre of sideways hand movement.")]
        [SerializeField] private float _gainXWebcam = 7f;
        [SerializeField] private float _gainXXR = 6f;
        [Tooltip("Nearest / farthest floor distance (m) the cursor can reach.")]
        [SerializeField] private float _zNear = 1.5f;
        [SerializeField] private float _zFar = 6.0f; // scaled by _reach, capped at 7.5 m (grid edge)
        [Tooltip("Hand height (relative to eye) mapped to nearest / farthest, and to min / max wall height.")]
        [SerializeField] private float _handLowWebcam = -0.35f;
        [SerializeField] private float _handHighWebcam = 0.15f;
        [SerializeField] private float _handLowXR = -0.65f;
        [SerializeField] private float _handHighXR = -0.10f;
        [SerializeField] private float _planHalfWidth = 5f;
        [Tooltip("Click uses the hand position from this many seconds ago (compensates pinch drift).")]
        [SerializeField] private float _clickLatency = 0.10f;
        [Tooltip("Multiplies how much floor a hand movement covers. Change at runtime with the - and + keys.")]
        [SerializeField] private float _reach = 1.5f;

        [Header("Pinch detection (stricter than the shared recognizer)")]
        [Tooltip("Thumb-tip to index-tip distance / palm size below which a pinch starts.")]
        [SerializeField] private float _pinchEnter = 0.22f;
        [Tooltip("Distance / palm size above which a pinch ends. Raise _pinchEnter to ~0.28 if real pinches are missed.")]
        [SerializeField] private float _pinchExit = 0.40f;
        [SerializeField] private float _pinchOnTime = 0.08f;
        [SerializeField] private float _pinchOffTime = 0.06f;

        [Header("Room closing")]
        [SerializeField] private float _closeRadius = 0.6f;
        [SerializeField] private float _minRoomArea = 0.5f;
        [SerializeField] private float _heightStep = 0.1f;
        [SerializeField] private float _paintReach = 0.8f;

        [Header("Shortcuts")]
        [SerializeField] private float _shortcutCooldown = 0.6f;
        [Tooltip("A left-hand pose must be held this long (s) before it triggers.")]
        [SerializeField] private float _shortcutHold = 0.25f;
        [SerializeField] private float _modeHoldTime = 0.8f;
        [SerializeField] private float _modeCooldown = 1.2f;

        [Header("View mode")]
        [SerializeField] private float _rotateDegPerMeter = 220f;
        [SerializeField] private float _minViewScale = 0.2f;
        [SerializeField] private float _maxViewScale = 2.5f;

        // ---- public read-only state (used by TrialLogger) ----
        public BuildState State => _state;
        public InteractionMode Mode => _mode;
        public int CornerCount => _corners.Count;
        public int RoomCount => _rooms.Count;

        // ---- read-only text/state for the headset guide panel (XRGuidePanel) ----
        public Handedness Dominant => _dominant;
        public string GuideText => GetGuide();
        public float ModeHoldProgress => _modeHoldTime > 0f ? Mathf.Clamp01(_pointHold / _modeHoldTime) : 0f;
        public string ToastText => Time.unscaledTime < _toastUntil ? _toast : "";
        public string StatusText
        {
            get
            {
                string modeName = _mode == InteractionMode.Edit ? "EDIT (draw rooms)"
                                : _mode == InteractionMode.Color ? "COLOR (paint rooms)" : "VIEW (zoom / rotate)";
                return "MODE: " + modeName + "   |   Rooms: " + _rooms.Count + "   Corners: " + _corners.Count;
            }
        }
        public float ExtrudeHeight => _extrudeHeight;

        public event Action<int> CornerPlaced;
        public event Action CornerUndone;
        public event Action<Room> RoomCompleted;
        public event Action<Room> RoomColored;
        public event Action RoomDeleted;

        // ---- palette ----
        private static readonly Color[] s_Palette =
        {
            new Color(0.90f, 0.30f, 0.30f), // red
            new Color(0.98f, 0.62f, 0.20f), // orange
            new Color(0.96f, 0.86f, 0.30f), // yellow
            new Color(0.30f, 0.75f, 0.45f), // green
            new Color(0.30f, 0.55f, 0.90f), // blue
            new Color(0.92f, 0.90f, 0.85f)  // cream
        };
        private static readonly Color s_DefaultRoomColor = new Color(0.78f, 0.78f, 0.80f);

        private const float SwatchZ = 0.55f;
        private const float SwatchY = -0.10f;
        private const float SwatchSpacing = 0.10f;
        private const float SwatchHitRadius = 0.05f;
        private const float SwatchDiameter = 0.07f;
        private const float CornerCooldown = 0.6f;

        // ---- references ----
        private HandFilter _filter;
        private GestureRecognizer _rec;
        private HandProviderSelector _selector;
        private HUDController _hud;
        private Camera _cam;

        // ---- floor / aiming anchor ----
        private float _floorY;
        private bool _floorSet;
        private Vector3 _anchorPos;
        private Quaternion _anchorRot = Quaternion.identity;
        private string _providerName;

        // ---- mode ----
        private InteractionMode _mode = InteractionMode.Edit;
        private AppMode _lastGmMode;
        private bool _initialized;
        [SerializeField] private bool _disableBothPalmsSwitchInXR = true; // both open palms flips Edit/Color; too easy to trigger in a headset
        private bool _bothPalmsHooked = true;
        private float _pointHold;
        private float _lastModeSwitch = -10f;

        // ---- building state ----
        private BuildState _state = BuildState.Drawing;
        private readonly List<Vector3> _corners = new List<Vector3>();
        private readonly List<Room> _rooms = new List<Room>();
        private int _roomCounter;
        private float _extrudeHeight = 2.5f;
        private bool _nearFirst;
        private float _lastCornerTime = -10f;

        // ---- input edges ----
        private class PinchTracker
        {
            public bool Active;
            public bool Pending;
            public bool PendingTarget;
            public float PendingSince;
        }
        private readonly PinchTracker _pinchL = new PinchTracker();
        private readonly PinchTracker _pinchR = new PinchTracker();
        private bool _prevPinch;
        private GestureType _prevOffGesture = GestureType.None;
        private float _offSince;
        private bool _offFired;
        private float _lastShortcutTime = -10f;
        private bool _handTracked;

        // ---- click-latency history ----
        private readonly List<float> _histT = new List<float>();
        private readonly List<Vector3> _histP = new List<Vector3>();
        private readonly List<float> _histH = new List<float>();

        // ---- palette state ----
        private int _selectedColor = 4;
        private int _hoverIndex = -1;
        private float _dwellTimer;
        private Room _hoverRoom;

        // ---- view state ----
        private Transform _modelRoot;
        private float _viewScale = 1f;
        private float _viewYaw;
        private Vector3 _viewPivot;
        private bool _zooming, _rotating;
        private float _zoomD0, _zoomS0, _rotX0, _rotYaw0;

        // ---- visuals ----
        private GameObject _gridGo;
        private GameObject _cursorGo;
        private Material _cursorMat;
        private LineRenderer _rayLine;
        private LineRenderer _previewLine;
        private GameObject _previewWalls;
        private float _previewHeight = -1f;
        private bool _previewDirty;
        private GameObject _paletteRoot;
        private readonly Transform[] _swatches = new Transform[s_Palette.Length];
        private readonly List<GameObject> _markers = new List<GameObject>();

        private GUIStyle _guiSmall;
        private GUIStyle _guiBig;
        private string _toast = "";
        private float _toastUntil;

        // ------------------------------------------------------------------ config helpers
        private GestureConfig Cfg => GameManager.Instance != null ? GameManager.Instance.Config : null;
        private float Grid => Cfg != null ? Mathf.Max(0.05f, Cfg.gridSnapSize) : 0.25f;
        private float MinH => Cfg != null ? Cfg.minExtrudeHeight : 0.2f;
        private float MaxH => Cfg != null ? Cfg.maxExtrudeHeight : 4.0f;
        private float Dwell => Cfg != null ? Cfg.dwellActivationTime : 0.8f;

        private bool IsWebcam => _selector != null && _selector.CurrentType == HandProviderSelector.ProviderType.Webcam;
        private float HandLow => IsWebcam ? _handLowWebcam : _handLowXR;
        private float HandHigh => IsWebcam ? _handHighWebcam : _handHighXR;
        private float GainX => IsWebcam ? _gainXWebcam : _gainXXR;

        private void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.unscaledTime + 2.5f;
        }

        // ------------------------------------------------------------------ setup
        private void Start()
        {
            BuildVisuals();
        }

        private static Material MakeUnlit(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default"));
            m.color = c;
            return m;
        }

        private static Material MakeLit(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            if (s == null) s = Shader.Find("Sprites/Default");
            var m = new Material(s);
            SetColor(m, c);
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static Color Darken(Color c)
        {
            return new Color(c.r * 0.85f, c.g * 0.85f, c.b * 0.85f, 1f);
        }

        private LineRenderer CreateLine(string lineName, Color c, float width)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = MakeUnlit(Color.white);
            lr.startColor = c;
            lr.endColor = c;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.positionCount = 0;
            lr.numCapVertices = 2;
            return lr;
        }

        private void BuildVisuals()
        {
            // Parent of all finished rooms (View mode scales/rotates this)
            _modelRoot = new GameObject("Group10_Model").transform;
            _modelRoot.SetParent(transform, false);

            _gridGo = new GameObject("Group10_FloorGrid");
            _gridGo.transform.SetParent(transform, false);
            _gridGo.AddComponent<MeshFilter>();
            _gridGo.AddComponent<MeshRenderer>().material = MakeUnlit(Color.white);
            _gridGo.SetActive(false);

            _cursorGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursorGo.name = "Group10_Cursor";
            _cursorGo.transform.SetParent(transform, false);
            _cursorGo.transform.localScale = Vector3.one * 0.12f;
            Destroy(_cursorGo.GetComponent<Collider>());
            _cursorMat = MakeUnlit(Color.white);
            _cursorGo.GetComponent<MeshRenderer>().material = _cursorMat;
            _cursorGo.SetActive(false);

            _rayLine = CreateLine("Group10_PointerRay", new Color(1f, 1f, 1f, 0.35f), 0.004f);
            _previewLine = CreateLine("Group10_PreviewOutline", new Color(0.2f, 0.9f, 1f, 1f), 0.03f);

            _previewWalls = new GameObject("Group10_ExtrudePreview");
            _previewWalls.transform.SetParent(transform, false);
            _previewWalls.AddComponent<MeshFilter>();
            _previewWalls.AddComponent<MeshRenderer>().material = MakeUnlit(new Color(0.3f, 0.8f, 1f, 0.35f));
            _previewWalls.SetActive(false);

            _paletteRoot = new GameObject("Group10_Palette");
            _paletteRoot.transform.SetParent(transform, false);
            for (int i = 0; i < s_Palette.Length; i++)
            {
                GameObject sw = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sw.name = "Swatch_" + i;
                Destroy(sw.GetComponent<Collider>());
                sw.transform.SetParent(_paletteRoot.transform, false);
                sw.transform.localPosition = new Vector3((i - (s_Palette.Length - 1) * 0.5f) * SwatchSpacing, SwatchY, SwatchZ);
                sw.transform.localScale = Vector3.one * SwatchDiameter;
                sw.GetComponent<MeshRenderer>().material = MakeLit(s_Palette[i]);
                _swatches[i] = sw.transform;
            }
            _paletteRoot.SetActive(false);
        }

        // ------------------------------------------------------------------ floor / grid / aiming anchor
        private void SetFloor()
        {
            float eyeToFloor = IsWebcam ? _eyeToFloorWebcam : _eyeToFloorXR;
            _anchorPos = _cam.transform.position;
            _floorY = _anchorPos.y - eyeToFloor;

            Vector3 fwd = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            _anchorRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            _floorSet = true;
            ClearHistory();
            RebuildGrid(_anchorPos);
        }

        private void RebuildGrid(Vector3 center)
        {
            float step = Grid;
            float half = 8f;
            center.x = Mathf.Round(center.x / step) * step;
            center.z = Mathf.Round(center.z / step) * step;

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var idx = new List<int>();
            Color minor = new Color(0.6f, 0.9f, 1f, 0.30f);
            Color major = new Color(0.6f, 0.9f, 1f, 0.90f);

            int n = Mathf.RoundToInt(half / step);
            for (int i = -n; i <= n; i++)
            {
                float p = i * step;
                bool isMajor = Mathf.Abs(Mathf.Round(p) - p) < 0.001f;
                Color c = isMajor ? major : minor;

                AddLine(verts, cols, idx,
                    new Vector3(center.x + p, _floorY, center.z - half),
                    new Vector3(center.x + p, _floorY, center.z + half), c);
                AddLine(verts, cols, idx,
                    new Vector3(center.x - half, _floorY, center.z + p),
                    new Vector3(center.x + half, _floorY, center.z + p), c);
            }

            var mf = _gridGo.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            var mesh = new Mesh { name = "Group10Grid" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            mf.sharedMesh = mesh;
            _gridGo.SetActive(_mode != InteractionMode.View);
        }

        private static void AddLine(List<Vector3> v, List<Color> c, List<int> idx, Vector3 a, Vector3 b, Color col)
        {
            int s = v.Count;
            v.Add(a); v.Add(b);
            c.Add(col); c.Add(col);
            idx.Add(s); idx.Add(s + 1);
        }

        /// <summary>Hand position -> point on the floor plan (hand up = farther, sideways = sideways).</summary>
        private Vector3 PlanPoint(Vector3 handPoint)
        {
            Vector3 local = Quaternion.Inverse(_anchorRot) * (handPoint - _anchorPos); // x right, y up, z forward
            float t = Mathf.Clamp01(Mathf.InverseLerp(HandLow, HandHigh, local.y));
            float half = Mathf.Min(7.5f, _planHalfWidth * _reach);
            float x = Mathf.Clamp(local.x * GainX * _reach, -half, half);
            float z = Mathf.Lerp(_zNear, Mathf.Min(7.5f, _zNear + (_zFar - _zNear) * _reach), t);
            Vector3 p = _anchorPos + _anchorRot * new Vector3(x, 0f, z);
            p.y = _floorY;
            return p;
        }

        private float HeightFromHand(Vector3 handPoint)
        {
            Vector3 local = Quaternion.Inverse(_anchorRot) * (handPoint - _anchorPos);
            float t = Mathf.Clamp01(Mathf.InverseLerp(HandLow, HandHigh, local.y));
            float h = Mathf.Lerp(MinH, MaxH, t);
            return Mathf.Clamp(Mathf.Round(h / _heightStep) * _heightStep, MinH, MaxH);
        }

        private Vector3 Snap(Vector3 p)
        {
            float g = Grid;
            return new Vector3(Mathf.Round(p.x / g) * g, _floorY, Mathf.Round(p.z / g) * g);
        }

        /// <summary>While drawing, snap the cursor onto the first corner when it is close (so rooms close exactly).</summary>
        private bool ApplyMagnet(ref Vector3 snapped)
        {
            if (_mode != InteractionMode.Edit || _state != BuildState.Drawing || _corners.Count < 3) return false;
            if (Vector3.Distance(snapped, _corners[0]) >= _closeRadius) return false;
            snapped = _corners[0];
            return true;
        }

        // ------------------------------------------------------------------ click-latency history
        private void PushHistory(Vector3 plan, float height)
        {
            float now = Time.unscaledTime;
            _histT.Add(now); _histP.Add(plan); _histH.Add(height);
            while (_histT.Count > 1 && now - _histT[0] > 0.6f)
            {
                _histT.RemoveAt(0); _histP.RemoveAt(0); _histH.RemoveAt(0);
            }
        }

        private int HistIndex(float secondsAgo)
        {
            float target = Time.unscaledTime - secondsAgo;
            int idx = 0;
            for (int i = 0; i < _histT.Count; i++)
            {
                if (_histT[i] <= target) idx = i; else break;
            }
            return idx;
        }

        private void ClearHistory()
        {
            _histT.Clear(); _histP.Clear(); _histH.Clear();
        }

        private void ResetWorld()
        {
            foreach (Room r in _rooms)
            {
                if (r.Root != null) Destroy(r.Root);
            }
            _rooms.Clear();
            _corners.Clear();
            _state = BuildState.Drawing;
            _floorSet = false;
            ClearHistory();
            _gridGo.SetActive(false);
            _previewWalls.SetActive(false);
            ResetViewTransform();
            Toast("Tracking source changed: scene reset.");
        }

        // ------------------------------------------------------------------ main loop
        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            if (_filter == null) _filter = FindAnyObjectByType<HandFilter>();
            if (_rec == null) _rec = FindAnyObjectByType<GestureRecognizer>();
            if (_selector == null) _selector = FindAnyObjectByType<HandProviderSelector>();
            if (_hud == null) _hud = FindAnyObjectByType<HUDController>();
            if (_cam == null) _cam = Camera.main;
            if (_filter == null || _rec == null || _cam == null || _cursorGo == null) return;

            // GameManager flips Edit/Color when both palms are open. With a headset both hands are often open at once,
            // which makes the mode flicker, so that shortcut is switched off for the Meta XR condition.
            // Holding the LEFT index finger up for 1 s still changes mode in both conditions.
            bool wantBothPalms = IsWebcam || !_disableBothPalmsSwitchInXR;
            if (wantBothPalms != _bothPalmsHooked)
            {
                if (wantBothPalms) _rec.OnBothHandsOpenTriggered += gm.ToggleMode;
                else _rec.OnBothHandsOpenTriggered -= gm.ToggleMode;
                _bothPalmsHooked = wantBothPalms;
            }

            if (!_initialized)
            {
                _initialized = true;
                _mode = gm.CurrentMode == AppMode.Edit ? InteractionMode.Edit : InteractionMode.Color;
                _lastGmMode = gm.CurrentMode;
                ApplyModeVisuals();
            }

            // The friends' both-palms-open gesture toggles GameManager's mode: follow it.
            if (gm.CurrentMode != _lastGmMode)
            {
                _lastGmMode = gm.CurrentMode;
                SetMode(gm.CurrentMode == AppMode.Edit ? InteractionMode.Edit : InteractionMode.Color, false);
            }

            if (_selector != null && _selector.ProviderName != _providerName)
            {
                _providerName = _selector.ProviderName;
                if (_floorSet) ResetWorld();
            }

            HandleKeys();

            GestureResult dom = _dominant == Handedness.Right ? _rec.RightResult : _rec.LeftResult;
            GestureResult off = _dominant == Handedness.Right ? _rec.LeftResult : _rec.RightResult;

            ProcessLeftShortcuts(off);
            ProcessModeSwitch(off);

            bool pinchL = EvalPinch(_pinchL, Handedness.Left, _rec.LeftResult.IsTracked);
            bool pinchR = EvalPinch(_pinchR, Handedness.Right, _rec.RightResult.IsTracked);
            bool pinchDom = _dominant == Handedness.Right ? pinchR : pinchL;

            if (_mode == InteractionMode.View)
            {
                UpdateView(pinchL, pinchR);
                return;
            }

            _handTracked = dom.IsTracked;
            if (!dom.IsTracked)
            {
                _cursorGo.SetActive(false);
                _rayLine.positionCount = 0;
                _prevPinch = false;
                ClearHistory();
                ResetDwell();
                SyncMarkers();
                return;
            }

            if (!_floorSet) SetFloor();

            Vector3[] j = _filter.GetFilteredJoints(_dominant);
            Vector3 pinchPoint = 0.5f * (j[4] + j[8]); // midpoint: stable while the fingers close
            bool pinch = pinchDom;
            bool pinchDown = pinch && !_prevPinch;
            _prevPinch = pinch;

            Vector3 plan = PlanPoint(pinchPoint);
            float rawHeight = HeightFromHand(pinchPoint);
            PushHistory(plan, rawHeight);
            int ci = HistIndex(_clickLatency);

            Vector3 snappedNow = Snap(plan);
            Vector3 snappedClick = Snap(_histP[ci]);
            _nearFirst = ApplyMagnet(ref snappedNow);
            ApplyMagnet(ref snappedClick);

            UpdateCursor(snappedNow, pinchPoint, pinch);

            if (_mode == InteractionMode.Edit)
                UpdateEdit(snappedNow, snappedClick, rawHeight, _histH[ci], pinchDown);
            else
                UpdateColor(snappedNow, snappedClick, j[8], pinchDown);

            SyncMarkers();
        }

        private void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.mKey.wasPressedThisFrame) CycleMode();
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) AdjustReach(1.15f);
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) AdjustReach(1f / 1.15f);
            if (kb.cKey.wasPressedThisFrame && _mode == InteractionMode.Edit) ClearDrawing();
            if (kb.spaceKey.wasPressedThisFrame) CloseShape();
            if (kb.rKey.wasPressedThisFrame && _floorSet)
            {
                SetFloor();
                Toast("Floor re-calibrated.");
            }
        }

        // ------------------------------------------------------------------ modes
        private void CycleMode()
        {
            SetMode((InteractionMode)(((int)_mode + 1) % 3), true);
        }

        private void SetMode(InteractionMode m, bool pushToApp)
        {
            if (m == _mode) return;

            if (_mode == InteractionMode.View) ExitView();
            _mode = m;
            if (m == InteractionMode.View) EnterView();

            GameManager gm = GameManager.Instance;
            if (pushToApp && gm != null && m != InteractionMode.View)
            {
                gm.SetMode(m == InteractionMode.Edit ? AppMode.Edit : AppMode.Color);
                _lastGmMode = gm.CurrentMode;
            }
            if (m == InteractionMode.View && _hud != null) _hud.SetModeName("VIEW MODE (Inspect)");

            ApplyModeVisuals();
        }

        private void ApplyModeVisuals()
        {
            bool color = _mode == InteractionMode.Color;
            bool view = _mode == InteractionMode.View;

            _paletteRoot.SetActive(color);
            if (color && _cam != null)
            {
                _paletteRoot.transform.SetParent(_cam.transform, false);
                _paletteRoot.transform.localPosition = Vector3.zero;
                _paletteRoot.transform.localRotation = Quaternion.identity;
            }
            else
            {
                _paletteRoot.transform.SetParent(transform, false);
            }

            _gridGo.SetActive(_floorSet && !view);
            _previewWalls.SetActive(false);
            if (_mode != InteractionMode.Edit) _previewLine.positionCount = 0;
            if (view)
            {
                _cursorGo.SetActive(false);
                _rayLine.positionCount = 0;
            }
            _hoverRoom = null;
            ResetDwell();
            SyncMarkers();
        }

        private void ProcessModeSwitch(GestureResult off)
        {
            bool pointing = off.IsTracked && off.Gesture == GestureType.Point;
            if (pointing && Time.time - _lastModeSwitch > _modeCooldown)
            {
                _pointHold += Time.deltaTime;
                if (_pointHold >= _modeHoldTime)
                {
                    _pointHold = 0f;
                    _lastModeSwitch = Time.time;
                    CycleMode();
                }
            }
            else if (!pointing)
            {
                _pointHold = 0f;
            }
        }

        /// <summary>
        /// Stricter pinch detector than the shared GestureRecognizer: tight enter/exit thresholds on the
        /// thumb-tip to index-tip distance (relative to palm size) and TIME-based debouncing. The recognizer
        /// debounces by frames, but Unity renders ~3x faster than the webcam sends packets, so a single noisy
        /// packet could pass its debounce and register as a pinch.
        /// </summary>
        private bool EvalPinch(PinchTracker t, Handedness h, bool tracked)
        {
            if (!tracked)
            {
                t.Active = false;
                t.Pending = false;
                return false;
            }

            Vector3[] j = _filter.GetFilteredJoints(h);
            float palm = Mathf.Max(0.04f, Vector3.Distance(j[0], j[9]));
            float ratio = Vector3.Distance(j[4], j[8]) / palm;
            bool target = t.Active ? ratio < _pinchExit : ratio < _pinchEnter;

            if (target == t.Active)
            {
                t.Pending = false;
            }
            else
            {
                float now = Time.unscaledTime;
                if (!t.Pending || t.PendingTarget != target)
                {
                    t.Pending = true;
                    t.PendingTarget = target;
                    t.PendingSince = now;
                }
                else if (now - t.PendingSince >= (target ? _pinchOnTime : _pinchOffTime))
                {
                    t.Active = target;
                    t.Pending = false;
                }
            }
            return t.Active;
        }

        private void AdjustReach(float factor)
        {
            _reach = Mathf.Clamp(_reach * factor, 0.5f, 3f);
            ClearHistory();
            Toast("Reach x" + _reach.ToString("F1") + "  (more = a small hand movement covers more floor)");
        }

        // ------------------------------------------------------------------ cursor
        private void UpdateCursor(Vector3 snapped, Vector3 pinchPoint, bool pinch)
        {
            _cursorGo.SetActive(true);
            _cursorGo.transform.position = snapped + Vector3.up * 0.03f;
            Color baseCol = _mode == InteractionMode.Color ? s_Palette[_selectedColor] : Color.white;
            _cursorMat.color = pinch ? new Color(0.2f, 1f, 0.4f) : baseCol;

            _rayLine.positionCount = 2;
            _rayLine.SetPosition(0, pinchPoint);
            _rayLine.SetPosition(1, snapped);
        }

        private void SyncMarkers()
        {
            int needed = _mode == InteractionMode.Edit ? _corners.Count : 0;
            while (_markers.Count < needed)
            {
                GameObject m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m.name = "Corner_" + _markers.Count;
                Destroy(m.GetComponent<Collider>());
                m.transform.SetParent(transform, false);
                bool first = _markers.Count == 0;
                m.GetComponent<MeshRenderer>().material = MakeUnlit(first ? new Color(0.2f, 1f, 0.4f) : new Color(0.2f, 0.9f, 1f));
                _markers.Add(m);
            }

            for (int i = 0; i < _markers.Count; i++)
            {
                bool on = i < needed;
                _markers[i].SetActive(on);
                if (!on) continue;
                _markers[i].transform.position = _corners[i] + Vector3.up * 0.04f;
                float size = i == 0 ? (_nearFirst ? 0.30f : 0.20f) : 0.10f;
                _markers[i].transform.localScale = Vector3.one * size;
            }
        }

        // ------------------------------------------------------------------ EDIT mode
        private void UpdateEdit(Vector3 snappedNow, Vector3 snappedClick, float rawHeight, float clickHeight, bool pinchDown)
        {
            if (_state == BuildState.Drawing)
            {
                _previewWalls.SetActive(false);
                if (pinchDown) TryPlaceCorner(snappedClick);
                RefreshOutline(snappedNow);
            }
            else
            {
                _extrudeHeight = rawHeight;
                RefreshExtrudePreview();
                RefreshOutline(snappedNow);

                if (pinchDown && Time.time - _lastCornerTime > CornerCooldown)
                {
                    _extrudeHeight = clickHeight;
                    CommitRoom();
                }
            }
        }

        private void TryPlaceCorner(Vector3 snapped)
        {
            if (Time.time - _lastCornerTime < CornerCooldown) return;

            if (_corners.Count >= 3 && Vector3.Distance(snapped, _corners[0]) < _closeRadius)
            {
                CloseShape();
                return;
            }

            if (_corners.Count > 0 && Vector3.Distance(snapped, _corners[_corners.Count - 1]) < Grid * 0.8f) return;

            _lastCornerTime = Time.time;
            _corners.Add(snapped);
            CornerPlaced?.Invoke(_corners.Count);
        }

        /// <summary>Close the polygon and move on to choosing the wall height.</summary>
        private void CloseShape()
        {
            if (_mode != InteractionMode.Edit || _state != BuildState.Drawing || _corners.Count < 3) return;
            if (Mathf.Abs(RoomMeshBuilder.SignedArea(_corners)) < _minRoomArea)
            {
                Toast("Room too small: add more corners first.");
                return;
            }
            _lastCornerTime = Time.time;
            _state = BuildState.Extruding;
            _previewDirty = true;
            Toast("Room closed! Now set the wall height.");
        }

        private void RefreshOutline(Vector3 snapped)
        {
            var pts = new List<Vector3>();
            foreach (Vector3 c in _corners) pts.Add(c + Vector3.up * 0.02f);

            if (_state == BuildState.Extruding)
            {
                if (_corners.Count > 0) pts.Add(_corners[0] + Vector3.up * 0.02f);
            }
            else if (_corners.Count > 0)
            {
                pts.Add(snapped + Vector3.up * 0.02f);
            }

            Color col = _nearFirst || _state == BuildState.Extruding ? new Color(0.3f, 1f, 0.5f) : new Color(0.2f, 0.9f, 1f);
            _previewLine.startColor = col;
            _previewLine.endColor = col;
            _previewLine.positionCount = pts.Count;
            if (pts.Count > 0) _previewLine.SetPositions(pts.ToArray());
        }

        private void RefreshExtrudePreview()
        {
            _previewWalls.SetActive(true);
            if (!_previewDirty && Mathf.Abs(_extrudeHeight - _previewHeight) < 0.001f) return;

            var mf = _previewWalls.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            mf.sharedMesh = RoomMeshBuilder.BuildWalls(_corners, _extrudeHeight);
            _previewHeight = _extrudeHeight;
            _previewDirty = false;
        }

        private void CommitRoom()
        {
            var room = new Room
            {
                Id = ++_roomCounter,
                Corners = new List<Vector3>(_corners),
                Height = _extrudeHeight,
                Color = s_DefaultRoomColor
            };

            room.Root = new GameObject("Room_" + room.Id);
            room.Root.transform.SetParent(_modelRoot, false);

            room.WallMat = MakeLit(room.Color);
            var walls = new GameObject("Walls");
            walls.transform.SetParent(room.Root.transform, false);
            walls.AddComponent<MeshFilter>().sharedMesh = RoomMeshBuilder.BuildWalls(room.Corners, room.Height);
            walls.AddComponent<MeshRenderer>().sharedMaterial = room.WallMat;

            room.FloorMat = MakeLit(Darken(room.Color));
            var floor = new GameObject("Floor");
            floor.transform.SetParent(room.Root.transform, false);
            floor.AddComponent<MeshFilter>().sharedMesh = RoomMeshBuilder.BuildFloor(room.Corners, _floorY + 0.005f);
            floor.AddComponent<MeshRenderer>().sharedMaterial = room.FloorMat;

            _rooms.Add(room);
            _corners.Clear();
            _state = BuildState.Drawing;
            _previewWalls.SetActive(false);

            Toast("Room " + room.Id + " built (" + room.Area.ToString("F1") + " m2, " + room.Height.ToString("F1") + " m high).");
            RoomCompleted?.Invoke(room);
        }

        // ------------------------------------------------------------------ left-hand shortcuts
        private void ProcessLeftShortcuts(GestureResult off)
        {
            GestureType g = off.IsTracked ? off.Gesture : GestureType.None;
            if (g != _prevOffGesture)
            {
                _prevOffGesture = g;
                _offSince = Time.time;
                _offFired = false;
            }

            // The pose must be held steadily before it counts (the shared recognizer can flicker for single frames).
            if (g == GestureType.None || _offFired) return;
            if (Time.time - _offSince < _shortcutHold) return;
            if (Time.time - _lastShortcutTime < _shortcutCooldown) return;
            _offFired = true;

            if (g == GestureType.Fist)
            {
                if (_mode == InteractionMode.View) { _lastShortcutTime = Time.time; ResetView(); }
                else if (_mode == InteractionMode.Edit) { _lastShortcutTime = Time.time; Undo(); }
            }
            else if (g == GestureType.Peace && _mode == InteractionMode.Edit)
            {
                _lastShortcutTime = Time.time;
                DeleteLastRoom();
            }
            else if (g == GestureType.ThumbsUp && _mode == InteractionMode.Edit)
            {
                _lastShortcutTime = Time.time;
                CloseShape();
            }
        }

        private void Undo()
        {
            if (_state == BuildState.Extruding)
            {
                _state = BuildState.Drawing;
                _previewWalls.SetActive(false);
                CornerUndone?.Invoke();
            }
            else if (_corners.Count > 0)
            {
                _corners.RemoveAt(_corners.Count - 1);
                CornerUndone?.Invoke();
            }
        }

        private void ClearDrawing()
        {
            int n = _corners.Count + (_state == BuildState.Extruding ? 1 : 0);
            _corners.Clear();
            _state = BuildState.Drawing;
            _previewWalls.SetActive(false);
            for (int i = 0; i < n; i++) CornerUndone?.Invoke();
            Toast("Drawing cleared.");
        }

        private void DeleteLastRoom()
        {
            if (_rooms.Count == 0) return;
            Room r = _rooms[_rooms.Count - 1];
            if (r.Root != null) Destroy(r.Root);
            _rooms.RemoveAt(_rooms.Count - 1);
            RoomDeleted?.Invoke();
            Toast("Last room deleted.");
        }

        // ------------------------------------------------------------------ COLOR mode
        private void UpdateColor(Vector3 snappedNow, Vector3 snappedClick, Vector3 indexTip, bool pinchDown)
        {
            // Palette is head-locked: compare the index fingertip in camera space.
            Vector3 local = _cam.transform.InverseTransformPoint(indexTip);
            int over = -1;
            for (int i = 0; i < s_Palette.Length; i++)
            {
                Vector3 sp = _swatches[i].localPosition;
                var d = new Vector2(local.x - sp.x, local.y - sp.y);
                if (d.magnitude < SwatchHitRadius && Mathf.Abs(local.z - SwatchZ) < 0.30f)
                {
                    over = i;
                    break;
                }
            }

            if (over != _hoverIndex)
            {
                _hoverIndex = over;
                _dwellTimer = 0f;
            }
            else if (over >= 0 && over != _selectedColor)
            {
                _dwellTimer += Time.deltaTime;
                if (_dwellTimer >= Dwell)
                {
                    _selectedColor = over;
                    _dwellTimer = 0f;
                }
            }

            float dwellProgress = Dwell > 0f ? Mathf.Clamp01(_dwellTimer / Dwell) : 0f;
            for (int i = 0; i < _swatches.Length; i++)
            {
                float k = 1f;
                if (i == _selectedColor) k = 1.35f;
                else if (i == _hoverIndex) k = 1f + 0.35f * dwellProgress;
                _swatches[i].localScale = Vector3.one * SwatchDiameter * k;
            }

            // Outline the room that a pinch would paint
            _hoverRoom = FindRoomNear(snappedNow);
            if (_hoverRoom != null)
            {
                var pts = new List<Vector3>();
                foreach (Vector3 c in _hoverRoom.Corners) pts.Add(c + Vector3.up * 0.02f);
                pts.Add(_hoverRoom.Corners[0] + Vector3.up * 0.02f);
                Color oc = s_Palette[_selectedColor];
                _previewLine.startColor = oc;
                _previewLine.endColor = oc;
                _previewLine.positionCount = pts.Count;
                _previewLine.SetPositions(pts.ToArray());
            }
            else
            {
                _previewLine.positionCount = 0;
            }

            if (pinchDown && over < 0)
            {
                Room target = FindRoomNear(snappedClick);
                if (target != null) PaintRoom(target);
            }
        }

        private Room FindRoomNear(Vector3 p)
        {
            Room best = null;
            float bestD = _paintReach;
            for (int i = _rooms.Count - 1; i >= 0; i--)
            {
                float d = RoomMeshBuilder.DistanceToPolygon(p, _rooms[i].Corners);
                if (d <= 0f) return _rooms[i];
                if (d < bestD) { bestD = d; best = _rooms[i]; }
            }
            return best;
        }

        private void PaintRoom(Room r)
        {
            r.Color = s_Palette[_selectedColor];
            SetColor(r.WallMat, r.Color);
            SetColor(r.FloorMat, Darken(r.Color));
            Toast("Room " + r.Id + " painted.");
            RoomColored?.Invoke(r);
        }

        private void ResetDwell()
        {
            _hoverIndex = -1;
            _dwellTimer = 0f;
        }

        // ------------------------------------------------------------------ VIEW mode
        private void EnterView()
        {
            if (_rooms.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                int n = 0;
                float maxH = 0f;
                foreach (Room r in _rooms)
                {
                    foreach (Vector3 c in r.Corners) { sum += c; n++; }
                    if (r.Height > maxH) maxH = r.Height;
                }
                _viewPivot = sum / n + Vector3.up * (maxH * 0.5f);
            }
            _corners.Clear();
            _state = BuildState.Drawing;
            ResetView();
        }

        private void ExitView()
        {
            ResetViewTransform();
        }

        private void ResetView()
        {
            _viewScale = 1f;
            _viewYaw = 0f;
            _zooming = false;
            _rotating = false;
            ApplyViewTransform();
        }

        private void ResetViewTransform()
        {
            _viewScale = 1f;
            _viewYaw = 0f;
            _zooming = false;
            _rotating = false;
            if (_modelRoot != null)
            {
                _modelRoot.localScale = Vector3.one;
                _modelRoot.rotation = Quaternion.identity;
                _modelRoot.position = Vector3.zero;
            }
        }

        private void ApplyViewTransform()
        {
            Quaternion rot = Quaternion.Euler(0f, _viewYaw, 0f);
            _modelRoot.localScale = Vector3.one * _viewScale;
            _modelRoot.rotation = rot;
            _modelRoot.position = _viewPivot - rot * (_viewPivot * _viewScale); // scale/rotate about the model centre
        }

        private void UpdateView(bool lp, bool rp)
        {
            _handTracked = true;
            _cursorGo.SetActive(false);
            _rayLine.positionCount = 0;
            _previewLine.positionCount = 0;
            SyncMarkers();

            if (lp && rp)
            {
                Vector3[] jl = _filter.GetFilteredJoints(Handedness.Left);
                Vector3[] jr = _filter.GetFilteredJoints(Handedness.Right);
                float d = Vector3.Distance(0.5f * (jl[4] + jl[8]), 0.5f * (jr[4] + jr[8]));
                if (!_zooming)
                {
                    _zooming = true;
                    _zoomD0 = d;
                    _zoomS0 = _viewScale;
                }
                else if (_zoomD0 > 0.03f)
                {
                    _viewScale = Mathf.Clamp(_zoomS0 * d / _zoomD0, _minViewScale, _maxViewScale);
                }
                _rotating = false;
            }
            else
            {
                _zooming = false;
                if (rp)
                {
                    Vector3[] jr = _filter.GetFilteredJoints(Handedness.Right);
                    float x = (Quaternion.Inverse(_anchorRot) * (0.5f * (jr[4] + jr[8]) - _anchorPos)).x;
                    if (!_rotating)
                    {
                        _rotating = true;
                        _rotX0 = x;
                        _rotYaw0 = _viewYaw;
                    }
                    else
                    {
                        _viewYaw = _rotYaw0 + (x - _rotX0) * _rotateDegPerMeter;
                    }
                }
                else
                {
                    _rotating = false;
                }
            }

            // Keyboard fallback (also handy when only one hand is visible)
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed) _viewYaw -= 90f * Time.deltaTime;
                if (kb.rightArrowKey.isPressed) _viewYaw += 90f * Time.deltaTime;
                if (kb.upArrowKey.isPressed) _viewScale = Mathf.Clamp(_viewScale * (1f + 1.0f * Time.deltaTime), _minViewScale, _maxViewScale);
                if (kb.downArrowKey.isPressed) _viewScale = Mathf.Clamp(_viewScale * (1f - 0.7f * Time.deltaTime), _minViewScale, _maxViewScale);
            }

            ApplyViewTransform();
        }

        // ------------------------------------------------------------------ on-screen guidance (desktop)
        /// <summary>One plain-language instruction telling the user exactly what to do next.</summary>
        private string KeyHint(string s) { return IsWebcam ? s : ""; }

        private string GetGuide()
        {
            if (_mode == InteractionMode.View)
            {
                if (_rooms.Count == 0) return "Nothing to view yet. Hold your LEFT index finger up" + KeyHint(" (or press M)") + " to go back to EDIT and build a room.";
                return "VIEW: pinch with BOTH hands and move them apart / together to zoom (now " +
                       Mathf.RoundToInt(_viewScale * 100f) + "%). Pinch with ONE hand and drag sideways to rotate. LEFT FIST resets.";
            }

            if (!_handTracked)
                return IsWebcam
                    ? "Show your " + _dominant.ToString().ToUpper() + " hand to the webcam."
                    : "Bring your " + _dominant.ToString().ToUpper() + " hand up in front of you so the headset can see it.";

            if (_mode == InteractionMode.Edit)
            {
                if (_state == BuildState.Extruding)
                    return "STEP 2/4: Raise or lower your hand to set the wall height (" +
                           _extrudeHeight.ToString("F1") + " m), then PINCH to confirm.";
                if (_corners.Count == 0 && _rooms.Count > 0)
                    return "STEP 3/4: Room built! Hold your LEFT index finger up for 1 s" + KeyHint(" (or press M)") + " to go to COLOR mode. PINCH to start another room.";
                if (_corners.Count == 0)
                    return "STEP 1/4: Move your hand to aim the white dot on the grid (hand up = farther away). PINCH thumb + index to mark the first corner.";
                if (_corners.Count < 3)
                    return "STEP 1/4: Aim and PINCH for the next corner (" + _corners.Count + " placed, need at least 3).";
                if (_nearFirst)
                    return "The dot is on the FIRST corner. PINCH now to CLOSE the room.";
                return "STEP 1/4: Add more corners, or move onto the big GREEN dot (it snaps) and PINCH to close. LEFT THUMBS-UP" + KeyHint(" or Space") + " also closes.";
            }

            if (_rooms.Count == 0)
                return IsWebcam
                    ? "There is no room to paint yet. Press M (or hold LEFT index finger up) until you are back in EDIT mode."
                    : "There is no room to paint yet. Hold your LEFT index finger up until you are back in EDIT mode.";
            if (_hoverRoom == null)
                return "STEP 3/4: Hold your index fingertip on a colour until it grows. Then move the white dot onto a room (it gets outlined).";
            return "STEP 4/4: The outlined room is selected. PINCH to paint it with the chosen colour.";
        }

        private void OnGUI()
        {
            if (_guiSmall == null)
            {
                _guiSmall = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                _guiSmall.normal.textColor = new Color(0.8f, 0.9f, 1f);
                _guiBig = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = true };
                _guiBig.normal.textColor = Color.white;
            }

            float w = Screen.width - 40f;
            var box = new Rect(20f, Screen.height - 175f, w, 155f);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            string modeName = _mode == InteractionMode.Edit ? "EDIT (draw rooms)"
                            : _mode == InteractionMode.Color ? "COLOR (paint rooms)" : "VIEW (zoom / rotate)";
            string status = "MODE: " + modeName + "   |   Rooms: " + _rooms.Count + "   Corners: " + _corners.Count +
                            "   |   Keys: [M] mode  [C] clear  [Space] close  [R] recalibrate  [-/+] reach x" + _reach.ToString("F1");
            GUI.Label(new Rect(box.x + 12f, box.y + 4f, box.width - 24f, 24f), status, _guiSmall);
            GUI.Label(new Rect(box.x + 12f, box.y + 28f, box.width - 24f, 88f), GetGuide(), _guiBig);
            GUI.Label(new Rect(box.x + 12f, box.y + 124f, box.width - 24f, 24f),
                "LEFT hand:  FIST = undo / reset view   |   PEACE = delete room   |   THUMBS-UP = close room   |   POINT (hold 1 s) = next mode",
                _guiSmall);

            if (_pointHold > 0.05f)
            {
                float p = Mathf.Clamp01(_pointHold / _modeHoldTime);
                var bar = new Rect(box.x + 12f, box.y - 14f, 260f, 10f);
                GUI.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.85f, 0.2f, 1f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * p, bar.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            if (Time.unscaledTime < _toastUntil)
            {
                var toastStyle = _guiBig;
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(new Rect(Screen.width * 0.5f - 330f, 150f, 660f, 44f), Texture2D.whiteTexture);
                GUI.color = prev;
                GUI.Label(new Rect(Screen.width * 0.5f - 320f, 154f, 640f, 40f), _toast, toastStyle);
            }
        }
    }
}
