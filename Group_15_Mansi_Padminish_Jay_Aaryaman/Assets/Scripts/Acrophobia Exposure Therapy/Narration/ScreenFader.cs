using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Acrophobia.Narration
{
    /// <summary>
    /// A full-screen fade used for interludes that happen off-camera.
    ///
    /// Separate from the teleport blink, which is a fast in-and-out with nothing held in
    /// between. Here the black is the point: it stands in for a whole scene - a bathroom,
    /// a kettle - that is never built, carried entirely by sound. Two audio clips replace
    /// a room.
    ///
    /// Holding black in VR is only tolerable if something is clearly happening. Silence
    /// behind a black screen reads as a crash, so an interlude with no audio still shows
    /// its subtitle, and the fade timings stay slow enough to read as deliberate.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 3f)] private float _defaultFadeSeconds = 0.8f;

        private Canvas _canvas;
        private Image _image;
        private float _alpha;

        public bool IsBlack => _alpha > 0.99f;

        public float Alpha => _alpha;

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("FadeCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;   // above the teleport blink
            _canvas.enabled = false;

            var imageGo = new GameObject("Black");
            imageGo.transform.SetParent(canvasGo.transform, false);

            _image = imageGo.AddComponent<Image>();
            _image.color = new Color(0f, 0f, 0f, 0f);
            _image.raycastTarget = false;

            var rt = _image.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public IEnumerator FadeTo(float target, float seconds = -1f)
        {
            Build();

            if (seconds < 0f) seconds = _defaultFadeSeconds;

            float from = _alpha;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds)));
                yield return null;
            }

            SetAlpha(target);
        }

        public void SetAlphaImmediate(float a)
        {
            Build();
            SetAlpha(a);
        }

        private void SetAlpha(float a)
        {
            _alpha = Mathf.Clamp01(a);

            if (_image != null)
            {
                Color c = _image.color;
                c.a = _alpha;
                _image.color = c;
            }

            if (_canvas != null)
            {
                _canvas.enabled = _alpha > 0.001f;
            }
        }
    }
}
