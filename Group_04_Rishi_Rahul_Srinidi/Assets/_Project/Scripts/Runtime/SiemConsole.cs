using System.Collections;
using TMPro;
using UnityEngine;

namespace CSER
{
    /// Chapter 1 analyst console: alert queue -> log investigation (which host? what attack?) -> escalate.
    public class SiemConsole : MonoBehaviour
    {
        public RectTransform screen; // 1280 x 800 canvas
        RectTransform root;
        bool alertArrived;
        string feedback;

        static readonly (string sev, string text)[] Benign =
        {
            ("LOW", "Failed login (1x) - ANNA-PC-03 - user anna.v"),
            ("MEDIUM", "Port scan blocked at perimeter firewall"),
            ("LOW", "Antivirus definitions updated on 412 endpoints"),
            ("INFO", "Backup job 'nightly-fileserver' finished OK - 00:00"),
        };

        static readonly string[] Logs =
        {
            "01:52  ANNA-PC-03    anna.v       sharepoint.northwind.local     2.1 MB",
            "01:56  PRINTER-2F    -            print.northwind.local          0.3 MB",
            "01:58  FRED-PC-07    svc_backup   cdn-sync[.]xyz  (unknown)    214.0 MB",
            "02:01  MAIL-GW       -            relay.northwind.local          4.4 MB",
            "02:04  FRED-PC-07    svc_backup   cdn-sync[.]xyz  (unknown)    401.6 MB",
            "02:07  ANNA-PC-03    anna.v       teams.microsoft.com            0.8 MB",
            "02:09  FRED-PC-07    svc_backup   cdn-sync[.]xyz  (unknown)    196.4 MB",
        };

        void Start()
        {
            root = UIKit.ContentRoot(screen);
            GameManager.I.TaskCompleted += t =>
            {
                if (t == TaskId.SOC_Badge) StartCoroutine(AlertArrives());
                Rebuild();
            };
            Rebuild();
        }

        IEnumerator AlertArrives()
        {
            yield return new WaitForSeconds(2.5f);
            alertArrived = true;
            Sfx.PlayAt(Sfx.I ? Sfx.I.glitch : null, transform.position, 0.8f);
            Rebuild();
        }

        void Rebuild()
        {
            UIKit.Clear(root);
            var bg = UIKit.Panel(root, "Bg", Palette.Hex("0E1622"), false);
            bg.rectTransform.Fill();
            var bar = UIKit.Panel(bg.transform, "Bar", Palette.Hex("15243A"), false);
            bar.rectTransform.Top(70);
            var title = UIKit.Text(bar.transform, "<b>NORTHWIND SIEM</b>   <color=#7FA6D9>Security Operations Centre - night shift</color>", 26, Color.white, TextAlignmentOptions.MidlineLeft);
            title.rectTransform.Fill(28, 28, 0, 0);
            var gm = GameManager.I;
            if (!gm.IsDone(TaskId.SOC_OpenAlert)) BuildQueue(bg.transform);
            else BuildInvestigation(bg.transform);
        }

        void BuildQueue(Transform parent)
        {
            var head = UIKit.Text(parent, alertArrived ? "<color=#FF5A5F><b>1 CRITICAL ALERT</b></color>  - select it to investigate" : "Alert queue - all quiet", 30, Color.white);
            head.rectTransform.Top(60, 90, 40, 40);
            int row = 0;
            if (alertArrived)
            {
                var crit = UIKit.Button(parent, "", Palette.Red, Color.white, () =>
                {
                    GameManager.I.Complete(TaskId.SOC_OpenAlert);
                }, 24, "Alert_Critical");
                crit.GetComponent<RectTransform>().Top(96, 160, 40, 40);
                var l = crit.GetComponentInChildren<TMP_Text>();
                l.alignment = TextAlignmentOptions.MidlineLeft;
                l.text = "<b>CRITICAL</b>    Unusual outbound transfer: 812 MB to unknown domain cdn-sync[.]xyz\n<size=20>02:09  -  rule DLP-07 'Large upload outside business hours'  -  point here and pull the trigger</size>";
                crit.gameObject.AddComponent<Pulse>();
                row = 1;
            }
            foreach (var (sev, text) in Benign)
            {
                var r = UIKit.Panel(parent, "Row", Palette.Hex("17263B"));
                r.rectTransform.Top(70, 160 + row * 108 + (row > 0 && alertArrived ? 0 : 0), 40, 40);
                var t = UIKit.Text(r.transform, $"<color=#{SevColor(sev)}><b>{sev}</b></color>    {text}", 24, Palette.Hex("C9D6E8"), TextAlignmentOptions.MidlineLeft);
                t.rectTransform.Fill(24, 24, 0, 0);
                row++;
            }
        }

