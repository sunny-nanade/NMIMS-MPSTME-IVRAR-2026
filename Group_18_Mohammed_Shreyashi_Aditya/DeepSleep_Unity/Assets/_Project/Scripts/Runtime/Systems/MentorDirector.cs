using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Rose during a smoke-room block. Every line comes from the exit door through AudioConditionSwitch, so the only thing that differs
    /// between conditions is spatial vs stereo. Beacon call (R201) every 4 s, junction calls (R202 to R204), dead end (R205), idle nudge at 20 s (R207),
    /// near the exit (R208), finish (R209), timeout (R210). Also measures head-turn latency to each beacon call.
    /// </summary>
    public class MentorDirector : MonoBehaviour
    {
        [SerializeField] AudioConditionSwitch audioSwitch;
        [SerializeField] PathTracker tracker;
        [SerializeField] float beaconIntervalSeconds = 4f;
        [SerializeField] float firstCallDelaySeconds = 1.5f;
        [SerializeField] float idleNudgeSeconds = 20f;
        [SerializeField] float nearExitMeters = 3f;
        [SerializeField] float minGapAfterEventSeconds = 5f;
        [SerializeField] float latencyThresholdDegrees = 30f;
        [SerializeField] float latencyMinStartDegrees = 45f;
        [SerializeField] float latencyCapSeconds = 4f;

        public bool Running { get; private set; }
        public int BeaconCalls { get; private set; }
        public List<float> HeadTurnLatenciesMs { get; } = new List<float>();
        public float MeanHeadTurnLatencyMs { get { if (HeadTurnLatenciesMs.Count == 0) return 0f; float s = 0; foreach (var v in HeadTurnLatenciesMs) s += v; return s / HeadTurnLatenciesMs.Count; } }
        public List<string> Played { get; } = new List<string>();

        Transform _anchor;
        float _nextBeacon, _blockStart, _lastEventLine = -99f;
        bool _nudged, _nearSaid;
        int _junctionCallIndex;
        static readonly string[] JunctionLines = { "R202", "R203", "R204" };

        // latency measurement
        bool _measuring; float _measureStart; float _lastAngle;

        public AudioConditionSwitch AudioSwitch { get => audioSwitch; set => audioSwitch = value; }
        public PathTracker Tracker { get => tracker; set => tracker = value; }

        public void BeginBlock(Transform exitAnchor)
        {
            _anchor = exitAnchor;
            if (audioSwitch != null) audioSwitch.Bind(exitAnchor);
            Running = true; BeaconCalls = 0; _nudged = false; _nearSaid = false; _junctionCallIndex = 0;
            HeadTurnLatenciesMs.Clear(); Played.Clear(); _measuring = false;
            _blockStart = Time.time; _nextBeacon = Time.time + firstCallDelaySeconds; _lastEventLine = -99f;
            if (tracker != null)
            {
                tracker.JunctionEntered += OnJunction;
                tracker.DeadEndEntered += OnDeadEnd;
            }
        }

        public void EndBlock(bool timeout)
        {
            if (tracker != null) { tracker.JunctionEntered -= OnJunction; tracker.DeadEndEntered -= OnDeadEnd; }
            Running = false; _measuring = false;
            // beacon calls stop with the block: a call still in flight is cut, the finish line starts at once
            if (audioSwitch != null && audioSwitch.IsPlaying && audioSwitch.LastClipId == "R201") audioSwitch.Stop();
            // a priority line that was only waiting for that beacon call (R208 near the exit, R207 nudge) starts now; the finish line follows it
            if (_pending != null && audioSwitch != null && !audioSwitch.IsPlaying && !CommsBusy) { string waiting = _pending; _pending = null; BeginLine(waiting); }
            Say(timeout ? "R210" : "R209", true);
        }

        void OnDestroy()
        {
            if (tracker != null) { tracker.JunctionEntered -= OnJunction; tracker.DeadEndEntered -= OnDeadEnd; }
        }

        void OnJunction(MazeCell cell)
        {
            if (Time.time - _lastEventLine < minGapAfterEventSeconds) return;
            Say(JunctionLines[_junctionCallIndex++ % JunctionLines.Length], false);
        }

        void OnDeadEnd(MazeCell cell)
        {
            if (Time.time - _lastEventLine < minGapAfterEventSeconds) return;
            Say("R205", true); // feedback for a wrong turn may cut a beacon call
        }

        string _pending;

        /// <summary>True while a line is playing or a priority line is waiting for the current one to end.</summary>
        public bool IsBusy => _pending != null || (audioSwitch != null && audioSwitch.IsPlaying);

        /// <summary>A comms line (Rose over the headset) is still talking: guidance waits so that her voices never overlap.</summary>
        static bool CommsBusy => NarrativeVoiceDirector.Instance != null && NarrativeVoiceDirector.Instance.IsSpeaking;

        bool Say(string id, bool force)
        {
            if (audioSwitch == null) return false;
            VoiceLatency.Triggered(id, audioSwitch.IsPlaying ? audioSwitch.LastClipId : (_pending != null ? _pending : (CommsBusy ? NarrativeVoiceDirector.Instance.Current : null)));
            if (audioSwitch.IsPlaying || _pending != null || CommsBusy)
            {
                if (!force) { VoiceLatency.Dropped(id); return false; }
                _pending = id; // a priority line never cuts the line that is playing: it starts when that one ends
                return true;
            }
            BeginLine(id);
            return true;
        }

        void BeginLine(string id)
        {
            float len = audioSwitch.Play(id);
            Played.Add(id);
            _lastEventLine = Time.time;
            Duck(len);
        }

        static void Duck(float seconds) { SfxDirector.Duck(seconds); }

        void Update()
        {
            if (_pending != null && audioSwitch != null && !audioSwitch.IsPlaying && !CommsBusy) { string id = _pending; _pending = null; BeginLine(id); }
            if (!(Running || _pending != null) || _anchor == null) return;
            if (!Running) return;
            var rig = PlayerRig.Instance;

            if (Time.time >= _nextBeacon)
            {
                _nextBeacon = Time.time + beaconIntervalSeconds;
                VoiceLatency.Triggered("R201");
                if (!audioSwitch.IsPlaying && _pending == null && !CommsBusy)
                {
                    float len = audioSwitch.Play("R201");
                    Played.Add("R201"); BeaconCalls++;
                    Duck(len);
                    StartMeasure(rig);
                }
                else VoiceLatency.Dropped("R201");
            }

            if (_measuring && rig != null && rig.Head != null)
            {
                float ang = AngleToAnchor(rig);
                float t = Time.time - _measureStart;
                if (ang <= latencyThresholdDegrees) { HeadTurnLatenciesMs.Add(t * 1000f); _measuring = false; }
                else if (t >= latencyCapSeconds) { HeadTurnLatenciesMs.Add(latencyCapSeconds * 1000f); _measuring = false; }
            }

            if (tracker != null)
            {
                if (!_nudged && tracker.IdleStreak >= idleNudgeSeconds && Say("R207", true)) _nudged = true; // a stuck player is nudged even while a beacon call is playing
                if (tracker.IdleStreak < 1f) _nudged = false;
            }
            if (!_nearSaid && rig != null && rig.Head != null)
            {
                Vector3 d = rig.Head.position - _anchor.position; d.y = 0;
                if (d.magnitude <= nearExitMeters && Say("R208", true)) _nearSaid = true;
            }
        }

        void StartMeasure(PlayerRig rig)
        {
            if (rig == null || rig.Head == null) return;
            _measuring = false;
            if (AngleToAnchor(rig) < latencyMinStartDegrees) return; // already facing the door: no turn to time
            _measuring = true; _measureStart = Time.time; _lastAngle = AngleToAnchor(rig);
        }

        float AngleToAnchor(PlayerRig rig)
        {
            Vector3 to = _anchor.position - rig.Head.position; to.y = 0;
            Vector3 f = rig.Head.forward; f.y = 0;
            if (to.sqrMagnitude < 0.0001f || f.sqrMagnitude < 0.0001f) return 0f;
            return Vector3.Angle(f, to);
        }
    }
}
