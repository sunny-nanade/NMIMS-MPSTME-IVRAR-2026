using TMPro;
using UnityEngine;

namespace CSER
{
    /// Title screen: continue, chapter select (locked until earned), how to play, settings, credits, quit.
    public class MainMenu : MonoBehaviour
    {
        public RectTransform canvas; // 1400 x 820
        RectTransform root;
        int page;

        void Start()
        {
            root = UIKit.ContentRoot(canvas);
            Build();
            Narrator.I?.Play("menu_welcome");
        }

        void Build()
        {
            UIKit.Clear(root);
            var bg = UIKit.Panel(root, "Bg", new Color(0.05f, 0.08f, 0.12f, 0.9f));
            bg.rectTransform.Fill();
            var title = UIKit.Text(bg.transform, "<b>CYBER SECURITY ESCAPE ROOM</b>  <color=#5BF0B0>VR</color>\n<size=50%><color=#9FB3CC>The Northwind Breach - a blue-team story in three chapters</color></size>", 64, Color.white, TextAlignmentOptions.TopLeft);
            title.rectTransform.Top(150, 40, 60, 60);
            var nav = UIKit.Rect(bg.transform, "Nav");
            nav.anchorMin = new Vector2(0, 0); nav.anchorMax = new Vector2(0, 1);
            nav.pivot = new Vector2(0, 0.5f);
            nav.offsetMin = new Vector2(60, 60); nav.offsetMax = new Vector2(420, -220);
            string[] items = { StoryProgress.UnlockedChapter > 1 ? "Continue" : "New story", "Chapters", "How to play", "Settings", "Credits", "Quit" };
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(nav, items[i], i == page ? Palette.Mint : Palette.Hex("1C2A3D"), i == page ? Palette.Ink : Color.white, () => Nav(idx), 30, "Menu_" + items[i]);
                b.GetComponent<RectTransform>().Top(76, i * 88);
            }
            var panel = UIKit.Panel(bg.transform, "Page", Palette.Hex("0E1622"));
            panel.rectTransform.Fill(450, 60, 220, 60);
            var p = (RectTransform)panel.transform;
            switch (page)
            {
                case 0: Continue(p); break;
                case 1: Chapters(p); break;
                case 2: HowTo(p); break;
                case 3: Settings(p); break;
                case 4: CreditsPage(p); break;
            }
        }

