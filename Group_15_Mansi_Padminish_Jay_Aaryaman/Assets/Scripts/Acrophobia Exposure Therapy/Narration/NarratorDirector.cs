using System;
using System.Collections.Generic;
using UnityEngine;
using Acrophobia.AdaptiveFSM;
using Acrophobia.GazeGeometry;
using Acrophobia.ProceduralEnvironment;
using Acrophobia.Telemetry;

namespace Acrophobia.Narration
{
    /// <summary>
    /// Plays narrator lines in response to what the participant does, and to what they
    /// conspicuously do not do.
    ///
    /// A voice replaces the floating instruction panel, which is the least convincing
    /// thing in a VR scene - nobody has a status readout bolted to their vision. A calm
    /// guiding voice is also literally what a therapist provides, so the device is doing
    /// therapeutic work rather than just being a stylistic reference.
    ///
    /// The reactive cues are the interesting half, and they cost almost nothing because
    /// the telemetry layer already computes the signals: gaze avoidance ratio, hesitation
    /// index, dwell time, retreat events. Noticing that someone has been staring at the
    /// wall rather than the window is a Stanley Parable move and a clinically meaningful
    /// observation at the same time.
    ///
    /// Audio is optional throughout. Every cue plays its subtitle whether or not a clip
    /// exists, so the system is complete and demonstrable before any voice is recorded,
    /// and starts speaking the moment clips are dropped in.
    ///
    /// Nothing here advances or gates anything. The narrator comments; the participant
    /// decides.
    /// </summary>
    public class NarratorDirector : MonoBehaviour
    {
        public enum CueTrigger
        {
            Manual,
            SessionStart,
            DoorOpened,
            DoorClosed,
            FirstLookDown,
            TierEntered,
            SteppedBack,
            RetreatDetected,
            ProlongedAvoidance,
            ProlongedStillness,
            ReachedTopTier
        }

        [Serializable]
        public class Cue
        {
            public string Id = "cue";
            public CueTrigger Trigger = CueTrigger.Manual;

            [Tooltip("Only fires on this tier. 0 means any tier.")]
            public int TierFilter = 0;

            [TextArea(2, 4)]
            public string Line = "";

            [Tooltip("Optional. The subtitle shows regardless.")]
            public AudioClip Clip;

            [Tooltip("Seconds the subtitle stays up when there is no clip.")]
            public float HoldSeconds = 4.5f;

            [Tooltip("Fire at most once per session.")]
            public bool OnceOnly = true;

            [NonSerialized] public bool HasFired;
        }

        [SerializeField] private Cue[] _cues = Array.Empty<Cue>();

        [Tooltip("Seconds of sustained gaze avoidance near an edge before the narrator notices.")]
        [SerializeField, Range(3f, 30f)] private float _avoidanceNoticeSeconds = 9f;

        [Tooltip("Gaze avoidance ratio above which the participant counts as avoiding.")]
        [SerializeField, Range(0.3f, 1f)] private float _avoidanceThreshold = 0.72f;

        [Tooltip("Seconds of near-stillness before the narrator notices.")]
        [SerializeField, Range(5f, 60f)] private float _stillnessNoticeSeconds = 25f;

        [Tooltip("Head speed below which the participant counts as still, in m/s.")]
        [SerializeField, Range(0.01f, 0.3f)] private float _stillnessSpeedThreshold = 0.06f;

        [Tooltip("Minimum gap between any two lines, so the narrator never talks over itself.")]
        [SerializeField, Range(0.5f, 10f)] private float _minimumGapSeconds = 2.5f;

        [SerializeField] private NarratorSubtitleView _subtitles;
        [SerializeField] private AudioSource _voiceSource;
        [SerializeField] private AdaptiveExposureFSM _fsm;
        [SerializeField] private BalconyDoorController _door;
        [SerializeField] private GazeAvoidanceDetector _gaze;
        [SerializeField] private EdgeProximityTracker _edge;
        [SerializeField] private RetreatEventDetector _retreat;
        [SerializeField] private SessionTelemetryLogger _logger;

        private readonly Queue<Cue> _queue = new Queue<Cue>();
        private Cue _speaking;
        private float _speakingUntil;
        private float _nextAllowedAt;

        private float _avoidanceTimer;
        private float _stillnessTimer;
        private bool _seenLookDown;
        private AdaptiveExposureFSM.ExposureState _lastState = AdaptiveExposureFSM.ExposureState.AwaitingStart;

        public bool IsSpeaking => _speaking != null;

        public string CurrentLine => _speaking != null ? _speaking.Line : string.Empty;

        public event Action<string> OnLineStarted;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced += HandleTierAdvanced;
                _fsm.OnTierSteppedBack += HandleSteppedBack;
            }

            if (_door != null)
            {
                _door.OnDoorOpened += HandleDoorOpened;
                _door.OnDoorClosed += HandleDoorClosed;
            }

            if (_retreat != null)
            {
                _retreat.OnRetreatDetected += HandleRetreat;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= HandleTierAdvanced;
                _fsm.OnTierSteppedBack -= HandleSteppedBack;
            }

            if (_door != null)
            {
                _door.OnDoorOpened -= HandleDoorOpened;
                _door.OnDoorClosed -= HandleDoorClosed;
            }

