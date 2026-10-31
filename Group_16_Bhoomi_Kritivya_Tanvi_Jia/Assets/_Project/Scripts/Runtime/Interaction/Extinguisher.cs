using System.Collections.Generic;
using RescueSim.Audio;
using RescueSim.Hazards;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Interaction
{
    /// <summary>
    /// Grab the extinguisher and squeeze the trigger to spray. Fires inside the spray cone (range + angle) lose intensity.
    /// Uses a geometric cone test rather than physics layers so it behaves identically in the editor, simulator and headset.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Extinguisher : MonoBehaviour
    {
        [SerializeField] Transform nozzle;
        [SerializeField] ParticleSystem sprayParticles;
        [SerializeField] SpatialSound sound;
        [SerializeField] List<FireHazard> fires = new List<FireHazard>();
        [SerializeField, Min(0.5f)] float range = 3.5f;
        [SerializeField, Range(5f, 60f)] float coneAngle = 24f;
        [SerializeField, Min(1f)] float totalSprayDuration = 22f;

        XRGrabInteractable _grab;
        bool _triggerHeld;

        public float Remaining { get; private set; }
        public float RemainingFraction => totalSprayDuration > 0f ? Remaining / totalSprayDuration : 0f;
        public bool IsSpraying { get; private set; }

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            Remaining = totalSprayDuration;
        }

        void OnEnable()
        {
            _grab.activated.AddListener(OnActivated);
            _grab.deactivated.AddListener(OnDeactivated);
            _grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            _grab.activated.RemoveListener(OnActivated);
            _grab.deactivated.RemoveListener(OnDeactivated);
            _grab.selectExited.RemoveListener(OnReleased);
            SetSpraying(false);
        }

        public void SetFires(IEnumerable<FireHazard> list) { fires = new List<FireHazard>(list); }

        void OnActivated(ActivateEventArgs _) { _triggerHeld = true; }
        void OnDeactivated(DeactivateEventArgs _) { _triggerHeld = false; }
        void OnReleased(SelectExitEventArgs _) { _triggerHeld = false; }

        void Update()
        {
            bool shouldSpray = _triggerHeld && Remaining > 0f;
            SetSpraying(shouldSpray);
            if (!shouldSpray) return;

            Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
            SprayStep(Time.deltaTime);
        }

        /// <summary>Applies one step of spray to all fires in the cone. Public for tests.</summary>
        public void SprayStep(float dt)
        {
            Transform n = nozzle != null ? nozzle : transform;
            Vector3 origin = n.position;
            Vector3 forward = n.forward;
            for (int i = 0; i < fires.Count; i++)
            {
                var f = fires[i];
                if (f == null || !f.IsActive) continue;
                Vector3 to = f.transform.position - origin;
                float dist = to.magnitude;
                if (dist > range) continue;
                if (dist > 0.01f && Vector3.Angle(forward, to) > coneAngle) continue;
                f.ApplyExtinguisher(dt);
            }
        }

        void SetSpraying(bool on)
        {
            if (IsSpraying == on) return;
            IsSpraying = on;
            if (sprayParticles != null)
            {
                if (on) sprayParticles.Play(); else sprayParticles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (sound != null)
            {
                if (on) sound.PlayLoop(SpatialSound.Cue.Spray); else sound.Stop();
            }
        }
    }
}
