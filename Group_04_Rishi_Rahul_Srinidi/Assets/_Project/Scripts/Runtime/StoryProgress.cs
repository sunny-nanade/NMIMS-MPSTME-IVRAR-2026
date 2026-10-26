using System;
using System.Collections.Generic;
using UnityEngine;

namespace CSER
{
    /// Story save data (PlayerPrefs): unlocked chapters, learning points found and per-chapter results.
    public static class StoryProgress
    {
        const string KeyUnlocked = "cser.unlocked";
        const string KeyPoints = "cser.points";
        const string KeyResult = "cser.result.";

        public static int UnlockedChapter => Mathf.Clamp(PlayerPrefs.GetInt(KeyUnlocked, 1), 1, GameData.Chapters.Length);

        public static bool IsUnlocked(int chapter) => chapter <= UnlockedChapter;

        public static bool HasPoint(TaskId t) => Points.Contains(t.ToString());

        public static int PointCount => Points.Count;

        static HashSet<string> Points => new HashSet<string>(PlayerPrefs.GetString(KeyPoints, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

        public static void AddPoint(TaskId t)
        {
            var set = Points;
            if (set.Add(t.ToString()))
            {
                PlayerPrefs.SetString(KeyPoints, string.Join(",", set));
                PlayerPrefs.Save();
            }
        }

        public struct Result
        {
            public bool done;
            public float seconds;
            public int hints, mistakes;
        }

        public static Result Get(int chapter)
        {
            var s = PlayerPrefs.GetString(KeyResult + chapter, "");
            var p = s.Split(';');
            if (p.Length != 3) return default;
            return new Result { done = true, seconds = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), hints = int.Parse(p[1]), mistakes = int.Parse(p[2]) };
        }

        public static void Complete(int chapter, float seconds, int hints, int mistakes)
        {
            var prev = Get(chapter);
            if (!prev.done || seconds < prev.seconds)
                PlayerPrefs.SetString(KeyResult + chapter, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0};{1};{2}", seconds, hints, mistakes));
            if (chapter + 1 > UnlockedChapter) PlayerPrefs.SetInt(KeyUnlocked, Mathf.Min(chapter + 1, GameData.Chapters.Length));
            PlayerPrefs.Save();
        }

        public static bool StoryComplete => Get(GameData.Chapters.Length).done;

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(KeyUnlocked);
            PlayerPrefs.DeleteKey(KeyPoints);
            for (int i = 1; i <= GameData.Chapters.Length; i++) PlayerPrefs.DeleteKey(KeyResult + i);
            PlayerPrefs.Save();
        }
    }
}