            if (_retreat != null)
            {
                _retreat.OnRetreatDetected -= HandleRetreat;
            }
        }

        public void ResolveReferences()
        {
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();
            if (_door == null) _door = FindAnyObjectByType<BalconyDoorController>();
            if (_gaze == null) _gaze = FindAnyObjectByType<GazeAvoidanceDetector>();
            if (_edge == null) _edge = FindAnyObjectByType<EdgeProximityTracker>();
            if (_retreat == null) _retreat = FindAnyObjectByType<RetreatEventDetector>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();
            if (_subtitles == null) _subtitles = FindAnyObjectByType<NarratorSubtitleView>();

            if (_voiceSource == null)
            {
                _voiceSource = GetComponent<AudioSource>();
                if (_voiceSource == null)
                {
                    _voiceSource = gameObject.AddComponent<AudioSource>();
                    _voiceSource.playOnAwake = false;
                    _voiceSource.loop = false;
                    _voiceSource.spatialBlend = 0f;   // the narrator is not in the room
                    _voiceSource.volume = 0.9f;
                }
            }
        }

        private void Update()
        {
            PollStateTransitions();
            PollReactiveTriggers();
            PumpQueue();
        }

        private void PollStateTransitions()
        {
            if (_fsm == null) return;

            var state = _fsm.CurrentState;
            if (state != _lastState)
            {
                if (_lastState == AdaptiveExposureFSM.ExposureState.AwaitingStart
                    && state == AdaptiveExposureFSM.ExposureState.Exposing)
                {
                    Fire(CueTrigger.SessionStart);
                }

                _lastState = state;
            }
        }

        /// <summary>
        /// The reactive half. These read signals the telemetry layer already computes,
        /// so noticing avoidance costs nothing beyond a timer.
        /// </summary>
        private void PollReactiveTriggers()
        {
            float dt = Time.deltaTime;

            if (_gaze != null && !_seenLookDown && _gaze.IsLookingDown)
            {
                _seenLookDown = true;
                Fire(CueTrigger.FirstLookDown);
            }

            bool nearEdge = _edge != null && _edge.IsNearEdge;
            float avoidance = _gaze != null ? _gaze.GazeAvoidanceRatio : 0f;

            if (nearEdge && avoidance >= _avoidanceThreshold)
            {
                _avoidanceTimer += dt;
                if (_avoidanceTimer >= _avoidanceNoticeSeconds)
                {
                    _avoidanceTimer = 0f;
                    Fire(CueTrigger.ProlongedAvoidance);
                }
            }
            else
            {
                _avoidanceTimer = 0f;
            }

            float speed = _edge != null ? _edge.LocomotionSpeed : 1f;
            if (speed <= _stillnessSpeedThreshold)
            {
                _stillnessTimer += dt;
                if (_stillnessTimer >= _stillnessNoticeSeconds)
                {
                    _stillnessTimer = 0f;
                    Fire(CueTrigger.ProlongedStillness);
                }
            }
            else
            {
                _stillnessTimer = 0f;
            }
        }

        private void PumpQueue()
        {
            if (_speaking != null)
            {
                bool clipDone = _voiceSource == null || !_voiceSource.isPlaying;
                bool holdDone = Time.time >= _speakingUntil;

                if (clipDone && holdDone)
                {
                    _speaking = null;
                    _subtitles?.Hide();
                    _nextAllowedAt = Time.time + _minimumGapSeconds;
                }

                return;
            }

            if (_queue.Count == 0 || Time.time < _nextAllowedAt)
            {
                return;
            }

            Speak(_queue.Dequeue());
        }

        private void Speak(Cue cue)
        {
            _speaking = cue;
            cue.HasFired = true;

            _subtitles?.Show(cue.Line);

            float hold = cue.HoldSeconds;
            if (cue.Clip != null && _voiceSource != null)
            {
                _voiceSource.clip = cue.Clip;
                _voiceSource.Play();
                hold = Mathf.Max(hold, cue.Clip.length);
            }

            _speakingUntil = Time.time + hold;

            _logger?.RecordEvent("narrator_line", _fsm != null ? _fsm.CurrentTier : 0, cue.Id, 0f);
            OnLineStarted?.Invoke(cue.Line);
        }

        /// <summary>
        /// Queues every cue matching a trigger. Queued rather than played immediately so
        /// two events landing in the same frame do not talk over each other.
        /// </summary>
        public void Fire(CueTrigger trigger, int tier = 0)
        {
            if (_cues == null) return;

            for (int i = 0; i < _cues.Length; i++)
            {
                Cue c = _cues[i];
                if (c == null || c.Trigger != trigger) continue;
                if (c.OnceOnly && c.HasFired) continue;
                if (c.TierFilter != 0 && c.TierFilter != tier) continue;
                if (string.IsNullOrEmpty(c.Line)) continue;

                _queue.Enqueue(c);
            }
        }

        public void SetCues(Cue[] cues)
        {
            _cues = cues;
        }

        public int CueCount => _cues != null ? _cues.Length : 0;

        private void HandleTierAdvanced(int tier)
        {
            Fire(CueTrigger.TierEntered, tier);

            if (_fsm != null && tier >= 5)
            {
                Fire(CueTrigger.ReachedTopTier, tier);
            }
        }

        private void HandleSteppedBack(int tier) => Fire(CueTrigger.SteppedBack, tier);

        private void HandleDoorOpened() => Fire(CueTrigger.DoorOpened);

        private void HandleDoorClosed() => Fire(CueTrigger.DoorClosed);

        private void HandleRetreat(float gained, float from) => Fire(CueTrigger.RetreatDetected);
    }
}
