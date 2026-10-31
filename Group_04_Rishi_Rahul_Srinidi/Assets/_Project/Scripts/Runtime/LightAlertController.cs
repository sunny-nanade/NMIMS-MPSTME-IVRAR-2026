using System;
using UnityEngine;

namespace CSER
{
    /// Add to any Light (and optionally emissive renderers) to make it follow the global alert state.
    /// Colour / intensity / pulse rate are configurable per state; changes blend smoothly. Pulsing is a sine wave
    /// (never hard on/off), capped at 3 Hz, and turns into a steady colour when "reduce flashing" is on.
    public class LightAlertController : MonoBehaviour
    {
        [Serializable]
        public class Style
        {
            public Color color = Color.white;
            public float intensity = 1f;
            [Range(0f, 3f)] public float pulseHz;
            [Range(0f, 1f)] public float pulseDepth = 0.5f;
        }

        public Light[] lights;
        [Tooltip("Optional emissive meshes (strobe covers, lamp glass) that follow the same colour.")]
        public Renderer[] emissiveRenderers;
        [Tooltip("Emission multiplier applied to the state colour on emissive renderers.")]
        public float emissionScale = 1.5f;
        [Tooltip("Use the light's authored colour / intensity as the NORMAL state.")]
        public bool captureNormalFromLight = true;
        public Style normal = new Style();
        public Style high = new Style { color = new Color(1f, 0.55f, 0.1f), intensity = 1.6f, pulseHz = 1.2f, pulseDepth = 0.55f };
        public Style critical = new Style { color = new Color(1f, 0.1f, 0.08f), intensity = 2.2f, pulseHz = 2.5f, pulseDepth = 0.7f };
        public float blendSpeed = 3f;

        MaterialPropertyBlock block;
        Color curColor;
        float curIntensity, curHz, curDepth, phase;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>(true);
            if (captureNormalFromLight && lights.Length > 0 && lights[0])
            {
                normal.color = lights[0].color;
                normal.intensity = lights[0].intensity;
            }
            curColor = normal.color;
            curIntensity = normal.intensity;
            block = new MaterialPropertyBlock();
        }

        void OnEnable() => AlertStateManager.Register(this);
        void OnDisable() => AlertStateManager.Unregister(this);

        void Update()
        {
            var mgr = AlertStateManager.I;
            var style = mgr ? Pick(mgr.State) : normal;
            bool steady = mgr && mgr.ReduceFlashing;
            float k = 1 - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            curColor = Color.Lerp(curColor, style.color, k);
            curIntensity = Mathf.Lerp(curIntensity, style.intensity, k);
            curHz = Mathf.Lerp(curHz, steady ? 0f : Mathf.Min(style.pulseHz, 3f), k);
            curDepth = Mathf.Lerp(curDepth, steady ? 0f : style.pulseDepth, k);

            phase += curHz * Time.unscaledDeltaTime;
            float pulse = 1f - curDepth * (0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f));
            float level = curIntensity * pulse;
            foreach (var l in lights)
            {
                if (!l) continue;
                l.color = curColor;
                l.intensity = level;
            }
            if (emissiveRenderers == null) return;
            var emit = curColor * (emissionScale * pulse);
            foreach (var r in emissiveRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, emit);
                r.SetPropertyBlock(block);
            }
        }

        Style Pick(AlertState s)
        {
            switch (s)
            {
                case AlertState.High: return high;
                case AlertState.Critical: return critical;
                default: return normal;   // Normal and Clear both settle back to the authored look
            }
        }
    }
}
