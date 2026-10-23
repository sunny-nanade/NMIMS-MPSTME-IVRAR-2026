using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// HALCYON, the ship AI that lies. Plays every VoiceBus request for an H line from the ceiling speaker of the room (object "HalcyonSpeaker"),
    /// or flat in the head if the scene has none. Waits for Rose to finish. H004 (wrong keypad sequence) plays at most once per 30 s.
    /// </summary>
    public class HalcyonDirector : MonoBehaviour
    {
        public static HalcyonDirector Instance { get; private set; }

        [SerializeField] float h004CooldownSeconds = 30f;
        [SerializeField] float maxWaitForRoseSeconds = 10f;
        [SerializeField] int maxQueue = 4;
        [SerializeField] float voicePitch = 0.78f; // deeper (and slightly slower) than Rose so the two voices are easy to tell apart
        [SerializeField] bool skipLeadingSilence = true; // the voice files start with about 0.2 s of silence

        public bool IsSpeaking => _src != null && _src.isPlaying;
        public bool IsBusy => IsSpeaking || _queue.Count > 0;
        /// <summary>Time of the oldest line still waiting, or -1. Rose lets an older HALCYON line go first.</summary>
        public float OldestWaitingSince => _queue.Count > 0 ? _queue.Peek().since : -1f;
        public string Current { get; private set; }
        public List<string> Played { get; } = new List<string>();

        AudioSource _src;
        readonly Queue<(string id, float since)> _queue = new Queue<(string, float)>();
        float _lastH004 = -999f;
        bool _wasPlaying;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            var go = new GameObject("HalcyonSpeakerSource"); go.transform.SetParent(transform, false);
            _src = go.AddComponent<AudioSource>();
            _src.pitch = voicePitch; _src.playOnAwake = false; _src.rolloffMode = AudioRolloffMode.Linear; _src.minDistance = 6f; _src.maxDistance = 40f; // inside the 8 m room his level hardly changes
            var g = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.Group("Voice_Halcyon") : null;
            if (g != null) _src.outputAudioMixerGroup = g;
        }

        void OnEnable() { VoiceBus.Requested += OnRequest; VoiceBus.Cancelled += OnCancel; VoiceBus.AllCancelled += StopAll; }
        void OnDisable() { VoiceBus.Requested -= OnRequest; VoiceBus.Cancelled -= OnCancel; VoiceBus.AllCancelled -= StopAll; }

        public void StopAll() { foreach (var q in _queue) VoiceLatency.Dropped(q.id); _queue.Clear(); if (_src != null) _src.Stop(); Current = null; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnRequest(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'H') return;
            if (InSmokeRoom()) return; // HALCYON is silent inside the smoke room
            if (id == "H004" && Time.time - _lastH004 < h004CooldownSeconds) return;
            foreach (var q in _queue) if (q.id == id) return;
            if (_queue.Count >= maxQueue) VoiceLatency.Dropped(_queue.Dequeue().id);
            var rose = NarrativeVoiceDirector.Instance;
            VoiceLatency.Triggered(id, _src.isPlaying ? Current : (_queue.Count > 0 ? _queue.Peek().id : (rose != null && rose.IsBusy ? (rose.IsSpeaking ? rose.Current : "queued") : null)));
            AudioUtil.LeadSilence(DeepSleepAssets.Get(id));
            _queue.Enqueue((id, Time.time));
        }

        void OnCancel(string id)
        {
            if (Current == id && _src.isPlaying) _src.Stop();
        }

        static Transform FindSpeaker()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var r in s.GetRootGameObjects())
                {
                    var t = Find(r.transform, "HalcyonSpeaker");
                    if (t != null) return t;
                }
            }
            return null;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name && t.gameObject.activeInHierarchy) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static bool InSmokeRoom() { return SceneFlow.Section == DS.SceneSmokeRoom; }

        void Update()
        {
            if (_wasPlaying && !_src.isPlaying && !AudioListener.pause) VoiceLatency.NoteEnded();
            _wasPlaying = _src.isPlaying;
            if (InSmokeRoom()) { if (_src.isPlaying) _src.Stop(); foreach (var q in _queue) VoiceLatency.Dropped(q.id); _queue.Clear(); return; }
            if (_src.isPlaying && Current != null && !AudioListener.pause && VoiceRules.Stale(Current)) { DS.Log($"HALCYON {Current} cut: its stage has passed"); _src.Stop(); Current = null; }
            if (_src.isPlaying || AudioListener.pause || _queue.Count == 0) return;
            if (!VoiceGate.Open) return;
            while (_queue.Count > 0 && VoiceRules.Stale(_queue.Peek().id)) { DS.Log($"HALCYON {_queue.Peek().id} dropped: its stage has passed"); VoiceLatency.Dropped(_queue.Dequeue().id); }
            if (_queue.Count == 0) return;
            var rose = NarrativeVoiceDirector.Instance;
            var next = _queue.Peek();
            if (rose != null && rose.IsSpeaking)
            {
                // never overlap Rose: wait for the line she is saying; a line that has waited too long is stale and is dropped
                if (Time.time - next.since > maxWaitForRoseSeconds * 3f) VoiceLatency.Dropped(_queue.Dequeue().id);
                return;
            }
            _queue.Dequeue();
            var clip = DeepSleepAssets.Get(next.id);
            if (clip == null) { DS.Warn("HalcyonDirector: no clip for " + next.id); return; }
            var speaker = FindSpeaker();
            if (speaker != null) { _src.transform.position = speaker.position; _src.spatialBlend = 1f; _src.spatialize = false; }
            else { _src.transform.localPosition = Vector3.zero; _src.spatialBlend = 0f; _src.spatialize = false; }
            _src.volume = VoiceLevels.TrimLinear(clip);
            float skipped = AudioUtil.PlaySkippingLead(_src, clip, skipLeadingSilence);
            VoiceLatency.Played(next.id, skipped);
            Current = next.id; Played.Add(next.id);
            if (next.id == "H004") _lastH004 = Time.time;
            SfxDirector.Duck(clip.length);
            DS.Log($"HALCYON {next.id} ({clip.length:F1} s)");
        }
    }
}
