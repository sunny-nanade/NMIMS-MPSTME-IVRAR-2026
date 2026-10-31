using RescueSim.Player;
using UnityEngine;

namespace RescueSim.UI
{
    /// <summary>Tints the view with a smoky haze as exposure rises, and drives screen fades. Static (no head-bob or shake).</summary>
    public class ExposureOverlay : MonoBehaviour
    {
        [SerializeField] Renderer haze;
        [SerializeField] Renderer fader;
        [SerializeField] PlayerVitals vitals;
        [SerializeField] Color hazeColor = new Color(0.18f, 0.1f, 0.05f, 1f);
        [SerializeField, Range(0f, 1f)] float maxHazeAlpha = 0.75f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock _mpb;
        float _fade = 1f;
        float _fadeTarget;
        float _fadeSpeed = 1.5f;

        public void Configure(Renderer hazeRenderer, Renderer faderRenderer, PlayerVitals v) { haze = hazeRenderer; fader = faderRenderer; vitals = v; }

        void Awake() { _mpb = new MaterialPropertyBlock(); }

        void Start()
        {
            // Every scene starts black and fades in.
            _fade = 1f; _fadeTarget = 0f; _fadeSpeed = 1.2f;
        }

        public void FadeTo(float alpha, float seconds)
        {
            _fadeTarget = Mathf.Clamp01(alpha);
            _fadeSpeed = seconds > 0.01f ? 1f / seconds : 100f;
        }

        public bool IsFadedOut => _fade >= 0.99f;

        void LateUpdate()
        {
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, _fadeSpeed * Time.unscaledDeltaTime);
            SetAlpha(fader, Color.black, _fade);

            float e = vitals != null ? vitals.Exposure : 0f;
            // Ease in so low exposure is barely visible.
            float a = maxHazeAlpha * e * e;
            SetAlpha(haze, hazeColor, a);
        }

        void SetAlpha(Renderer r, Color c, float a)
        {
            if (r == null) return;
            bool visible = a > 0.005f;
            if (r.enabled != visible) r.enabled = visible;
            if (!visible) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, new Color(c.r, c.g, c.b, a));
            r.SetPropertyBlock(_mpb);
        }
    }
}
