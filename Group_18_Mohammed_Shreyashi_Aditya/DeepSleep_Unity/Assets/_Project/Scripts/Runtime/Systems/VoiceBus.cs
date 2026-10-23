using System;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Gameplay scripts request narrative lines by ID; NarrativeVoiceDirector / HalcyonDirector decide whether and how to play them.</summary>
    public static class VoiceBus
    {
        public static event Action<string> Requested;
        public static event Action<string> Cancelled;

        public static void Request(string id)
        {
            DS.Log($"Voice request {id}");
            Requested?.Invoke(id);
        }

        public static void Cancel(string id) => Cancelled?.Invoke(id);

        /// <summary>Restart: every director drops its queue and stops talking.</summary>
        public static event Action AllCancelled;
        public static void CancelAll() { AllCancelled?.Invoke(); }

        /// <summary>For the leak audit: listeners must not pile up across scene reloads.</summary>
        public static int SubscriberCount => (Requested?.GetInvocationList().Length ?? 0) + (Cancelled?.GetInvocationList().Length ?? 0) + (AllCancelled?.GetInvocationList().Length ?? 0);
    }

    /// <summary>
    /// A queued line is dropped when the game has already moved past the stage it belongs to (a slow queue must never talk about the previous puzzle).
    /// The value is the last stage in which the line is still meaningful.
    /// </summary>
    public static class VoiceRules
    {
        static readonly System.Collections.Generic.Dictionary<string, Stage> LastStage = new System.Collections.Generic.Dictionary<string, Stage>
        {
            { "R001", Stage.Setup }, { "R002", Stage.Setup }, { "R003", Stage.Setup },
            { "R010", Stage.Awakening }, { "H001", Stage.Awakening }, { "H002", Stage.Awakening }, { "R011", Stage.Awakening }, { "R012", Stage.Awakening },
            { "R030", Stage.EarCheck }, { "R031", Stage.EarCheck }, { "R032", Stage.EarCheck }, { "H004", Stage.EarCheck },
            { "R040", Stage.KeypadSolved }, { "R041", Stage.EarCheck }, { "R043", Stage.KeypadSolved }, { "R050", Stage.KeypadSolved }, { "R052", Stage.TrailFollowed },
            { "R140", Stage.Block1 }, { "R150", Stage.Block2 }, { "R151", Stage.Block2 }, { "R153", Stage.CoolantRouted },
            { "H007", Stage.CoolantRouted }, { "H008", Stage.Escaped }, { "R160", Stage.Escaped }, { "R161", Stage.Questionnaire },
        };

        /// <summary>Audio audits request lines out of their stage on purpose.</summary>
        public static bool BypassForTests;
        public static bool IsStale(string id, Stage current) => !BypassForTests && LastStage.TryGetValue(id, out var last) && current > last;
        public static bool Stale(string id) { var sm = SessionManager.Instance; return sm != null && IsStale(id, sm.Current); }
    }

    /// <summary>Player comfort choices made in SCN_Setup.</summary>
    public static class ComfortSettings
    {
        public static bool VignetteEnabled = true;
        public static float MasterVolume = 0.6f;
    }
}
