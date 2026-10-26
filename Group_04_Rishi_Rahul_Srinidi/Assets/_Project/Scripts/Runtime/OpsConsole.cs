using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Chapter 3 consoles: network switch (quarantine VLAN), backup restore and the incident report.
    public class OpsConsole : MonoBehaviour
    {
        public enum Mode { Switch, Backup, Report }
        public Mode mode;
        public RectTransform screen; // 1280 x 800

        RectTransform root;
        int selectedPort = -1;
        int selectedBackup = -1;
        int question;
        float restore = -1;
        string feedback;

        static readonly string[] PortNames =
        {
            "srv-dc-01", "srv-dc-02", "srv-file-01", "srv-file-02", "srv-mail", "srv-web-01", "srv-web-02", "srv-db-01",
            "srv-db-02", "backup-nas", "ups-mgmt", "kvm-01", "printer-2f", "UNKNOWN", "ap-floor3", "ap-floor2",
            "cam-lobby", "cam-server", "badge-ctrl", "voip-gw", "srv-log-01", "siem-01", "spare", "uplink-core",
        };

        static readonly (string name, string detail, bool correct, string why)[] Backups =
        {
            ("Tonight 00:00", "nightly-fileserver - verified clean (hash OK)", true, null),
            ("Tonight 02:00", "taken after the breach began at 01:58", false, "That snapshot was taken after the breach at 01:58: it may contain the attacker's changes."),
            ("Tonight 02:30", "INTEGRITY CHECK FAILED", false, "Never restore a backup that fails its integrity check."),
        };

        static readonly (string q, string[] options, int correct, string why)[] Report =
        {
            ("1/3  How did the attacker first get into Fred's account?",
                new[] { "Fred's weak, guessable password", "A broken firewall", "A power outage" }, 0, "The logs show a normal login with Fred's password: 'Fluffy2019!' was guessed."),
            ("2/3  What stopped the theft of Project AURORA?",
                new[] { "Turning off the printer", "Encrypting AURORA and revoking the attacker's access", "Deleting Fred's emails" }, 1, "Encryption made the stolen data useless and revocation kicked the attacker out."),
            ("3/3  What should Northwind change first?",
                new[] { "Nothing, it was bad luck", "Ban USB sticks only", "Awareness training and 2FA for everyone" }, 2, "Most of tonight's mistakes were human: training plus 2FA closes the biggest gaps."),
        };

        void Start()
        {
            root = UIKit.ContentRoot(screen);
            GameManager.I.TaskCompleted += _ => Rebuild();
            Rebuild();
        }

        void Rebuild()
        {
            UIKit.Clear(root);
            var bg = UIKit.Panel(root, "Bg", Palette.Hex("0B131E"), false);
            bg.rectTransform.Fill();
            var bar = UIKit.Panel(bg.transform, "Bar", Palette.Hex("15243A"), false);
            bar.rectTransform.Top(70);
            string title = mode == Mode.Switch ? "CORE SWITCH  sw-core-01  -  port management" : mode == Mode.Backup ? "BACKUP CONSOLE  -  srv-file-01" : "INCIDENT REPORT  -  INC-2026-1021";
            var t = UIKit.Text(bar.transform, "<b>" + title + "</b>", 26, Color.white, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.Fill(28, 28, 0, 0);
            var body = UIKit.Rect(bg.transform, "Body");
            body.Fill(40, 40, 90, 60);
            switch (mode)
            {
                case Mode.Switch: BuildSwitch(body); break;
                case Mode.Backup: BuildBackup(body); break;
                case Mode.Report: BuildReport(body); break;
            }
            if (!string.IsNullOrEmpty(feedback))
            {
                var f = UIKit.Text(bg.transform, feedback, 22, Palette.Hex("FF8A8D"), TextAlignmentOptions.Center);
                f.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(1200, 44), new Vector2(0.5f, 0));
            }
        }

        void Done(RectTransform body, string text)
        {
            var d = UIKit.Text(body, text, 34, Palette.Terminal, TextAlignmentOptions.Center);
            d.rectTransform.Fill();
        }

        void BuildSwitch(RectTransform body)
        {
            if (GameManager.I.IsDone(TaskId.SRV_Isolate)) { Done(body, "<b>Port 14 moved to QUARANTINE VLAN 999</b>\nNo other unknown devices on the network."); return; }
            var h = UIKit.Text(body, "Select a port to see what's connected:", 24, Palette.Hex("C9D6E8"));
            h.rectTransform.Top(36);
            for (int i = 0; i < 24; i++)
            {
                int idx = i;
                bool unknown = PortNames[i] == "UNKNOWN";
                var c = i == selectedPort ? Palette.Blue : unknown ? Palette.Orange : Palette.Hex("1F7A4D");
                var b = UIKit.Button(body, (i + 1).ToString(), c, Color.white, () => { selectedPort = idx; feedback = null; Rebuild(); }, 28, "Port" + (i + 1));
                b.GetComponent<RectTransform>().At(new Vector2(0, 1), new Vector2((i % 12) * 99, -50 - (i / 12) * 96), new Vector2(90, 86), new Vector2(0, 1));
            }
            if (selectedPort < 0) return;
            bool rogue = PortNames[selectedPort] == "UNKNOWN";
            var info = UIKit.Panel(body, "Info", Palette.Hex("15243A"));
            info.rectTransform.Fill(0, 0, 260, 110);
            var it = UIKit.Text(info.transform, rogue
                ? $"<b>Port {selectedPort + 1}</b>   VLAN 20 (servers)\nDevice: <color=#F07A22><b>UNKNOWN</b></color>   MAC b8:27:eb:4f:21:9c   vendor: <b>Raspberry Pi Foundation</b>\nNo asset tag - first seen tonight 01:41"
                : $"<b>Port {selectedPort + 1}</b>   VLAN 20 (servers)\nDevice: <b>{PortNames[selectedPort]}</b>   asset tag NW-{1000 + selectedPort * 17}\nKnown device - in the asset register", 24, Color.white);
            it.rectTransform.Fill(24, 24, 16, 16);
            var q = UIKit.Button(body, "Move to QUARANTINE VLAN 999", Palette.Red, Color.white, () =>
            {
                if (rogue) { feedback = null; GameManager.I.Complete(TaskId.SRV_Isolate); }
                else
                {
                    feedback = $"{PortNames[selectedPort]} is a legitimate server: quarantining it would cause an outage.";
                    GameManager.I.Mistake(feedback);
                    Rebuild();
                }
            }, 28);
            q.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(560, 90), new Vector2(0.5f, 0));
        }

        void BuildBackup(RectTransform body)
        {
            if (GameManager.I.IsDone(TaskId.SRV_Restore)) { Done(body, "<b>srv-file-01 restored from the 00:00 snapshot</b>\nIntegrity verified. Users won't lose a byte."); return; }
            if (restore >= 0)
            {
                Done(body, $"Restoring 00:00 snapshot...  <b>{restore * 100:0}%</b>");
                return;
            }
            var h = UIKit.Text(body, "The attacker touched srv-file-01 tonight (breach began <b>01:58</b>). Pick a snapshot to restore:", 24, Palette.Hex("C9D6E8"));
            h.rectTransform.Top(70);
            for (int i = 0; i < Backups.Length; i++)
            {
                int idx = i;
                var bk = Backups[i];
                var b = UIKit.Button(body, $"<b>{bk.name}</b>\n<size=20>{bk.detail}</size>", i == selectedBackup ? Palette.Blue : Palette.Hex("24426B"), Color.white, () => { selectedBackup = idx; feedback = null; Rebuild(); }, 26, "Backup" + i);
                b.GetComponent<RectTransform>().Top(130, 80 + i * 142, 0, 0);
            }
            if (selectedBackup < 0) return;
            var r = UIKit.Button(body, "Restore selected snapshot", Palette.Green, Color.white, () =>
            {
                var bk = Backups[selectedBackup];
                if (bk.correct) StartCoroutine(Restore());
                else
                {
                    feedback = bk.why;
                    GameManager.I.Mistake(bk.why);
                    Rebuild();
                }
            }, 28);
            r.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), Vector2.zero, new Vector2(520, 86), new Vector2(0.5f, 0));
        }

        IEnumerator Restore()
        {
            for (restore = 0; restore < 1; restore += Time.deltaTime / 3.5f)
            {
                if (Time.frameCount % 8 == 0) Rebuild();
                yield return null;
            }
            restore = -1;
            GameManager.I.Complete(TaskId.SRV_Restore);
        }

        void BuildReport(RectTransform body)
        {
            if (GameManager.I.IsDone(TaskId.SRV_Report)) { Done(body, "<b>Report filed.</b>\nIncident INC-2026-1021 closed."); return; }
            if (!GameManager.I.IsDone(TaskId.SRV_Restore))
            {
                Done(body, "<color=#C9D6E8>Finish the containment and recovery steps before writing the report.</color>");
                return;
            }
            var (q, options, correct, why) = Report[question];
            var h = UIKit.Text(body, "<b>" + q + "</b>", 30, Color.white);
            h.rectTransform.Top(90);
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(body, options[i], Palette.Hex("24426B"), Color.white, () =>
                {
                    if (idx == correct)
                    {
                        feedback = null;
                        GameUI.I?.Caption(why, Palette.Ink, 6f);
                        question++;
                        if (question >= Report.Length) GameManager.I.Complete(TaskId.SRV_Report);
                        else Rebuild();
                    }
                    else
                    {
                        feedback = "Think about what actually happened tonight.";
                        GameManager.I.Mistake(feedback);
                        Rebuild();
                    }
                }, 26, "Report" + i);
                b.GetComponent<RectTransform>().Top(96, 120 + i * 112, 0, 0);
            }
        }

        public void DebugSolve()
        {
            switch (mode)
            {
                case Mode.Switch: GameManager.I.Complete(TaskId.SRV_Isolate); break;
                case Mode.Backup: GameManager.I.Complete(TaskId.SRV_Restore); break;
                case Mode.Report: GameManager.I.Complete(TaskId.SRV_Report); break;
            }
        }
    }
}
