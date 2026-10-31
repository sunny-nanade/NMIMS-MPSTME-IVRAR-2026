using System;
using System.Collections.Generic;

namespace RescueSim.Core
{
    public enum ObjectiveStatus { Pending, Complete, Failed }

    public class ObjectiveState
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Hint;
        public readonly bool Required;
        public ObjectiveStatus Status;
        /// <summary>Optional "2/3" style progress text.</summary>
        public string Progress;

        public ObjectiveState(string id, string title, string hint, bool required)
        {
            Id = id; Title = title; Hint = hint; Required = required; Status = ObjectiveStatus.Pending;
        }
    }

    /// <summary>Tracks objective completion. Plain C# so it is easy to unit test.</summary>
    public class ObjectiveTracker
    {
        readonly List<ObjectiveState> _objectives = new List<ObjectiveState>();
        readonly Dictionary<string, ObjectiveState> _byId = new Dictionary<string, ObjectiveState>();

        public IReadOnlyList<ObjectiveState> Objectives => _objectives;
        public event Action<ObjectiveState> ObjectiveChanged;
        public event Action AllRequiredComplete;

        public ObjectiveTracker(IEnumerable<MissionDefinition.ObjectiveDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (var d in definitions)
            {
                if (string.IsNullOrEmpty(d.id) || _byId.ContainsKey(d.id)) continue;
                var s = new ObjectiveState(d.id, d.title, d.hint, d.required);
                _objectives.Add(s);
                _byId.Add(d.id, s);
            }
        }

        public bool TryGet(string id, out ObjectiveState state) => _byId.TryGetValue(id, out state);

        public bool IsComplete(string id) => _byId.TryGetValue(id, out var s) && s.Status == ObjectiveStatus.Complete;

        public void SetProgress(string id, string progress)
        {
            if (!_byId.TryGetValue(id, out var s) || s.Progress == progress) return;
            s.Progress = progress;
            ObjectiveChanged?.Invoke(s);
        }

        /// <summary>Marks an objective complete. Returns false if unknown or already complete.</summary>
        public bool Complete(string id)
        {
            if (!_byId.TryGetValue(id, out var s) || s.Status == ObjectiveStatus.Complete) return false;
            s.Status = ObjectiveStatus.Complete;
            ObjectiveChanged?.Invoke(s);
            if (AllRequiredDone()) AllRequiredComplete?.Invoke();
            return true;
        }

        public bool Fail(string id)
        {
            if (!_byId.TryGetValue(id, out var s) || s.Status != ObjectiveStatus.Pending) return false;
            s.Status = ObjectiveStatus.Failed;
            ObjectiveChanged?.Invoke(s);
            return true;
        }

        public bool AllRequiredDone()
        {
            foreach (var o in _objectives)
                if (o.Required && o.Status != ObjectiveStatus.Complete) return false;
            return _objectives.Count > 0;
        }

        public int CompletedCount()
        {
            int n = 0;
            foreach (var o in _objectives) if (o.Status == ObjectiveStatus.Complete) n++;
            return n;
        }
    }
}
