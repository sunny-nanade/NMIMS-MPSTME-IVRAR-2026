using RescueSim.Player;
using UnityEngine;

namespace RescueSim.Hazards
{
    /// <summary>A spherical area that raises exposure while its hazard is active. Strength fades towards the edge.</summary>
    public class ExposureSource : MonoBehaviour
    {
        [SerializeField] Hazard hazard;
        [SerializeField] PlayerVitals vitals;
        [SerializeField, Min(0.1f)] float radius = 4f;
        [Tooltip("Exposure gained per second at the centre.")]
        [SerializeField, Min(0f)] float ratePerSecond = 0.12f;

        public float Radius { get => radius; set => radius = Mathf.Max(0.1f, value); }
        public float RatePerSecond { get => ratePerSecond; set => ratePerSecond = Mathf.Max(0f, value); }
        public Hazard Hazard { get => hazard; set => hazard = value; }

        public void Bind(PlayerVitals v)
        {
            if (vitals != null) vitals.Unregister(this);
            vitals = v;
            if (isActiveAndEnabled && vitals != null) vitals.Register(this);
        }

        void OnEnable() { if (vitals != null) vitals.Register(this); }
        void OnDisable() { if (vitals != null) vitals.Unregister(this); }

        public float RateAt(Vector3 worldPosition)
        {
            if (hazard != null && !hazard.IsActive) return 0f;
            float d = Vector3.Distance(worldPosition, transform.position);
            if (d >= radius) return 0f;
            float t = 1f - d / radius;
            return ratePerSecond * t * t * (3f - 2f * t); // smoothstep falloff
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
