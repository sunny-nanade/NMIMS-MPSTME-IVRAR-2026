using System;
using UnityEngine;

namespace RescueSim.Core
{
    /// <summary>Fixed campaign layout: scene names in play order. Mission data (titles, text) lives in MissionDefinition assets.</summary>
    public static class StageCatalog
    {
        public const string MenuScene = "MainMenu";
        /// <summary>Stand-alone START triage study scene. Not a campaign stage: no unlock, stars or progress.</summary>
        public const string TriageScene = "TriageSimulation";
        public static readonly string[] Scenes = { "FactoryRescue", "EarthquakeRescue", "FloodEvacuation" };
        public static int Count => Scenes.Length;

        public static bool IsValid(int stage) => stage >= 0 && stage < Scenes.Length;
        public static string SceneFor(int stage) => IsValid(stage) ? Scenes[stage] : null;
        public static bool IsFinal(int stage) => stage == Scenes.Length - 1;

        /// <summary>Index of the stage that runs <paramref name="sceneName"/>, or -1.</summary>
        public static int IndexOf(string sceneName) => Array.IndexOf(Scenes, sceneName);

        /// <summary>Next stage index, or -1 when <paramref name="stage"/> is invalid or the last stage.</summary>
        public static int NextAfter(int stage) => IsValid(stage) && stage + 1 < Scenes.Length ? stage + 1 : -1;
    }

    /// <summary>Where campaign progress is persisted. Abstracted so tests can use memory.</summary>
    public interface ICampaignStore
    {
        int GetInt(string key, int defaultValue);
        void SetInt(string key, int value);
        void DeleteKey(string key);
        void Save();
    }

    public sealed class PlayerPrefsStore : ICampaignStore
    {
        public int GetInt(string key, int d) => PlayerPrefs.GetInt(key, d);
        public void SetInt(string key, int v) => PlayerPrefs.SetInt(key, v);
        public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public void Save() => PlayerPrefs.Save();
    }

    public sealed class MemoryStore : ICampaignStore
    {
        readonly System.Collections.Generic.Dictionary<string, int> _d = new System.Collections.Generic.Dictionary<string, int>();
        public int GetInt(string key, int d) => _d.TryGetValue(key, out var v) ? v : d;
        public void SetInt(string key, int v) => _d[key] = v;
        public void DeleteKey(string key) => _d.Remove(key);
        public void Save() { }
    }

    /// <summary>Converts the internal 0-1000 score into the 0-100 score and 1-3 star rating shown to the player.</summary>
    public static class StageScoring
    {
        public const int TwoStarScore = 60;
        public const int ThreeStarScore = 85;

        public static int ToHundred(int total, int maxScore) => maxScore <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(100f * total / maxScore), 0, 100);

        /// <summary>0 stars if the stage was not completed, otherwise 1-3.</summary>
        public static int Stars(int scoreOutOf100, bool completed)
        {
            if (!completed) return 0;
            if (scoreOutOf100 >= ThreeStarScore) return 3;
            if (scoreOutOf100 >= TwoStarScore) return 2;
            return 1;
        }
    }

    /// <summary>Unlock state, best scores and stars. Persists through an <see cref="ICampaignStore"/> (PlayerPrefs by default).</summary>
    public static class CampaignProgress
    {
        const string KeyPrefix = "rs.campaign.";
        const string KeyUnlockAll = KeyPrefix + "unlockAll";
        static ICampaignStore _store = new PlayerPrefsStore();

        public static event Action Changed;

        public static ICampaignStore Store { get => _store; set { _store = value ?? new PlayerPrefsStore(); Changed?.Invoke(); } }

        /// <summary>Demonstration switch: when on, every stage can be selected regardless of progress.</summary>
        public static bool UnlockAll
        {
            get => _store.GetInt(KeyUnlockAll, 0) == 1;
            set { _store.SetInt(KeyUnlockAll, value ? 1 : 0); _store.Save(); Changed?.Invoke(); }
        }

        public static bool IsCompleted(int stage) => StageCatalog.IsValid(stage) && _store.GetInt(KeyPrefix + "done." + stage, 0) == 1;
        public static int BestScore(int stage) => StageCatalog.IsValid(stage) ? _store.GetInt(KeyPrefix + "score." + stage, 0) : 0;
        public static int BestStars(int stage) => StageCatalog.IsValid(stage) ? _store.GetInt(KeyPrefix + "stars." + stage, 0) : 0;

        public static bool IsUnlocked(int stage)
        {
            if (!StageCatalog.IsValid(stage)) return false;
            if (stage == 0 || UnlockAll) return true;
            return IsCompleted(stage - 1);
        }

        public static int CompletedCount()
        {
            int n = 0;
            for (int i = 0; i < StageCatalog.Count; i++) if (IsCompleted(i)) n++;
            return n;
        }

        public static int TotalStars()
        {
            int n = 0;
            for (int i = 0; i < StageCatalog.Count; i++) n += BestStars(i);
            return n;
        }

        public static int MaxStars => StageCatalog.Count * 3;
        public static bool CampaignComplete => CompletedCount() == StageCatalog.Count;

        /// <summary>Stores the outcome of an attempt. Only completed runs unlock the next stage; best score/stars never decrease.</summary>
        /// <returns>false if the stage index is invalid.</returns>
        public static bool RecordResult(int stage, int scoreOutOf100, int stars, bool completed)
        {
            if (!StageCatalog.IsValid(stage)) return false;
            if (completed)
            {
                _store.SetInt(KeyPrefix + "done." + stage, 1);
                if (scoreOutOf100 > BestScore(stage)) _store.SetInt(KeyPrefix + "score." + stage, scoreOutOf100);
                if (stars > BestStars(stage)) _store.SetInt(KeyPrefix + "stars." + stage, stars);
            }
            _store.Save();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Wipes all progress (not comfort/audio settings). Demo unlock-all is also cleared.</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < StageCatalog.Count; i++)
            {
                _store.DeleteKey(KeyPrefix + "done." + i);
                _store.DeleteKey(KeyPrefix + "score." + i);
                _store.DeleteKey(KeyPrefix + "stars." + i);
            }
            _store.DeleteKey(KeyUnlockAll);
            _store.Save();
            Changed?.Invoke();
        }
    }
}