        void Nav(int idx)
        {
            if (idx == 5)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }
            page = idx;
            Build();
        }

        void Continue(RectTransform p)
        {
            int ch = StoryProgress.UnlockedChapter;
            if (StoryProgress.StoryComplete) ch = 1;
            var info = GameData.Chapters[ch - 1];
            var t = UIKit.Text(p, $"<size=60%><color=#5BF0B0>{(StoryProgress.StoryComplete ? "STORY COMPLETE - PLAY AGAIN" : ch == 1 ? "START" : "CONTINUE")}</color></size>\n<b>Chapter {ch}: {info.title}</b>\n<size=55%><color=#9FB3CC>{info.subtitle}</color></size>\n\n<size=60%>{Strip(info.intro)}</size>\n\n<size=55%><color=#5BF0B0>Walk to the stairwell door on the right, point at it and pull the trigger to knock.</color></size>", 44, Color.white);
            t.rectTransform.Fill(40, 40, 30, 140);
            t.enableAutoSizing = true;
            t.fontSizeMin = 16;
            t.fontSizeMax = 44;
            var go = UIKit.Button(p, "Play", Palette.Mint, Palette.Ink, () => SceneFlow.LoadChapter(ch), 40, "Play");
            go.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(40, 30), new Vector2(360, 90), Vector2.zero);
            go.gameObject.AddComponent<Pulse>().amount = 0.015f;
        }

        void Chapters(RectTransform p)
        {
            for (int i = 0; i < GameData.Chapters.Length; i++)
            {
                var c = GameData.Chapters[i];
                bool unlocked = StoryProgress.IsUnlocked(c.number);
                var r = StoryProgress.Get(c.number);
                var card = UIKit.Panel(p, "Chapter" + c.number, unlocked ? Palette.Hex("1C2A3D") : Palette.Hex("141B26"));
                card.rectTransform.At(new Vector2(0, 1), new Vector2(30 + i * 300, -30), new Vector2(280, 470), new Vector2(0, 1));
                var t = UIKit.Text(card.transform,
                    $"<size=60%><color=#5BF0B0>CHAPTER {c.number}</color></size>\n<b>{c.title}</b>\n<size=55%><color=#9FB3CC>{c.subtitle}</color></size>\n\n<size=55%>{(r.done ? $"Best time {GameManager.FormatTime(r.seconds)}\nHints {r.hints} - Mistakes {r.mistakes}" : unlocked ? "Not finished yet" : "Locked: finish the previous chapter")}</size>",
                    36, unlocked ? Color.white : Palette.InkSoft);
                t.rectTransform.Fill(20, 20, 20, 110);
                if (!unlocked) continue;
                int n = c.number;
                var b = UIKit.Button(card.transform, r.done ? "Replay" : "Play", Palette.Mint, Palette.Ink, () => SceneFlow.LoadChapter(n), 30, "PlayChapter" + n);
                b.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(220, 74), new Vector2(0.5f, 0));
            }
        }

        void HowTo(RectTransform p)
        {
            var t = UIKit.Text(p,
                "<b>Controllers</b>\n" +
                "- Point the ray and pull the <b>trigger</b> to press buttons and screens.\n" +
                "- Squeeze <b>grip</b> to grab. Point at an object and squeeze grip to pull it to you.\n" +
                "- Left thumbstick: <b>teleport</b> (push forward, aim, release). Right stick: turn.\n" +
                "- <b>Y / B</b> or the wrist panel opens the menu: objectives, hints, learning points.\n\n" +
                "<b>Hand tracking</b>\n" +
                "- Pinch to click with the hand ray; poke screens with your finger; make a fist to grab.\n" +
                "- Look at your left wrist: tap MENU or HINT.\n\n" +
                "<b>Meta XR Simulator (keyboard & mouse)</b>\n" +
                "- Hold right mouse to look, WASD to move, Q/E up/down, left click = trigger.\n" +
                "- <b>P</b> at a door = knock / tap your badge. <b>F</b> = flashlight on/off. <b>M</b> = menu.\n\n" +
                "<b>Stuck?</b>  Follow Maya on the radio and the green marker. Hints get stronger each time you ask.",
                25, Color.white);
            t.rectTransform.Fill(36, 36, 28, 20);
        }

        void Settings(RectTransform p)
        {
            Toggle(p, 0, "Guide marker", Guide.Enabled, v => Guide.Enabled = v);
            Toggle(p, 1, "Ambient sound", Sfx.AmbienceOn, v => Sfx.I?.SetAmbience(v));
            var q = UIKit.Text(p, "<b>Quality</b>", 28, Color.white);
            q.rectTransform.At(new Vector2(0, 1), new Vector2(40, -230), new Vector2(300, 60), new Vector2(0, 1));
            string[] levels = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = UIKit.Button(p, levels[i], Quality.Level == i ? Palette.Blue : Palette.Hex("1C2A3D"), Color.white, () => { Quality.Set(idx); Build(); }, 26);
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(340 + i * 180, -230), new Vector2(170, 60), new Vector2(0, 1));
            }
            var reset = UIKit.Button(p, "Reset story progress", Palette.Red, Color.white, () => { StoryProgress.ResetAll(); page = 0; Build(); }, 26);
            reset.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(40, 40), new Vector2(380, 70), Vector2.zero);
        }

        void Toggle(RectTransform p, int row, string label, bool value, System.Action<bool> set)
        {
            var l = UIKit.Text(p, $"<b>{label}</b>", 28, Color.white);
            l.rectTransform.At(new Vector2(0, 1), new Vector2(40, -40 - row * 90), new Vector2(300, 60), new Vector2(0, 1));
            var on = UIKit.Button(p, "On", value ? Palette.Blue : Palette.Hex("1C2A3D"), Color.white, () => { set(true); Build(); }, 26);
            on.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(340, -40 - row * 90), new Vector2(170, 60), new Vector2(0, 1));
            var off = UIKit.Button(p, "Off", !value ? Palette.Blue : Palette.Hex("1C2A3D"), Color.white, () => { set(false); Build(); }, 26);
            off.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(520, -40 - row * 90), new Vector2(170, 60), new Vector2(0, 1));
        }

        void CreditsPage(RectTransform p)
        {
            var t = UIKit.Text(p, Credits.Text, 22, Color.white, TextAlignmentOptions.TopLeft);
            t.rectTransform.Fill(36, 36, 24, 20);
        }

        static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
    }

    public static class Credits
    {
        public const string Text =
            "<b>Cyber Security Escape Room VR - The Northwind Breach</b>\n" +
            "Design, story & development: K075 AR/VR project team\n\n" +
            "<b>Built with</b>  Unity 6 (URP), Meta XR Core & Interaction SDK\n" +
            "<b>3D models</b>  Poly Haven (CC0)\n" +
            "<b>Materials & textures</b>  ambientCG (CC0), Poly Haven (CC0)\n" +
            "<b>Sky</b>  Poly Haven HDRI 'modern_buildings_night' (CC0)\n" +
            "<b>UI sounds</b>  Kenney Interface Sounds (CC0)\n" +
            "<b>Voices</b>  Windows speech synthesis (Microsoft Zira / David)\n" +
            "<b>Photos</b>  Wikimedia Commons (public domain)\n\n" +
            "Inspired by the 2D game 'Cyber Security Escape Room Online'. No assets or text from that game are used.";
    }
}
