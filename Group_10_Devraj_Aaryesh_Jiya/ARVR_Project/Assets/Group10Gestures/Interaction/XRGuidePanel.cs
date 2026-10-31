using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Group10Gestures.Core;
using Group10Gestures.Providers;
using Group10Gestures.Recognition;

namespace Group10Gestures.Interaction
{
    /// <summary>
    /// Headset version of the on-screen guide. The desktop guide (RoomBuilderController.OnGUI) is IMGUI,
    /// which is never drawn inside a headset, so this builds a small world-space panel in front of the user
    /// that shows the same step-by-step instruction, status line and toast messages.
    ///
    /// Behaviour:
    ///  - Only visible while the Meta XR hand provider is active (the desktop overlay is still used for webcam).
    ///  - Floats about 1.2 m in front of the user, slightly below eye level, and follows head turns lazily
    ///    (it only re-centres after you turn well away from it, which is easier on the eyes than head-locked text).
    ///  - The controls cheat sheet can be hidden / shown with the hands:
    ///    lower the RIGHT hand and hold the LEFT palm open for 2 s. (A left open palm together with an open right
    ///    palm is the existing Edit/Color switch, so the right hand must NOT be open for this gesture to count.)
    ///
    /// Delete this file (and its line in InteractionBootstrap) to remove the feature.
    /// </summary>
    public class XRGuidePanel : MonoBehaviour
    {
        [Header("Placement")]
        [SerializeField] private float _distance = 1.2f;
        [SerializeField] private float _heightBelowEye = 0.30f;
        [SerializeField] private float _panelScale = 0.0009f;      // metres per canvas pixel (1100 px -> ~0.99 m)
        [SerializeField] private float _followDeadZoneDeg = 25f;
        [SerializeField] private float _followSmoothTime = 0.5f;

        [Header("Behaviour")]
        [SerializeField] private float _toggleHoldTime = 2f;
        [SerializeField] private bool _showWhenWebcam = false;

        private const float CanvasW = 1100f;
        private const float CanvasH = 500f;

        private RoomBuilderController _builder;
        private HandProviderSelector _selector;
        private GestureRecognizer _rec;
        private Camera _cam;

        private GameObject _root;
        private TextMeshProUGUI _statusText, _toastText, _guideText, _cheatText;
        private RectTransform _barFill;
        private Image _barFillImage;
        private GameObject _barBg;

        private bool _built;
        private bool _cheatVisible = true;
        private float _palmHold;
        private bool _toggleLock;
        private string _ownToast = "";
        private float _ownToastUntil;

        private float _yaw;
        private float _yawVel;
        private bool _following;
        private bool _placed;

        private string _lastStatus, _lastGuide, _lastToast, _lastCheat;

        private const string CheatShown =
            "LEFT hand:  FIST = undo   |   PEACE = delete room   |   THUMBS-UP = close room   |   POINT (hold 1 s) = next mode\n" +
            "RIGHT hand:  PINCH thumb + index = place / confirm\n" +
            "Know the controls?  Lower your RIGHT hand and hold your LEFT palm open for 2 s to hide this.";

        private const string CheatHidden =
            "Lower your RIGHT hand and hold your LEFT palm open for 2 s to show the controls again.";

        private void Awake()
        {
            _builder = GetComponent<RoomBuilderController>();
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }

