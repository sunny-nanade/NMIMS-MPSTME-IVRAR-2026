using System.Linq;

namespace CSER
{
    public enum TaskId
    {
        // Chapter 1 - Night Shift (SOC)
        SOC_Badge,
        SOC_OpenAlert,
        SOC_FindHost,
        SOC_Classify,
        SOC_Escalate,
        SOC_Exit,
        // Chapter 2 - Fred's Office
        LockWorkstation,
        EvidencePrintout,
        EvidenceNote,
        ShredDocs,
        QuarantineUSB,
        FredLogin,
        NewPassword,
        Enable2FA,
        PhishingTriage,
        SocialEngineering,
        EncryptData,
        Escape,
        // Chapter 3 - The Server Room
        SRV_Find,
        SRV_Unplug,
        SRV_Bag,
        SRV_Isolate,
        SRV_Restore,
        SRV_Report,
        SRV_Flashlight,
        // Chapter 1 - infected laptop (appended so saved data keeps working)
        SOC_Malware,
    }

    public enum ItemKind { Prop, EvidencePrintout, EvidenceNote, Confidential, Recycle, USB, Token, Badge, Implant, Plug, Flashlight }

    public struct LearningPoint
    {
        public TaskId task;
        public string title;
        public string text;
    }

    public struct TaskInfo
    {
        public TaskId id;
        public string label;
        public string[] hints;
    }

    public class ChapterInfo
    {
        public int number;
        public string scene;
        public string title;
        public string subtitle;
        public string intro;
        public string outro;             // debrief on the chapter-complete card
        public TaskInfo[] tasks;
        public float normalMinutes;      // 0 = no timer
        public float experiencedMinutes;
    }

    /// All story text: chapters, objectives, escalating hints (nudge -> direction -> answer) and learning points.
    public static class GameData
    {
        public const string MenuScene = "MainMenu";

