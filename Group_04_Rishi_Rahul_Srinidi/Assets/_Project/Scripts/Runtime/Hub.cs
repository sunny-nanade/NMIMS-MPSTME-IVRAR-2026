using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// One pop-up for everything: Menu (objectives, controls, settings), a Minecraft-style Inventory and auto Hints.
    /// L on the keyboard (left thumbstick click on Quest) opens or closes it. It stays in front of the player and only
    /// re-centres when the player turns or walks away, so it never jitters under a ray or a poke.
    public class Hub : MonoBehaviour
    {
        public static Hub I { get; private set; }

        public Transform head;
        public Transform root;
        public RectTransform canvas;   // 1000 x 700

        public bool IsOpen { get; private set; }
        public int Tab { get; private set; }
        public bool AutoHints { get; private set; }

        RectTransform body;
        RectTransform tabBody;
        TMP_Text objectiveText;
        string message = "";
        int selected = -1;
        string signature = "";
        float refreshTimer, hintTimer;
        TaskId lastObjective;
        bool haveObjective;
        bool recentre;

        const float HintInterval = 12f;

        void Awake()
        {
            I = this;
            canvas = UIKit.ContentRoot(canvas);
        }

        void Start() => root.gameObject.SetActive(false);

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            bool toggle = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);
            bool close = false;
            int step = 0;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.lKey.wasPressedThisFrame) toggle = true;
                if (kb.escapeKey.wasPressedThisFrame) close = true;
                if (IsOpen && kb.rightArrowKey.wasPressedThisFrame) step = 1;
                if (IsOpen && kb.leftArrowKey.wasPressedThisFrame) step = -1;
            }
