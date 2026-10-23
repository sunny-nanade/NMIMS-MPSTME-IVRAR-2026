using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Cosmetic oxygen readout (wrist or wall). At zero the bar flashes and nothing else happens.</summary>
    public class OxygenDisplay : MonoBehaviour
    {
        public enum Mode { Wall, Wrist }

        [SerializeField] Mode mode = Mode.Wall;
        [SerializeField] Vector2 size = new Vector2(0.6f, 0.3f);
        [SerializeField] float flashHz = 2f;
        [SerializeField] float wristShowDot = 0.55f;

        TextMeshPro _label;
        Transform _fill;
        MeshRenderer _fillR, _backR;
        GameObject _root;
        static readonly Color Ok = new Color(0.2f, 0.9f, 0.5f), Low = new Color(0.95f, 0.7f, 0.15f), Crit = new Color(0.95f, 0.2f, 0.2f);

        public Mode DisplayMode { get => mode; set => mode = value; }

        void Start() { Build(); }

        void Build()
        {
            _root = new GameObject("OxygenUI");
            _root.transform.SetParent(transform, false);
            UiStyle.Panel(_root.transform, "Back", size, Vector3.zero, 0.015f);
            WorldUI.Text(_root.transform, "O2", WorldUI.Cm(size.y * 0.16f), new Vector3(-size.x * 0.38f, size.y * 0.25f, -0.002f), new Vector2(size.x * 0.3f, size.y * 0.3f), TextAlignmentOptions.Left, new Color(0.7f, 0.9f, 1f));
            _label = WorldUI.Text(_root.transform, "20:00", WorldUI.Cm(size.y * 0.28f), new Vector3(size.x * 0.18f, size.y * 0.2f, -0.002f), new Vector2(size.x * 0.6f, size.y * 0.4f), TextAlignmentOptions.Right);
            var bar = WorldUI.Panel(_root.transform, "BarBack", new Vector2(size.x * 0.9f, size.y * 0.18f), new Color(0.12f, 0.14f, 0.18f), new Vector3(0, -size.y * 0.25f, -0.001f));
            _backR = bar.GetComponent<MeshRenderer>();
            var fill = WorldUI.Panel(_root.transform, "BarFill", new Vector2(size.x * 0.9f, size.y * 0.18f), Ok, new Vector3(0, -size.y * 0.25f, -0.002f));
            _fill = fill.transform; _fillR = fill.GetComponent<MeshRenderer>();
        }

        void Update()
        {
            var ox = OxygenSystem.Instance;
            if (ox == null || _root == null) return;

            if (mode == Mode.Wrist)
            {
                var cam = Camera.main;
                bool show = false;
                if (cam != null)
                {
                    Vector3 toHead = (cam.transform.position - transform.position).normalized;
                    show = Vector3.Dot(-transform.forward, toHead) > wristShowDot || Vector3.Dot(transform.forward, toHead) > wristShowDot;
                }
                _root.SetActive(show);
                if (!show) return;
            }

            _label.text = ox.Formatted;
            float f = Mathf.Clamp01(ox.Fraction);
            float w = size.x * 0.9f;
            _fill.localScale = new Vector3(Mathf.Max(0.001f, w * f), size.y * 0.18f, 1f);
            _fill.localPosition = new Vector3(-w * 0.5f + w * f * 0.5f, -size.y * 0.25f, -0.002f);
            Color c = f > 0.4f ? Ok : f > 0.15f ? Low : Crit;
            if (ox.Depleted)
            {
                bool on = Mathf.FloorToInt(Time.unscaledTime * flashHz * 2f) % 2 == 0;
                _fillR.enabled = on;
                _label.color = on ? Crit : Color.white;
                _label.text = "00:00";
            }
            else { _fillR.enabled = true; _label.color = Color.white; }
            _fillR.sharedMaterial = WorldUI.ColorMaterial(c);
        }
    }
}
