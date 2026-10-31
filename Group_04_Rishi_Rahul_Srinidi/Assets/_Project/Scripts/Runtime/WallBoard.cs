using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Wall display: exfiltration countdown + Containment Board revealing one door-code digit per area.
    public class WallBoard : MonoBehaviour
    {
        public RectTransform canvas; // 900 x 560

        TMP_Text timer, percent;
        Image bar;
        readonly TMP_Text[] digits = new TMP_Text[4];
        readonly Image[] tiles = new Image[4];

        void Start()
        {
            var bg = UIKit.Panel(UIKit.ContentRoot(canvas), "Bg", Palette.Dark);
            bg.rectTransform.Fill();
            var title = UIKit.Text(bg.transform, "CONTAINMENT BOARD", 34, Palette.Terminal, TextAlignmentOptions.Center, FontStyles.Bold);
            title.rectTransform.Top(56, 14);

            timer = UIKit.Text(bg.transform, "25:00", 80, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            timer.rectTransform.Top(90, 70);
            var exLabel = UIKit.Text(bg.transform, "EXFILTRATION OF PROJECT AURORA", 20, Palette.Red, TextAlignmentOptions.Left, FontStyles.Bold);
            exLabel.rectTransform.Top(30, 170, 40, 40);
            percent = UIKit.Text(bg.transform, "0%", 22, Palette.Red, TextAlignmentOptions.Right, FontStyles.Bold);
            percent.rectTransform.Top(30, 170, 40, 40);
            var track = UIKit.Panel(bg.transform, "Track", Color.white.A(0.12f));
            track.rectTransform.Top(26, 206, 40, 40);
            bar = UIKit.Panel(track.transform, "Bar", ProgressColor(0));
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;

            for (int i = 0; i < 4; i++)
            {
                tiles[i] = UIKit.Panel(bg.transform, "Area" + i, Color.white.A(0.08f));
                tiles[i].rectTransform.At(new Vector2(0, 0), new Vector2(40 + i * 210, 30), new Vector2(190, 250), Vector2.zero);
                var name = UIKit.Text(tiles[i].transform, GameData.Areas[i].name, 24, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                name.rectTransform.Top(44, 10);
                digits[i] = UIKit.Text(tiles[i].transform, "?", 110, Color.white.A(0.35f), TextAlignmentOptions.Center, FontStyles.Bold);
                digits[i].rectTransform.Fill(0, 0, 50, 10);
            }
            GameManager.I.AreaCompleted += _ => Refresh();
            Refresh();
        }

        void Refresh()
        {
            var gm = GameManager.I;
            for (int i = 0; i < 4; i++)
            {
                bool d = gm.AreaDone(i);
                digits[i].text = d ? gm.DoorCode[i].ToString() : "?";
                digits[i].color = d ? Palette.Terminal : Color.white.A(0.35f);
                tiles[i].color = d ? Palette.Terminal.A(0.18f) : Color.white.A(0.08f);
            }
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || !timer) return;
            timer.text = GameManager.FormatTime(gm.TimeLimit - gm.Elapsed);
            percent.text = $"{gm.Exfil01 * 100:0}%";
            bar.rectTransform.anchorMax = new Vector2(gm.Exfil01, 1);
            bar.color = percent.color = ProgressColor(gm.Exfil01);
            bool contained = gm.IsDone(TaskId.EncryptData);
            timer.color = contained ? Palette.Terminal : gm.Exfil01 > 0.8f && Mathf.Repeat(Time.time, 1f) > 0.5f ? Palette.Red : Color.white;
        }

        /// Green while there is plenty of time, orange at the halfway mark, red as the upload nears 100%.
        public static Color ProgressColor(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? Color.Lerp(Palette.Green, Palette.Orange, t / 0.5f) : Color.Lerp(Palette.Orange, Palette.Red, (t - 0.5f) / 0.5f);
        }
    }
}
