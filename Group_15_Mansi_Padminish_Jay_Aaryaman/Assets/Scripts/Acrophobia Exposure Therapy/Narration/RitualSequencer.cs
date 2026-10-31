using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Acrophobia.AdaptiveFSM;
using Acrophobia.Locomotion;
using Acrophobia.Telemetry;

namespace Acrophobia.Narration
{
    /// <summary>
    /// Runs a short ordinary routine before each exposure: wash, coffee, and only then
    /// the narrator noticing the light outside.
    ///
    /// This is therapeutically the right shape, not just atmosphere. An exposure session
    /// should not open on the feared stimulus - a few minutes of mundane, controllable
    /// activity gives the participant a settled baseline, which is also when the honest
    /// SUDS reading can be taken. The height then arrives into a grounded state rather
    /// than a cold start.
    ///
    /// Most beats cost almost nothing to build. An Interlude fades to black, plays a
    /// sound, and holds: a bathroom that is never modelled, a kettle that does not exist,
    /// each standing up on two audio clips. That is the cheapest scene in the project by
    /// a wide margin.
    ///
    /// Deliberately non-blocking. The balcony door stays usable throughout, so a
    /// participant who wants to walk straight out can, and the narrator simply notices.
    /// Gating the exposure behind a routine would take away the agency the whole design
    /// rests on, and would make the run tedious on the fifth repetition.
    /// </summary>
    public class RitualSequencer : MonoBehaviour
    {
        public enum BeatKind
        {
            Narrate,
            Interlude,
            WaitForApproach,
            SetProp,
            Pause
        }

        [Serializable]
        public class Beat
        {
            public BeatKind Kind = BeatKind.Narrate;

            [TextArea(2, 3)]
            public string Line = "";

            [Tooltip("Interlude audio. The beat still runs, silently, without one.")]
            public AudioClip Clip;

            [Tooltip("Seconds held. For an Interlude this is the time spent on black.")]
            public float Seconds = 3f;

            [Tooltip("WaitForApproach: the place the participant has to reach.")]
            public Transform Marker;

            [Tooltip("WaitForApproach: how close counts, in metres.")]
            public float Radius = 1.0f;

            [Tooltip("WaitForApproach: give up after this long so nobody gets stuck.")]
            public float TimeoutSeconds = 45f;

            [Tooltip("SetProp: object toggled.")]
            public GameObject Prop;

            public bool PropActive = true;

            [Tooltip("Hold the participant still for this beat. Teleport is disabled; " +
                     "they can still look around, and the session controls stay live.")]
            public bool LockMovement = false;
        }

        [Serializable]
        public class TierRitual
        {
            public string Label = "Tier";
            public Beat[] Beats = Array.Empty<Beat>();
        }

        [Tooltip("One ritual per tier, in order. Entry 0 is tier 1.")]
        [SerializeField] private TierRitual[] _rituals = new TierRitual[5];

        [Tooltip("Run the ritual automatically when a tier begins.")]
        [SerializeField] private bool _autoRunOnTier = true;

        [Tooltip("Seconds of black before the first fade-in, so a session opens on waking.")]
        [SerializeField, Range(0f, 4f)] private float _openingBlackSeconds = 1.2f;

        [SerializeField] private ScreenFader _fader;
        [SerializeField] private NarratorSubtitleView _subtitles;
        [SerializeField] private AudioSource _interludeSource;
        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private AdaptiveExposureFSM _fsm;
        [SerializeField] private SessionTelemetryLogger _logger;
        [SerializeField] private TeleportLocomotion _teleport;

        private Coroutine _running;
        private int _runningTier = -1;
        private bool _firstRitualDone;
        private AdaptiveExposureFSM.ExposureState _lastState = AdaptiveExposureFSM.ExposureState.AwaitingStart;

        public bool IsRunning => _running != null;

        public int RunningTier => _runningTier;

        public event Action<int> OnRitualStarted;