        public static readonly ChapterInfo[] Chapters =
        {
            new ChapterInfo
            {
                number = 1, scene = "Ch1_NightShift", title = "Night Shift", subtitle = "Northwind HQ - Security Operations Centre - 02:10",
                intro = "It's your first night shift as a <b>blue-team analyst</b> at Northwind.\n\nThe SOC is quiet, the coffee is cold, and the wall of screens is green. Maya, your SOC lead, is on the radio and will walk you through the job.\n\nLearn the controls, read the alerts, and be ready: tonight won't stay quiet for long.",
                outro = "You read the SIEM, traced an 812 MB upload to <b>FRED-PC-07</b>, classified it as <b>data exfiltration</b> and escalated it to the incident team. You also stopped the malware on the infected laptop and cut it off the network.\n\nThe attacker is still on Fred's workstation, uploading Project AURORA.",
                tasks = new[]
                {
                    T(TaskId.SOC_Badge, "Pick up your access badge from your desk",
                        "Your badge is lying on your desk, next to the keyboard.",
                        "Reach for the blue badge and squeeze the grip button. From a distance: point at it and squeeze grip to pull it in.",
                        "Grab the blue access badge on the desk in front of you."),
                    T(TaskId.SOC_OpenAlert, "Open the critical alert on the SIEM console",
                        "Your console is the big screen on your desk.",
                        "Point at the red CRITICAL alert in the list and pull the trigger.",
                        "On the SIEM console, select the alert 'Unusual outbound transfer'."),
                    T(TaskId.SOC_FindHost, "Find the compromised machine in the logs",
                        "Read the log lines: which machine sends far more data than the rest?",
                        "Look for the host with an 812 MB upload to an unknown domain.",
                        "Answer FRED-PC-07."),
                    T(TaskId.SOC_Classify, "Identify the type of attack",
                        "Data is leaving the company towards an unknown server.",
                        "Copying files out of the network is called exfiltration.",
                        "Answer 'Data exfiltration'."),
                    T(TaskId.SOC_Escalate, "Declare a critical incident",
                        "An active attack must be escalated straight away.",
                        "Press the red escalate button at the bottom of the console.",
                        "Press 'Declare CRITICAL incident'."),
                    T(TaskId.SOC_Malware, "Clean the infected laptop on K. de Vries' desk",
                        "A laptop on the left-hand desk is flashing: SECURITY BREACH DETECTED.",
                        "Step 1: stop the malicious process. Step 2: cut the network. Step 3: quarantine the file.",
                        "Kill 'svch0st32.exe' (unsigned, 97% CPU), choose 'Disconnect from network', then quarantine the file."),
                    T(TaskId.SOC_Exit, "Tap your badge on the reader to open the door",
                        "The door to the office floor needs your badge. Keep it in your hand (or press I to store it).",
                        "Walk to the door and touch the badge to the reader, or point at the door and pull the trigger.",
                        "Hold the badge against the reader by the door until the light turns green: the next chapter starts."),
                },
            },
            new ChapterInfo
            {
                number = 2, scene = "Ch2_FredsOffice", title = "Fred's Office", subtitle = "Floor 3 - Fred Dekker's office - 02:13", normalMinutes = 25, experiencedMinutes = 16,
                intro = "The attacker is still on <b>Fred Dekker's</b> unlocked workstation, uploading the secret file <b>Project AURORA</b>.\n\nContain the breach in four areas: <b>PHYSICAL</b>, <b>ACCOUNT</b>, <b>COMMS</b> and <b>DATA</b>. Each finished area reveals one digit of the door override code on the Containment Board.\n\nEscape before the exfiltration bar reaches 100%.",
                outro = "You locked Fred's workstation, secured the evidence, shredded the confidential paper and quarantined the baited USB. You reset the weak password, turned on 2FA, saw through the phish and the fake helpdesk, and <b>encrypted Project AURORA</b> before the upload finished.\n\nBut the attacker's traffic is still coming from somewhere inside the building.",
                tasks = new[]
                {
                    T(TaskId.LockWorkstation, "Kill the remote session on Fred's workstation",
                        "Fred's screen is still showing something it shouldn't.", "There's a remote session running on Fred's monitor.", "On Fred's monitor, press 'Terminate session & lock workstation'."),
                    T(TaskId.EvidencePrintout, "Bag the printout left on the printer",
                        "Printers are a classic place for sensitive paper to be forgotten.", "Pick up the page in the printer tray on the left desk.", "Drop the printout in the EVIDENCE box on the floor."),
                    T(TaskId.EvidenceNote, "Bag the note Fred threw away",
                        "Not everything in the bin is rubbish.", "There's a yellow sticky note on top of the waste bin.", "Put the sticky note in the EVIDENCE box."),
                    T(TaskId.ShredDocs, "Shred the confidential documents",
                        "Check the papers on Fred's desk.", "Two papers are marked CONFIDENTIAL. The shredder stands next to the desk.", "Drop both CONFIDENTIAL papers into the slot on top of the shredder."),
                    T(TaskId.QuarantineUSB, "Quarantine the unknown USB stick",
                        "Fred's briefcase is on the sideboard under the window. Open it.", "The USB stick inside isn't Fred's. Never plug it in.", "Put the USB stick in the red QUARANTINE box by the door."),
                    T(TaskId.FredLogin, "Sign in to Fred's account",
                        "The attacker guessed Fred's password from his habits.", "Fred's photo frame shows his cat; the sticky note explains the pattern.", "The password is  Fluffy2019!"),
                    T(TaskId.NewPassword, "Set a strong new password",
                        "The PASSWORDS poster lists the rules.", "12+ characters, upper and lower case, a number, a symbol, nothing about Fred.", "Try a passphrase like  Blue-Tiger-Coffee-42!"),
                    T(TaskId.Enable2FA, "Turn on two-factor authentication",
                        "Open the 'Account Security' tab.", "You need the 6-digit code from Fred's hardware token in the briefcase.", "Read the code on the token and type it into Account Security."),
                    T(TaskId.PhishingTriage, "Report every phishing email in Fred's inbox",
                        "Open SecureMail and go through every email.", "Check sender domains, urgency and odd requests (gift cards, fees).", "Phishing: 'mailbox deleted', 'customs fee' and 'CEO gift cards'. The other three are safe."),
                    T(TaskId.SocialEngineering, "Handle the 'IT Helpdesk' chat",
                        "Someone is messaging Fred in TeamChat.", "Real IT staff never ask for your 2FA code.", "Choose 'Refuse & report to Security'."),
                    T(TaskId.EncryptData, "Encrypt Project AURORA and revoke the attacker",
                        "When everything else is contained, open the SOC Console tab.", "The final action unlocks when every other task is green.", "Press 'Encrypt & revoke access'."),
                    T(TaskId.Escape, "Enter the override code at the door",
                        "The Containment Board shows a digit for every finished area.", "Read all four digits from left to right, then walk to the door and enter them on the keypad.", "Type the four digits on the keypad by the door and press OK."),
                },
            },
            new ChapterInfo
            {
                number = 3, scene = "Ch3_ServerRoom", title = "The Server Room", subtitle = "Basement - Data Centre - 02:40",
                intro = "Project AURORA is encrypted and Fred's account is safe.\n\nBut the attacker's traffic didn't start on Fred's PC: it is still coming from <b>inside the server room</b>. Something down here doesn't belong.\n\nThe power is out in the basement, so <b>find a flashlight first</b>. Then find the device, cut it off, recover the damage and close the incident.",
                outro = "You found the rogue Raspberry Pi in the server rack, cut its connection, bagged it as evidence, isolated the network, restored a clean backup and filed the incident report.\n\nThe Northwind breach is contained.",
                tasks = new[]
                {
                    T(TaskId.SRV_Flashlight, "Find a flashlight in the dark server room",
                        "The lights are out. Something near the entrance may help.", "Check the cart beside the door: a small torch is lying on it.",
                        "Grab the flashlight from the cart on your right (press F to switch it on or off)."),
                    T(TaskId.SRV_Find, "Find the rogue device in the server racks",
                        "Walk along the racks. One device doesn't match the others.", "Look for a small box with a blinking red light in the middle rack.", "Point at the small box with the red LED and pull the trigger."),
                    T(TaskId.SRV_Unplug, "Pull out its network cable",
                        "Cut the attacker's connection first.", "Grab the plug of the yellow cable going into the implant.", "Grab the yellow plug and pull it away from the implant."),
                    T(TaskId.SRV_Bag, "Bag the implant as evidence",
                        "Forensics will want the device.", "The evidence bag is on the cart in the aisle.", "Grab the implant and drop it in the evidence bag."),
                    T(TaskId.SRV_Isolate, "Move the infected port to the quarantine VLAN",
                        "Use the switch console on the wall at the end of the aisle.", "The implant was on the port with the unknown device (Raspberry Pi).", "Select port 14 and press 'Move to QUARANTINE VLAN'."),
                    T(TaskId.SRV_Restore, "Restore the file server from a clean backup",
                        "The backup console is on the laptop cart.", "The breach started at 01:58. Pick a backup from before that.", "Restore the 00:00 snapshot (verified clean)."),
                    T(TaskId.SRV_Report, "File the incident report",
                        "The report terminal is next to the backup console.", "Think back: how did the attacker get in, and what stopped them?", "Answers: weak guessable password / encrypting AURORA + revoking access / training + 2FA for everyone."),
                },
            },
        };