        static string SevColor(string sev) => sev == "MEDIUM" ? "F5C518" : sev == "LOW" ? "5BF0B0" : "7FA6D9";

        void BuildInvestigation(Transform parent)
        {
            var gm = GameManager.I;
            var head = UIKit.Text(parent, "<color=#FF5A5F><b>CRITICAL</b></color>  Unusual outbound transfer - proxy logs 01:50 - 02:10", 26, Color.white);
            head.rectTransform.Top(44, 84, 40, 40);
            var logBox = UIKit.Panel(parent, "Logs", Palette.Hex("08101A"));
            logBox.rectTransform.Top(270, 132, 40, 40);
            // <mspace> gives the log a fixed-width terminal look so the columns line up
            var lt = UIKit.Text(logBox.transform, "<mspace=0.56em><color=#7FA6D9>TIME   HOST          USER         DESTINATION                     SENT</color>\n" + string.Join("\n", Logs) + "</mspace>", 22, Palette.Terminal, TextAlignmentOptions.TopLeft);
            lt.rectTransform.Fill(20, 20, 14, 10);

            var q = UIKit.Rect(parent, "Questions");
            q.Fill(40, 40, 420, 20);
            if (!gm.IsDone(TaskId.SOC_FindHost))
                Question(q, "Q1. Which machine is compromised?", new[] { "ANNA-PC-03", "FRED-PC-07", "PRINTER-2F", "MAIL-GW" }, 1, TaskId.SOC_FindHost,
                    "Look at the SENT column: which host uploads hundreds of MB to an unknown domain?");
            else if (!gm.IsDone(TaskId.SOC_Classify))
                Question(q, "Q2. FRED-PC-07 is sending 812 MB to an unknown server at night. What is this?", new[] { "Ransomware", "Data exfiltration", "Phishing email", "DDoS attack" }, 1, TaskId.SOC_Classify,
                    "Nothing is encrypted or flooded: data is being copied OUT of the company.");
            else if (!gm.IsDone(TaskId.SOC_Escalate))
            {
                var t = UIKit.Text(q, "<b>Confirmed: data exfiltration from FRED-PC-07 (Fred Dekker, Floor 3).</b>\nAn active attack must be escalated immediately.", 26, Color.white);
                t.rectTransform.Top(90);
                var b = UIKit.Button(q, "Declare CRITICAL incident", Palette.Red, Color.white, () => GameManager.I.Complete(TaskId.SOC_Escalate), 30);
                b.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(560, 90), new Vector2(0.5f, 0));
                b.gameObject.AddComponent<Pulse>();
            }
            else
            {
                var t = UIKit.Text(q, "<color=#5BF0B0><b>INCIDENT INC-2026-1021 DECLARED</b></color>\nResponse team paged. Analyst on site: <b>you</b>.\nGo to Fred's office (Floor 3) and contain the breach.", 30, Color.white, TextAlignmentOptions.Center);
                t.rectTransform.Fill();
            }
            if (!string.IsNullOrEmpty(feedback))
            {
                var f = UIKit.Text(parent, feedback, 22, Palette.Hex("FF8A8D"), TextAlignmentOptions.Center);
                f.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(1200, 40), new Vector2(0.5f, 0));
            }
        }

        void Question(RectTransform parent, string question, string[] options, int correct, TaskId task, string wrongHint)
        {
            var t = UIKit.Text(parent, $"<b>{question}</b>", 27, Color.white);
            t.rectTransform.Top(80);
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(parent, options[i], Palette.Hex("24426B"), Color.white, () =>
                {
                    if (idx == correct)
                    {
                        feedback = null;
                        GameManager.I.Complete(task);
                    }
                    else
                    {
                        feedback = wrongHint;
                        GameManager.I.Mistake("Not quite. " + wrongHint);
                        Rebuild();
                    }
                }, 26, "Answer_" + options[i]);
                b.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(i * 300, 40), new Vector2(285, 90), Vector2.zero);
            }
        }

        public void DebugAnswerAll()
        {
            var gm = GameManager.I;
            foreach (var t in new[] { TaskId.SOC_OpenAlert, TaskId.SOC_FindHost, TaskId.SOC_Classify, TaskId.SOC_Escalate }) gm.Complete(t);
        }
    }

    /// Gently pulses a UI element's scale to draw the eye.
    public class Pulse : MonoBehaviour
    {
        public float amount = 0.025f, speed = 3.5f;
        void Update() => transform.localScale = Vector3.one * (1 + Mathf.Sin(Time.time * speed) * amount);
    }
}
