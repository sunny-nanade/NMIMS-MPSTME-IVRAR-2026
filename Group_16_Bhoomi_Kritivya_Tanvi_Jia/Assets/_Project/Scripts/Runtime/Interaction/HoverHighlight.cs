using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Interaction
{
    /// <summary>
    /// Gives an interactable a pulsing emissive glow while it is hovered, so the player can tell what is usable.
    /// Works on URP Lit materials via per-renderer property blocks (no material instancing, no GC at runtime).
    /// </summary>
    [RequireComponent(typeof(XRBaseInteractable))]
    public class HoverHighlight : MonoBehaviour
    {
        [SerializeField] Color glowColor = new Color(0.2f, 0.85f, 1f);
        [SerializeField, Min(0f)] float intensity = 1.6f;
        [SerializeField, Min(0f)] float pulseSpeed = 5f;
        [SerializeField] Renderer[] renderers;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        XRBaseInteractable _interactable;
        MaterialPropertyBlock _block;
        int _hoverCount;
        bool _lit;

        void Awake()
        {
            _interactable = GetComponent<XRBaseInteractable>();
            _block = new MaterialPropertyBlock();
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
        }

        void OnEnable()
        {
            _interactable.hoverEntered.AddListener(OnHoverEntered);
            _interactable.hoverExited.AddListener(OnHoverExited);
        }

        void OnDisable()
        {
            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
            _interactable.hoverExited.RemoveListener(OnHoverExited);
            _hoverCount = 0;
            Apply(Color.black);
            _lit = false;
        }

        void OnHoverEntered(HoverEnterEventArgs _) { _hoverCount++; }
        void OnHoverExited(HoverExitEventArgs _) { _hoverCount = Mathf.Max(0, _hoverCount - 1); }

        void Update()
        {
            bool want = _hoverCount > 0;
            if (want)
            {
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * pulseSpeed);
                Apply(glowColor * (intensity * pulse));
                _lit = true;
            }
            else if (_lit)
            {
                Apply(Color.black);
                _lit = false;
            }
        }

        void Apply(Color emission)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(EmissionId, emission);
                r.SetPropertyBlock(_block);
                // Property blocks only take effect if the material has emission keyword enabled; the builder enables it
                // on the shared materials of interactive props (see SceneBuilder.EnableEmission).
            }
        }
    }
}
