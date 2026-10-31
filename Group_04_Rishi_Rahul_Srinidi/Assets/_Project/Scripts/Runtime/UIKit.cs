using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Colours sampled from the reference footage (UI cards, posters, room).
    public static class Palette
    {
        public static readonly Color Mint = Hex("5BF0B0");
        public static readonly Color MintDark = Hex("2FBF85");
        public static readonly Color Blue = Hex("5B9BEF");
        public static readonly Color BlueDark = Hex("2F6FD0");
        public static readonly Color MailBlue = Hex("0F72C9");
        public static readonly Color Card = Hex("FFFFFF");
        public static readonly Color CardTint = Hex("F3F5F8");
        public static readonly Color Ink = Hex("1E2430");
        public static readonly Color InkSoft = Hex("5A6372");
        public static readonly Color Line = Hex("D5DAE1");
        public static readonly Color Red = Hex("E5484D");
        public static readonly Color Green = Hex("2FB45A");
        public static readonly Color Yellow = Hex("F5C518");
        public static readonly Color Orange = Hex("F07A22");
        public static readonly Color Purple = Hex("8E2DB8");
        public static readonly Color PosterBlue = Hex("2443B5");
        public static readonly Color PosterGreen = Hex("2E9E3E");
        public static readonly Color PosterMagenta = Hex("A3197A");
        public static readonly Color PosterOlive = Hex("B59B12");
        public static readonly Color Dark = Hex("121821");
        public static readonly Color Terminal = Hex("39FF88");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color A(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// Small runtime UI builder so every panel shares the reference look (white rounded cards, coloured headers).
    public static class UIKit
    {
        static Sprite _rounded;
        static Sprite _circle;

        public static Sprite Rounded => _rounded ? _rounded : (_rounded = MakeRounded(96, 12));
        public static Sprite Circle => _circle ? _circle : (_circle = MakeRounded(96, 47));

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// Anchors a rect inside its parent using pixel insets (left, right, top, bottom).
        public static RectTransform Fill(this RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// Places a rect of fixed size relative to an anchor point of the parent (anchor 0..1, pos in pixels from it).
        public static RectTransform At(this RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// Stretches across the top of the parent with a fixed height.
        public static RectTransform Top(this RectTransform rt, float height, float y = 0, float l = 0, float r = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(l, -y - height);
            rt.offsetMax = new Vector2(-r, -y);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool rounded = true, float cornerScale = 1f)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (rounded)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, cornerScale);
            }
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal, string name = "Text")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            Sharpen(t);
            return t;
        }

        static Material sharpMat;
        static TMP_FontAsset sharpFont;

        /// Crisper glyph edges for small text on world-space canvases (one shared material for every label).
        static void Sharpen(TMP_Text t)
        {
            var font = t.font;
            if (!font || !font.material) return;
            if (!sharpMat || sharpFont != font)
            {
                sharpFont = font;
                sharpMat = new Material(font.material) { name = font.material.name + " (sharp)" };
                if (sharpMat.HasProperty("_Sharpness")) sharpMat.SetFloat("_Sharpness", 0.5f);
            }
            t.fontSharedMaterial = sharpMat;
        }

        public static Button Button(Transform parent, string label, Color bg, Color fg, Action onClick, float fontSize = 28, string name = null)
        {
            var img = Panel(parent, name ?? "Btn_" + label, bg);
            var btn = img.gameObject.AddComponent<Button>();
            img.gameObject.AddComponent<ButtonHover>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.92f, 1f);
            colors.pressedColor = new Color(0.7f, 0.75f, 0.85f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1, 1, 1, 0.35f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            // a light-ink label on a bright accent button would vanish: flip to dark
            if (fg == Palette.Ink || fg == Color.white) { if (bg.grayscale > 0.62f) fg = Palette.Ink; }
            var txt = Text(img.transform, label, fontSize, fg, TextAlignmentOptions.Center, FontStyles.Bold, "Label");
            txt.rectTransform.Fill(8, 8, 4, 4);
            txt.textWrappingMode = TextWrappingModes.Normal;
            btn.onClick.AddListener(() =>
            {
                Sfx.Play(Sfx.I ? Sfx.I.click : null, 0.6f);
                Sfx.Haptic(0.25f, 0.04f);   // press feedback
                onClick?.Invoke();
            });
            return btn;
        }

        public static void SetLabel(this Button b, string label)
        {
            var t = b.GetComponentInChildren<TMP_Text>();
            if (t) t.text = label;
        }

        public static void SetColor(this Button b, Color c) => b.GetComponent<Image>().color = c;

        static readonly Color HeaderBlack = new Color(0.055f, 0.06f, 0.07f, 1f);

        /// Card with a near-black header, bold yellow upper-case title and a coloured accent strip (GTA-style mission
        /// panel). The accent colour carries the meaning (mint = info, blue = hint, red = failure).
        public static RectTransform Card(Transform parent, string name, string title, Color header, float headerHeight = 90, float titleSize = 44)
        {
            var card = Panel(parent, name, Palette.Card);
            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.55f);
            shadow.effectDistance = new Vector2(0, -10);
            var head = Panel(card.transform, "Header", HeaderBlack);
            head.rectTransform.Top(headerHeight);
            // square off the bottom of the header so it blends into the card body
            var patch = Panel(head.transform, "Patch", HeaderBlack, false);
            patch.rectTransform.anchorMin = new Vector2(0, 0);
            patch.rectTransform.anchorMax = new Vector2(1, 0);
            patch.rectTransform.pivot = new Vector2(0.5f, 0);
            patch.rectTransform.offsetMin = new Vector2(0, 0);
            patch.rectTransform.offsetMax = new Vector2(0, 30);
            var strip = Panel(head.transform, "Accent", header, false);
            strip.rectTransform.anchorMin = new Vector2(0, 0);
            strip.rectTransform.anchorMax = new Vector2(1, 0);
            strip.rectTransform.pivot = new Vector2(0.5f, 0);
            strip.rectTransform.offsetMin = new Vector2(0, 0);
            strip.rectTransform.offsetMax = new Vector2(0, 8);
            var t = Text(head.transform, title.ToUpperInvariant(), titleSize, Palette.Yellow, TextAlignmentOptions.Center, FontStyles.Bold, "Title");
            t.characterSpacing = 6f;
            t.rectTransform.Fill(20, 20, 0, 8);
            var body = Rect(card.transform, "Body");
            body.Fill(32, 32, headerHeight + 20, 24);
            return body;
        }

        /// Child container for runtime UI. The ISDK canvas quick actions put their ray/poke interaction objects
        /// inside the canvas, so runtime code must only ever build into (and clear) this child.
        public static RectTransform ContentRoot(RectTransform canvas)
        {
            if (canvas.Find("Content") is RectTransform existing) return existing;
            var rt = Rect(canvas, "Content");
            rt.Fill();
            rt.SetAsFirstSibling();
            return rt;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static VerticalLayoutGroup VLayout(GameObject go, float spacing, int pad = 0, TextAnchor align = TextAnchor.UpperLeft, bool expandWidth = true)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = expandWidth;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement Size(this Component c, float prefH = -1, float prefW = -1, float flexW = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (prefH >= 0) le.preferredHeight = prefH;
            if (prefW >= 0) le.preferredWidth = prefW;
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }
    }
}
