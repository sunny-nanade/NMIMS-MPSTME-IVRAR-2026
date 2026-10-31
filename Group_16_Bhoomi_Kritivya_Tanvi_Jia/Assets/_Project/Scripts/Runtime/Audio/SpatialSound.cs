using UnityEngine;

namespace RescueSim.Audio
{
    /// <summary>Convenience wrapper around an AudioSource for positional, procedurally generated sounds.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class SpatialSound : MonoBehaviour
    {
        public enum Cue
        {
            None, Click, Clunk, Confirm, Success, Failure, Warning, Alarm, DistressWhistle,
            Hiss, Crackle, ElectricBuzz, AmbientHum, Spray, Cough,
            // Appended (values are serialized in scenes: never reorder).
            Rain, Rumble, WaterLap, Wind
        }

        [SerializeField] Cue loopCue = Cue.None;
        [SerializeField] bool playOnAwake = false;
        [SerializeField, Range(0f, 1f)] float volume = 0.7f;
        [SerializeField, Range(0f, 1f)] float spatialBlend = 1f;
        [SerializeField, Min(0.1f)] float minDistance = 1.5f;
        [SerializeField, Min(1f)] float maxDistance = 25f;

        AudioSource _source;
        Cue _currentLoop = Cue.None;

        AudioSource Source
        {
            get
            {
                if (_source == null)
                {
                    _source = GetComponent<AudioSource>();
                    _source.playOnAwake = false;
                    _source.spatialBlend = spatialBlend;
                    _source.rolloffMode = AudioRolloffMode.Logarithmic;
                    _source.minDistance = minDistance;
                    _source.maxDistance = maxDistance;
                    _source.dopplerLevel = 0f;
                }
                return _source;
            }
        }

        public float Volume { get => volume; set { volume = Mathf.Clamp01(value); Source.volume = volume; } }

        void Awake()
        {
            _ = Source;
            Source.volume = volume;
            if (playOnAwake && loopCue != Cue.None) PlayLoop(loopCue);
        }

        public void PlayLoop(Cue cue)
        {
            if (cue == Cue.None) return;
            if (_currentLoop == cue && Source.isPlaying) return;
            _currentLoop = cue;
            Source.clip = ProceduralAudio.Get(cue);
            Source.loop = true;
            Source.volume = volume;
            Source.Play();
        }

        public void PlayOneShot(Cue cue)
        {
            if (cue == Cue.None) return;
            Source.PlayOneShot(ProceduralAudio.Get(cue), volume);
        }

        public void Stop()
        {
            if (_source == null) return;
            _source.Stop();
            _currentLoop = Cue.None;
        }

        public bool IsPlaying => _source != null && _source.isPlaying;
    }
}
