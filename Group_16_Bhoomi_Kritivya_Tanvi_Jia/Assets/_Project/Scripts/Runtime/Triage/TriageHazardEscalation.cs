using UnityEngine;

namespace RescueSim.Triage
{
    /// <summary>
    /// Cosmetic "dynamic industrial hazard": while a session runs, fires, smoke and flicker intensify in steps
    /// (level 0..maxLevel, one step every secondsPerLevel). The level is written to telemetry. It never harms the player or casualties.
    /// </summary>
    public class TriageHazardEscalation : MonoBehaviour
    {
        [SerializeField, Min(5f)] float secondsPerLevel = 60f;
        [SerializeField, Range(1, 6)] int maxLevel = 3;
        [SerializeField] ParticleSystem[] smoke;
        [SerializeField] Light[] fireLights;

        float[] _baseRates, _baseIntensity;
        float _startTime;
        bool _running;

        public int Level { get; private set; }
        public int MaxLevel => maxLevel;
        public bool IsRunning => _running;

        public void Configure(ParticleSystem[] s, Light[] l, float secPerLevel, int max) { smoke = s; fireLights = l; secondsPerLevel = secPerLevel; maxLevel = max; Cache(); }

        void Awake() { Cache(); }

        void Cache()
        {
            smoke = smoke ?? new ParticleSystem[0]; fireLights = fireLights ?? new Light[0];
            _baseRates = new float[smoke.Length];
            for (int i = 0; i < smoke.Length; i++) _baseRates[i] = smoke[i] != null ? smoke[i].emission.rateOverTimeMultiplier : 0f;
            _baseIntensity = new float[fireLights.Length];
            for (int i = 0; i < fireLights.Length; i++) _baseIntensity[i] = fireLights[i] != null ? fireLights[i].intensity : 0f;
        }

        public void BeginRun() { _startTime = Time.time; _running = true; SetLevel(0); }
        public void StopRun() { _running = false; }
        public void ResetRun() { _running = false; SetLevel(0); }

        void Update()
        {
            if (_running)
            {
                int lvl = Mathf.Min(maxLevel, Mathf.FloorToInt((Time.time - _startTime) / secondsPerLevel));
                if (lvl != Level) SetLevel(lvl);
            }
            for (int i = 0; i < fireLights.Length; i++)
            {
                if (fireLights[i] == null) continue;
                float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, i * 7.3f);
                fireLights[i].intensity = _baseIntensity[i] * (1f + 0.35f * Level) * flicker;
            }
        }

        void SetLevel(int lvl)
        {
            Level = lvl;
            for (int i = 0; i < smoke.Length; i++)
            {
                if (smoke[i] == null) continue;
                var em = smoke[i].emission; em.rateOverTimeMultiplier = _baseRates[i] * (1f + 0.6f * lvl);
            }
        }
    }
}
