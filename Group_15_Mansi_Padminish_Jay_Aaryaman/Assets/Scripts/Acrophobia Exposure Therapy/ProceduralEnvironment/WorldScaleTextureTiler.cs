using UnityEngine;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Sets each renderer's texture tiling from its real-world size, so texel density is
    /// consistent across every surface regardless of how the primitive was scaled.
    ///
    /// Unity's built-in cube maps UV 0-1 across each face whatever the object's scale, so
    /// one shared material gives a 5 m wall and a 0.2 m window jamb completely different
    /// texel density - the jamb shows a single enormous smear of the texture. With a room
    /// built entirely from scaled primitives that affects nearly every piece.
    ///
    /// Tiling is computed as surface size / the texture's stated physical size, which is
    /// the same arithmetic the texture README asks authors to do by hand. Doing it from
    /// the transform means it cannot drift when geometry is resized, and it makes the
    /// claim that texture scale matches scene metres actually true rather than aspirational
    /// - which matters here, because perceived surface scale feeds the familiar-size cues
    /// the participant uses to judge how high up they are.
    ///
    /// Applied through a MaterialPropertyBlock so no per-object material instances are
    /// created. That does opt these renderers out of SRP batching, which is an acceptable
    /// trade for a few dozen interior pieces.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WorldScaleTextureTiler : MonoBehaviour
    {
        private static readonly int TilingId = Shader.PropertyToID("_Tiling");

        [Tooltip("Real-world size of one texture repeat, in metres, from the Poly Haven asset page.")]
        [SerializeField] private float _texturePhysicalSize = 2.0f;

        [Tooltip("Extra multiplier if a surface should read denser or coarser than true scale.")]
        [SerializeField, Range(0.25f, 4f)] private float _densityMultiplier = 1f;

        [Tooltip("Recompute every frame. Only needed if the object is resized at runtime.")]
        [SerializeField] private bool _continuousUpdate = false;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        public float TexturePhysicalSize
        {
            get => _texturePhysicalSize;
            set { _texturePhysicalSize = value; Apply(); }
        }

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            if (_continuousUpdate)
            {
                Apply();
            }
        }

        /// <summary>
        /// Recomputes and pushes the tiling. Safe to call from editor tooling.
        /// </summary>
        public void Apply()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer == null) return;

            if (_block == null) _block = new MaterialPropertyBlock();

            float size = Mathf.Max(0.01f, _texturePhysicalSize);
            Vector3 scale = transform.lossyScale;

            // The two largest axes are the face the texture is read across: for a floor
            // that is width and depth, for a wall width and height.
            float a = Mathf.Abs(scale.x);
            float b = Mathf.Abs(scale.y);
            float c = Mathf.Abs(scale.z);

            float largest = Mathf.Max(a, Mathf.Max(b, c));
            float smallest = Mathf.Min(a, Mathf.Min(b, c));
            float middle = a + b + c - largest - smallest;

            Vector2 tiling = new Vector2(
                largest / size * _densityMultiplier,
                middle / size * _densityMultiplier);

            _renderer.GetPropertyBlock(_block);
            _block.SetVector(TilingId, new Vector4(tiling.x, tiling.y, 0f, 0f));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
