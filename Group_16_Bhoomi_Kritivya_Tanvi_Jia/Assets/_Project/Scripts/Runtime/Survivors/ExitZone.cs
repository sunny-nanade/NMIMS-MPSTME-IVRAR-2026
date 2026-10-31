using UnityEngine;

namespace RescueSim.Survivors
{
    /// <summary>
    /// Axis-aligned (in local space) evacuation volume. No physics needed, just a containment test.
    /// While a carried casualty is inside, the zone's markers glow brighter and a light pulses so the player
    /// knows that releasing the grip here will deliver them.
    /// </summary>
    public class ExitZone : MonoBehaviour
    {
        [SerializeField] Vector3 size = new Vector3(6f, 3f, 5f);

        public Vector3 Size { get => size; set => size = value; }
        /// <summary>True while a carried casualty is inside (feedback only; delivery is decided by Survivor).</summary>
        public bool CarrierInside { get; private set; }

        static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorLegacyId = Shader.PropertyToID("_Color");
        static readonly Color Idle = new Color(0.2f, 0.9f, 0.4f, 1f);
        static readonly Color Active = new Color(0.75f, 1f, 0.8f, 1f);

        Renderer[] _markers;
        Light[] _lights;
        float[] _lightBase;
        MaterialPropertyBlock _mpb;

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 p = transform.InverseTransformPoint(worldPosition);
            Vector3 h = size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        /// <summary>Highlights the zone while a carried casualty is inside it.</summary>
        public void SetCarrierInside(bool inside)
        {
            if (CarrierInside == inside) return;
            CarrierInside = inside;
            Cache();
            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in _markers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, inside ? Active : Idle);
                _mpb.SetColor(ColorLegacyId, inside ? Active : Idle);
                r.SetPropertyBlock(_mpb);
            }
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null) _lights[i].intensity = inside ? _lightBase[i] * 2f : _lightBase[i];
        }

        void Cache()
        {
            if (_markers != null) return;
            _markers = GetComponentsInChildren<MeshRenderer>(true);
            _lights = GetComponentsInChildren<Light>(true);
            _lightBase = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; i++) _lightBase[i] = _lights[i].intensity;
        }

        void OnDisable() { if (CarrierInside) SetCarrierInside(false); }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
