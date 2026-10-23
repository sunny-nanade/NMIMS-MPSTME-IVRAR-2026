using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// The experimental manipulation. Rose's block voice comes from the exit door either
    ///   Spatial: 3D source through the Meta XR Audio spatializer (HRTF), or
    ///   Stereo: 2D source, identical in both ears.
    /// Everything else is the same: the same mono clip data, the same distance gain computed in code (engine rolloff is flattened),
    /// the same mixer group. Stereo gets the gain trim computed by the level-match tool (DeepSleepAssets.stereoGainTrimDb).
    /// </summary>
    public class AudioConditionSwitch : MonoBehaviour
    {
        public static AudioConditionSwitch Instance { get; private set; }

        [SerializeField] float nearMeters = 1f;
        [SerializeField] float farMeters = 25f;
        [SerializeField, Range(0f, 1f)] float farGain = 0.6f;

        public GuidanceCondition Condition { get; private set; } = GuidanceCondition.Spatial;
        public AudioSource Source { get; private set; }
        public bool IsPlaying => Source != null && Source.isPlaying;
        public string LastClipId { get; private set; }

        readonly Dictionary<string, AudioClip> _mono = new Dictionary<string, AudioClip>();
        Transform _anchor;
        float _clipTrim = 1f; // peak equalisation of the current line (VoiceLevels), the same in both conditions

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public AudioSource Bind(Transform anchor)
        {
            _anchor = anchor;
            if (Source == null)
            {
                var go = new GameObject("RoseGuidanceSource");
                Source = go.AddComponent<AudioSource>();
                Source.playOnAwake = false; Source.loop = false;
                var g = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.Group("Voice_Rose") : null;
                if (g != null) Source.outputAudioMixerGroup = g;
            }
            Source.transform.SetParent(anchor, false);
            Source.transform.localPosition = Vector3.zero;
            Apply(Condition);
            return Source;
        }

        public void Apply(GuidanceCondition condition)
        {
            Condition = condition;
            if (Source == null) return;
            // flat engine rolloff in both conditions: distance gain is applied in code (UpdateGain)
            Source.rolloffMode = AudioRolloffMode.Custom;
            Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 500f, 1f));
            Source.minDistance = 1f; Source.maxDistance = 500f;
            if (condition == GuidanceCondition.Spatial)
            {
                Source.spatialBlend = 1f; Source.spatialize = true; Source.spread = 0f; Source.panStereo = 0f;
            }
            else
            {
                Source.spatialBlend = 0f; Source.spatialize = false; Source.panStereo = 0f;
            }
            UpdateGain();
        }

        /// <summary>Set by the level-match tool while it measures the raw difference between the conditions.</summary>
        public bool IgnoreTrim { get; set; }

        float TrimLinear(float meters)
        {
            if (IgnoreTrim || Condition != GuidanceCondition.Stereo || DeepSleepAssets.Instance == null) return 1f;
            return Mathf.Pow(10f, DeepSleepAssets.Instance.StereoTrimAt(meters) / 20f);
        }

        public float DistanceGain(float meters)
        {
            float t = Mathf.InverseLerp(nearMeters, farMeters, meters);
            return Mathf.Lerp(1f, farGain, t);
        }

        bool _wasPlaying;

        void Update()
        {
            UpdateGain();
            bool playing = IsPlaying;
            if (_wasPlaying && !playing) VoiceLatency.NoteEnded();
            _wasPlaying = playing;
        }

        void UpdateGain()
        {
            if (Source == null) return;
            float d = 0f;
            var rig = PlayerRig.Instance;
            if (rig != null && rig.Head != null) d = Vector3.Distance(rig.Head.position, Source.transform.position);
            Source.volume = DistanceGain(d) * TrimLinear(d) * _clipTrim;
        }

        /// <summary>Mono down-mix of a clip (cached) so both conditions play identical sample data.</summary>
        public AudioClip MonoClip(string id)
        {
            if (_mono.TryGetValue(id, out var c)) return c;
            var mono = AudioUtil.ToMono(DeepSleepAssets.Get(id));
            _mono[id] = mono;
            return mono;
        }

        /// <summary>Plays a Rose line from the exit door. Returns its length in seconds (0 when the clip is missing).</summary>
        public float Play(string id)
        {
            var clip = MonoClip(id);
            if (clip == null || Source == null) { DS.Warn("AudioConditionSwitch: no clip for " + id); return 0f; }
            if (!Source.isActiveAndEnabled) return 0f; // the exit door is gone (scene unloading): nothing to play from
            _clipTrim = VoiceLevels.TrimLinear(DeepSleepAssets.Get(id));
            UpdateGain();
            Source.Stop();
            float skipped = AudioUtil.PlaySkippingLead(Source, clip); // the files start with about 0.2 s of silence
            VoiceLatency.Played(id, skipped);
            LastClipId = id;
            return clip.length;
        }

        public void Stop() { if (Source != null) Source.Stop(); }
    }
}
