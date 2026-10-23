using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fractured_Final
{
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }

        [SerializeField] bool logTransitions = true;

        public Stage Current { get; private set; } = Stage.Setup;
        /// <summary>0 outside blocks, 1 or 2 during/after the smoke-room blocks.</summary>
        public int CurrentBlock { get; set; }
        public string SessionId { get; private set; }
        public event Action<Stage, Stage> StageChanged;
        public List<string> History { get; } = new List<string>();
        readonly Dictionary<Stage, float> _enter = new Dictionary<Stage, float>();
        public Dictionary<Stage, float> StageDurations { get; } = new Dictionary<Stage, float>();
        public int StageChangedSubscribers => StageChanged?.GetInvocationList().Length ?? 0;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            SessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _enter[Stage.Setup] = Time.unscaledTime;
        }

        public float TimeInStage => Time.unscaledTime - (_enter.TryGetValue(Current, out var t) ? t : Time.unscaledTime);

        /// <summary>Advance exactly one stage. Skipping or going backwards is rejected.</summary>
        public bool Advance(Stage next)
        {
            if ((int)next != (int)Current + 1)
            {
                DS.Warn($"Stage skip rejected: {Current} -> {next}");
                return false;
            }
            Transition(next);
            return true;
        }

        /// <summary>Advance only if currently at the stage just before next (idempotent helper for scene scripts).</summary>
        public bool TryAdvance(Stage next)
        {
            if (Current >= next) return false;
            return Advance(next);
        }

        /// <summary>Editor / experimenter jump. Bypasses the no-skip rule on purpose.</summary>
        public void JumpTo(Stage s)
        {
            DS.Log($"Jump to stage {s} (from {Current})");
            Transition(s);
        }

        public void ResetSession()
        {
            Current = Stage.Setup; CurrentBlock = 0;
            SessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _enter.Clear(); StageDurations.Clear(); History.Clear();
            _enter[Stage.Setup] = Time.unscaledTime;
        }

        void Transition(Stage next)
        {
            var from = Current;
            StageDurations[from] = TimeInStage;
            Current = next;
            _enter[next] = Time.unscaledTime;
            History.Add(next.ToString());
            if (logTransitions) DS.Log($"Stage {from} -> {next}");
            StageChanged?.Invoke(from, next);
        }

        /// <summary>For scenes opened on their own in the Editor: make sure the stage is at least s.</summary>
        public void EnsureAtLeast(Stage s)
        {
            if (Current < s) JumpTo(s);
        }
    }
}
