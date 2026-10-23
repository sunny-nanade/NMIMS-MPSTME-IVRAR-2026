using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Builds simple world-space panels (text, buttons, sliders) from code. All use PokeTarget + RayTarget.</summary>
    public static class WorldUI
    {
        /// <summary>TMP fontSize that gives roughly 1 cm... tuned in the Editor: Text() size argument 1.0 is about a 5 cm line.</summary>
        public static float FontScale = 0.45f;

        /// <summary>Text size argument for a line height in metres (1.0 = 5 cm).</summary>
        public static float Cm(float metres) { return metres / 0.05f; }

        static Mesh _quad;
        static readonly Dictionary<Color, Material> Mats = new Dictionary<Color, Material>();

        public static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "UIQuad" };
            _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            return _quad;
        }

        public static Material ColorMaterial(Color c)
        {
            if (Mats.TryGetValue(c, out var m) && m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(sh) { color = c };
            m.SetColor("_BaseColor", c);
            Mats[c] = m;
            return m;
        }

        public static GameObject Panel(Transform parent, string name, Vector2 size, Color color, Vector3 localPos, bool interactableLayer = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ColorMaterial(color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (interactableLayer) SetLayer(go, "Interactable");
            return go;
        }

        public static void SetLayer(GameObject go, string layer)
        {
            int l = LayerMask.NameToLayer(layer);
            if (l >= 0) go.layer = l;
        }

        public static TextMeshPro Text(Transform parent, string text, float fontSize, Vector3 localPos, Vector2 box,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = fontSize * FontScale;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.sizeDelta = box;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public class WorldButton : MonoBehaviour
        {
            public PokeTarget Poke;
            public RayTarget Ray;
            public TextMeshPro Label;
            public event Action<WorldButton> Clicked;
            public Color Normal = new Color(0.12f, 0.25f, 0.35f), Hover = new Color(0.2f, 0.5f, 0.7f);
            MeshRenderer _r;
            public bool Interactable { get => Poke.Interactable; set { Poke.Interactable = value; Ray.Interactable = value; _r.sharedMaterial = ColorMaterial(value ? Normal : new Color(0.15f, 0.15f, 0.15f)); } }
            public void Init()
            {
                _r = GetComponent<MeshRenderer>();
                Poke.Poked += (_, __) => Fire();
                Ray.Down += (_, __) => Fire();
                Ray.HoverChanged += (_, h) => { if (Poke.Interactable) _r.sharedMaterial = ColorMaterial(h ? Hover : Normal); };
            }
            void Fire() { if (!Poke.Interactable) return; SfxEvents.Raise(SfxEvent.UI); Clicked?.Invoke(this); }
            public void Click() { Fire(); }
        }

        public static WorldButton Button(Transform parent, string label, Vector2 size, Vector3 localPos, Action<WorldButton> onClick,
            float fontSize = 1.2f)
        {
            var go = Panel(parent, "Btn_" + label, size, new Color(0.12f, 0.25f, 0.35f), localPos, true);
            var col = go.AddComponent<BoxCollider>();
            // depth is in metres (the quad's z scale is 1): thin, so a neighbouring button never blocks an angled ray
            col.size = new Vector3(1f, 1f, 0.05f);
            col.center = new Vector3(0, 0, -0.02f);
            var wb = go.AddComponent<WorldButton>();
            wb.Poke = go.AddComponent<PokeTarget>();
            wb.Ray = go.AddComponent<RayTarget>();
            wb.Label = Text(go.transform, label, fontSize, new Vector3(0, 0, -0.01f), Vector2.one * 0.95f);
            // text sits in unit-space of the scaled quad: counter-scale so font size is world-based
            wb.Label.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);
            wb.Label.rectTransform.sizeDelta = new Vector2(size.x * 0.95f, size.y * 0.95f);
            wb.Init();
            if (onClick != null) wb.Clicked += onClick;
            return wb;
        }

        public class WorldSlider : MonoBehaviour
        {
            public RayTarget Ray;
            public PokeTarget Poke;
            public float Min = 0, Max = 100;
            public float Value;
            public bool HasValue;
            public event Action<float> Changed;
            public Transform Fill;
            public Transform Handle;
            public TextMeshPro ValueLabel;
            public float Width = 0.5f;
            public bool Locked;

            public void Init()
            {
                Ray.Down += (_, p) => SetFromWorld(p);
                Ray.Drag += (_, p) => SetFromWorld(p);
                Poke.Poked += (_, h) =>
                {
                    var tip = HandInteractor.For(h);
                    if (tip != null) SetFromWorld(InputService.Instance.HandPosition(h));
                };
            }

            void SetFromWorld(Vector3 world)
            {
                if (Locked) return;
                Vector3 l = transform.InverseTransformPoint(world);
                float t = Mathf.Clamp01(l.x + 0.5f);
                SetValue(Mathf.Lerp(Min, Max, t));
            }

            public void SetValue(float v)
            {
                Value = Mathf.Round(Mathf.Clamp(v, Min, Max));
                HasValue = true;
                float t = Mathf.InverseLerp(Min, Max, Value);
                if (Handle != null) Handle.localPosition = new Vector3(t - 0.5f, 0, -0.02f);
                if (Fill != null) { Fill.localScale = new Vector3(Mathf.Max(0.001f, t), 0.5f, 1f); Fill.localPosition = new Vector3(t * 0.5f - 0.5f, 0, -0.01f); }
                if (ValueLabel != null) ValueLabel.text = Value.ToString("0");
                Changed?.Invoke(Value);
            }
        }

        public static WorldSlider Slider(Transform parent, string name, float width, Vector3 localPos, float min = 0, float max = 100)
        {
            var go = Panel(parent, "Slider_" + name, new Vector2(width, 0.05f), new Color(0.2f, 0.2f, 0.25f), localPos, true);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 6f, 0.05f);
            var s = go.AddComponent<WorldSlider>();
            s.Ray = go.AddComponent<RayTarget>();
            s.Poke = go.AddComponent<PokeTarget>();
            s.Min = min; s.Max = max; s.Width = width;
            var fill = Panel(go.transform, "Fill", Vector2.one, new Color(0.2f, 0.6f, 0.8f), new Vector3(-0.5f, 0, -0.01f));
            fill.transform.localScale = new Vector3(0.001f, 0.5f, 1f);
            s.Fill = fill.transform;
            var handle = Panel(go.transform, "Handle", new Vector2(0.02f / width, 2.2f), new Color(0.95f, 0.95f, 1f), new Vector3(-0.5f, 0, -0.02f));
            s.Handle = handle.transform;
            s.ValueLabel = Text(go.transform, "-", 1f, new Vector3(0.58f, 0, 0), new Vector2(0.15f, 0.06f));
            s.ValueLabel.transform.localScale = new Vector3(1f / width, 1f / 0.05f, 1f);
            s.Init();
            return s;
        }
    }
}
