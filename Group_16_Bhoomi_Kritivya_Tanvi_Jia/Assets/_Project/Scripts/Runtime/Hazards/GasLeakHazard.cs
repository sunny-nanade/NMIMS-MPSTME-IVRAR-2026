using RescueSim.Audio;
using UnityEngine;

namespace RescueSim.Hazards
{
    /// <summary>A leaking gas cylinder. Pressure (and the visible cloud / exposure strength) drops as the valve is closed.</summary>
    public class GasLeakHazard : Hazard
    {
        [SerializeField] ParticleSystem cloud;
        [SerializeField] ExposureSource exposure;
        [SerializeField] SpatialSound hiss;
        [SerializeField] float baseExposureRate = 0.14f;

        float _baseEmission = 30f;
        float _pressure = 1f;

        public float Pressure => _pressure;

        void Awake()
        {
            if (cloud != null) _baseEmission = cloud.emission.rateOverTimeMultiplier;
        }

        void Start()
        {
            hiss?.PlayLoop(SpatialSound.Cue.Hiss);
            Apply();
        }

        /// <summary>Sets leak strength 0..1 (1 = fully open).</summary>
        public void SetPressure(float p)
        {
            if (!IsActive) return;
            _pressure = Mathf.Clamp01(p);
            Apply();
        }

        void Apply()
        {
            if (cloud != null)
            {
                var em = cloud.emission;
                em.rateOverTimeMultiplier = _baseEmission * _pressure;
            }
            if (exposure != null) exposure.RatePerSecond = baseExposureRate * _pressure;
            if (hiss != null) hiss.Volume = 0.15f + 0.55f * _pressure;
        }

        public override void Neutralize()
        {
            if (!IsActive) return;
            _pressure = 0f;
            Apply();
            if (cloud != null) cloud.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            hiss?.Stop();
            RaiseNeutralized();
        }
    }
}
