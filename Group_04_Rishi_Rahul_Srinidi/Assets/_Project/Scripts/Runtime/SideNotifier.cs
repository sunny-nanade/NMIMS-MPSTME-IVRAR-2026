using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Small notification cards stacked at the right edge of the head-following HUD: each slides in, holds, then fades
    /// away. Replaces the big centre toasts / captions / radio subtitles so nothing covers the room or the screens.
    /// Channels: "radio" and "caption" each own one card that is replaced in place; everything else ("toast") stacks.
    public class SideNotifier : MonoBehaviour
    {
        const int PoolSize = 4;
        const float CardW = 360f, CardH = 104f, MaxCardH = 230f, Gap = 10f, BaseY = 130f, Margin = 14f;
        const float SlideIn = 0.28f, FadeOut = 0.5f;

        class Card
        {
            public RectTransform rt;
            public CanvasGroup group;
            public Image bar;
            public TMP_Text title, body;
            public string channel;
            public float age, life;
            public int order;
            public float y;
            public bool active;
        }

        readonly List<Card> cards = new List<Card>();
        readonly List<Card> order = new List<Card>(PoolSize);   // reused every frame (no allocation)
        int counter;
        bool visible = true;

        public void Init(RectTransform parent)
        {
            for (int i = 0; i < PoolSize; i++) cards.Add(Build(parent, i));
        }

        Card Build(RectTransform parent, int i)
        {
            var bg = UIKit.Panel(parent, "Note" + i, new Color(0.055f, 0.06f, 0.07f, 0.9f));
            var c = new Card { rt = bg.rectTransform };
            c.rt.At(new Vector2(1, 0), new Vector2(-Margin, BaseY), new Vector2(CardW, CardH), new Vector2(1, 0));
            c.group = bg.gameObject.AddComponent<CanvasGroup>();
            c.group.blocksRaycasts = false;
            c.group.interactable = false;
            c.bar = UIKit.Panel(c.rt, "Bar", Palette.Mint, false);
            c.bar.rectTransform.anchorMin = Vector2.zero;
            c.bar.rectTransform.anchorMax = new Vector2(0, 1);
            c.bar.rectTransform.pivot = new Vector2(0, 0.5f);
            c.bar.rectTransform.offsetMin = Vector2.zero;
            c.bar.rectTransform.offsetMax = new Vector2(8, 0);
            c.title = UIKit.Text(c.rt, "", 18, Palette.Yellow, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            c.title.characterSpacing = 3f;
            c.title.rectTransform.Top(24, 8, 22, 12);
            c.body = UIKit.Text(c.rt, "", 24, Color.white, TextAlignmentOptions.TopLeft);
            c.body.rectTransform.Fill(22, 12, 32, 8);
            c.body.enableAutoSizing = true;
            c.body.fontSizeMin = 15;
            c.body.fontSizeMax = 24;
            c.rt.gameObject.SetActive(false);
            return c;
        }

        /// Shows a card. An empty title collapses the title row and gives the body the whole card.
        public void Show(string title, string body, Color accent, float seconds, string channel = "toast")
        {
            Card c = null;
            if (channel != "toast") c = cards.Find(x => x.active && x.channel == channel);
            if (c == null) c = cards.Find(x => !x.active);
            if (c == null)
            {
                // recycle the oldest stacking card so something is always shown
                foreach (var x in cards) if (x.channel == "toast" && (c == null || x.order < c.order)) c = x;
                if (c == null) c = cards[0];
            }
            bool fresh = !c.active;
            c.active = true;
            c.channel = channel;
            c.age = fresh ? 0f : Mathf.Max(c.age, SlideIn);   // replaced in place: no second slide-in
            c.life = Mathf.Max(0.8f, seconds);
            c.order = ++counter;
            c.bar.color = accent;
            bool hasTitle = !string.IsNullOrEmpty(title);
            c.title.text = hasTitle ? title.ToUpperInvariant() : "";
            c.title.gameObject.SetActive(hasTitle);
            c.body.text = body;
            float top = hasTitle ? 32 : 10;
            c.body.rectTransform.Fill(22, 12, top, 8);
            // grow the card for longer text (learning points, hints) instead of shrinking the font to unreadable
            c.body.enableAutoSizing = false;
            c.body.fontSize = 21;
            float want = top + c.body.GetPreferredValues(body, CardW - 34, 0).y + 14;
            c.rt.sizeDelta = new Vector2(CardW, Mathf.Clamp(want, CardH, MaxCardH));
            c.body.enableAutoSizing = want > MaxCardH;   // only very long text shrinks
            c.rt.gameObject.SetActive(visible);
            if (fresh) c.y = BaseY;
            if (channel == "toast") Sfx.Play(Sfx.I ? Sfx.I.toast : null, 0.3f);
        }

        public void HideChannel(string channel)
        {
            foreach (var c in cards) if (c.active && c.channel == channel) c.life = Mathf.Min(c.life, FadeOut);
        }

        public void HideAll()
        {
            foreach (var c in cards) { c.active = false; c.rt.gameObject.SetActive(false); }
        }

        /// Hidden while the player is using a screen up close.
        public void SetVisible(bool on)
        {
            visible = on;
            foreach (var c in cards) if (c.active) c.rt.gameObject.SetActive(on);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            // newest card sits lowest; older ones are pushed up
            float stackY = BaseY;
            order.Clear();
            foreach (var c in cards) if (c.active) order.Add(c);
            order.Sort((a, b) => b.order.CompareTo(a.order));
            foreach (var c in order)
            {
                float target = stackY;
                stackY += c.rt.sizeDelta.y + Gap;
                c.y = Mathf.Lerp(c.y, target, 1 - Mathf.Exp(-10f * dt));
                c.age += dt;
                c.life -= dt;
                float s = Mathf.SmoothStep(0, 1, Mathf.Clamp01(c.age / SlideIn));
                float fade = Mathf.Clamp01(c.life / FadeOut);
                c.group.alpha = Mathf.Min(s, fade);
                c.rt.anchoredPosition = new Vector2(-Margin + (1 - s) * 90f, c.y);
                if (c.life <= 0f)
                {
                    c.active = false;
                    c.rt.gameObject.SetActive(false);
                }
            }
        }
    }
}
