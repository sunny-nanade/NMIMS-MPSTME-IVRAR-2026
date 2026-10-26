using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Fred's monitor: a fake in-game browser/OS on a world-space canvas (ray + poke via ISDK).
    /// Flow: unlocked desktop with live attacker session -> lock -> SecureMail login (guess) ->
    /// forced password change -> tabs: SecureMail (phishing), TeamChat (social engineering),
    /// Account Security (2FA), SOC Console (encrypt + revoke).
    public class Workstation : MonoBehaviour
    {
        public RectTransform screen;     // 1280 x 800 canvas
        [Tooltip("Optional floating keyboard canvas (1120 x 560, ray + poke) in front of the monitor; 6 x 6 cm keys. " +
                 "Without it the keyboard is drawn on the bottom of the screen.")]
        public RectTransform keyboardCanvas;
        public string fredEmail = "fred.dekker@northwind.local";
        public string fredPassword = "Fluffy2019!";

        enum Mode { Desktop, Login, ChangePassword, Main }
        Mode mode;
        int tab;
        RectTransform content;
        RectTransform keyboard;
        RectTransform kbContent;   // floating panel content (null = keyboard on the screen)
        GameObject kbPanel;        // shown only while a text field is active
        Field activeField;
        bool shift, symbols;
        int loginAttempts;
        string terminalLog = "";
        Coroutine terminalCo;

        // ------------------------------------------------------------ email data
        class Mail
        {
            public string from, address, subject, body, verdict;
            public bool phishing;
            public int state; // 0 not tagged yet, 1 tagged correctly, 2 tagged wrongly
        }

        readonly List<Mail> mails = new List<Mail>
        {
            new Mail { from = "IT Service Desk", address = "it-support@northwind-helpdesk.co", subject = "URGENT: your mailbox will be deleted in 2 hours", phishing = true,
                body = "Dear user,\n\nWe have detected unusual activity on your mailbox. It will be <b>permanently deleted in 2 hours</b> unless you verify your login now:\n\n<color=#0F72C9><u>http://northwind-mail.verify-login.co/secure</u></color>\n\nIT Service Desk",
                verdict = "Phishing: an external look-alike domain (northwind-helpdesk.co), a threat with a deadline, and a link to a fake login page." },
            new Mail { from = "Anna Visser", address = "anna.visser@northwind.local", subject = "Minutes from Tuesday's project meeting", phishing = false,
                body = "Hi Fred,\n\nAttached are the minutes from Tuesday. Action points are on page 2. Let me know if I missed anything.\n\n[Minutes_Tuesday.pdf - internal share]\n\nCheers,\nAnna",
                verdict = "Safe: an internal colleague, an expected topic, and no pressure or unusual request." },
            new Mail { from = "DHL Express", address = "noreply@dhl-parcels-track.info", subject = "Parcel on hold - pay EUR 1.99 customs fee", phishing = true,
                body = "Your parcel <b>#NL88231</b> is on hold at customs.\n\nPay the outstanding fee of <b>EUR 1.99</b> within 24 hours or it will be returned:\n\n<color=#0F72C9><u>https://dhl-parcels-track.info/pay</u></color>",
                verdict = "Phishing: a small 'fee' to steal card details, a domain that isn't DHL's, and time pressure." },
            new Mail { from = "HR Department", address = "hr@northwind.local", subject = "Reminder: security awareness training due Friday", phishing = false,
                body = "Hello Fred,\n\nA friendly reminder that your yearly security awareness training is due this Friday. You can find it on the internal learning portal:\n\n<color=#0F72C9><u>https://learning.northwind.local</u></color>\n\nKind regards, HR",
                verdict = "Safe: an internal sender, an internal link, and a routine announcement." },
            new Mail { from = "Mark Jansen (CEO)", address = "mark.jansen.ceo@gmail.com", subject = "Quick favour - are you at your desk?", phishing = true,
                body = "Fred,\n\nI'm in a meeting and need a quick favour. Please buy <b>5 gift cards of EUR 100</b> for a client and send me the codes. Keep this confidential, I'll explain later.\n\nMark\n<size=18>Sent from my phone</size>",
                verdict = "Phishing (CEO fraud): a personal Gmail address, secrecy, urgency and gift cards. Always verify through another channel." },
            new Mail { from = "Facilities", address = "facilities@northwind.local", subject = "Office closed Monday for maintenance", phishing = false,
                body = "Dear colleagues,\n\nThe office will be closed on Monday for maintenance of the air conditioning. Please work from home.\n\nFacilities team",
                verdict = "Safe: an internal sender with plain information and nothing to click." },
        };
        int openMail = -1;
        string mailFeedback;   // result of the last tag, shown under the next email
        bool mailFeedbackOk;
        int chatState; // 0 not yet, 1 message pending, 2 handled
        float encryptProgress = -1;

        // ------------------------------------------------------------ input fields

        class Field
        {
            public string value = "";
            public bool masked;
            public TMP_Text text;
            public Image bg;
            public int maxLen = 24;
            public Action onSubmit;
            public void Refresh(bool active)
            {
                string shown = masked ? new string('*', value.Length) : value;
                text.text = shown + (active ? "<color=#0F72C9>|</color>" : "");
                bg.color = active ? Color.white : new Color(0.93f, 0.94f, 0.96f);
            }
        }

        void Start()
        {
            screen = UIKit.ContentRoot(screen);
            if (keyboardCanvas)
            {
                kbContent = UIKit.ContentRoot(keyboardCanvas);
                kbPanel = keyboardCanvas.parent.gameObject;   // pivot holding the canvas and its ISDK ray / poke surfaces
            }
            GameManager.I.TaskCompleted += OnTaskCompleted;
            Show(Mode.Desktop);
        }

        void OnTaskCompleted(TaskId t)
        {
            if (t == TaskId.Enable2FA && chatState == 0) StartCoroutine(IncomingChat());
            if (mode == Mode.Main && tab == 3) Rebuild();
        }

        IEnumerator IncomingChat()
        {
            yield return new WaitForSeconds(6f);
            chatState = 1;
            Sfx.PlayAt(Sfx.I ? Sfx.I.toast : null, transform.position);
            GameUI.I?.Toast("TeamChat - Fred's workstation", "New message from 'IT-Helpdesk (external)'", Palette.Orange);
            if (mode == Mode.Main) Rebuild();
        }

        void Show(Mode m)
        {
            mode = m;
            activeField = null;
            fieldChanged = null;
            Rebuild();
        }

        void Rebuild()
        {
            if (terminalCo != null) { StopCoroutine(terminalCo); terminalCo = null; }
            UIKit.Clear(screen);
            var bg = UIKit.Panel(screen, "Background", Color.white, false);
            bg.rectTransform.Fill();
            keyboard = null;
            if (kbPanel)
            {
                UIKit.Clear(kbContent);
                kbPanel.SetActive(false);
            }
            switch (mode)
            {
                case Mode.Desktop: BuildDesktop(); break;
                case Mode.Login: BuildLogin(); break;
                case Mode.ChangePassword: BuildChangePassword(); break;
                case Mode.Main: BuildMain(); break;
            }
        }

        // ------------------------------------------------------------ desktop (attacker active)

        void BuildDesktop()
        {
            var desk = UIKit.Panel(screen, "Desktop", Palette.Hex("1B3A5C"), false);
            desk.rectTransform.Fill();
            var banner = UIKit.Panel(desk.transform, "Alert", Palette.Red, false);
            banner.rectTransform.Top(90);
            var bt = UIKit.Text(banner.transform, "<b>REMOTE SESSION ACTIVE</b>   user 'svc_backup' connected from 185.220.101.47 (TOR exit node)", 28, Color.white, TextAlignmentOptions.Center);
            bt.rectTransform.Fill(20, 20, 0, 0);

            var term = UIKit.Panel(desk.transform, "Terminal", Palette.Dark.A(0.95f));
            term.rectTransform.Fill(60, 60, 130, 210);
            var tt = UIKit.Text(term.transform, "", 22, Palette.Terminal, TextAlignmentOptions.BottomLeft);
            tt.rectTransform.Fill(24, 24, 16, 16);
            tt.overflowMode = TextOverflowModes.Truncate;
            terminalCo = StartCoroutine(TerminalRoutine(tt));

            var info = UIKit.Text(desk.transform, "Fred left his workstation <b>unlocked</b>. An attacker is using it right now.", 26, Color.white, TextAlignmentOptions.Center);
            info.rectTransform.At(new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(1100, 50), new Vector2(0.5f, 0));
            var b = UIKit.Button(desk.transform, "Terminate session & lock workstation", Palette.Red, Color.white, () =>
            {
                GameManager.I.Complete(TaskId.LockWorkstation);
                Show(Mode.Login);
            }, 30);
            b.GetComponent<RectTransform>().At(new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(700, 112), new Vector2(0.5f, 0));
        }

        IEnumerator TerminalRoutine(TMP_Text t)
        {
            string[] lines =
            {
                "svc_backup@FRED-PC-07:~$ whoami /priv",
                "svc_backup@FRED-PC-07:~$ dir \\\\fileserver\\projects\\AURORA",
                "svc_backup@FRED-PC-07:~$ compress Project_AURORA -> aurora.zip",
                "svc_backup@FRED-PC-07:~$ upload aurora.zip https://cdn-sync[.]xyz/u",
            };
            int i = 0;
            while (true)
            {
                if (i < lines.Length) terminalLog += lines[i++] + "\n";
                var gm = GameManager.I;
                t.text = terminalLog + $"uploading aurora.zip ... <b>{gm.Exfil01 * 100:0.0}%</b>  ({gm.Exfil01 * 812:0} / 812 MB)";
                yield return new WaitForSeconds(i < lines.Length ? 0.8f : 0.5f);
            }
        }

        // ------------------------------------------------------------ login (Company Mailbox look from the reference)

        void BuildLogin()
        {
            var chrome = BrowserChrome(false);
            var left = UIKit.Panel(chrome, "Brand", Palette.MailBlue, false);
            left.rectTransform.anchorMin = Vector2.zero;
            left.rectTransform.anchorMax = new Vector2(0.38f, 1);
            left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;
            Envelope(left.transform, new Vector2(0, 180), 220);
            var lt = UIKit.Text(left.transform, "Workstation locked.\nSign in to SecureMail as Fred to take back his account.", 22, Color.white, TextAlignmentOptions.Center);
            lt.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(400, 120));

            var form = UIKit.Rect(chrome, "Form");
            form.anchorMin = new Vector2(0.38f, 0);
            form.anchorMax = Vector2.one;
            form.offsetMin = new Vector2(60, 0);
            form.offsetMax = new Vector2(-60, 0);
            var title = UIKit.Text(form, "SecureMail", 46, Palette.MailBlue);
            title.rectTransform.Top(70, 20);
            var l1 = UIKit.Text(form, "Email address", 22, Palette.InkSoft);
            l1.rectTransform.Top(30, 95);
            var email = UIKit.Panel(form, "Email", new Color(0.88f, 0.89f, 0.91f));
            email.rectTransform.Top(56, 125, 0, 120);
            var et = UIKit.Text(email.transform, fredEmail, 24, Palette.InkSoft, TextAlignmentOptions.MidlineLeft);
            et.rectTransform.Fill(14, 10, 0, 0);
            var l2 = UIKit.Text(form, "Password", 22, Palette.InkSoft);
            l2.rectTransform.Top(30, 190);
            var pw = MakeField(form, true, 220, 250);
            var error = UIKit.Text(form, loginAttempts > 0 ? "The password you entered isn't correct." : "", 22, Palette.Red);
            error.rectTransform.Top(34, 306);
            var hint = UIKit.Text(form, "The attacker guessed this password from Fred's personal life. So can you.", 20, Palette.InkSoft, TextAlignmentOptions.TopLeft, FontStyles.Italic);
            hint.rectTransform.Top(30, 342);
            Action submit = () =>
            {
                if (pw.value == fredPassword || pw.value.Equals(fredPassword, StringComparison.OrdinalIgnoreCase))
                {
                    GameManager.I.Complete(TaskId.FredLogin);
                    Show(Mode.ChangePassword);
                }
                else
                {
                    loginAttempts++;
                    GameManager.I.Mistake(null);
                    pw.value = "";
                    error.text = $"The password you entered isn't correct. (attempt {loginAttempts})";
                    pw.Refresh(true);
                }
            };
            pw.onSubmit = submit;
            var login = UIKit.Button(form, "Login", Palette.MailBlue, Color.white, submit, 28);
            login.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(0, -198), new Vector2(230, 104));
            Focus(pw);
        }

        void Envelope(Transform parent, Vector2 pos, float size)
        {
            var env = UIKit.Rect(parent, "Envelope");
            env.At(new Vector2(0.5f, 0.5f), pos, new Vector2(size, size * 0.66f));
            var body = UIKit.Panel(env, "Body", Color.white, true, 0.3f);
            body.rectTransform.Fill();
            for (int s = -1; s <= 1; s += 2)
            {
                var flap = UIKit.Panel(env, "Flap", Palette.MailBlue, false);
                flap.rectTransform.At(new Vector2(0.5f, 1), new Vector2(s * size * 0.24f, -size * 0.16f), new Vector2(size * 0.62f, size * 0.05f));
                flap.rectTransform.localRotation = Quaternion.Euler(0, 0, s * 33);
            }
        }

        // ------------------------------------------------------------ forced password change

        static readonly string[] Banned = { "fluffy", "fred", "dekker", "2019", "password", "northwind", "qwerty", "123456" };

        static (string rule, Func<string, bool> ok)[] Rules =>
            new (string, Func<string, bool>)[]
            {
                ("At least 12 characters", p => p.Length >= 12),
                ("Upper-case and lower-case letters", p => p.Any(char.IsUpper) && p.Any(char.IsLower)),
                ("At least one number", p => p.Any(char.IsDigit)),
                ("At least one symbol  ! ? - _ # @ ...", p => p.Any(c => !char.IsLetterOrDigit(c))),
                ("Nothing about Fred (names, pets, years)", p => !Banned.Any(b => p.ToLowerInvariant().Contains(b))),
            };

        void BuildChangePassword()
        {
            var chrome = BrowserChrome(false);
            var wrap = UIKit.Rect(chrome, "Wrap");
            wrap.Fill(120, 120, 20, 0);
            var title = UIKit.Text(wrap, "<b>This account was compromised.</b>  Set a new password now.", 32, Palette.Ink);
            title.rectTransform.Top(50);
            var sub = UIKit.Text(wrap, "Follow the rules on the PASSWORDS poster. A long passphrase is easiest to remember.", 22, Palette.InkSoft);
            sub.rectTransform.Top(34, 52);
            var pw = MakeField(wrap, false, 96, 330);
            pw.maxLen = 40;
            var checklist = UIKit.Text(wrap, "", 24, Palette.Ink);
            checklist.rectTransform.Top(190, 196, 0, 0);
            var set = UIKit.Button(wrap, "Set password", Palette.MailBlue, Color.white, null, 28);
            set.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(0, -84), new Vector2(300, 104), new Vector2(1, 1));

            void Validate()
            {
                var sb = new System.Text.StringBuilder();
                bool all = true;
                foreach (var (rule, ok) in Rules)
                {
                    bool pass = ok(pw.value);
                    all &= pass;
                    sb.Append(pass ? "<color=#2FB45A>[OK]</color>  " : "<color=#E5484D>[ -- ]</color>  ").Append(rule).Append('\n');
                }
                checklist.text = sb.ToString();
                set.interactable = all;
            }
            pw.onSubmit = () => set.onClick.Invoke();
            fieldChanged = Validate;
            set.onClick.AddListener(() =>
            {
                if (Rules.All(r => r.ok(pw.value)))
                {
                    GameManager.I.Complete(TaskId.NewPassword);
                    tab = 0;
                    Show(Mode.Main);
                }
            });
            Validate();
            Focus(pw);
        }

        Action fieldChanged;

        // ------------------------------------------------------------ main browser with tabs

        RectTransform BrowserChrome(bool tabs)
        {
            // every button on this screen is at least 104 units tall = 6 cm at the canvas scale (0.00058 m/unit)
            float barH = tabs ? 116 : 64;
            var bar = UIKit.Panel(screen, "TabBar", Palette.Hex("DEE1E6"), false);
            bar.rectTransform.Top(barH);
            if (tabs)
            {
                string[] names = { "SecureMail", "TeamChat", "Account Security", "SOC Console" };
                for (int i = 0; i < names.Length; i++)
                {
                    int idx = i;
                    string label = names[i];
                    if (i == 1 && chatState == 1) label += "  (1)";
                    var b = UIKit.Button(bar.transform, label, i == tab ? Color.white : Palette.Hex("C9CDD3"), Palette.Ink, () => { tab = idx; openMail = -1; Rebuild(); }, 22);
                    b.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(12 + i * 262, 6), new Vector2(252, 104), new Vector2(0, 0));
                }
            }
            else
            {
                var t = UIKit.Text(bar.transform, "FRED-PC-07  |  Northwind Secure Workspace", 22, Palette.InkSoft, TextAlignmentOptions.MidlineLeft);
                t.rectTransform.Fill(20, 20, 0, 0);
            }
            content = UIKit.Rect(screen, "Content");
            content.Fill(0, 0, barH, 0);
            return content;
        }

        void BuildMain()
        {
            BrowserChrome(true);
            switch (tab)
            {
                case 0: BuildMail(); break;
                case 1: BuildChat(); break;
                case 2: BuildSecurity(); break;
                case 3: BuildSoc(); break;
            }
        }

        void BuildMail()
        {
            var list = UIKit.Panel(content, "List", Palette.CardTint, false);
            list.rectTransform.anchorMin = Vector2.zero;
            list.rectTransform.anchorMax = new Vector2(0.4f, 1);
            list.rectTransform.offsetMin = list.rectTransform.offsetMax = Vector2.zero;
            int remaining = mails.Count(m => m.state == 0);
            var head = UIKit.Text(list.transform, $"<b>Inbox</b>   <size=20>{remaining} to review</size>", 28, Palette.Ink, TextAlignmentOptions.MidlineLeft);
            head.rectTransform.Top(50, 0, 18, 10);
            for (int i = 0; i < mails.Count; i++)
            {
                int idx = i;
                var m = mails[i];
                string tag = m.state == 0 ? "" : m.state == 2 ? "<color=#F07A22>[WRONG]</color> " : m.phishing ? "<color=#E5484D>[REPORTED]</color> " : "<color=#2FB45A>[SAFE]</color> ";
                var row = UIKit.Button(list.transform, "", i == openMail ? Color.white : Palette.Hex("E8EBEF"), Palette.Ink, () => { openMail = idx; mailFeedback = null; Rebuild(); }, 20);
                row.GetComponent<RectTransform>().Top(104, 52 + i * 105, 10, 10);
                var lbl = row.GetComponentInChildren<TMP_Text>();
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
                lbl.fontStyle = FontStyles.Normal;
                lbl.text = $"{tag}<b>{m.from}</b>\n<size=18>{m.subject}</size>";
            }

            var view = UIKit.Rect(content, "View");
            view.anchorMin = new Vector2(0.4f, 0);
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(30, 20);
            view.offsetMax = new Vector2(-30, -20);
            if (openMail < 0)
            {
                int right = mails.Count(m => m.state == 1);
                var t = UIKit.Text(view, remaining == 0
                    ? $"<b>Inbox triaged.</b>\n{right} of {mails.Count} emails tagged correctly." + (right < mails.Count ? "\nSelect an email marked [WRONG] to see why." : "\nAll phishing emails reported.")
                    : "Select an email on the left.\n\nFor every email decide: <b>Report phishing</b> or <b>Mark safe</b>.\nCheck the sender address, links and the tone.", 28, Palette.InkSoft, TextAlignmentOptions.Center);
                t.rectTransform.Fill();
                if (mailFeedback != null) MailFeedback(view, mailFeedback, mailFeedbackOk);
                return;
            }
            var mail = mails[openMail];
            var subj = UIKit.Text(view, $"<b>{mail.subject}</b>", 30, Palette.Ink);
            subj.rectTransform.Top(80);
            var from = UIKit.Text(view, $"From: <b>{mail.from}</b>  <color=#5A6372>&lt;{mail.address}&gt;</color>\nTo: {fredEmail}", 21, Palette.Ink);
            from.rectTransform.Top(60, 80);
            var body = UIKit.Text(view, mail.body, 23, Palette.Ink);
            body.rectTransform.Fill(0, 0, 155, 184);
            if (mail.state == 0)
            {
                var rep = UIKit.Button(view, "Report phishing", Palette.Red, Color.white, () => Classify(mail, true), 24);
                rep.GetComponent<RectTransform>().At(Vector2.zero, new Vector2(0, 72), new Vector2(300, 104), Vector2.zero);
                var safe = UIKit.Button(view, "Mark safe", Palette.Green, Color.white, () => Classify(mail, false), 24);
                safe.GetComponent<RectTransform>().At(Vector2.zero, new Vector2(320, 72), new Vector2(240, 104), Vector2.zero);
                if (mailFeedback != null) MailFeedback(view, mailFeedback, mailFeedbackOk);
            }
            else MailFeedback(view, (mail.state == 2 ? "You tagged this one wrongly. " : "") + mail.verdict, mail.state == 1);
        }

        static void MailFeedback(RectTransform view, string text, bool ok)
        {
            var fb = UIKit.Text(view, text, 20, ok ? Palette.Green : Palette.Red);
            fb.rectTransform.At(Vector2.zero, Vector2.zero, new Vector2(700, 68), Vector2.zero);
            fb.enableAutoSizing = true;
            fb.fontSizeMin = 14;
            fb.fontSizeMax = 20;
        }

        /// Every tag is saved, right or wrong (a wrong one counts as a mistake and explains why), and the inbox moves
        /// straight on to the next email that still needs a decision.
        void Classify(Mail mail, bool reportAsPhishing)
        {
            if (mail.state != 0) return;
            bool ok = reportAsPhishing == mail.phishing;
            mail.state = ok ? 1 : 2;
            mailFeedbackOk = ok;
            mailFeedback = (ok ? "Previous email: correct. " : "Previous email: not quite. ") + mail.verdict;
            if (ok)
            {
                Sfx.Play(Sfx.I ? Sfx.I.confirm : null);
                GameUI.I?.Caption("Correct. " + mail.verdict, Palette.Ink);
            }
            else GameManager.I.Mistake("Not quite. " + mail.verdict);

            int idx = mails.IndexOf(mail), next = -1;
            for (int k = 1; k <= mails.Count && next < 0; k++)
                if (mails[(idx + k) % mails.Count].state == 0) next = (idx + k) % mails.Count;
            openMail = next;
            if (next < 0) GameManager.I.Complete(TaskId.PhishingTriage);
            Rebuild();
        }

        void BuildChat()
        {
            var wrap = UIKit.Rect(content, "Chat");
            wrap.Fill(60, 60, 30, 30);
            if (chatState == 0)
            {
                var t = UIKit.Text(wrap, "No new messages.", 30, Palette.InkSoft, TextAlignmentOptions.Center);
                t.rectTransform.Fill();
                return;
            }
            var bubble = UIKit.Panel(wrap, "Msg", Palette.Hex("E8EBEF"));
            bubble.rectTransform.Top(250, 20, 0, 260);
            var mt = UIKit.Text(bubble.transform,
                "<b>IT-Helpdesk (external)</b>  <size=18><color=#5A6372>02:21</color></size>\n" +
                "Hi Fred, Kevin from IT here. We just saw suspicious logins on your account! To keep you safe I need the <b>6-digit code from your token</b> right now so I can verify it's really you. Quick please!", 25, Palette.Ink);
            mt.rectTransform.Fill(24, 24, 18, 18);
            if (chatState == 1)
            {
                var send = UIKit.Button(wrap, "Send the code from the token", Palette.CardTint, Palette.Ink, () =>
                {
                    GameManager.I.Mistake("You just gave the attacker a valid 2FA code! IT will NEVER ask for it. Try again.");
                    GameUI.I?.Toast("Account at risk", "The attacker used the code. Security reset the token. Handle the chat correctly.", Palette.Red);
                }, 26);
                send.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(0, 140), new Vector2(520, 104), new Vector2(1, 0));
                var refuse = UIKit.Button(wrap, "Refuse & report to Security", Palette.Red, Color.white, () =>
                {
                    chatState = 2;
                    GameManager.I.Complete(TaskId.SocialEngineering);
                    Rebuild();
                }, 26);
                refuse.GetComponent<RectTransform>().At(new Vector2(1, 0), new Vector2(0, 24), new Vector2(520, 104), new Vector2(1, 0));
            }
            else
            {
                var reply = UIKit.Panel(wrap, "Reply", Palette.MailBlue);
                reply.rectTransform.At(new Vector2(1, 1), new Vector2(0, -300), new Vector2(700, 130), new Vector2(1, 1));
                var rt = UIKit.Text(reply.transform, "<b>You</b>\nNice try. Real IT never asks for 2FA codes. Reported to the Security team.", 24, Color.white);
                rt.rectTransform.Fill(20, 20, 14, 14);
            }
        }

        void BuildSecurity()
        {
            var wrap = UIKit.Rect(content, "Security");
            wrap.Fill(100, 100, 30, 0);
            bool on = GameManager.I.IsDone(TaskId.Enable2FA);
            var t = UIKit.Text(wrap, $"<b>Two-factor authentication:</b>  {(on ? "<color=#2FB45A>ON</color>" : "<color=#E5484D>OFF</color>")}", 34, Palette.Ink);
            t.rectTransform.Top(60);
            if (on)
            {
                var d = UIKit.Text(wrap, "Fred's account now needs his password <b>and</b> a code from his hardware token. A stolen password alone is useless.", 26, Palette.Ink);
                d.rectTransform.Top(120, 80);
                return;
            }
            var steps = UIKit.Text(wrap, "1.  Find Fred's hardware token (he keeps it in his briefcase).\n2.  Type the 6-digit code it shows. The code changes every 30 seconds.", 26, Palette.Ink);
            steps.rectTransform.Top(100, 80);
            var code = MakeField(wrap, false, 200, 500);
            code.maxLen = 6;
            var err = UIKit.Text(wrap, "", 22, Palette.Red);
            err.rectTransform.Top(40, 290);
            Action verify = () =>
            {
                if (AuthToken.I && AuthToken.I.IsValid(code.value))
                {
                    GameManager.I.Complete(TaskId.Enable2FA);
                    Rebuild();
                }
                else
                {
                    GameManager.I.Mistake(null);
                    err.text = "That code is not valid. Read the current code on the token.";
                    code.value = "";
                    code.Refresh(true);
                }
            };
            code.onSubmit = verify;
            var b = UIKit.Button(wrap, "Verify & enable", Palette.MailBlue, Color.white, verify, 26);
            b.GetComponent<RectTransform>().At(new Vector2(1, 1), new Vector2(-200, -182), new Vector2(280, 104), new Vector2(1, 1));
            Focus(code);
        }

        void BuildSoc()
        {
            var wrap = UIKit.Panel(content, "SOC", Palette.Dark, false);
            wrap.rectTransform.Fill();
            var title = UIKit.Text(wrap.transform, "<b>SOC CONSOLE</b>  -  incident #2026-1021-0213", 30, Palette.Terminal);
            title.rectTransform.Top(50, 20, 40, 40);
            var gm = GameManager.I;
            var sb = new System.Text.StringBuilder();
            foreach (var info in GameManager.I.Chapter.tasks)
            {
                if (info.id == TaskId.EncryptData || info.id == TaskId.Escape) continue;
                sb.Append(gm.IsDone(info.id) ? "<color=#39FF88>[DONE]</color>  " : "<color=#E5484D>[OPEN]</color>  ").Append(info.label).Append('\n');
            }
            var list = UIKit.Text(wrap.transform, sb.ToString(), 22, Color.white);
            list.rectTransform.Fill(40, 40, 80, 220);
            bool ready = gm.AllContainedExcept(TaskId.EncryptData, TaskId.Escape);
            bool doneEnc = gm.IsDone(TaskId.EncryptData);
            var status = UIKit.Text(wrap.transform,
                doneEnc ? "<color=#39FF88>Project_AURORA.zip encrypted (AES-256). Attacker tokens revoked. The FINAL PASSKEY is in the Data Section popup (and on the Containment Board).</color>"
                : ready ? "All other containment steps are complete. Final action available:"
                : "Final action is locked until every other containment step is DONE.", 22, Color.white);
            status.rectTransform.At(new Vector2(0, 0), new Vector2(40, 150), new Vector2(1100, 60), new Vector2(0, 0));
            if (!doneEnc)
            {
                var b = UIKit.Button(wrap.transform, encryptProgress >= 0 ? $"Encrypting... {encryptProgress * 100:0}%" : "Encrypt & revoke access", ready ? Palette.Terminal : Palette.InkSoft, Palette.Dark, () =>
                {
                    if (encryptProgress < 0 && gm.State == GameState.Playing && gm.AllContainedExcept(TaskId.EncryptData, TaskId.Escape)) StartCoroutine(Encrypt());
                }, 28);
                b.interactable = ready && encryptProgress < 0;
                b.GetComponent<RectTransform>().At(new Vector2(0, 0), new Vector2(40, 30), new Vector2(460, 104), new Vector2(0, 0));
                if (encryptProgress >= 0)
                {
                    var cipher = UIKit.Text(wrap.transform, CipherLine(encryptProgress), 24, Color.white, TextAlignmentOptions.MidlineLeft);
                    cipher.rectTransform.At(new Vector2(0, 0), new Vector2(530, 30), new Vector2(700, 104), new Vector2(0, 0));
                }
            }
        }

        IEnumerator Encrypt()
        {
            // the chapter clock stops for the final sequence so the timer can't expire mid-encryption;
            // progress is driven by unscaled time, so the sequence always finishes in ~3.5 s
            var gm = GameManager.I;
            gm.TimerPaused = true;
            GameUI.I?.Toast("Encryption started", "Encrypting Project AURORA (AES-256)...", Palette.Blue);
            float start = Time.unscaledTime;
            for (encryptProgress = 0; encryptProgress < 1; encryptProgress = (Time.unscaledTime - start) / 3.5f)
            {
                if (mode == Mode.Main && tab == 3 && Time.frameCount % 10 == 0) Rebuild();
                if (Time.frameCount % 20 == 0) Sfx.Play(Sfx.I ? Sfx.I.key : null, 0.35f);
                yield return null;
            }
            encryptProgress = 1;
            // the clock stays stopped: encryption contains the exfiltration, so the passkey entry can't time out
            gm.Complete(TaskId.EncryptData);
            if (mode == Mode.Main && tab == 3) Rebuild();
        }

        /// Scrolling hex "cipher stream" shown under the progress bar while encrypting.
        static string CipherLine(float progress)
        {
            var sb = new System.Text.StringBuilder(64);
            int seed = (int)(Time.unscaledTime * 12f);
            for (int i = 0; i < 40; i++) sb.Append("0123456789ABCDEF"[(seed * 31 + i * 7 + i * i) & 15]);
            return $"<color=#39FF88>{sb}</color>   {(int)(progress * 100)}%";
        }

        // ------------------------------------------------------------ fields + on-screen keyboard

        Field MakeField(RectTransform parent, bool masked, float topY, float rightInset)
        {
            var f = new Field { masked = masked };
            f.bg = UIKit.Panel(parent, "Field", Color.white);
            f.bg.rectTransform.Top(60, topY, 0, rightInset);
            var outline = f.bg.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.Line;
            f.text = UIKit.Text(f.bg.transform, "", 28, Palette.Ink, TextAlignmentOptions.MidlineLeft);
            f.text.rectTransform.Fill(16, 10, 0, 0);
            var btn = f.bg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => Focus(f));
            f.Refresh(false);
            return f;
        }

        void Focus(Field f)
        {
            if (activeField != null && activeField != f) activeField.Refresh(false);
            activeField = f;
            f.Refresh(true);
            BuildKeyboard();
        }

        static readonly string[] Lower = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };
        static readonly string[] Symbols = { "1234567890", "!@#$%&*?-_", "+=/.,:;'\"", "()[]<>~" };

        void BuildKeyboard()
        {
            if (keyboard) Destroy(keyboard.gameObject);
            bool floating = kbPanel;
            if (floating)
            {
                // floating panel: 100 x 100 units = 6 x 6 cm keys at its 0.6 mm/unit scale
                kbPanel.SetActive(true);
                keyboard = UIKit.Panel(kbContent, "Keyboard", Palette.Hex("2A2F38"), false).rectTransform;
                keyboard.Fill();
            }
            else
            {
                keyboard = UIKit.Panel(screen, "Keyboard", Palette.Hex("2A2F38"), false).rectTransform;
                keyboard.At(new Vector2(0.5f, 0), Vector2.zero, new Vector2(1280, 320), new Vector2(0.5f, 0));
            }
            var rows = symbols ? Symbols : Lower;
            float kw = floating ? 100 : 104, gap = 8;
            keyH = floating ? 100 : 54;
            float kh = keyH;
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                float width = row.Length * (kw + gap);
                float x0 = -width / 2 + kw / 2;
                for (int i = 0; i < row.Length; i++)
                {
                    char c = row[i];
                    string label = shift && char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : c.ToString();
                    var k = UIKit.Button(keyboard, label, Palette.Hex("4A5160"), Color.white, () => Type(label), floating ? 44 : 30, "Key");
                    k.GetComponent<RectTransform>().At(new Vector2(0.5f, 1), new Vector2(x0 + i * (kw + gap), -10 - r * (kh + gap)), new Vector2(kw, kh), new Vector2(0.5f, 1));
                }
            }
            float y = -10 - 4 * (kh + gap);
            if (floating)
            {
                // 1120 wide: Shift 150 | #+= 150 | Space 400 | Delete 180 | Enter 200, 8-unit gaps
                Key("Shift", -481, 150, shift ? Palette.Blue : Palette.Hex("6B7382"), () => { shift = !shift; BuildKeyboard(); }, y);
                Key(symbols ? "abc" : "#+=", -323, 150, Palette.Hex("6B7382"), () => { symbols = !symbols; BuildKeyboard(); }, y);
                Key("Space", -40, 400, Palette.Hex("4A5160"), () => Type(" "), y);
                Key("Delete", 258, 180, Palette.Hex("6B7382"), Backspace, y);
                Key("Enter", 456, 200, Palette.Green, () => activeField?.onSubmit?.Invoke(), y);
            }
            else
            {
                Key("Shift", -560, 150, shift ? Palette.Blue : Palette.Hex("6B7382"), () => { shift = !shift; BuildKeyboard(); }, y);
                Key(symbols ? "abc" : "#+=", -400, 150, Palette.Hex("6B7382"), () => { symbols = !symbols; BuildKeyboard(); }, y);
                Key("Space", -60, 500, Palette.Hex("4A5160"), () => Type(" "), y);
                Key("Delete", 300, 180, Palette.Hex("6B7382"), Backspace, y);
                Key("Enter", 500, 200, Palette.Green, () => activeField?.onSubmit?.Invoke(), y);
            }
        }

        float keyH = 54;

        void Key(string label, float x, float w, Color c, Action a, float y)
        {
            var k = UIKit.Button(keyboard, label, c, Color.white, a, keyH > 60 ? 34 : 26, "Key");
            k.GetComponent<RectTransform>().At(new Vector2(0.5f, 1), new Vector2(x, y), new Vector2(w, keyH), new Vector2(0.5f, 1));
        }

        void Type(string s)
        {
            if (activeField == null || activeField.value.Length >= activeField.maxLen) return;
            activeField.value += s;
            Sfx.Haptic(0.2f, 0.02f);
            if (shift && char.IsLetter(s[0])) { shift = false; BuildKeyboard(); }
            activeField.Refresh(true);
            fieldChanged?.Invoke();
        }

        void Backspace()
        {
            if (activeField == null || activeField.value.Length == 0) return;
            activeField.value = activeField.value.Substring(0, activeField.value.Length - 1);
            activeField.Refresh(true);
            fieldChanged?.Invoke();
        }

        // ------------------------------------------------------------ test helpers
        public void DebugLogin() { if (mode == Mode.Login) { GameManager.I.Complete(TaskId.FredLogin); Show(Mode.ChangePassword); } }
        public void DebugType(string s) { foreach (char c in s) Type(c.ToString()); }
        public void DebugSubmit() => activeField?.onSubmit?.Invoke();
        public void DebugTab(int t) { tab = t; if (mode == Mode.Main) Rebuild(); }
        public void DebugMain() => Show(Mode.Main);
    }
}
