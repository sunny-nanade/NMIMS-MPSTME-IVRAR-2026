using TMPro;
using UnityEngine;

namespace RescueSim.Triage
{
    /// <summary>Scene object for one casualty: identifier, findings and the visuals that show tag state.</summary>
    public class CasualtyProfile : MonoBehaviour
    {
        [SerializeField] string casualtyId = "C01";
        [SerializeField] VitalSigns vitals = new VitalSigns();
        [SerializeField, TextArea] string description = "";
        [SerializeField] Renderer tagRenderer;
        [SerializeField] TMP_Text label;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock _block;
        TriageCategory _tag;
        bool _selected;

        public string Id => casualtyId;
        public VitalSigns Vitals => vitals;
        public string Description => description;
        public TriageCategory Tag => _tag;

        public void Configure(string id, VitalSigns v, string desc) { casualtyId = id; vitals = v; description = desc; RefreshVisual(); }
        public void SetVisualRefs(Renderer tag, TMP_Text lbl) { tagRenderer = tag; label = lbl; }
        public CasualtyCase ToCase() => new CasualtyCase(casualtyId, vitals.Clone(), description);

        public static Color ColorFor(TriageCategory c)
        {
            switch (c)
            {
                case TriageCategory.Green: return new Color(0.15f, 0.8f, 0.3f);
                case TriageCategory.Yellow: return new Color(1f, 0.85f, 0.1f);
                case TriageCategory.Red: return new Color(0.9f, 0.12f, 0.1f);
                case TriageCategory.Black: return new Color(0.04f, 0.04f, 0.04f);
                default: return new Color(0.55f, 0.58f, 0.62f);
            }
        }

        public void SetTag(TriageCategory c) { _tag = c; RefreshVisual(); }
        public void SetSelected(bool s) { _selected = s; RefreshVisual(); }
        public void ResetVisual() { _tag = TriageCategory.None; _selected = false; RefreshVisual(); }

        void Awake() { RefreshVisual(); }

        void RefreshVisual()
        {
            if (tagRenderer != null)
            {
                _block = _block ?? new MaterialPropertyBlock();
                tagRenderer.GetPropertyBlock(_block);
                var col = ColorFor(_tag);
                _block.SetColor(BaseColorId, col); _block.SetColor(ColorId, col);
                tagRenderer.SetPropertyBlock(_block);
            }
            if (label != null)
                label.text = (_selected ? "> " : "") + casualtyId + (_tag != TriageCategory.None ? "\n" + _tag.ToString().ToUpperInvariant() : "");
        }
    }
}
