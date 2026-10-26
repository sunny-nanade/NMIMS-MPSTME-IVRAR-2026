using UnityEngine;

namespace CSER
{
    /// Failing fluorescent tube: mostly steady, with short bursts of dips. Never goes fully dark and the steps are
    /// slow enough (>= 40 ms) not to strobe. Drives the real-time light(s) and the glow of the tube mesh together.
    public class FlickerLight : MonoBehaviour
    {
        public Light[] lights;
        public Renderer glow;
        public Color glowColor = new Color(1.5f, 1.6f, 1.8f);
        [Range(0.05f, 1f)] public float minLevel = 0.3f;

        float[] baseIntensity;
        Material glowMat;
        float next, level = 1f;
        int burstLeft;

        void Awake()
        {
            baseIntensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) baseIntensity[i] = lights[i].intensity;
            if (glow) glowMat = glow.material;
        }

        void Update()
        {
            if (Time.time >= next)
            {
                if (burstLeft > 0)
                {
                    burstLeft--;
                    level = Random.value < 0.35f ? 1f : Random.Range(minLevel, 0.9f);
                    next = Time.time + Random.Range(0.04f, 0.12f);
                }
                else
                {
                    level = 1f;
                    next = Time.time + Random.Range(1.2f, 4.5f);
                    burstLeft = Random.Range(4, 11);
                }
            }
            Apply(level);
        }

        void Apply(float l)
        {
            for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].intensity = baseIntensity[i] * l;
            if (glowMat) glowMat.SetColor("_EmissionColor", glowColor * l);
        }
    }
}