        public static readonly LearningPoint[] Points =
        {
            P(TaskId.SOC_Badge, "Your badge is a key", "Never lend your access badge or hold the door for strangers ('tailgating'). Physical access is security too."),
            P(TaskId.SOC_OpenAlert, "Triage by severity", "A SOC sees thousands of alerts. Critical alerts with data leaving the network come first."),
            P(TaskId.SOC_FindHost, "Logs tell the story", "Unusual volumes, odd hours and unknown destinations in the logs point to the compromised machine."),
            P(TaskId.SOC_Classify, "Know the attack", "Exfiltration = copying data out of the company. It often happens at night when nobody is watching."),
            P(TaskId.SOC_Escalate, "Escalate fast", "Follow the incident response plan: declare the incident early so the right people act in minutes, not hours."),
            P(TaskId.SOC_Malware, "Malware: contain, then clean", "Stop the malicious process, isolate the machine from the network so it cannot spread or phone home, then quarantine the file. Never pay a ransom."),
            P(TaskId.LockWorkstation, "Lock your workstation", "Fred left his PC unlocked and an attacker took over. Always lock your screen when you step away (Windows+L)."),
            P(TaskId.EvidencePrintout, "Collect your printouts", "Sensitive printouts left in a shared printer can be read by anyone. Use secure 'follow-me' printing."),
            P(TaskId.EvidenceNote, "Never write passwords down", "A note in the bin gave away Fred's password pattern. Bins are a goldmine for attackers ('dumpster diving')."),
            P(TaskId.ShredDocs, "Clean desk & shred", "Confidential paper belongs in the shredder, not on your desk or in the bin."),
            P(TaskId.QuarantineUSB, "Beware unknown USB sticks", "Attackers drop 'interesting' USB sticks hoping you plug them in (USB baiting). Hand them to IT security."),
            P(TaskId.FredLogin, "Personal info = weak passwords", "Pet names, birthdays and years are the first things attackers try. 'Fluffy2019!' took them minutes."),
            P(TaskId.NewPassword, "Long, unique passphrases", "Use a long passphrase that is unique to every account, and a password manager to remember them."),
            P(TaskId.Enable2FA, "Turn on two-factor authentication", "With 2FA a stolen password isn't enough: the attacker also needs your token or phone."),
            P(TaskId.PhishingTriage, "Spot the phish", "Look for spoofed domains, urgency, threats and unusual requests. When in doubt, report it."),
            P(TaskId.SocialEngineering, "Never share 2FA codes", "No real helpdesk will ask for your one-time code. Anyone who does is an attacker."),
            P(TaskId.EncryptData, "Encrypt sensitive data", "Encrypted files are useless to a thief. Encrypt data at rest and revoke compromised sessions quickly."),
            P(TaskId.SRV_Flashlight, "Be prepared", "Incident kits need basics like a torch, evidence bags and a contact list: during an outage or a breach the lights may be off."),
            P(TaskId.SRV_Find, "Guard the server room", "A rogue device plugged into a rack can bypass every firewall. Restrict and monitor physical access."),
            P(TaskId.SRV_Unplug, "Contain first", "Cut the attacker's connection before investigating further, so the damage stops growing."),
            P(TaskId.SRV_Bag, "Preserve evidence", "Keep devices and logs untouched in evidence bags: they prove what happened and who did it."),
            P(TaskId.SRV_Isolate, "Segment the network", "Quarantine VLANs and segmentation stop an infection from spreading to other systems."),
            P(TaskId.SRV_Restore, "3-2-1 backups", "3 copies, 2 media, 1 offline. Restore from a backup taken before the breach, and verify it."),
            P(TaskId.SRV_Report, "Learn from incidents", "A blameless incident report turns one bad night into better security for everyone."),
        };

