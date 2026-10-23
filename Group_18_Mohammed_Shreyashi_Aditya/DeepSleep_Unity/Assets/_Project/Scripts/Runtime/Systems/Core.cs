using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    public enum SfxEvent
    {
        Grab, Release, Teleport, WallContact, Footstep, ExitDoor, KeyPress, WrongSequence, TerminalBoot,
        ServiceDoor, Plug, Unplug, WrongPlug, RackSolved, BlastDoor, UI, EarPing, Calibration, Lever
    }

    public static class SfxEvents
    {
        public static event Action<SfxEvent, Vector3, int> Raised;
        public static int SubscriberCount => Raised?.GetInvocationList().Length ?? 0;
        public static void Raise(SfxEvent e) => Raised?.Invoke(e, Vector3.zero, 0);
        public static void Raise(SfxEvent e, Vector3 pos, int index = 0) => Raised?.Invoke(e, pos, index);
    }

    [Serializable]
    public class StudyConfigData
    {
        public string participant_id = "1";
        public int seed = 12345;
    }

    public static class StudyConfig
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "StudyConfig.json");
        public static bool WasMissing { get; private set; }
        public static StudyConfigData Data { get; private set; } = new StudyConfigData();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Data = JsonUtility.FromJson<StudyConfigData>(File.ReadAllText(FilePath)) ?? new StudyConfigData();
                    WasMissing = false;
                }
                else
                {
                    WasMissing = true;
                    Data = new StudyConfigData();
                    Save();
                    DS.Warn("StudyConfig.json missing; template written to " + FilePath);
                }
            }
            catch (Exception e) { DS.Warn("StudyConfig load failed: " + e.Message); WasMissing = true; }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e) { DS.Warn("StudyConfig save failed: " + e.Message); }
        }

        public static int ParticipantNumber
        {
            get
            {
                string digits = "";
                foreach (char c in Data.participant_id) if (char.IsDigit(c)) digits += c;
                return int.TryParse(digits, out int n) ? n : 0;
            }
        }
    }
}
