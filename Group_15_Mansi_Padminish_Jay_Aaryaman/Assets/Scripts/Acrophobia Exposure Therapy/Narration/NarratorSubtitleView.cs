using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Acrophobia.GazeGeometry;

namespace Acrophobia.Narration
{
    /// <summary>
    /// Displays narrator lines as subtitles on a world-space panel.
    ///
    /// Sits low in the field of view and follows loosely, so it reads as subtitling
    /// rather than as a HUD element bolted to the face. It is deliberately plainer than
    /// FloatingInstructionHUD: no borders, no status colours, no progress bar. The voice
    /// is meant to be the thing carrying instruction, and a panel competing with it would
    /// undo the point of having a narrator at all.
    ///
    /// Text fades rather than cutting, because a hard pop reads as a bug and a fade reads
    /// as speech ending.
    /// </summary>
    public class NarratorSubtitleView : MonoBehaviour
    {
        [Tooltip("Metres in front of the eyes.")]
        [SerializeField, Range(0.8f, 3f)] private float _followDistance = 1.6f;

        [Tooltip("Metres below eye level. Negative sits it under the natural sightline.")]
        [SerializeField, Range(-1f, 0.2f)] private float _verticalOffset = -0.42f;

        [SerializeField, Range(0.5f, 8f)] private float _positionFollowSpeed = 2.2f;
        [SerializeField, Range(0.5f, 8f)] private float _rotationFollowSpeed = 2.6f;
        [SerializeField, Range(0.05f, 1.5f)] private float _fadeSeconds = 0.35f;

        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private TextMeshProUGUI _text;
        [SerializeField] private CanvasGroup _group;

        private string _pending;
        private float _targetAlpha;

        private static readonly Color SubtitleColour = new Color(0.94f, 0.93f, 0.90f, 1f);

        public bool IsShowing => _group != null && _group.alpha > 0.01f;

        private void Awake()
        {
            ResolveReferences();
            EnsureUIHierarchy();
            if (_group != null) _group.alpha = 0f;
        }

        public void ResolveReferences()
        {
            if (_centerEyeAnchor == null)
            {
                var gaze = FindAnyObjectByType<GazeAvoidanceDetector>();
                if (gaze != null) _centerEyeAnchor = gaze.CenterEyeAnchor;
                if (_centerEyeAnchor == null && Camera.main != null) _centerEyeAnchor = Camera.main.transform;
            }
        }

        public void Show(string line)
        {
            // Narrator removed per user request: keep subtitles completely hidden
            _pending = line;
            _targetAlpha = 0f;
            if (_group != null) _group.alpha = 0f;
            if (_canvas != null) _canvas.enabled = false;
        }

        public void Hide()
        {
            _targetAlpha = 0f;
        }

        private void Update()
        {
            if (_centerEyeAnchor == null)
            {
                ResolveReferences();
                if (_centerEyeAnchor == null) return;
            }

            if (_group != null)
            {
                float step = Time.deltaTime / Mathf.Max(0.01f, _fadeSeconds);
                _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, step);
                if (_canvas != null) _canvas.enabled = _group.alpha > 0.01f;
            }

            transform.position = Vector3.Lerp(transform.position, ComputePosition(),
                Time.deltaTime * _positionFollowSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, ComputeRotation(),
                Time.deltaTime * _rotationFollowSpeed);
        }

        private Vector3 ComputePosition()
        {
            Vector3 fwd = _centerEyeAnchor.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            return _centerEyeAnchor.position + fwd * _followDistance + Vector3.up * _verticalOffset;
        }

        private Quaternion ComputeRotation()
        {
            Vector3 away = transform.position - _centerEyeAnchor.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = _centerEyeAnchor.forward;
            return Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        private void SnapToView()
        {
            transform.position = ComputePosition();
            transform.rotation = ComputeRotation();
        }

        /// <summary>
        /// Builds the panel if unassigned, matching the world-space convention used
        /// elsewhere: authored at 1000 units per metre then scaled by 0.001.
        /// </summary>
        public void EnsureUIHierarchy()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            var rect = _canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1100f, 230f);
            rect.localScale = Vector3.one * 0.001f;     // 1.10 m x 0.23 m

            if (GetComponent<CanvasScaler>() == null) gameObject.AddComponent<CanvasScaler>();

            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            if (_text == null)
            {
                Transform existing = transform.Find("SubtitleText");
                GameObject obj = existing != null ? existing.gameObject : new GameObject("SubtitleText");
                obj.transform.SetParent(transform, false);

                var tr = obj.GetComponent<RectTransform>();
                if (tr == null) tr = obj.AddComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = Vector2.zero;
                tr.offsetMax = Vector2.zero;

                _text = obj.GetComponent<TextMeshProUGUI>();
                if (_text == null) _text = obj.AddComponent<TextMeshProUGUI>();

                _text.fontSize = 46f;
                _text.alignment = TextAlignmentOptions.Center;
                _text.color = SubtitleColour;
                _text.richText = false;
                _text.textWrappingMode = TextWrappingModes.Normal;

                // A soft outline keeps the text legible against both sky and concrete
                // without needing a panel background behind it.
                _text.fontMaterial.EnableKeyword("OUTLINE_ON");
                _text.outlineColor = new Color32(0, 0, 0, 190);
                _text.outlineWidth = 0.18f;
            }

            if (!string.IsNullOrEmpty(_pending)) _text.text = _pending;
        }
    }
}