        public event Action<int> OnRitualCompleted;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_fsm != null && _autoRunOnTier)
            {
                _fsm.OnTierAdvanced += HandleTier;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= HandleTier;
            }
        }

        public void ResolveReferences()
        {
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();
            if (_teleport == null) _teleport = FindAnyObjectByType<TeleportLocomotion>();
            if (_logger == null) _logger = FindAnyObjectByType<SessionTelemetryLogger>();
            if (_subtitles == null) _subtitles = FindAnyObjectByType<NarratorSubtitleView>();

            if (_fader == null)
            {
                _fader = FindAnyObjectByType<ScreenFader>();
                if (_fader == null) _fader = gameObject.AddComponent<ScreenFader>();
            }

            if (_interludeSource == null)
            {
                _interludeSource = GetComponent<AudioSource>();
                if (_interludeSource == null)
                {
                    _interludeSource = gameObject.AddComponent<AudioSource>();
                    _interludeSource.playOnAwake = false;
                    _interludeSource.spatialBlend = 0f;
                }
            }

            if (_centerEyeAnchor == null)
            {
                var found = GameObject.Find("CenterEyeAnchor");
                if (found != null) _centerEyeAnchor = found.transform;
                else if (Camera.main != null) _centerEyeAnchor = Camera.main.transform;
            }
        }

        public void RunRitual(int tier)
        {
            if (_rituals == null || _rituals.Length == 0) return;
            if (_running != null) StopCoroutine(_running);

            int index = Mathf.Clamp(tier - 1, 0, _rituals.Length - 1);
            _running = StartCoroutine(Play(index, tier));
        }

        public void SkipRitual()
        {
            if (_running == null) return;

            StopCoroutine(_running);
            _running = null;

            _subtitles?.Hide();
            _fader?.SetAlphaImmediate(0f);

            // A skip can land mid-locked-beat, so movement is restored unconditionally.
            if (_teleport != null) _teleport.enabled = true;

            _logger?.RecordEvent("ritual_skipped", _runningTier, "participant_or_system", 0f);
            _runningTier = -1;
        }

        public void SetRituals(TierRitual[] rituals)
        {
            _rituals = rituals;
        }

        public int RitualCount => _rituals != null ? _rituals.Length : 0;

        private IEnumerator Play(int index, int tier)
        {
            _runningTier = tier;
            TierRitual ritual = _rituals != null && index < _rituals.Length ? _rituals[index] : null;

            _logger?.RecordEvent("ritual_started", tier,
                ritual != null ? ritual.Label : "unnamed", 0f);
            OnRitualStarted?.Invoke(tier);

            // Hide subtitles and ensure clean screen - no blackouts or locking
            _subtitles?.Hide();
            if (_fader != null) _fader.SetAlphaImmediate(0f);
            if (_teleport != null) _teleport.enabled = true;

            // Wait for participant movement before triggering the atmospheric sound effect (e.g. coffee pour)
            Vector3 startPos = _centerEyeAnchor != null ? _centerEyeAnchor.position : transform.position;
            bool moved = false;

            float waitTimeout = 120f;
            float waitTimer = 0f;

            while (!moved && waitTimer < waitTimeout)
            {
                waitTimer += Time.deltaTime;

                if (_centerEyeAnchor != null && Vector3.Distance(_centerEyeAnchor.position, startPos) > 0.12f)
                {
                    moved = true;
                    break;
                }

                try
                {
                    if (OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).sqrMagnitude > 0.04f ||
                        OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).sqrMagnitude > 0.04f)
                    {
                        moved = true;
                        break;
                    }
                }
                catch (Exception) { }

#if ENABLE_INPUT_SYSTEM
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.wKey.isPressed || kb.sKey.isPressed || kb.aKey.isPressed || kb.dKey.isPressed))
                {
                    moved = true;
                    break;
                }
