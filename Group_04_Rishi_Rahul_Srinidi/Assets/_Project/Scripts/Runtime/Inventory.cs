using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Four-slot backpack. Press I (X on the left controller, or BAG on the wrist) to open it, store what is in your
    /// hands so it can't get lost, take things out again, and when it is full choose one item to throw out.
    /// Thrown-out items land on the floor in front of you, so a puzzle item is never destroyed.
    public class Inventory : MonoBehaviour
    {
        public static Inventory I { get; private set; }
        public const int Capacity = 4;

        public Transform head;
        public Transform root;           // panel root, placed in front of the player when opened
        public RectTransform canvas;     // 1000 x 640

        static bool hintShown;
        readonly List<GrabItem> slots = new List<GrabItem>();
        RectTransform content;
        GrabItem pending;                // item waiting for a free slot (inventory was full)
        string note;
        string heldSignature = "";
        bool open;
        float refreshTimer;

        public bool IsOpen => Hub.I != null && Hub.I.IsOpen && Hub.I.Tab == 1;
        public IReadOnlyList<GrabItem> Slots => slots;
        public string Note => note;
        public int Count => slots.Count;
        public bool Contains(GrabItem item) => slots.Contains(item);

        void Awake()
        {
            I = this;
            canvas = UIKit.ContentRoot(canvas);
        }

        void Start()
        {
            root.gameObject.SetActive(false);
            GrabItem.AnyGrabbed += OnGrab;
        }

        void OnDestroy()
        {
            GrabItem.AnyGrabbed -= OnGrab;
            if (I == this) I = null;
        }

        void OnGrab(GrabItem item)
        {
            if (hintShown || !item.CanStore) return;
            hintShown = true;
            GameUI.I?.Toast("Inventory", "Press L (pop-up, Inventory tab) or I to store what you hold, so it can't get lost.", Palette.Blue, 6f);
        }

        void Update()
        {
            bool toggle = OVRInput.GetDown(OVRInput.Button.Three);
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame) toggle = true;
#endif
            if (toggle) Toggle();
            if (!open) return;
            if (GameManager.I.State != GameState.Playing) { Close(); return; }
            // follow what the player is holding while the panel is open
            if ((refreshTimer -= Time.unscaledDeltaTime) <= 0)
            {
                refreshTimer = 0.4f;
                if (HeldSignature() != heldSignature) Rebuild();
            }
        }

        public void Toggle() => Hub.I?.Toggle(1);

        public void Open() => Hub.I?.Open(1);

        public void Close() { if (IsOpen) Hub.I.Close(); }

        /// Takes a stored item out and puts it straight into the player's hand.
        public bool TakeToHand(int index)
        {
            if (index < 0 || index >= slots.Count) return false;
            var kg = KeyboardGrab.I;
            if (!kg || kg.HasItem) return false;
            var item = slots[index];
            slots.RemoveAt(index);
            var rig = FindAnyObjectByType<OVRCameraRig>();
            Vector3 pos; Quaternion rot;
            if (rig && rig.rightHandAnchor) { pos = rig.rightHandAnchor.position + rig.rightHandAnchor.forward * 0.18f; rot = rig.rightHandAnchor.rotation; }
            else { FlatForward(out var f); pos = head.position + f * 0.5f + Vector3.down * 0.3f; rot = Quaternion.LookRotation(f, Vector3.up); }
            item.Restore(pos, rot);
            kg.Hold(item);
            return true;
        }

        void OpenOld()
        {
            if (open || GameManager.I.State != GameState.Playing) return;
            if (GameUI.I != null && GameUI.I.MenuOpen) GameUI.I.CloseMenu();
            open = true;
            pending = null;
            note = null;
            root.gameObject.SetActive(true);
            Place();
            Sfx.Play(Sfx.I ? Sfx.I.open : null, 0.6f);
            Rebuild();
        }

        void CloseOld()
        {
            if (!open) return;
            open = false;
            pending = null;
            root.gameObject.SetActive(false);
            Sfx.Play(Sfx.I ? Sfx.I.close : null, 0.6f);
        }

        void Place()
        {
            if (!head) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            root.position = head.position + fwd * 0.8f + Vector3.up * -0.1f;
            root.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        // ---------------------------------------------------------------- actions

        public List<GrabItem> HeldItems()
        {
            var list = new List<GrabItem>();
            foreach (var g in FindObjectsByType<GrabItem>(FindObjectsInactive.Exclude))
                if ((g.IsHeld || g.Mounted) && g.CanStore && !slots.Contains(g)) list.Add(g);
            return list;
        }

        string HeldSignature()
        {
            var sb = new StringBuilder();
            foreach (var g in HeldItems()) sb.Append(g.name).Append(',');
            return sb.ToString();
        }

        public bool Store(GrabItem item)
        {
            if (!item || !item.CanStore || slots.Contains(item)) return false;
            if (slots.Count >= Capacity)
            {
                pending = item;
                note = $"Your bag is full. Throw one item out to make room for the {item.displayName}.";
                Sfx.Play(Sfx.I ? Sfx.I.error : null);
                if (open) Rebuild();
                return false;
            }
            slots.Add(item);
            item.Store();
            pending = null;
            note = $"{item.displayName} stored.";
            Sfx.Play(Sfx.I ? Sfx.I.confirm : null, 0.7f);
            if (open) Rebuild();
            return true;
        }

        public void TakeOut(int index)
        {
            if (index < 0 || index >= slots.Count) return;
            var item = slots[index];
            slots.RemoveAt(index);
            FlatForward(out var fwd);
            item.Restore(head.position + fwd * 0.5f + Vector3.down * 0.3f, Quaternion.LookRotation(fwd, Vector3.up));
            GameUI.I?.Caption($"{item.displayName} is in front of you. Grab it.", Palette.Ink, 3.5f);
            Close();
        }

        public void ThrowOut(int index)
        {
            if (index < 0 || index >= slots.Count) return;
            var item = slots[index];
            slots.RemoveAt(index);
            FlatForward(out var fwd);
            var pos = head.position + fwd * 1.0f;
            pos.y = 0.12f;
            item.Restore(pos, Quaternion.LookRotation(fwd, Vector3.up));
            Sfx.Play(Sfx.I ? Sfx.I.drop : null);
            note = $"Threw out the {item.displayName}. It lies on the floor if you need it again.";
            if (pending) Store(pending);
            else Rebuild();
        }

        void FlatForward(out Vector3 fwd)
        {
            fwd = head ? Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        }

        // ---------------------------------------------------------------- UI

        void Rebuild()
        {
            heldSignature = HeldSignature();
            UIKit.Clear(canvas);
            var body = UIKit.Card(canvas, "Inventory", $"Inventory  {slots.Count} / {Capacity}", Palette.Blue, 80, 40);
            body.parent.GetComponent<RectTransform>().Fill();
            var close = UIKit.Button(body.parent, "Close", Palette.Red, Color.white, Close, 26);
            close.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-14, -12), new Vector2(130, 56), new Vector2(1, 1));

            bool choosing = pending != null;

            // ---- in your hands
            var held = HeldItems();
            var heading = UIKit.Text(body, choosing ? $"<color=#E5484D><b>Bag full.</b></color> Pick an item to throw out for the {pending.displayName}:" : "<b>In your hands</b>", 28, Palette.Ink);
            heading.rectTransform.Top(40);
            if (!choosing)
            {
                if (held.Count == 0)
                {
                    var none = UIKit.Text(body, "Nothing. Grab an item, then press I and store it.", 26, Palette.InkSoft);
                    none.rectTransform.Top(40, 46);
                }
                for (int i = 0; i < held.Count && i < 3; i++)
                {
                    var item = held[i];
                    var b = UIKit.Button(body, $"Store: {item.displayName}", Palette.Mint, Palette.Ink, () => Store(item), 26);
                    b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(i * 300, -46), new Vector2(290, 66), new Vector2(0, 1));
                }
            }

            // ---- slots
            const float slotW = 214, slotH = 250, gap = 14;
            for (int i = 0; i < Capacity; i++)
            {
                int idx = i;
                var slot = UIKit.Panel(body, "Slot" + i, Palette.CardTint);
                slot.rectTransform.At(new Vector2(0, 0), new Vector2(i * (slotW + gap), 56), new Vector2(slotW, slotH), Vector2.zero);
                if (i < slots.Count)
                {
                    var item = slots[i];
                    var name = UIKit.Text(slot.transform, $"<b>{item.displayName}</b>", 28, Palette.Ink, TextAlignmentOptions.Center);
                    name.rectTransform.Top(110, 12, 8, 8);
                    name.enableAutoSizing = true;
                    name.fontSizeMin = 20;
                    name.fontSizeMax = 28;
                    if (!choosing)
                    {
                        var take = UIKit.Button(slot.transform, "Take out", Palette.Blue, Color.white, () => TakeOut(idx), 26);
                        take.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 84), new Vector2(190, 60), new Vector2(0.5f, 0));
                    }
                    var throwOut = UIKit.Button(slot.transform, "Throw out", Palette.Red, Color.white, () => ThrowOut(idx), 26);
                    throwOut.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, choosing ? 84 : 14), new Vector2(190, 60), new Vector2(0.5f, 0));
                }
                else
                {
                    var empty = UIKit.Text(slot.transform, "empty", 26, Palette.InkSoft, TextAlignmentOptions.Center);
                    empty.rectTransform.Fill();
                }
            }

            if (!string.IsNullOrEmpty(note) || choosing)
            {
                var n = UIKit.Text(body, note ?? "", 24, choosing ? Palette.Red : Palette.InkSoft, TextAlignmentOptions.Left);
                n.rectTransform.At(new Vector2(0, 0), new Vector2(0, 4), new Vector2(760, 46), Vector2.zero);
                n.textWrappingMode = TextWrappingModes.NoWrap;
            }
            if (choosing)
            {
                var cancel = UIKit.Button(body, "Cancel", Palette.CardTint, Palette.Ink, () => { pending = null; note = null; Rebuild(); }, 24);
                cancel.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(0, 0), new Vector2(180, 52), new Vector2(1, 0));
            }
        }

        // ---------------------------------------------------------------- test helpers

        public string DebugStore(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (!go || !go.TryGetComponent<GrabItem>(out var g)) return "no item " + objectName;
            return Store(g) ? "stored " + objectName : "not stored (full or not storable)";
        }

        public void DebugTakeOut(int index) => TakeOut(index);
        public void DebugThrowOut(int index) => ThrowOut(index);
        public string DebugList() => $"{slots.Count}/{Capacity}: " + string.Join(", ", slots.ConvertAll(s => s.displayName));
    }
}
