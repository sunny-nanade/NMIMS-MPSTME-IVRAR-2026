using TMPro;
using UnityEngine;

namespace CSER
{
    /// Exit-door keypad (ray / poke). Accepts the 4-digit override code revealed on the Containment Board.
    public class DoorKeypad : MonoBehaviour
    {
        public RectTransform canvas; // 360 x 520
        public Hinge door;
        TMP_Text display;
        string entry = "";
        bool open;

        void Start()
        {
            var bg = UIKit.Panel(UIKit.ContentRoot(canvas), "Bg", Palette.Hex("2A2F38"));
            bg.rectTransform.Fill();
            // matte dark screen (no glare), large digits; keys are 100 x 100 units = 6 x 6 cm at this canvas scale
            var screen = UIKit.Panel(bg.transform, "Screen", Palette.Dark);
            screen.rectTransform.Top(80, 14, 20, 110);
            display = UIKit.Text(screen.transform, "", 64, Palette.Terminal, TextAlignmentOptions.Center, FontStyles.Bold);
            display.rectTransform.Fill();
            var back = UIKit.Button(bg.transform, "<", Palette.Hex("6A7385"), Color.white, () => Press("<"), 48, "KeyBack");
            back.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-20, -12), new Vector2(88, 88), new Vector2(1, 1));   // >= 5 cm for a finger poke
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "OK" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                Color c = k == "OK" ? Palette.Green : k == "C" ? Palette.Red : Palette.Hex("4A5160");
                var b = UIKit.Button(bg.transform, k, c, Color.white, () => Press(k), 44, "Key" + k);
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(20 + (i % 3) * 110, -104 - (i / 3) * 103), new Vector2(100, 100), new Vector2(0, 1));
            }
            Show();
        }

        public void Press(string k)
        {
            if (open || flashing) return;
            Sfx.Haptic(0.25f, 0.03f);
            Sfx.Play(Sfx.I ? Sfx.I.key : null, 0.5f);
            if (k == "C") entry = "";
            else if (k == "<") { if (entry.Length > 0) entry = entry.Substring(0, entry.Length - 1); }
            else if (k == "OK") Submit();
            else if (entry.Length < 4) entry += k;
            Show();
        }

        void Submit()
        {
            var gm = GameManager.I;
            if (gm.State != GameState.Playing) return;
            string code = string.Concat(gm.DoorCode);
            if (entry == code && gm.IsDone(TaskId.EncryptData))
            {
                open = true;
                display.text = "OPEN";
                display.color = Palette.Green;
                door.Open();
                gm.Complete(TaskId.Escape);
            }
            else
            {
                gm.Mistake(entry == code ? "Override refused: the attacker still has access. Finish containment first." : "Wrong passkey. Check the Data Section popup or the Containment Board.");
                entry = "";
                StartCoroutine(Flash("DENIED"));
            }
        }

        bool flashing;

        /// Red "DENIED" for a moment, then back to the empty entry.
        System.Collections.IEnumerator Flash(string text)
        {
            flashing = true;
            display.text = text;
            display.color = Palette.Red;
            yield return new WaitForSeconds(0.9f);
            display.color = Palette.Terminal;
            flashing = false;
            Show();
        }

        void Show()
        {
            if (!open && display) display.text = string.Join(" ", entry.PadRight(4, '_').ToCharArray());
        }

        /// Keyboard path (P at the door): enters the override code for the player once containment is complete.
        public bool TryOpen()
        {
            if (!open) { entry = string.Concat(GameManager.I.DoorCode); Submit(); }
            return open;
        }

        public void DebugEnterCode() { entry = string.Concat(GameManager.I.DoorCode); Submit(); }
    }
}