#endif
                yield return null;
            }

            // Participant moved! Trigger the ambient morning / apartment sound effect (coffee, washing, etc.)
            AudioClip clipToPlay = null;
            if (ritual != null && ritual.Beats != null)
            {
                for (int b = 0; b < ritual.Beats.Length; b++)
                {
                    if (ritual.Beats[b].Clip != null)
                    {
                        clipToPlay = ritual.Beats[b].Clip;
                        break;
                    }
                }
            }

            if (clipToPlay != null && _interludeSource != null)
            {
                _interludeSource.clip = clipToPlay;
                _interludeSource.volume = 0.85f;
                _interludeSource.Play();
                yield return new WaitForSeconds(Mathf.Min(clipToPlay.length, 7.0f));
            }

            _subtitles?.Hide();
            _logger?.RecordEvent("ritual_completed", tier,
                ritual != null ? ritual.Label : "unnamed", 0f);
            OnRitualCompleted?.Invoke(tier);

            _running = null;
            _runningTier = -1;
        }

        private IEnumerator RunBeat(Beat beat)
        {
            if (beat == null) yield break;

            // Movement lock is scoped to the beat and restored in every exit path,
            // including a skip, so a participant can never be left unable to move.
            // Only deliberate travel is withheld: looking around still works, and the
            // step-back and end-session controls stay live throughout, which the brief
            // requires unconditionally.
            bool lockedHere = beat.LockMovement && _teleport != null && _teleport.enabled;
            if (lockedHere)
            {
                _teleport.enabled = false;
                _logger?.RecordEvent("movement_locked", _runningTier, "ritual_beat", 0f);
            }

            switch (beat.Kind)
            {
                case BeatKind.Narrate:
                    _subtitles?.Show(beat.Line);
                    yield return new WaitForSeconds(Mathf.Max(0.5f, beat.Seconds));
                    _subtitles?.Hide();
                    break;

                case BeatKind.Interlude:
                    // The whole trick: a scene implied by sound, never built.
                    if (_fader != null) yield return _fader.FadeTo(1f, 0.7f);

                    if (!string.IsNullOrEmpty(beat.Line)) _subtitles?.Show(beat.Line);

                    if (beat.Clip != null && _interludeSource != null)
                    {
                        _interludeSource.clip = beat.Clip;
                        _interludeSource.Play();
                    }

                    yield return new WaitForSeconds(Mathf.Max(0.5f, beat.Seconds));

                    if (_interludeSource != null && _interludeSource.isPlaying)
                    {
                        _interludeSource.Stop();
                    }

                    _subtitles?.Hide();
                    if (_fader != null) yield return _fader.FadeTo(0f, 0.9f);
                    break;

                case BeatKind.WaitForApproach:
                    if (!string.IsNullOrEmpty(beat.Line)) _subtitles?.Show(beat.Line);

                    float waited = 0f;
                    while (waited < beat.TimeoutSeconds)
                    {
                        if (beat.Marker == null || _centerEyeAnchor == null) break;

                        Vector3 a = _centerEyeAnchor.position;
                        Vector3 b = beat.Marker.position;
                        float dx = a.x - b.x, dz = a.z - b.z;
                        if (Mathf.Sqrt(dx * dx + dz * dz) <= beat.Radius) break;

                        waited += Time.deltaTime;
                        yield return null;
                    }

                    _subtitles?.Hide();
                    break;

                case BeatKind.SetProp:
                    if (beat.Prop != null) beat.Prop.SetActive(beat.PropActive);
                    break;

                case BeatKind.Pause:
                    yield return new WaitForSeconds(Mathf.Max(0.1f, beat.Seconds));
                    break;
            }

            if (lockedHere)
            {
                _teleport.enabled = true;
                _logger?.RecordEvent("movement_unlocked", _runningTier, "ritual_beat", 0f);
            }
        }

        private void HandleTier(int tier)
        {
            RunRitual(tier);
        }

        /// <summary>
        /// OnTierAdvanced only fires moving from one tier to the next, so tier 1 - the
        /// first and most important ritual - would never have run off it. The session
        /// starting is its own trigger.
        /// </summary>
        private void Update()
        {
            if (_fsm == null || !_autoRunOnTier) return;

            var state = _fsm.CurrentState;
            if (state != _lastState)
            {
                if (_lastState == AdaptiveExposureFSM.ExposureState.AwaitingStart
                    && state == AdaptiveExposureFSM.ExposureState.Exposing
                    && !_firstRitualDone)
                {
                    _firstRitualDone = true;
                    RunRitual(_fsm.CurrentTier);
                }

                _lastState = state;
            }
        }
    }
}