        /// Containment areas on Chapter 2's wall board; each reveals one door-code digit.
        public static readonly (string name, TaskId[] tasks)[] Areas =
        {
            ("PHYSICAL", new[] { TaskId.EvidencePrintout, TaskId.EvidenceNote, TaskId.ShredDocs, TaskId.QuarantineUSB }),
            ("ACCOUNT", new[] { TaskId.LockWorkstation, TaskId.FredLogin, TaskId.NewPassword, TaskId.Enable2FA }),
            ("COMMS", new[] { TaskId.PhishingTriage, TaskId.SocialEngineering }),
            ("DATA", new[] { TaskId.EncryptData }),
        };

        public static ChapterInfo ChapterOf(TaskId id) => Chapters.First(c => c.tasks.Any(t => t.id == id));

        public static TaskInfo Info(TaskId id)
        {
            foreach (var c in Chapters)
            foreach (var t in c.tasks)
                if (t.id == id) return t;
            return default;
        }

        public static bool HasPoint(TaskId id) => Points.Any(p => p.task == id);

        public static LearningPoint Point(TaskId id)
        {
            foreach (var p in Points) if (p.task == id) return p;
            return default;
        }

        static TaskInfo T(TaskId id, string label, params string[] hints) => new TaskInfo { id = id, label = label, hints = hints };
        static LearningPoint P(TaskId id, string title, string text) => new LearningPoint { task = id, title = title, text = text };
    }
}
