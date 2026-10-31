using RescueSim.Audio;
using RescueSim.Core;
using RescueSim.Interaction;
using RescueSim.Player;
using UnityEngine;

namespace RescueSim.Hazards
{
    /// <summary>
    /// A fire with an intensity from 1 (raging) to 0 (out). The extinguisher knocks intensity down; the fire slowly
    /// regrows when left alone, so the player has to commit to putting it out.
    /// </summary>
    public class FireHazard : Hazard
    {
        [Header("References")]
        [SerializeField] ParticleSystem flames;
        [SerializeField] ParticleSystem smoke;
        [SerializeField] Light fireLight;
        [SerializeField] ExposureSource exposure;
        [SerializeField] SpatialSound crackle;
        [SerializeField] MissionController mission;
        [SerializeField] PlayerVitals vitals;

        [Header("Electrical fires")]
        [Tooltip("If set, attacking this fire while the breaker is live is a mistake and gives a shock.")]
        [SerializeField] MainBreaker liveSource;

        [Header("Tuning")]
        [SerializeField, Min(0.1f)] float secondsOfSprayToExtinguish = 4f;
        [SerializeField, Range(0f, 0.2f)] float regrowthPerSecond = 0.03f;
        [SerializeField] float baseExposureRate = 0.16f;
        [SerializeField] float baseLightIntensity = 5f;

        float _flameEmission = 40f;
        float _smokeEmission = 20f;
        float _flicker;
        float _mistakeCooldown;

        public float Intensity { get; private set; } = 1f;

        void Awake()
        {
            if (flames != null) _flameEmission = flames.emission.rateOverTimeMultiplier;
            if (smoke != null) _smokeEmission = smoke.emission.rateOverTimeMultiplier;
        }

        void Start()
        {
            crackle?.PlayLoop(SpatialSound.Cue.Crackle);
            ApplyVisuals();
        }

        void Update()
        {
            if (!IsActive) return;
            _mistakeCooldown -= Time.deltaTime;

            if (Intensity < 1f)
            {
                Intensity = Mathf.Min(1f, Intensity + regrowthPerSecond * Time.deltaTime);
                ApplyVisuals();
            }

            if (fireLight != null)
            {
                _flicker -= Time.deltaTime;
                if (_flicker <= 0f)
                {
                    _flicker = Random.Range(0.04f, 0.14f);
                    fireLight.intensity = baseLightIntensity * Intensity * Random.Range(0.7f, 1.2f);
                }
            }
        }

        /// <summary>Applies extinguishing agent. <paramref name="seconds"/> is time-on-target.</summary>
        public void ApplyExtinguisher(float seconds)
        {
            if (!IsActive || seconds <= 0f) return;

            if (liveSource != null && liveSource.IsLive && _mistakeCooldown <= 0f)
            {
                _mistakeCooldown = 5f;
                vitals?.AddInstant(0.12f);
                mission?.ReportMistake("Live electrical fire. Isolate main power before spraying.");
            }

            Intensity = Mathf.Max(0f, Intensity - seconds / secondsOfSprayToExtinguish);
            ApplyVisuals();
            if (Intensity <= 0.001f) Neutralize();
        }

        void ApplyVisuals()
        {
            if (flames != null)
            {
                var em = flames.emission;
                em.rateOverTimeMultiplier = _flameEmission * Intensity;
                var main = flames.main;
                main.startSizeMultiplier = Mathf.Lerp(0.35f, 1f, Intensity);
            }
            if (smoke != null)
            {
                var em = smoke.emission;
                em.rateOverTimeMultiplier = _smokeEmission * Mathf.Lerp(0.4f, 1f, Intensity);
            }
            if (exposure != null) exposure.RatePerSecond = baseExposureRate * Intensity;
            if (crackle != null) crackle.Volume = 0.1f + 0.6f * Intensity;
        }

        public override void Neutralize()
        {
            if (!IsActive) return;
            Intensity = 0f;
            ApplyVisuals();
            if (flames != null) flames.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (smoke != null)
            {
                var em = smoke.emission; em.rateOverTimeMultiplier = 3f; // lingering wisps that die out
                smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (fireLight != null) fireLight.enabled = false;
            crackle?.Stop();
            RaiseNeutralized();
        }
    }
}
