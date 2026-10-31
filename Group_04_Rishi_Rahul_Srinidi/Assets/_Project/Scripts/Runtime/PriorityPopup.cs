using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Always-visible objective / notification popup. One pooled panel, a message queue (messages never overlap),
    /// fade + scale in/out, radio static + chime, a dismiss button (ray/poke) and a re-open shortcut.
    /// It follows the head with a gentle lag at ~1.5 m, slightly below eye level, is pulled closer if geometry is in
    /// the way, and draws with the overlay shader (OverlayUI) so it can't hide behind anything.
    /// The root + interactive canvas are created by the scene builder (StoryBuilder.CommonUI).
    public class PriorityPopup : MonoBehaviour
    {
        public static PriorityPopup I { get; private set; }

        public Transform head;
        public RectTransform canvas;          // 1000 x 720, ISDK ray + poke
        public float distance = 1.5f;
        public float drop = 0.12f;            // below eye level, metres
        public float followSharpness = 3f;    // lower = lazier

        struct Msg { public string title, body; public Color accent; public float seconds; public bool objective; }

        readonly Queue<Msg> queue = new Queue<Msg>();
        static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        RectTransform content;
        CanvasGroup group;
        TMP_Text titleText, bodyText, listText;
        Image headerBar;
        AudioSource audioSrc;
        AudioClip staticClip, chimeClip;
        Coroutine routine;
        bool showing, dismissRequested, built;
        Vector3 smoothPos;
        string lastObjective;
        int lastIndex, lastTotal;

        public bool IsShowing => showing;
        /// True while a message is on screen or queued (other popups wait for it).
        public bool Busy => routine != null;

        void Awake()
        {
            I = this;
            canvas = UIKit.ContentRoot(canvas);
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;
            staticClip = MakeStatic();
            chimeClip = MakeChime();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        void Start()
        {
            Build();
            // a message may already have been queued before Start ran
            if (routine == null) gameObject.SetActive(false);
        }

        void Update()
        {
            bool dismiss = OVRInput.GetDown(OVRInput.Button.One);
            bool reopen = false;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.jKey.wasPressedThisFrame) { if (showing) dismiss = true; else reopen = true; }
#endif
            if (showing && ((Hub.I && Hub.I.IsOpen) || (Inventory.I && Inventory.I.IsOpen))) dismiss = true;   // menus take priority
            if (dismiss && showing) Dismiss();
            if (reopen) Reopen();
        }

        void LateUpdate()
        {
            if (!head || !showing) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            float dist = distance;
            var want = head.position + fwd * dist + Vector3.down * drop;
            // pull closer when a wall / prop sits between the head and the popup
            var dir = want - head.position;
            float len = dir.magnitude;
            int count = Physics.RaycastNonAlloc(head.position, dir / len, hitBuffer, len, ~0, QueryTriggerInteraction.Ignore);
            float nearest = len;
            for (int i = 0; i < count; i++)
            {
                var h = hitBuffer[i];
                if (h.distance < 0.35f || h.collider.transform.IsChildOf(head.root)) continue;
                nearest = Mathf.Min(nearest, h.distance - 0.12f);
            }
            nearest = Mathf.Max(nearest, 0.7f);
            want = head.position + dir / len * nearest;

            float k = 1 - Mathf.Exp(-followSharpness * Time.deltaTime);
            smoothPos = Vector3.Lerp(smoothPos, want, k);
            transform.position = smoothPos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(smoothPos - head.position, Vector3.up), k);
        }

        // ---------------------------------------------------------------- public API

        /// Radio-style "NEW OBJECTIVE" popup with the task checklist.
        public void ShowObjective(int index, int total, string label)
        {
            lastIndex = index; lastTotal = total; lastObjective = label;
            Enqueue(new Msg { title = $"NEW OBJECTIVE {index}/{total}", body = label, accent = Palette.Mint, seconds = 9f, objective = true });
        }

        /// Warning / hint / notification with the same always-visible treatment.
        public void Notify(string title, string body, Color accent, float seconds = 6f)
        {
            Enqueue(new Msg { title = title.ToUpperInvariant(), body = body, accent = accent, seconds = seconds });
        }

        /// Shows the current objective again (wrist button / J key).
        public void Reopen()
        {
            var gm = GameManager.I;
            if (!gm || gm.State != GameState.Playing) return;
            var info = GameData.Info(gm.CurrentObjective());
            Enqueue(new Msg { title = "CURRENT OBJECTIVE", body = info.label, accent = Palette.Blue, seconds = 14f, objective = true });
        }

        public void Dismiss()
        {
            if (showing) dismissRequested = true;
        }

        /// Clears everything (level change / restart).
        public void ResetState()
        {
            queue.Clear();
            if (routine != null) StopCoroutine(routine);
            routine = null;
            showing = false;
            gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- internals

        void Enqueue(Msg m)
        {
            if (queue.Count >= 6) queue.Dequeue();
            queue.Enqueue(m);
            if (routine == null)
            {
                gameObject.SetActive(true);
                if (!built) Build();
                routine = StartCoroutine(Run());
            }
        }

        void Build()
        {
            if (built) return;
            built = true;
            content = canvas;
            if (!content.TryGetComponent(out group)) group = content.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            var card = UIKit.Card(content, "PopupCard", "NEW OBJECTIVE", Palette.Mint, 110, 56);
            var cardRect = card.parent.GetComponent<RectTransform>();
            cardRect.Fill();
            headerBar = cardRect.Find("Header/Accent").GetComponent<Image>();
            titleText = cardRect.Find("Header/Title").GetComponent<TMP_Text>();
            titleText.fontStyle = FontStyles.Bold;

            bodyText = UIKit.Text(card, "", 46, Palette.Ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            bodyText.rectTransform.Fill(0, 0, 0, 400);
            bodyText.enableAutoSizing = true;
            bodyText.fontSizeMin = 32;
            bodyText.fontSizeMax = 46;

            var panel = UIKit.Panel(card, "ListPanel", Palette.CardTint);
            panel.rectTransform.Fill(0, 0, 170, 100);
            listText = UIKit.Text(panel.transform, "", 30, Palette.Ink);
            listText.rectTransform.Fill(20, 20, 10, 10);
            listText.enableAutoSizing = true;
            listText.fontSizeMin = 22;
            listText.fontSizeMax = 34;

            var btn = UIKit.Button(card, "Dismiss", Palette.Mint, Palette.Ink, Dismiss, 36, "Btn_Dismiss");
            btn.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(0, 0), new Vector2(260, 84), new Vector2(1, 0));
            var hint = UIKit.Text(card, "A button / J = dismiss", 24, Palette.InkSoft, TextAlignmentOptions.MidlineLeft);
            hint.rectTransform.At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(520, 84), new Vector2(0, 0));

            var cv = canvas.GetComponentInParent<Canvas>();
            if (cv) cv.sortingOrder = 20;   // HUD canvas is 10
            OverlayUI.Apply(canvas.parent, 4100);   // above the HUD (4000)
        }

        string Checklist()
        {
            var gm = GameManager.I;
            if (!gm) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var info in gm.Chapter.tasks)
            {
                bool d = gm.IsDone(info.id);
                sb.Append(d ? "<color=#2FB45A>[x]</color> <color=#8A93A3>" : "[  ] ").Append(info.label).Append(d ? "</color>" : "").Append('\n');
            }
            return sb.ToString();
        }

        IEnumerator Run()
        {
            var root = transform;
            while (queue.Count > 0)
            {
                // stay invisible until the text is in: an empty card used to show while waiting for the radio
                group.alpha = 0;
                // let the radio finish its line first so the two never talk over each other
                for (float w = 0; w < 10f && Narrator.I && Narrator.I.Speaking; w += Time.unscaledDeltaTime) yield return null;
                var m = queue.Dequeue();
                titleText.text = m.title;
                headerBar.color = m.accent;
                bodyText.text = m.body;
                listText.text = m.objective ? Checklist() : "";
                listText.transform.parent.gameObject.SetActive(m.objective);
                bodyText.rectTransform.Fill(0, 0, 0, m.objective ? 400 : 110);
                if (head)
                {
                    var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                    smoothPos = head.position + fwd * distance + Vector3.down * drop;
                    root.position = smoothPos;
                    root.rotation = Quaternion.LookRotation(smoothPos - head.position, Vector3.up);
                }
                showing = true;
                dismissRequested = false;
                audioSrc.PlayOneShot(staticClip, 0.35f);
                audioSrc.PlayOneShot(chimeClip, 0.6f);
                Sfx.Haptic(0.3f, 0.08f);

                yield return Fade(0, 1, 0.25f);
                float t = 0;
                while (t < m.seconds && !dismissRequested)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                yield return Fade(1, 0, 0.2f);
                showing = false;
                yield return new WaitForSeconds(0.15f);
            }
            routine = null;
            gameObject.SetActive(false);
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / seconds)
            {
                float e = Mathf.SmoothStep(0, 1, t);
                float a = Mathf.Lerp(from, to, e);
                group.alpha = a;
                transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, a);
                yield return null;
            }
            group.alpha = to;
            transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, to);
        }

        // ---------------------------------------------------------------- procedural audio (no assets needed)

        static AudioClip MakeStatic()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.4f);
            var data = new float[n];
            var rng = new System.Random(21);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Clamp01(t * 20) * Mathf.Clamp01((1 - t) * 4);
                data[i] = ((float)rng.NextDouble() * 2 - 1) * 0.35f * env;
            }
            var clip = AudioClip.Create("RadioStatic", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeChime()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.6f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float f = t < 0.18f ? 880f : 1318f;
                float env = Mathf.Exp(-(t < 0.18f ? t : t - 0.18f) * 9f) * Mathf.Clamp01(t * 400);
                data[i] = Mathf.Sin(2 * Mathf.PI * f * t) * 0.4f * env;
            }
            var clip = AudioClip.Create("Chime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
