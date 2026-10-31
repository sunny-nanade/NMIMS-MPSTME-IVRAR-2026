using RescueSim.Audio;
using RescueSim.Core;
using RescueSim.Interaction;
using RescueSim.Player;
using UnityEngine;

namespace RescueSim.Hazards
{
    /// <summary>
    /// A sparking, live electrical panel. Neutralised when main power is cut. Getting too close while live
    /// gives a shock (exposure spike) and counts as a mistake.
    /// </summary>
    public class ElectricalHazard : Hazard
    {
        [SerializeField] MainBreaker breaker;
        [SerializeField] MissionController mission;
        [SerializeField] PlayerVitals vitals;
        [SerializeField] ParticleSystem sparks;
        [SerializeField] Light arcLight;
        [SerializeField] SpatialSound buzz;
        [SerializeField, Min(0.2f)] float shockRadius = 1.1f;
        [SerializeField, Range(0f, 1f)] float shockAmount = 0.2f;
        [SerializeField, Min(0.5f)] float shockCooldown = 4f;

        float _cooldown;
        float _flicker;

        public bool IsLive => IsActive;

        void OnEnable()
        {
            if (breaker == null) return;
            breaker.PowerCut += Neutralize;
            if (!breaker.IsLive) Neutralize();
        }

        void OnDisable()
        {
            if (breaker != null) breaker.PowerCut -= Neutralize;
        }

        void Start()
        {
            if (IsActive) buzz?.PlayLoop(SpatialSound.Cue.ElectricBuzz);
        }

        void Update()
        {
            if (!IsActive) return;

            if (arcLight != null)
            {
                _flicker -= Time.deltaTime;
                if (_flicker <= 0f)
                {
                    _flicker = Random.Range(0.03f, 0.25f);
                    arcLight.intensity = Random.value > 0.35f ? Random.Range(1.5f, 4f) : 0f;
                }
            }

            _cooldown -= Time.deltaTime;
            if (vitals != null && vitals.Head != null && _cooldown <= 0f && mission != null && mission.State == MissionState.Active)
            {
                if (Vector3.Distance(vitals.Head.position, transform.position) < shockRadius)
                {
                    _cooldown = shockCooldown;
                    vitals.AddInstant(shockAmount);
                    mission.ReportMistake("Too close to live electrics. Cut main power first.");
                    buzz?.PlayOneShot(SpatialSound.Cue.Warning);
                }
            }
        }

        public override void Neutralize()
        {
            if (!IsActive) return;
            if (sparks != null) sparks.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (arcLight != null) arcLight.enabled = false;
            buzz?.Stop();
            RaiseNeutralized();
        }
    }
}
