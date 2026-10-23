using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Rose over comms. Plays every VoiceBus request for an R line (except the block-only calls R201 to R210, which MentorDirector plays from the exit door).
    /// One line at a time, queued in order; the ambience ducks while she speaks. 2D (head-locked comms), mixer group Voice_Rose.
    /// </summary>
    public class NarrativeVoiceDirector : MonoBehaviour
    {
        public static NarrativeVoiceDirector Instance { get; private set; }

        [SerializeField] int maxQueue = 6;
        [SerializeField] float duplicateGapSeconds = 1f;
        [SerializeField] bool skipLeadingSilence = true; // the voice files start with about 0.2 s of silence
        [Tooltip("Comms are 2D and need no distance gain; the Voice_Rose group is run hot for the guidance voice in the smoke room, so the comms source is turned down by this much")]
        [SerializeField] float commsTrimDb = -9.5f;

        public bool IsSpeaking => _src != null && (_src.isPlaying || _paused);
        public string Current { get; private set; }
        public List<string> Played { get; } = new List<string>();
        public int QueueLength => _queue.Count;
        public bool IsBusy => IsSpeaking || _queue.Count > 0;

        AudioSource _src;
        readonly Queue<(string id, float since)> _queue = new Queue<(string, float)>();
        readonly Dictionary<string, float> _last = new Dictionary<string, float>();
        bool _paused, _wasPlaying;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            var go = new GameObject("RoseComms"); go.transform.SetParent(transform, false);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false; _src.spatialBlend = 0f; _src.spatialize = false;
            var g = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.Group("Voice_Rose") : null;
            if (g != null) _src.outputAudioMixerGroup = g;
        }

        void OnEnable() { VoiceBus.Requested += OnRequest; VoiceBus.Cancelled += OnCancel; VoiceBus.AllCancelled += StopAll; }
        void OnDisable() { VoiceBus.Requested -= OnRequest; VoiceBus.Cancelled -= OnCancel; VoiceBus.AllCancelled -= StopAll; }

        public void StopAll() { foreach (var q in _queue) VoiceLatency.Dropped(q.id); _queue.Clear(); if (_src != null) _src.Stop(); Current = null; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static bool IsBlockOnly(string id)
        {
            if (id.Length != 4 || id[0] != 'R' || !int.TryParse(id.Substring(1), out int n)) return false;
            return n >= 201 && n <= 210;
        }

        void OnRequest(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'R' || IsBlockOnly(id)) return;
            if (_last.TryGetValue(id, out float t) && Time.time - t < duplicateGapSeconds) return;
            foreach (var q in _queue) if (q.id == id) return;
            if (Current == id && _src.isPlaying) return;
            if (_queue.Count >= maxQueue) VoiceLatency.Dropped(_queue.Dequeue().id);
            var hal = HalcyonDirector.Instance;
            VoiceLatency.Triggered(id, _src.isPlaying ? Current : (_queue.Count > 0 ? _queue.Peek().id : (hal != null && hal.IsBusy ? (hal.IsSpeaking ? hal.Current : "queued") : null)));
            AudioUtil.LeadSilence(DeepSleepAssets.Get(id)); // read the clip now so the first Play has no work to do
            _queue.Enqueue((id, Time.time));
        }

        void OnCancel(string id)
        {
            if (Current == id && _src.isPlaying) _src.Stop();
            VoiceLatency.Dropped(id);
            var keep = new List<(string id, float since)>(_queue); if (keep.RemoveAll(x => x.id == id) == 0) return;
            _queue.Clear(); foreach (var k in keep) _queue.Enqueue(k);
        }

        void Update()
        {
            _paused = AudioListener.pause;
            if (_wasPlaying && !_src.isPlaying && !_paused) VoiceLatency.NoteEnded();
            _wasPlaying = _src.isPlaying;
            // a line never carries over into the next stage: cut it the moment its stage has passed
            if (_src.isPlaying && Current != null && !_paused && VoiceRules.Stale(Current)) { DS.Log($"Rose {Current} cut: its stage has passed"); _src.Stop(); Current = null; }
            if (_src.isPlaying || _paused || _queue.Count == 0) return;
            if (!VoiceGate.Open) return; // not during the scene-load hitch: a line started there crackles
            while (_queue.Count > 0 && VoiceRules.Stale(_queue.Peek().id)) { DS.Log($"Rose {_queue.Peek().id} dropped: its stage has passed"); VoiceLatency.Dropped(_queue.Dequeue().id); }
            if (_queue.Count == 0) return;
            var hal = HalcyonDirector.Instance;
            if (hal != null && hal.IsSpeaking) return; // never talk over HALCYON
            if (hal != null && hal.OldestWaitingSince >= 0f && hal.OldestWaitingSince < _queue.Peek().since) return; // HALCYON asked first: his line goes between ours
            string id = _queue.Dequeue().id;
            var clip = DeepSleepAssets.Get(id);
            if (clip == null) { DS.Warn("NarrativeVoiceDirector: no clip for " + id); return; }
            _src.volume = VoiceLevels.TrimLinear(clip) * Mathf.Pow(10f, commsTrimDb / 20f);
            float skipped = AudioUtil.PlaySkippingLead(_src, clip, skipLeadingSilence);
            VoiceLatency.Played(id, skipped);
            Current = id; _last[id] = Time.time; Played.Add(id);
            SfxDirector.Duck(clip.length);
            DS.Log($"Rose {id} ({clip.length:F1} s){(DeepSleepAssets.Instance != null && DeepSleepAssets.Instance.IsPlaceholder(id) ? " [placeholder]" : "")}");
        }
    }
}
