using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// All player-facing panels: chapter title/complete cards, the Y/B menu (objectives, hints, learning points,
    /// settings), the wrist panel, head-following toasts/captions/subtitles and the final certificate.
    /// Content is built at runtime with UIKit inside each canvas's Content child; the canvases themselves
    /// (with ISDK ray/poke interaction) are created by the scene builder.
    public class GameUI : MonoBehaviour
    {
        public static GameUI I { get; private set; }

        public Transform head;
        public Transform cardRoot;
        public RectTransform cardCanvas;   // 1100 x 760
        public Transform menuRoot;
        public RectTransform menuCanvas;   // 1000 x 700
        public RectTransform hudCanvas;    // 1000 x 640, follows the head
        public RectTransform wristCanvas;  // 420 x 220, on the left hand

        SideNotifier notifier;
        TMP_Text wristObjective;
        RectTransform menuBody;
        TMP_Text objectiveText;
        int menuTab;
        bool menuOpen;
        int hintTierShown;
        public bool MenuOpen => menuOpen;

        // clear the static when the scene unloads, so later scenes never call into a destroyed instance
        void OnDestroy() { if (I == this) I = null; }

        void Awake()
        {
            I = this;
            cardCanvas = UIKit.ContentRoot(cardCanvas);
            menuCanvas = UIKit.ContentRoot(menuCanvas);
            hudCanvas = UIKit.ContentRoot(hudCanvas);
            if (wristCanvas) wristCanvas = UIKit.ContentRoot(wristCanvas);
        }

        GameObject wristRoot;

        void Start()
        {
            BuildHud();
            BuildWrist();
            if (wristRoot) wristRoot.SetActive(false);
            menuRoot.gameObject.SetActive(false);
            GameManager.I.StateChanged += OnState;
            GameManager.I.TaskCompleted += _ => { if (menuOpen) RebuildMenu(); };
            GameManager.I.TaskCompleted += t => { if (t == TaskId.EncryptData) ShowDataSection(); };
            OnState(GameManager.I.State);
        }

        void Update()
        {
            bool toggle = OVRInput.GetDown(OVRInput.Button.Start) || OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four);
            bool wrist = false, hint = false;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.mKey.wasPressedThisFrame) toggle = true;
                if (kb.xKey.wasPressedThisFrame) wrist = true;
                if (kb.hKey.wasPressedThisFrame) hint = true;
            }
