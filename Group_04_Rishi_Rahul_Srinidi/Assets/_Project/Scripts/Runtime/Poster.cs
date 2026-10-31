using TMPro;
using UnityEngine;

namespace CSER
{
    /// Awareness posters in the reference's bold flat style (original wording).
    public class Poster : MonoBehaviour
    {
        public enum Kind { Passwords, CleanDesk, Phishing, Shred }
        public Kind kind;
        public RectTransform canvas; // 600 x 840

        void Start()
        {
            switch (kind)
            {
                case Kind.Passwords: Passwords(); break;
                case Kind.CleanDesk: Simple("Clean Desk!", Palette.Hex("F8F4E8"), Palette.Red, "Leave nothing behind:\n\n- Lock your screen\n- Papers in the drawer\n- Shred what's confidential\n- No passwords on notes", Palette.Ink); break;
                case Kind.Phishing: Simple("THINK\nBEFORE YOU\nCLICK", Palette.Hex("1E2A4A"), Palette.Yellow, "Check the sender's domain.\nUrgent? Threatening? Gift cards?\n\nWhen in doubt:\nREPORT IT.", Color.white); break;
                case Kind.Shred: Simple("ONLY FOR TRASH", Palette.Hex("F8F4E8"), Palette.Ink, "Confidential paper goes in the SHREDDER.\n\nUnknown USB sticks go to IT.", Palette.Ink); break;
            }
        }

        void Simple(string title, Color bg, Color titleColor, string body, Color bodyColor)
        {
            var p = UIKit.Panel(UIKit.ContentRoot(canvas), "Bg", bg, false);
            p.rectTransform.Fill();
            var t = UIKit.Text(p.transform, title, 78, titleColor, TextAlignmentOptions.Center, FontStyles.Bold);
            t.rectTransform.Top(300, 40, 30, 30);
            t.enableAutoSizing = true;
            t.fontSizeMin = 30;
            t.fontSizeMax = 90;
            var b = UIKit.Text(p.transform, body, 34, bodyColor, TextAlignmentOptions.Top);
            b.rectTransform.Fill(40, 40, 360, 40);
        }

        void Passwords()
        {
            var p = UIKit.Panel(UIKit.ContentRoot(canvas), "Bg", Palette.Hex("15161A"), false);
            p.rectTransform.Fill();
            var t = UIKit.Text(p.transform, "PASSWORDS", 80, Palette.Red, TextAlignmentOptions.Center, FontStyles.Bold);
            t.rectTransform.Top(110, 6);
            (string h, string b, Color c)[] cells =
            {
                ("LONG", "12+ characters.\nA passphrase of\nrandom words works", Palette.PosterOlive),
                ("MIXED", "Upper & lower case,\nnumbers and\nsymbols", Palette.Purple),
                ("NOT PERSONAL", "No pets, names,\nbirthdays or years.\nNo: Fluffy2019!", Palette.PosterBlue),
                ("UNIQUE", "Never reuse a\npassword across\naccounts", Palette.Orange),
                ("2-FACTOR", "Add a second\nfactor wherever\nyou can", Palette.PosterMagenta),
                ("SECRET", "Never share it,\nnever write it down\nor throw it away", Palette.PosterGreen),
            };
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = UIKit.Panel(p.transform, "Cell", cells[i].c, false);
                cell.rectTransform.At(new Vector2(0, 1), new Vector2(10 + (i % 2) * 295, -120 - (i / 2) * 215), new Vector2(285, 205), new Vector2(0, 1));
                var h = UIKit.Text(cell.transform, cells[i].h, 38, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                h.rectTransform.Top(60, 8, 6, 6);
                h.enableAutoSizing = true;
                h.fontSizeMin = 20;
                h.fontSizeMax = 38;
                var b = UIKit.Text(cell.transform, cells[i].b, 24, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
                b.rectTransform.Fill(8, 8, 70, 6);
            }
            var foot = UIKit.Text(p.transform, "Use a password manager to remember them all!", 24, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            foot.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(580, 60), new Vector2(0.5f, 0));
        }
    }
}
