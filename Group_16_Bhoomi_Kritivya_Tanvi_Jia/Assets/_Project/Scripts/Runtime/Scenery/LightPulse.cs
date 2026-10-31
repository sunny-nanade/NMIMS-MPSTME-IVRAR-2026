using UnityEngine;

namespace RescueSim.World
{
    /// <summary>Pulses or flickers a light's intensity (emergency beacons, failing lamps). Intensity only: no allocations.</summary>
    [RequireComponent(typeof(Light))]
    public class LightPulse : MonoBehaviour
    {
        public enum Mode { Pulse, Flicker, Strobe }
        [SerializeField] Mode mode = Mode.Pulse;
        [SerializeField, Min(0f)] float minIntensity = 0.3f;
        [SerializeField, Min(0f)] float maxIntensity = 3f;
        [SerializeField, Min(0.1f)] float speed = 2f;
        [SerializeField] float phase;

        Light _light;

        void Awake() { _light = GetComponent<Light>(); }

        void Update()
        {
            float t = Time.time * speed + phase;
            float k;
            switch (mode)
            {
                case Mode.Strobe: k = Mathf.Repeat(t, 1f) < 0.25f ? 1f : 0f; break;
                case Mode.Flicker: k = Mathf.PerlinNoise(t * 3f, phase) > 0.35f ? 1f : 0.15f; break;
                default: k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f); break;
            }
            _light.intensity = Mathf.Lerp(minIntensity, maxIntensity, k);
        }
    }
}