#endif
            if (toggle) ToggleMenu();
            if (wrist) ToggleWrist();
            if (hint) ShowNextHint();
            if (menuOpen && objectiveText && GameManager.I.State == GameState.Playing)
                objectiveText.text = ObjectiveLine();
        }

        // ---------------------------------------------------------------- cards

        void OnState(GameState s)
        {
            if (s != GameState.Playing) notifier?.HideAll();
            switch (s)
            {
                case GameState.Intro: ShowIntro(); break;
                case GameState.Playing:
                    cardRoot.gameObject.SetActive(false);
                    notifier?.Show("Chapter " + GameManager.I.chapter + " - " + GameManager.I.Chapter.title, "Z use screen, O step back, L menu, H hint", Palette.Blue, 5f);
                    break;
                case GameState.Won: ShowEnd(true); break;
                case GameState.Lost: ShowEnd(false); break;
            }
        }

        RectTransform NewCard(string title, Color header, Vector2 size)
        {
            cardRoot.gameObject.SetActive(true);
            UIKit.Clear(cardCanvas);
            // the canvas (and its ray surface) keeps its size; only the content shrinks to fit the card
            cardCanvas.anchorMin = cardCanvas.anchorMax = new Vector2(0.5f, 0.5f);
            cardCanvas.sizeDelta = size;
            var body = UIKit.Card(cardCanvas, "Card", title, header);
            body.parent.GetComponent<RectTransform>().Fill();
            return body;
        }

        void ShowIntro()
        {
            var ch = GameManager.I.Chapter;
            var body = NewCard("Chapter " + ch.number + " of " + GameData.Chapters.Length, Palette.Mint, new Vector2(1100, 760));
            var title = UIKit.Text(body, $"<size=58><b>{ch.title}</b></size>\n<color=#5A6372>{ch.subtitle}</color>", 30, Palette.Ink, TextAlignmentOptions.TopLeft);
            title.rectTransform.Top(120);
            var t = UIKit.Text(body, ch.intro, 28, Palette.Ink);
            t.rectTransform.Fill(0, 0, 135, GameManager.I.HasTimer ? 200 : 110);

            if (GameManager.I.HasTimer)
            {
                var diffLabel = UIKit.Text(body, "Difficulty", 26, Palette.InkSoft, TextAlignmentOptions.Left, FontStyles.Bold);
                diffLabel.rectTransform.At(Vector2.zero, new Vector2(0, 120), new Vector2(200, 50), Vector2.zero);
                Button normal = null, expert = null;
                void Refresh()
                {
                    normal.SetColor(GameManager.Experienced ? Palette.CardTint : Palette.Blue);
                    expert.SetColor(GameManager.Experienced ? Palette.Blue : Palette.CardTint);
                }
                normal = UIKit.Button(body, $"Normal  ({ch.normalMinutes:0} min)", Palette.Blue, Palette.Ink, () => { GameManager.Experienced = false; Refresh(); }, 24);
                normal.GetComponent<RectTransform>().At(Vector2.zero, new Vector2(200, 120), new Vector2(300, 56), Vector2.zero);
                expert = UIKit.Button(body, $"Experienced  ({ch.experiencedMinutes:0} min)", Palette.CardTint, Palette.Ink, () => { GameManager.Experienced = true; Refresh(); }, 24);
                expert.GetComponent<RectTransform>().At(Vector2.zero, new Vector2(520, 120), new Vector2(360, 56), Vector2.zero);
                Refresh();
            }
            var go = UIKit.Button(body, "Begin", Palette.Mint, Palette.Ink, () => GameManager.I.BeginPlaying(), 34);
            go.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(120, 16), new Vector2(360, 80), new Vector2(0.5f, 0));
            var menu = UIKit.Button(body, "Main menu", Palette.CardTint, Palette.InkSoft, SceneFlow.LoadMenu, 24);
            menu.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-230, 16), new Vector2(240, 80), new Vector2(0.5f, 0));
        }

        void ShowEnd(bool won)
        {
            CloseMenu();
            StopAllCoroutines();
            notifier?.HideAll();
            PriorityPopup.I?.ResetState();
            PlaceInFront(cardRoot, 1.3f, -0.05f);
            var gm = GameManager.I;
            var ch = gm.Chapter;
            bool last = ch.number == GameData.Chapters.Length;
            if (won && last)
            {
                ShowCertificate();
                StartCoroutine(PopIn(cardRoot));
                return;
            }
            if (!won)
            {
                ShowGameOver();
                StartCoroutine(PopIn(cardRoot));
                return;
            }
            ShowChapterComplete();
            StartCoroutine(PopIn(cardRoot));
        }

        /// Chapter debrief: what you did, your stats, every learning point of the chapter (with its explanation) and
        /// where the story goes next.
        void ShowChapterComplete()
        {
            var gm = GameManager.I;
            var ch = gm.Chapter;
            var next = GameData.Chapters[ch.number];
            var body = NewCard($"Chapter {ch.number} of {GameData.Chapters.Length} complete", Palette.Mint, new Vector2(1100, 760));

            var t = UIKit.Text(body, $"<size=150%><b>{ch.title}: mission accomplished</b></size>\n{ch.outro}", 25, Palette.Ink, TextAlignmentOptions.TopLeft);
            t.rectTransform.Top(166);
            t.enableAutoSizing = true;
            t.fontSizeMin = 18;
            t.fontSizeMax = 25;

            int tasksDone = 0, points = 0, pointTotal = 0;
            foreach (var task in ch.tasks)
            {
                if (gm.IsDone(task.id)) tasksDone++;
                if (!GameData.HasPoint(task.id)) continue;
                pointTotal++;
                if (gm.IsDone(task.id)) points++;
            }
            var stats = UIKit.Panel(body, "Stats", Palette.Mint.A(0.3f));
            stats.rectTransform.Top(52, 176);
            string timeLine = gm.HasTimer ? $"Time  <b>{GameManager.FormatTime(gm.Elapsed)}</b> / {GameManager.FormatTime(gm.TimeLimit)}" : $"Time  <b>{GameManager.FormatTime(gm.Elapsed)}</b>";
            var st = UIKit.Text(stats.transform, $"{timeLine}     Objectives  <b>{tasksDone}/{ch.tasks.Length}</b>     Hints  <b>{gm.HintsUsed}</b>     Mistakes  <b>{gm.Mistakes}</b>     Learning points  <b>{points}/{pointTotal}</b>",
                22, Palette.Ink, TextAlignmentOptions.Center);
            st.rectTransform.Fill(12, 12, 4, 4);
            st.enableAutoSizing = true;
            st.fontSizeMin = 14;
            st.fontSizeMax = 22;

            // learning point titles in two readable columns; the full explanations are in Menu > Learning points
            var learned = UIKit.Panel(body, "Learned", Palette.CardTint);
            learned.rectTransform.Fill(0, 0, 238, 150);
            var lh = UIKit.Text(learned.transform, "<b>WHAT YOU LEARNED</b>   <size=80%><color=#5A6372>full explanations: Menu > Learning points</color></size>", 22, Palette.Ink, TextAlignmentOptions.MidlineLeft);
            lh.rectTransform.Top(36, 8, 22, 22);
            var grid = UIKit.Rect(learned.transform, "Grid");
            grid.Fill(22, 22, 48, 8);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(318, 40);   // three columns: up to 12 points fit (chapter 2 has 11)
            g.spacing = new Vector2(10, 4);
            foreach (var task in ch.tasks)
            {
                if (!GameData.HasPoint(task.id)) continue;
                bool got = gm.IsDone(task.id);
                var cell = UIKit.Text(grid, (got ? "<color=#2FB45A>[x]</color> " : "<color=#8A93A3>[  ] ") + GameData.Point(task.id).title + (got ? "" : "</color>"),
                    21, Palette.Ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                cell.enableAutoSizing = true;
                cell.fontSizeMin = 14;
                cell.fontSizeMax = 21;
            }

            var up = UIKit.Text(body, $"<color=#5A6372>UP NEXT</color>  <b>Chapter {next.number} - {next.title}</b>  <color=#5A6372>{next.subtitle}</color>", 22, Palette.Ink, TextAlignmentOptions.Center);
            up.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 94), new Vector2(1000, 44), new Vector2(0.5f, 0));
            up.enableAutoSizing = true;
            up.fontSizeMin = 14;
            up.fontSizeMax = 22;

            var menu = UIKit.Button(body, "Main menu", Palette.CardTint, Palette.InkSoft, SceneFlow.LoadMenu, 24);
            menu.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-300, 10), new Vector2(240, 76), new Vector2(0.5f, 0));
            var go = UIKit.Button(body, $"Continue to Chapter {next.number}", Palette.Mint, Palette.Ink, () => SceneFlow.LoadChapter(next.number), 30);
            go.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(130, 10), new Vector2(560, 76), new Vector2(0.5f, 0));
        }

        /// Timer ran out: big GAME OVER card. Retry reloads the chapter scene (its checkpoint), which rebuilds every
        /// drawer, item, task, alert, popup and the encryption / data section from scratch; the run clock keeps going.
        void ShowGameOver()
        {
            var gm = GameManager.I;
            var ch = gm.Chapter;
            var body = NewCard("GAME OVER", Palette.Red, new Vector2(1100, 760));
            var t = UIKit.Text(body,
                "<size=72><b>TIME'S UP</b></size>\n<size=34>The upload finished: the attacker got away with Project AURORA.</size>",
                30, Palette.Ink, TextAlignmentOptions.Center);
            t.rectTransform.Top(170);

            // what was done, what was left, and the first hint for the objective that stopped the player
            int doneCount = 0;
            var left = new System.Text.StringBuilder();
            foreach (var task in ch.tasks)
            {
                if (gm.IsDone(task.id)) { doneCount++; continue; }
                left.Append("[  ] ").Append(task.label).Append('\n');
            }
            var cur = GameData.Info(gm.CurrentObjective());
            string tip = cur.hints != null && cur.hints.Length > 0 ? cur.hints[Mathf.Min(1, cur.hints.Length - 1)] : "";
            var panel = UIKit.Panel(body, "Progress", Palette.CardTint);
            panel.rectTransform.Fill(0, 0, 180, 150);
            var p = UIKit.Text(panel.transform,
                $"<b>Objectives completed  {doneCount} / {ch.tasks.Length}</b>      Time  <b>{GameManager.FormatTime(gm.TimeLimit)}</b>      Hints  <b>{gm.HintsUsed}</b>      Mistakes  <b>{gm.Mistakes}</b>\n" +
                $"<color=#E5484D><b>Still to do</b></color>\n{left}" +
                (tip.Length > 0 ? $"<color=#2F6FD0><b>Tip for your retry:</b></color> {tip}" : ""),
                28, Palette.Ink, TextAlignmentOptions.TopLeft);
            p.rectTransform.Fill(24, 24, 14, 12);
            p.enableAutoSizing = true;
            p.fontSizeMin = 14;
            p.fontSizeMax = 28;
            string retry = ch.number == 2 ? "RETRY PHASE 2" : $"RETRY CHAPTER {ch.number}";
            var again = UIKit.Button(body, retry, Palette.Mint, Palette.Ink, () => gm.Restart(false), 40, "Btn_Retry");
            again.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-235, 20), new Vector2(440, 110), new Vector2(0.5f, 0));
            var menu = UIKit.Button(body, "MAIN MENU", Palette.CardTint, Palette.Ink, SceneFlow.LoadMenu, 40, "Btn_MainMenu");
            menu.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(235, 20), new Vector2(440, 110), new Vector2(0.5f, 0));
        }

        void ShowCertificate()
        {
            var body = NewCard("Certificate of Completion", Palette.Mint, new Vector2(1100, 760));
            float time = 0; int hints = 0, mistakes = 0;
            for (int i = 1; i <= GameData.Chapters.Length; i++)
            {
                var r = StoryProgress.Get(i);
                time += r.seconds; hints += r.hints; mistakes += r.mistakes;
            }
            // total = wall clock since New Game (intros, loads and retries included); falls back to the sum of the
            // chapter times when the chapter was started directly (editor / chapter select without a run)
            if (RunState.Running) time = RunState.Seconds;
            string rank = hints <= 3 && mistakes <= 3 ? "Elite Defender" : hints <= 9 ? "Senior Analyst" : "Analyst";
            var t = UIKit.Text(body,
                "<size=32>Congratulations! This certifies that you</size>\n<size=54><b>contained the Northwind breach</b></size>\n<size=24><color=#5A6372>" + GameData.Chapters[GameData.Chapters.Length - 1].outro.Replace("\n\n", " ") + "</color></size>",
                30, Palette.Ink, TextAlignmentOptions.Center);
            t.rectTransform.Top(200);
            t.enableAutoSizing = true;
            t.fontSizeMin = 16;
            t.fontSizeMax = 54;
            var stats = UIKit.Panel(body, "Stats", Palette.CardTint);
            stats.rectTransform.Top(290, 205, 40, 40);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=42>TOTAL TIME  <b>{GameManager.FormatTime(time)}</b></size>\n");
            sb.Append($"Rank  <b>{rank}</b>      Learning points  <b>{StoryProgress.PointCount} / {GameData.Points.Length}</b>\nHints  <b>{hints}</b>      Mistakes  <b>{mistakes}</b>\n");
            for (int i = 1; i <= GameData.Chapters.Length; i++)
            {
                var r = StoryProgress.Get(i);
                sb.Append($"<size=80%><color=#5A6372>Chapter {i} - {GameData.Chapters[i - 1].title}:</color>  {GameManager.FormatTime(r.seconds)}   hints {r.hints}   mistakes {r.mistakes}</size>\n");
            }
            var s = UIKit.Text(stats.transform, sb.ToString(), 28, Palette.Ink, TextAlignmentOptions.Center);
            s.rectTransform.Fill(20, 20, 12, 12);
            s.enableAutoSizing = true;
            s.fontSizeMin = 14;
            s.fontSizeMax = 28;
            var credits = UIKit.Button(body, "Credits", Palette.CardTint, Palette.Ink, ShowCredits, 30);
            credits.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-390, 10), new Vector2(160, 100), new Vector2(0.5f, 0));
            var again = UIKit.Button(body, "PLAY AGAIN", Palette.CardTint, Palette.Ink, () => { RunState.Reset(); SceneFlow.LoadChapter(1); }, 32, "Btn_PlayAgain");
            again.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-140, 10), new Vector2(300, 100), new Vector2(0.5f, 0));
            var menu = UIKit.Button(body, "RETURN TO MAIN MENU", Palette.Mint, Palette.Ink, SceneFlow.LoadMenu, 30, "Btn_MainMenu");
            menu.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(245, 10), new Vector2(430, 100), new Vector2(0.5f, 0));
        }

        public void ShowCredits()
        {
            var body = NewCard("Credits", Palette.Blue, new Vector2(1100, 760));
            var t = UIKit.Text(body, Credits.Text, 24, Palette.Ink, TextAlignmentOptions.Top);
            t.rectTransform.Fill(0, 0, 0, 100);
            var back = UIKit.Button(body, "Main menu", Palette.Mint, Palette.Ink, SceneFlow.LoadMenu, 28);
            back.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(320, 76), new Vector2(0.5f, 0));
        }

        /// Data Section: built only once encryption has finished (the passkey never exists before that), shown with
        /// the always-on-top popup in front of the player. The passkey is the exit-door override code.
        void ShowDataSection()
        {
            var gm = GameManager.I;
            string key = string.Join(" ", System.Array.ConvertAll(gm.DoorCode, d => d.ToString()));
            // one digit per secured containment area, in board order
            var areas = new System.Text.StringBuilder();
            for (int i = 0; i < GameData.Areas.Length && i < gm.DoorCode.Length; i++)
                areas.Append(i > 0 ? "    " : "").Append($"{GameData.Areas[i].name} <b>{gm.DoorCode[i]}</b>");
            PriorityPopup.I?.Notify("Data section unlocked",
                $"<align=center><size=70%><color=#5A6372>FINAL PASSKEY</color></size>\n<size=260%>{key}</size>\n<size=70%>Enter it on the exit-door keypad to reach Chapter 3.</size>\n\n" +
                $"<size=55%><color=#5A6372>Project AURORA is encrypted and the attacker's session is revoked.\nOne digit per secured area:  {areas}</color></size></align>",
                Palette.Mint, 25f);
        }

        /// Fade + scale-in for the end cards; they also draw on top of everything (OverlayUI) so they never clip.
        IEnumerator PopIn(Transform t)
        {
            OverlayUI.Apply(t, 4200);
            var cv = t.GetComponentInChildren<Canvas>(true);
            if (cv) cv.sortingOrder = 30;
            var group = cardCanvas.GetComponent<CanvasGroup>();
            if (!group) group = cardCanvas.gameObject.AddComponent<CanvasGroup>();
            for (float k = 0; k < 1; k += Time.unscaledDeltaTime / 0.3f)
            {
                float e = Mathf.SmoothStep(0, 1, k);
                group.alpha = e;
                t.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, e);
                yield return null;
            }
            group.alpha = 1;
            t.localScale = Vector3.one;
        }

        // ---------------------------------------------------------------- objective / wrist

        public void SetObjective(int index, int total, string label)
        {
            if (wristObjective) wristObjective.text = $"<size=70%><color=#2FBF85>OBJECTIVE {index}/{total}</color></size>\n{label}";
            // small side notification; the full checklist popup opens from the OBJ button / J key
            notifier?.Show($"Objective {index} / {total}", label, Palette.Mint, 6f);
            hintTierShown = 0;
        }

        void BuildWrist()
        {
            if (!wristCanvas) return;
            var wc = wristCanvas.GetComponentInParent<Canvas>();
            if (wc && wc.transform.parent) wristRoot = wc.transform.parent.gameObject;
            var bg = UIKit.Panel(wristCanvas, "Bg", new Color(0.07f, 0.09f, 0.12f, 0.92f));
            bg.rectTransform.Fill();
            wristObjective = UIKit.Text(bg.transform, "", 26, Color.white, TextAlignmentOptions.TopLeft);
            wristObjective.rectTransform.Fill(18, 18, 12, 110);
            wristObjective.enableAutoSizing = true;
            wristObjective.fontSizeMin = 14;
            wristObjective.fontSizeMax = 26;
            var menu = UIKit.Button(bg.transform, "MENU", Palette.Blue, Color.white, ToggleMenu, 24);
            menu.GetComponent<RectTransform>().At(Vector2.zero, new Vector2(14, 12), new Vector2(150, 88), Vector2.zero);
            var bag = UIKit.Button(bg.transform, "BAG", Palette.Mint, Palette.Ink, () => Inventory.I?.Toggle(), 24);
            bag.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(150, 88), new Vector2(0.5f, 0));
            var hint = UIKit.Button(bg.transform, "HINT", Palette.Yellow, Palette.Ink, ShowNextHint, 24);
            hint.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(-14, 12), new Vector2(150, 88), new Vector2(1, 0));
            // re-open the current objective popup
            var obj = UIKit.Button(bg.transform, "OBJ", Palette.Mint, Palette.Ink, () => PriorityPopup.I?.Reopen(), 24);
            obj.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-14, -10), new Vector2(160, 84), new Vector2(1, 1));
            wristObjective.rectTransform.Fill(18, 190, 12, 110);
        }

        /// X: shows or hides the wrist panel (objective, MENU / BAG / HINT).
        public void ToggleWrist()
        {
            if (!wristRoot) return;
            wristRoot.SetActive(!wristRoot.activeSelf);
            Sfx.Play(Sfx.I ? (wristRoot.activeSelf ? Sfx.I.open : Sfx.I.close) : null, 0.5f);
        }

        public void ShowNextHint()
        {
            if (GameManager.I.State != GameState.Playing) return;
            var text = GameManager.I.NextHint(out _, out int tier);
            Caption($"Hint {Mathf.Min(tier + 1, 3)}/3: {text}", Palette.Ink, 9f);
            Sfx.Play(Sfx.I ? Sfx.I.toast : null);
        }

        // ---------------------------------------------------------------- menu

        public void ToggleMenu() => Hub.I?.Toggle(0);

        public void OpenMenu() => Hub.I?.Open(0);

        public void CloseMenu() => Hub.I?.Close();

        public void HideCaption() => notifier?.HideChannel("caption");

        void OpenMenuOld()
        {
            if (Inventory.I != null && Inventory.I.IsOpen) Inventory.I.Close();
            menuOpen = true;
            menuRoot.gameObject.SetActive(true);
            PlaceInFront(menuRoot, 0.8f, -0.12f);
            Sfx.Play(Sfx.I ? Sfx.I.open : null, 0.6f);
            RebuildMenu();
        }

        void CloseMenuOld()
        {
            if (!menuOpen) return;
            menuOpen = false;
            menuRoot.gameObject.SetActive(false);
            Sfx.Play(Sfx.I ? Sfx.I.close : null, 0.6f);
        }

        void PlaceInFront(Transform t, float distance, float drop)
        {
            if (!head) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            t.position = head.position + fwd * distance + Vector3.up * drop;
            t.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        void RebuildMenu()
        {
            UIKit.Clear(menuCanvas);
            var body = UIKit.Card(menuCanvas, "Menu", $"Chapter {GameManager.I.chapter}: {GameManager.I.Chapter.title}", Palette.Blue, 80, 34);
            body.parent.GetComponent<RectTransform>().Fill();

            string[] tabs = { "Objectives", "Hint", "Learning points", "Settings" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(body, tabs[i], i == menuTab ? Palette.Blue : Palette.CardTint, i == menuTab ? Color.white : Palette.Ink, () => ShowTab(idx), 24);
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(i * 205, 0), new Vector2(195, 56), new Vector2(0, 1));
            }
            var close = UIKit.Button(body, "Close", Palette.Red, Color.white, CloseMenu, 24);
            close.GetComponent<RectTransform>().At(new Vector2(1, 1), Vector2.zero, new Vector2(110, 56), new Vector2(1, 1));

            menuBody = UIKit.Rect(body, "TabBody");
            menuBody.Fill(0, 0, 76, 0);
            ShowTab(menuTab);
        }

        void ShowTab(int tab)
        {
            if (menuTab != tab)
            {
                menuTab = tab;
                RebuildMenu();
                return;
            }
            UIKit.Clear(menuBody);
            objectiveText = null;
            var gm = GameManager.I;
            switch (tab)
            {
                case 0:
                {
                    var header = UIKit.Text(menuBody, ObjectiveLine(), 26, Palette.Ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
                    header.rectTransform.Top(80);
                    objectiveText = header;
                    var list = UIKit.Text(menuBody, "", 24, Palette.Ink);
                    list.rectTransform.Fill(0, 0, 90, 0);
                    var sb = new System.Text.StringBuilder();
                    foreach (var info in gm.Chapter.tasks)
                    {
                        bool d = gm.IsDone(info.id);
                        sb.Append(d ? "<color=#2FB45A>[x]</color>  <color=#8A93A3>" : "[  ]  ").Append(info.label).Append(d ? "</color>" : "").Append('\n');
                    }
                    list.text = sb.ToString();
                    break;
                }
                case 1:
                {
                    var cur = gm.CurrentObjective();
                    var info = GameData.Info(cur);
                    var t = UIKit.Text(menuBody, $"<b>Current objective</b>\n{info.label}\n\nHints get stronger each time you ask. The third one gives the answer.", 26, Palette.Ink);
                    t.rectTransform.Top(170);
                    var hintBox = UIKit.Panel(menuBody, "HintBox", Palette.Yellow.A(0.25f));
                    hintBox.rectTransform.Fill(0, 0, 180, 100);
                    var hintText = UIKit.Text(hintBox.transform, gm.HintTierFor(cur) == 0 ? "No hint used for this objective yet." : "Press 'Get hint' again for a stronger hint.", 28, Palette.Ink, TextAlignmentOptions.MidlineLeft);
                    hintText.rectTransform.Fill(24, 24, 16, 16);
                    var b = UIKit.Button(menuBody, "Get hint", Palette.Yellow, Palette.Ink, () =>
                    {
                        var text = gm.NextHint(out _, out int tier);
                        hintText.text = $"<b>Hint {Mathf.Min(tier + 1, 3)} / 3</b>\n{text}";
                        Sfx.Play(Sfx.I ? Sfx.I.toast : null);
                    }, 28);
                    b.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(260, 72), new Vector2(0.5f, 0));
                    var used = UIKit.Text(menuBody, $"Hints used: {gm.HintsUsed}", 22, Palette.InkSoft, TextAlignmentOptions.Right);
                    used.rectTransform.At(new Vector2(1, 0), new Vector2(0, 30), new Vector2(240, 40), new Vector2(1, 0));
                    break;
                }
                case 2:
                {
                    int n = 0;
                    foreach (var task in gm.Chapter.tasks) if (GameData.HasPoint(task.id) && (gm.IsDone(task.id) || StoryProgress.HasPoint(task.id))) n++;
                    int total = 0;
                    foreach (var task in gm.Chapter.tasks) if (GameData.HasPoint(task.id)) total++;
                    var title = UIKit.Text(menuBody, $"Learning points - this chapter {n}/{total}    <color=#5A6372>(story total {StoryProgress.PointCount}/{GameData.Points.Length})</color>", 24, Palette.Ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
                    title.rectTransform.Top(44);
                    var grid = UIKit.Rect(menuBody, "Grid");
                    grid.Fill(0, 0, 50, 0);
                    var g = grid.gameObject.AddComponent<GridLayoutGroup>();
                    g.cellSize = new Vector2(300, 100);
                    g.spacing = new Vector2(10, 8);
                    foreach (var task in gm.Chapter.tasks)
                    {
                        if (!GameData.HasPoint(task.id)) continue;
                        var p = GameData.Point(task.id);
                        bool d = gm.IsDone(task.id) || StoryProgress.HasPoint(task.id);
                        var cell = UIKit.Panel(grid, "LP", d ? Palette.Mint.A(0.35f) : Palette.CardTint);
                        var txt = UIKit.Text(cell.transform, d ? $"<b>{p.title}</b>\n<size=15>{p.text}</size>" : "<b>???</b>\n<size=15>Complete an objective to unlock.</size>", 19, d ? Palette.Ink : Palette.InkSoft);
                        txt.rectTransform.Fill(10, 10, 6, 6);
                        txt.overflowMode = TextOverflowModes.Ellipsis;
                    }
                    break;
                }
                case 3:
                {
                    var col = UIKit.Rect(menuBody, "Settings");
                    col.Fill();
                    int row = 0;
                    Row(col, row++, "Quality", "Headset render resolution", new[] { "Low", "Medium", "High" }, Quality.Level, i => Quality.Set(i));
                    if (gm.HasTimer)
                        Row(col, row++, "Difficulty", "Changing it restarts the chapter", new[] { "Normal", "Experienced" }, GameManager.Experienced ? 1 : 0, i => gm.SetDifficulty(i == 1));
                    Row(col, row++, "Ambience", "Background room sound", new[] { "On", "Off" }, Sfx.AmbienceOn ? 0 : 1, i => Sfx.I.SetAmbience(i == 0));
                    Row(col, row++, "Guide marker", "Green marker over the next object", new[] { "On", "Off" }, Guide.Enabled ? 0 : 1, i => Guide.Enabled = i == 0);
                    var restart = UIKit.Button(col, "Restart chapter", Palette.Red, Color.white, () => gm.Restart(false), 24);
                    restart.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(-170, 10), new Vector2(300, 60), new Vector2(0.5f, 0));
                    var menu = UIKit.Button(col, "Main menu", Palette.CardTint, Palette.Ink, SceneFlow.LoadMenu, 24);
                    menu.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(170, 10), new Vector2(300, 60), new Vector2(0.5f, 0));
                    break;
                }
            }
        }

        void Row(RectTransform parent, int row, string label, string sub, string[] options, int selected, System.Action<int> onPick)
        {
            float y = -row * 92;
            var l = UIKit.Text(parent, $"<b>{label}</b>\n<size=18><color=#5A6372>{sub}</color></size>", 26, Palette.Ink);
            l.rectTransform.At(new Vector2(0, 1), new Vector2(0, y), new Vector2(330, 80), new Vector2(0, 1));
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(parent, options[i], i == selected ? Palette.Blue : Palette.CardTint, i == selected ? Color.white : Palette.Ink, () =>
                {
                    onPick(idx);
                    RebuildMenu();
                }, 22);
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2(350 + i * 175, y - 8), new Vector2(165, 58), new Vector2(0, 1));
            }
        }

        public string ObjectiveLine()
        {
            var gm = GameManager.I;
            string timer = gm.HasTimer ? $"Exfiltration {gm.Exfil01 * 100:0}%   |   Time left {GameManager.FormatTime(gm.TimeLimit - gm.Elapsed)}\n" : $"Time {GameManager.FormatTime(gm.Elapsed)}\n";
            return timer + "Next: " + GameData.Info(gm.CurrentObjective()).label;
        }

        // ---------------------------------------------------------------- HUD

        void BuildHud()
        {
            notifier = gameObject.AddComponent<SideNotifier>();
            notifier.Init(hudCanvas);

            // toasts, captions and subtitles draw over everything: they can't hide behind geometry
            OverlayUI.Apply(hudCanvas.parent);

            var follow = hudCanvas.GetComponentInParent<LazyFollow>();
            if (follow) { follow.target = head; follow.Snap(); }
        }

        public void Toast(string title, string body, Color accent, float seconds = 4.5f) => notifier?.Show(title, body, accent, Mathf.Min(seconds, 9f));

        /// Short message (hints, item captions, warnings): a small side card that fades away.
        /// The colour picks the accent: red / green keep their meaning, anything else is the neutral blue.
        public void Caption(string text, Color color, float seconds = 5f)
        {
            Color accent = color == Palette.Red ? Palette.Red : color == Palette.Green ? Palette.Green : Palette.Blue;
            notifier?.Show("", text, accent, Mathf.Min(seconds, 7f), "caption");
        }

        /// Radio subtitle for the narrator (null text hides it).
        public void Subtitle(string speaker, string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) { notifier?.HideChannel("radio"); return; }
            notifier?.Show(speaker, text, Palette.Yellow, seconds, "radio");
        }
    }

    /// Player preference for the guide marker.
    public static class Guide
    {
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("cser.guide", 1) == 1;
            set
            {
                PlayerPrefs.SetInt("cser.guide", value ? 1 : 0);
                if (!value) GuideBeacon.I?.Hide();
            }
        }
    }

    /// Quest-friendly quality presets: scales the eye-buffer resolution.
    public static class Quality
    {
        public static int Level = 2;
        static readonly float[] Scales = { 0.8f, 1.0f, 1.2f };

        public static void Set(int level)
        {
            Level = Mathf.Clamp(level, 0, 2);
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = Scales[Level];
        }
    }
}