        // ---------------------------------------------------------------- panel construction
        private void Build()
        {
            _root = new GameObject("Group10_XRGuidePanel");

            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            RectTransform rt = _root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            _root.transform.localScale = Vector3.one * _panelScale;

            // background
            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(_root.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.72f);
            bgImg.raycastTarget = false;
            Stretch(bg.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            // status line (top)
            _statusText = MakeText("Status", 26f, FontStyles.Normal, new Color(0.8f, 0.9f, 1f), TextAlignmentOptions.TopLeft);
            Anchor(_statusText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(0f, -14f), new Vector2(-48f, 38f));

            // toast (under the status line)
            _toastText = MakeText("Toast", 30f, FontStyles.Bold, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.TopLeft);
            Anchor(_toastText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(0f, -54f), new Vector2(-48f, 42f));

            // main instruction
            _guideText = MakeText("Guide", 40f, FontStyles.Bold, Color.white, TextAlignmentOptions.TopLeft);
            Stretch(_guideText.rectTransform, 24f, 160f, 24f, 104f);

            // cheat sheet (bottom)
            _cheatText = MakeText("Cheat", 24f, FontStyles.Normal, new Color(0.8f, 0.9f, 1f), TextAlignmentOptions.BottomLeft);
            Anchor(_cheatText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(0f, 24f), new Vector2(-48f, 130f));

            // hold progress bar (very bottom)
            _barBg = new GameObject("BarBg", typeof(RectTransform));
            _barBg.transform.SetParent(_root.transform, false);
            Image barBgImg = _barBg.AddComponent<Image>();
            barBgImg.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
            barBgImg.raycastTarget = false;
            Anchor(_barBg.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                   new Vector2(0f, 6f), new Vector2(-48f, 12f));

            GameObject fill = new GameObject("BarFill", typeof(RectTransform));
            fill.transform.SetParent(_barBg.transform, false);
            _barFillImage = fill.AddComponent<Image>();
            _barFillImage.color = new Color(1f, 0.85f, 0.2f, 1f);
            _barFillImage.raycastTarget = false;
            _barFill = fill.GetComponent<RectTransform>();
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            _barBg.SetActive(false);

            _built = true;
            _root.SetActive(false);
        }

        private TextMeshProUGUI MakeText(string name, float size, FontStyles style, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static void Stretch(RectTransform r, float left, float bottom, float right, float top)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom);
            r.offsetMax = new Vector2(-right, -top);
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        // ---------------------------------------------------------------- per frame
        private void LateUpdate()
        {
            if (_builder == null) _builder = GetComponent<RoomBuilderController>();
            if (_selector == null) _selector = FindAnyObjectByType<HandProviderSelector>();
            if (_rec == null) _rec = FindAnyObjectByType<GestureRecognizer>();
            if (_cam == null) _cam = Camera.main;
            if (_builder == null || _selector == null || _cam == null) return;

            bool wanted = _showWhenWebcam || _selector.CurrentType == HandProviderSelector.ProviderType.MetaXR;
            if (!_built && wanted) Build();
            if (!_built) return;

            if (_root.activeSelf != wanted)
            {
                _root.SetActive(wanted);
                _placed = false;
            }
            if (!wanted) return;

            ProcessToggleGesture();
            UpdateTexts();
            UpdateBar();
            Follow();
        }

        /// <summary>Lower the right hand and hold the left palm open for _toggleHoldTime seconds to hide / show the cheat sheet.</summary>
        private void ProcessToggleGesture()
        {
            if (_rec == null) return;

            bool rightDominant = _builder.Dominant == Handedness.Right;
            GestureResult off = rightDominant ? _rec.LeftResult : _rec.RightResult;
            GestureResult dom = rightDominant ? _rec.RightResult : _rec.LeftResult;

            bool offPalm = off.IsTracked && off.Gesture == GestureType.OpenPalm;
            bool domOpen = dom.IsTracked && dom.Gesture == GestureType.OpenPalm; // both open = Edit/Color switch, not ours

            if (!offPalm)
            {
                _toggleLock = false;
                _palmHold = 0f;
            }
            else if (domOpen)
            {
                _palmHold = 0f;
            }
            else if (!_toggleLock)
            {
                _palmHold += Time.unscaledDeltaTime;
                if (_palmHold >= _toggleHoldTime)
                {
                    _palmHold = 0f;
                    _toggleLock = true; // must drop the palm before it can toggle again
                    _cheatVisible = !_cheatVisible;
                    _ownToast = _cheatVisible ? "Controls shown." : "Controls hidden.";
                    _ownToastUntil = Time.unscaledTime + 2.0f;
                }
            }
        }

        private void UpdateTexts()
        {
            string status = _builder.StatusText;
            if (status != _lastStatus) { _lastStatus = status; _statusText.text = status; }

            string guide = _builder.GuideText;
            if (guide != _lastGuide) { _lastGuide = guide; _guideText.text = guide; }

            string toast = Time.unscaledTime < _ownToastUntil ? _ownToast : _builder.ToastText;
            if (toast != _lastToast) { _lastToast = toast; _toastText.text = toast; }

            string cheat = _cheatVisible ? CheatShown : CheatHidden;
            if (cheat != _lastCheat)
            {
                _lastCheat = cheat;
                _cheatText.text = cheat;
                _cheatText.fontSize = _cheatVisible ? 24f : 22f;
                _cheatText.color = _cheatVisible ? new Color(0.8f, 0.9f, 1f) : new Color(0.65f, 0.7f, 0.78f);
            }
        }

        private void UpdateBar()
        {
            float modeP = _builder.ModeHoldProgress;                       // left index finger held -> next mode
            float palmP = _toggleHoldTime > 0f ? Mathf.Clamp01(_palmHold / _toggleHoldTime) : 0f;
            float p = Mathf.Max(modeP, palmP);

            if (p > 0.05f)
            {
                _barBg.SetActive(true);
                _barFillImage.color = palmP >= modeP ? new Color(0.35f, 0.9f, 0.45f, 1f) : new Color(1f, 0.85f, 0.2f, 1f);
                _barFill.anchorMax = new Vector2(p, 1f);
            }
            else if (_barBg.activeSelf)
            {
                _barBg.SetActive(false);
            }
        }

        /// <summary>Floats in front of the user; only re-centres after the head has turned well away from it.</summary>
        private void Follow()
        {
            Transform ct = _cam.transform;
            float headYaw = ct.eulerAngles.y;

            if (!_placed)
            {
                _yaw = headYaw;
                _yawVel = 0f;
                _following = false;
                _placed = true;
            }

            float diff = Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw));
            if (diff > _followDeadZoneDeg) _following = true;
            else if (diff < 3f) _following = false;

            if (_following)
                _yaw = Mathf.SmoothDampAngle(_yaw, headYaw, ref _yawVel, _followSmoothTime);

            Vector3 flat = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 pos = ct.position + flat * _distance;
            pos.y = ct.position.y - _heightBelowEye;

            _root.transform.position = pos;
            // A world-space canvas reads correctly when its forward points away from the viewer.
            Vector3 away = pos - ct.position;
            if (away.sqrMagnitude > 1e-4f)
                _root.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
