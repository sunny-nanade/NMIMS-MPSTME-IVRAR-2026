using UnityEngine;

namespace RescueSim.Player
{
    /// <summary>
    /// Narrows the field of view with a dark ring while the player is moving or turning quickly, which reduces
    /// vection-induced discomfort. Driven by head-pose speed so it needs no locomotion-provider coupling.
    /// </summary>
    public class ComfortVignette : MonoBehaviour
    {
        [SerializeField] Renderer ring;
        [SerializeField] Transform rigRoot;
        [SerializeField, Min(0.1f)] float fullEffectSpeed = 2.0f;
        [SerializeField, Min(10f)] float fullEffectTurnDegPerSec = 120f;
        [SerializeField, Min(0.5f)] float smoothing = 6f;
        [SerializeField, Range(0f, 1f)] float maxAlpha = 0.9f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Aperture = Shader.PropertyToID("_Aperture");
        MaterialPropertyBlock _mpb;
        Vector3 _lastPos;
        float _lastYaw;
        float _level;

        public bool Enabled { get; set; } = true;

        void Awake() { _mpb = new MaterialPropertyBlock(); }

        void Start()
        {
            if (rigRoot != null) { _lastPos = rigRoot.position; _lastYaw = rigRoot.eulerAngles.y; }
        }

        void LateUpdate()
        {
            if (ring == null || rigRoot == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector3 p = rigRoot.position;
            float yaw = rigRoot.eulerAngles.y;
            float speed = new Vector2(p.x - _lastPos.x, p.z - _lastPos.z).magnitude / dt;
            float turn = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) / dt;
            _lastPos = p; _lastYaw = yaw;

            float target = Enabled ? Mathf.Clamp01(Mathf.Max(speed / fullEffectSpeed, turn / fullEffectTurnDegPerSec)) : 0f;
            _level = Mathf.Lerp(_level, target, 1f - Mathf.Exp(-smoothing * dt));

            bool visible = _level > 0.01f;
            if (ring.enabled != visible) ring.enabled = visible;
            if (!visible) return;
            ring.GetPropertyBlock(_mpb);
            _mpb.SetFloat(Aperture, Mathf.Lerp(1f, 0.55f, _level));
            _mpb.SetColor(BaseColor, new Color(0f, 0f, 0f, maxAlpha * _level));
            ring.SetPropertyBlock(_mpb);
        }
    }
}