#endif
            if (gm.State != GameState.Playing)
            {
                if (IsOpen) Close();
                if (AutoHints) SetAutoHints(false);
                return;
            }
            if (toggle) Toggle(Tab);
            if (close && IsOpen) Close();
            if (step != 0) SetTab((Tab + step + 2) % 2);

            UpdateAutoHints(gm);

            if (!IsOpen) return;
            if (Tab == 0 && objectiveText) objectiveText.text = GameUI.I ? GameUI.I.ObjectiveLine() : "";
            if ((refreshTimer -= Time.unscaledDeltaTime) <= 0)
            {
                refreshTimer = 0.4f;
                if (Tab == 1 && Signature() != signature) Rebuild();
            }
        }

        void LateUpdate()
        {
            if (!IsOpen || !head) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();
            var panelFwd = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            var flat = Vector3.ProjectOnPlane(root.position - head.position, Vector3.up);
            // only re-centre when the player has turned or walked away; otherwise stay put so clicks are steady
            if (Vector3.Angle(panelFwd, fwd) > 35f || Mathf.Abs(flat.magnitude - 0.85f) > 0.35f || Mathf.Abs(root.position.y - (head.position.y - 0.08f)) > 0.3f)
                recentre = true;
            if (!recentre) return;
            var targetPos = head.position + fwd * 0.85f + Vector3.up * -0.08f;
            float k = 1 - Mathf.Exp(-7f * Time.unscaledDeltaTime);
            root.position = Vector3.Lerp(root.position, targetPos, k);
            var dir = Vector3.ProjectOnPlane(root.position - head.position, Vector3.up);
            if (dir.sqrMagnitude > 1e-4f) root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), k);
            if ((root.position - targetPos).sqrMagnitude < 1e-4f) recentre = false;
        }

        // ---------------------------------------------------------------- open / close

        public void Toggle(int tab)
        {
            if (IsOpen && Tab == tab) Close(); else Open(tab);
        }

        public void Open(int tab)
        {
            if (GameManager.I == null || GameManager.I.State != GameState.Playing) return;
            Tab = Mathf.Clamp(tab, 0, 1);
            if (!IsOpen)
            {
                IsOpen = true;
                root.gameObject.SetActive(true);
                var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                root.position = head.position + fwd * 0.85f + Vector3.up * -0.08f;
                root.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                recentre = false;
                Sfx.Play(Sfx.I ? Sfx.I.open : null, 0.6f);
            }
            message = "";
            Rebuild();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            root.gameObject.SetActive(false);
            Sfx.Play(Sfx.I ? Sfx.I.close : null, 0.6f);
        }

        void SetTab(int tab)
        {
            if (!IsOpen) return;
            Tab = tab;
            message = "";
            Rebuild();
        }

        // ---------------------------------------------------------------- auto hints

        public void SetAutoHints(bool on)
        {
            if (AutoHints == on) return;
            AutoHints = on;
            haveObjective = false;
            hintTimer = 0;
            if (!on) GameUI.I?.HideCaption();
            Sfx.Play(Sfx.I ? Sfx.I.toast : null, 0.5f);
            if (IsOpen) Rebuild();
        }

        void UpdateAutoHints(GameManager gm)
        {
            if (!AutoHints) return;
            var cur = gm.CurrentObjective();
            if (!haveObjective || cur != lastObjective)
            {
                lastObjective = cur;
                haveObjective = true;
                hintTimer = 0;
            }
            hintTimer -= Time.deltaTime;
            if (hintTimer > 0) return;
            hintTimer = HintInterval;
            if (gm.HintTierFor(cur) >= 3)
            {
                // all three shown: keep the last one on screen until the objective changes
                var all = GameData.Info(cur).hints;
                GameUI.I?.Caption($"Hint 3/3: {all[Mathf.Min(2, all.Length - 1)]}", Palette.Ink, HintInterval + 1f);
                return;
            }
            var text = gm.NextHint(out _, out int tier);
            GameUI.I?.Caption($"Hint {Mathf.Min(tier + 1, 3)}/3: {text}", Palette.Ink, HintInterval + 1f);
            Sfx.Play(Sfx.I ? Sfx.I.toast : null);
        }

        // ---------------------------------------------------------------- UI

        void Rebuild()
        {
            signature = Signature();
            UIKit.Clear(canvas);
            objectiveText = null;
            body = UIKit.Card(canvas, "Hub", $"Chapter {GameManager.I.chapter}: {GameManager.I.Chapter.title}", Palette.Blue, 80, 34);
            body.parent.GetComponent<RectTransform>().Fill();

            Tabs();
            tabBody = UIKit.Rect(body, "TabBody");
            tabBody.Fill(0, 0, 76, 0);
            if (Tab == 0) BuildMenu(); else BuildInventory();
        }

        void Tabs()
        {
            string[] names = { "Menu", "Inventory" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(body, names[i], i == Tab ? Palette.Blue : Palette.CardTint, i == Tab ? Color.white : Palette.Ink, () => SetTab(idx), 26);
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(i * 215, 0), new Vector2(205, 60), new Vector2(0, 1));
            }
            var hints = UIKit.Button(body, AutoHints ? "Hints: ON" : "Hints: OFF", AutoHints ? Palette.Yellow : Palette.CardTint, Palette.Ink, () => SetAutoHints(!AutoHints), 26);
            hints.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(2 * 215, 0), new Vector2(205, 60), new Vector2(0, 1));
            var close = UIKit.Button(body, "Close", Palette.Red, Color.white, Close, 26);
            close.GetComponent<RectTransform>().At(new Vector2(1, 1), Vector2.zero, new Vector2(120, 60), new Vector2(1, 1));
        }

        void BuildMenu()
        {
            var gm = GameManager.I;
            var header = UIKit.Text(tabBody, GameUI.I ? GameUI.I.ObjectiveLine() : "", 26, Palette.Ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            header.rectTransform.Top(76);
            objectiveText = header;

            var list = UIKit.Text(tabBody, "", 22, Palette.Ink, TextAlignmentOptions.TopLeft);
            list.rectTransform.Fill(0, 520, 84, 90);
            var sb = new System.Text.StringBuilder();
            foreach (var info in gm.Chapter.tasks)
            {
                bool d = gm.IsDone(info.id);
                sb.Append(d ? "<color=#2FB45A>[x]</color>  <color=#8A93A3>" : "[  ]  ").Append(info.label).Append(d ? "</color>" : "").Append('\n');
            }
            list.text = sb.ToString();
            list.enableAutoSizing = true;
            list.fontSizeMin = 14;
            list.fontSizeMax = 22;

            var controls = UIKit.Panel(tabBody, "Controls", Palette.CardTint);
            controls.rectTransform.Fill(500, 0, 84, 90);
            var ct = UIKit.Text(controls.transform,
                "<b>Controls</b>\n<b>Z</b>  use screen   <b>O</b>  step back\n<b>L</b>  this pop-up\n<b>H</b>  next hint\n<b>X</b>  wrist panel\n<b>I</b>  inventory\n<b>4</b> grab    <b>3</b> release\n<b>F</b>  flashlight\n<b>P</b>  door / knock\n<b>Esc</b> close   <b>Arrows</b> switch tab\n<size=80%><color=#5A6372>Quest: left stick click = this pop-up</color></size>",
                20, Palette.Ink, TextAlignmentOptions.TopLeft);
            ct.rectTransform.Fill(16, 10, 10, 8);
            ct.enableAutoSizing = true;
            ct.fontSizeMin = 14;
            ct.fontSizeMax = 20;

            var guide = UIKit.Button(tabBody, Guide.Enabled ? "Guide marker: ON" : "Guide marker: OFF", Palette.CardTint, Palette.Ink, () => { Guide.Enabled = !Guide.Enabled; Rebuild(); }, 22);
            guide.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(0, 10), new Vector2(300, 60), Vector2.zero);
            var restart = UIKit.Button(tabBody, "Restart chapter", Palette.Red, Color.white, () => gm.Restart(false), 22);
            restart.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(280, 60), new Vector2(0.5f, 0));
            var menu = UIKit.Button(tabBody, "Main menu", Palette.CardTint, Palette.Ink, SceneFlow.LoadMenu, 22);
            menu.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(0, 10), new Vector2(240, 60), new Vector2(1, 0));
        }

        // ---------------------------------------------------------------- inventory (Minecraft style)

        static readonly Color McGrey = new Color32(198, 198, 198, 255);
        static readonly Color McSlot = new Color32(139, 139, 139, 255);
        static readonly Color McDark = new Color32(55, 55, 55, 255);
        static readonly Color[] ItemColors =
        {
            new Color32(232, 93, 93, 255), new Color32(93, 156, 232, 255), new Color32(93, 210, 130, 255),
            new Color32(240, 190, 70, 255), new Color32(180, 120, 230, 255), new Color32(240, 140, 80, 255),
            new Color32(90, 210, 210, 255), new Color32(220, 120, 180, 255),
        };

        string Signature()
        {
            var inv = Inventory.I;
            var sb = new System.Text.StringBuilder();
            if (inv != null)
            {
                foreach (var g in inv.Slots) sb.Append(g.name).Append(',');
                sb.Append('|');
                foreach (var g in inv.HeldItems()) sb.Append(g.name).Append(',');
            }
            return sb.ToString();
        }

        void BuildInventory()
        {
            var inv = Inventory.I;
            var mc = UIKit.Panel(tabBody, "Mc", McDark, false);
            mc.rectTransform.Fill(0, 0, 4, 0);
            var inner = UIKit.Panel(mc.transform, "Inner", McGrey, false);
            inner.rectTransform.Fill(6, 6, 6, 6);

            var title = UIKit.Text(inner.transform, "Inventory", 30, McDark, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            title.rectTransform.Top(44, 14, 20, 20);

            const float size = 150, bagSize = 132, gap = 14;
            var handLabel = UIKit.Text(inner.transform, "HAND", 22, McDark, TextAlignmentOptions.Center, FontStyles.Bold);
            handLabel.rectTransform.At(new Vector2(0, 1), new Vector2(26, -70), new Vector2(size, 30), new Vector2(0, 1));
            var held = inv != null ? inv.HeldItems() : new List<GrabItem>();
            GrabItem handItem = held.Count > 0 ? held[0] : null;
            Slot(inner.transform, new Vector2(26, -104), size, handItem, false, () =>
            {
                if (inv == null) return;
                if (handItem == null) { message = "Your hand is empty. Grab something (4), then click here."; Rebuild(); return; }
                inv.Store(handItem);
                message = inv.Note ?? "";
                Rebuild();
            });

            var arrow = UIKit.Text(inner.transform, ">", 60, McDark, TextAlignmentOptions.Center, FontStyles.Bold);
            arrow.rectTransform.At(new Vector2(0, 1), new Vector2(26 + size + 4, -138), new Vector2(50, 80), new Vector2(0, 1));

            float bx = 26 + size + 70;
            var bagLabel = UIKit.Text(inner.transform, $"BACKPACK  {inv?.Count ?? 0} / {Inventory.Capacity}", 22, McDark, TextAlignmentOptions.Left, FontStyles.Bold);
            bagLabel.rectTransform.At(new Vector2(0, 1), new Vector2(bx, -70), new Vector2(500, 30), new Vector2(0, 1));
            for (int i = 0; i < Inventory.Capacity; i++)
            {
                int idx = i;
                GrabItem item = inv != null && i < inv.Slots.Count ? inv.Slots[i] : null;
                Slot(inner.transform, new Vector2(bx + i * (bagSize + gap), -112), bagSize, item, i == selected, () =>
                {
                    if (inv == null) return;
                    selected = idx;
                    if (item != null)
                    {
                        if (handItem != null) { message = "Your hand is full. Click the HAND slot to store it first."; Rebuild(); return; }
                        bool ok = inv.TakeToHand(idx);
                        message = ok ? $"{item.displayName} is in your hand." : "Couldn't take that out.";
                    }
                    else if (handItem != null)
                    {
                        inv.Store(handItem);
                        message = inv.Note ?? "";
                    }
                    else message = "Empty slot.";
                    Rebuild();
                });
            }

            var help = UIKit.Text(inner.transform,
                "Click an item in the backpack to take it into your hand.\nClick the HAND slot (or an empty slot) to store what you are holding.",
                21, McDark, TextAlignmentOptions.TopLeft);
            help.rectTransform.At(new Vector2(0, 0), new Vector2(26, 70), new Vector2(880, 80), Vector2.zero);
            var msg = UIKit.Text(inner.transform, message, 24, Palette.Ink, TextAlignmentOptions.Left, FontStyles.Bold);
            msg.rectTransform.At(new Vector2(0, 0), new Vector2(26, 14), new Vector2(880, 44), Vector2.zero);
        }

        void Slot(Transform parent, Vector2 pos, float size, GrabItem item, bool isSelected, System.Action onClick)
        {
            var frame = UIKit.Panel(parent, "SlotFrame", isSelected ? Color.white : McDark, false);
            frame.rectTransform.At(new Vector2(0, 1), pos + new Vector2(-4, 4), new Vector2(size + 8, size + 8), new Vector2(0, 1));
            var b = UIKit.Button(parent, item ? item.displayName : "", McSlot, Color.white, onClick, 20, "Slot");
            b.GetComponent<Image>().sprite = null;
            b.GetComponent<RectTransform>().At(new Vector2(0, 1), pos, new Vector2(size, size), new Vector2(0, 1));
            var label = b.transform.Find("Label").GetComponent<TMP_Text>();
            if (item == null) { label.text = ""; return; }
            label.rectTransform.Fill(4, 4, size * 0.62f, 4);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = 20;
            var icon = UIKit.Panel(b.transform, "Icon", ItemColors[(int)item.kind % ItemColors.Length], false);
            icon.rectTransform.At(new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(size * 0.5f, size * 0.5f), new Vector2(0.5f, 1));
            icon.raycastTarget = false;
            var letter = UIKit.Text(icon.transform, item.displayName.Substring(0, 1).ToUpperInvariant(), 44, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            letter.rectTransform.Fill();
            letter.raycastTarget = false;
        }
    }
}
