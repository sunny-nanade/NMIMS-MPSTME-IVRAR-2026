using UnityEngine;

namespace RescueSim.Survivors
{
    /// <summary>
    /// Per-casualty look on top of the shared human avatars: a slight build change (height/width) and a layer of dust and soot
    /// so the people read as having been through a disaster. Deterministic from the object's name, so reloads and tests are stable.
    /// Uses a MaterialPropertyBlock only (no material instances, so the avatars still batch/share materials on Quest).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CasualtyAppearance : MonoBehaviour
    {
        [SerializeField] Transform model;
        [SerializeField] string avatar = "";
        [SerializeField, Range(0f, 0.6f)] float dust = 0.22f;

        static readonly Color Soot = new Color(0.36f, 0.33f, 0.30f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public int Variant { get; private set; }
        public Transform Model => model;
        /// <summary>Name of the avatar (Microsoft Rocketbox) this casualty wears.</summary>
        public string Avatar => avatar;
        public bool HasHumanBody => model != null && model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        public void Configure(Transform modelRoot, string avatarName) { model = modelRoot; avatar = avatarName; }

        void Awake() { Apply(); }

        /// <summary>Stable variant index for a name (also used by tests).</summary>
        public static int VariantFor(string key)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in key ?? "") h = h * 31 + c;
                return (h & 0x7fffffff) % 8;
            }
        }

        public void Apply()
        {
            if (model == null) return;
            Variant = VariantFor(gameObject.name);
            int h = Variant * 7919 + 13;
            float height = Mathf.Lerp(0.96f, 1.05f, (h % 100) / 100f);
            float width = Mathf.Lerp(0.96f, 1.06f, ((h / 3) % 100) / 100f);
            model.localScale = new Vector3(width, height, width);

            Color tint = Color.Lerp(Color.white, Soot, dust + 0.04f * (Variant % 3));
            var mpb = new MaterialPropertyBlock();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, tint);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
