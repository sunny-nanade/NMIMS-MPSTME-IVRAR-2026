using UnityEngine;

namespace RescueSim.World
{
    /// <summary>Cheap animated water: scrolls the surface texture and bobs the plane slightly. No simulation, no per-frame allocations.</summary>
    public class WaterSurface : MonoBehaviour
    {
        [SerializeField] Renderer target;
        [SerializeField] Vector2 scrollA = new Vector2(0.015f, 0.01f);
        [SerializeField, Min(0f)] float bobHeight = 0.02f;
        [SerializeField, Min(0f)] float bobSpeed = 0.8f;

        Material _mat;
        Vector3 _basePos;
        Vector2 _offset;
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        void Start()
        {
            if (target == null) target = GetComponent<Renderer>();
            if (target != null) _mat = target.material; // one instance per water plane
            _basePos = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _offset += scrollA * dt;
            if (_mat != null) _mat.SetTextureOffset(BaseMapId, _offset);
            transform.position = _basePos + Vector3.up * (bobHeight * Mathf.Sin(Time.time * bobSpeed));
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
