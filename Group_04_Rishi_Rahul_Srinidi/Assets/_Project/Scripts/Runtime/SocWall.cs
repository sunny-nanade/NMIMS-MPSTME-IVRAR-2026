using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Decorative-but-alive SOC video wall: traffic graphs, event feed and threat level that reacts to the story.
    public class SocWall : MonoBehaviour
    {
        public enum Kind { Traffic, Feed, Threat }
        public Kind kind;
        public RectTransform canvas; // 960 x 540

        RectTransform root;
        Image[] bars;
        TMP_Text feed, level;
        Image levelBg;
        float t;
        int line;

        static readonly string[] Events =
        {
            "auth  ok   vpn  m.jansen      office-ams",
            "dns   ok   srv-dc-01   updates.microsoft.com",
            "fw    drop 185.220.101.47 -> 10.0.3.7:445",
            "auth  ok   sso  a.visser     web",
            "edr   ok   scan complete  FIN-PC-11",
            "proxy ok   10.0.3.21 -> teams.microsoft.com",
            "fw    drop 45.155.205.9 -> 10.0.0.1:22",
            "mail  ok   inbound 214 msgs  spam 31",
        };

        void Start()
        {
            root = UIKit.ContentRoot(canvas);
            var bg = UIKit.Panel(root, "Bg", Palette.Hex("08101A"), false);
            bg.rectTransform.Fill();
            var frame = UIKit.Panel(bg.transform, "Head", Palette.Hex("12213A"), false);
            frame.rectTransform.Top(56);
            var title = UIKit.Text(frame.transform, kind == Kind.Traffic ? "OUTBOUND TRAFFIC (MB/min)" : kind == Kind.Feed ? "LIVE EVENT FEED" : "THREAT LEVEL", 28, Palette.Hex("7FA6D9"), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            title.rectTransform.Fill(24, 24, 0, 0);
            switch (kind)
            {
                case Kind.Traffic:
                    bars = new Image[32];
                    for (int i = 0; i < bars.Length; i++)
                    {
                        bars[i] = UIKit.Panel(bg.transform, "Bar", Palette.Hex("2F6FD0"), false);
                        bars[i].rectTransform.anchorMin = bars[i].rectTransform.anchorMax = new Vector2(0, 0);
                        bars[i].rectTransform.pivot = new Vector2(0, 0);
                        bars[i].rectTransform.anchoredPosition = new Vector2(30 + i * 28, 30);
                        bars[i].rectTransform.sizeDelta = new Vector2(20, 40);
                    }
                    break;
                case Kind.Feed:
                    feed = UIKit.Text(bg.transform, "", 24, Palette.Terminal, TextAlignmentOptions.BottomLeft);
                    feed.rectTransform.Fill(24, 24, 70, 20);
                    break;
                case Kind.Threat:
                    levelBg = UIKit.Panel(bg.transform, "Level", Palette.Green);
                    levelBg.rectTransform.Fill(40, 40, 90, 40);
                    level = UIKit.Text(levelBg.transform, "", 92, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                    level.rectTransform.Fill();
                    break;
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            var gm = GameManager.I;
            bool alert = gm && (gm.IsDone(TaskId.SOC_Badge) || gm.chapter > 1);
            bool escalated = gm && gm.IsDone(TaskId.SOC_Escalate);
            if (bars != null)
                for (int i = 0; i < bars.Length; i++)
                {
                    float h = 30 + 50 * Mathf.PerlinNoise(i * 0.31f, t * 0.6f);
                    if (alert && i > 22) h += (i - 22) * 38;
                    bars[i].rectTransform.sizeDelta = new Vector2(20, h);
                    bars[i].color = alert && i > 22 ? Palette.Red : Palette.Hex("2F6FD0");
                }
            if (feed && t > 0.9f)
            {
                t = 0;
                line++;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 9; i++)
                {
                    int k = (line + i) % Events.Length;
                    sb.Append($"02:{(line + i) % 60:00}:{(k * 7) % 60:00}  ").Append(Events[k]).Append('\n');
                }
                if (alert) sb.Append("<color=#FF5A5F>dlp   ALERT FRED-PC-07 -> cdn-sync[.]xyz  812 MB</color>");
                feed.text = sb.ToString();
            }
            if (level)
            {
                level.text = escalated ? "CRITICAL\n<size=40%>INCIDENT DECLARED</size>" : alert ? "HIGH\n<size=40%>1 critical alert</size>" : "LOW\n<size=40%>all systems normal</size>";
                levelBg.color = escalated ? Color.Lerp(Palette.Red, Palette.Hex("8E1B1F"), Mathf.PingPong(Time.time * 1.5f, 1)) : alert ? Palette.Orange : Palette.Green;
            }
        }
    }
}
