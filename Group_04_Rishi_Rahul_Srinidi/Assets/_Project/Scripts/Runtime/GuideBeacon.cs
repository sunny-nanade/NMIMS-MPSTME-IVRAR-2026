using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Floating marker above the next thing the player should use: a pill label with a pointer
    /// that bobs gently, always faces the player and scales with distance so it stays readable.
    public class GuideBeacon : MonoBehaviour
    {
        public static GuideBeacon I { get; private set; }
        public Transform head;

        Transform target;
        Vector3 offset;
        RectTransform canvas;
        TMP_Text label;
        CanvasGroup group;
        float visible;

        // clear the static when the scene unloads, so later scenes never call into a destroyed instance
        void OnDestroy() { if (I == this) I = null; }

        void Awake()
        {
            I = this;
            var go = new GameObject("BeaconCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            canvas = (RectTransform)go.transform;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<Canvas>().sortingOrder = 5;
            canvas.sizeDelta = new Vector2(520, 220);
            canvas.localScale = Vector3.one * 0.0006f;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 0;

            var pill = UIKit.Panel(canvas, "Pill", Palette.Mint);
            pill.rectTransform.At(new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 96), new Vector2(0.5f, 1));
            label = UIKit.Text(pill.transform, "", 34, Palette.Ink, TextAlignmentOptions.Center, FontStyles.Bold);
            label.rectTransform.Fill(18, 18, 6, 6);
            label.enableAutoSizing = true;
            label.fontSizeMin = 18;
            label.fontSizeMax = 34;
            // pointer: a rotated square under the pill
            var arrow = UIKit.Panel(canvas, "Arrow", Palette.Mint, false);
            arrow.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(46, 46), new Vector2(0.5f, 0.5f));
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var dot = UIKit.Panel(canvas, "Dot", Palette.Mint.A(0.5f));
            dot.sprite = UIKit.Circle;
            dot.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -175), new Vector2(40, 40), new Vector2(0.5f, 0.5f));
        }

        public void Show(Transform t, string text, float height = 0.22f)
        {
            target = t;
            offset = Vector3.up * height;
            label.text = text;
        }

        public void Hide() => target = null;

        void LateUpdate()
        {
            bool on = target && target.gameObject.activeInHierarchy;
            visible = Mathf.MoveTowards(visible, on ? 1 : 0, Time.deltaTime * 3f);
            group.alpha = visible;
            if (!on || !head) return;
            var b = Bounds(target);
            var pos = new Vector3(b.center.x, b.max.y, b.center.z) + offset + Vector3.up * (Mathf.Sin(Time.time * 2.4f) * 0.02f);
            float dist = Vector3.Distance(head.position, pos);
            canvas.position = pos;
            canvas.localScale = Vector3.one * 0.00075f * Mathf.Clamp(dist, 0.6f, 3.5f);
            var look = canvas.position - head.position;
            look.y = 0;
            if (look.sqrMagnitude > 1e-4f) canvas.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs)
                if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
