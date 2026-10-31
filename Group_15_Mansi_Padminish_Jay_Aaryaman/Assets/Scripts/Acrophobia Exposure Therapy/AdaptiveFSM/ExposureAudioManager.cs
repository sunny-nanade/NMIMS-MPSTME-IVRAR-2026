using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Acrophobia.AdaptiveFSM
{
    /// <summary>
    /// Manages immersive environmental audio and therapeutic auditory biofeedback
    /// for the Acrophobia Exposure Therapy application in Unity 6 LTS.
    /// 
    /// Features:
    /// - 2D Ambient high-altitude wind loop (Wind_base_loop.mp3) on the player's head.
    /// - 3D Muffled street traffic audio (City_traffic.mp3) projected 115m below on the ground.
    /// - One-shot cues: Tier completion chime (Bell_Chime.mp3), safety warning beep (Warning_beep.mp3),
    ///   and mechanical cable creaks (Metal_creak.mp3).
    /// </summary>
    public class ExposureAudioManager : MonoBehaviour
    {
        [Header("Audio Clips")]
        [Tooltip("Ambient wind loop playing in 2D around the player's ears.")]
        [SerializeField] private AudioClip _windBaseLoopClip;

        [Tooltip("3D spatialized traffic rumble playing from ground level (115m below).")]
        [SerializeField] private AudioClip _cityTrafficClip;

        [Tooltip("Pleasant chime played upon completing a 30s calm tier.")]
        [SerializeField] private AudioClip _bellChimeClip;

        [Tooltip("Pulsing warning alert played when acute anxiety/freeze triggers safety recovery.")]
        [SerializeField] private AudioClip _warningBeepClip;

        [Tooltip("Heavy structural steel creak played during cable perturbations.")]
        [SerializeField] private AudioClip _cableCreakClip;

        [Tooltip("Strong wind gust played during sudden platform instability.")]
        [SerializeField] private AudioClip _windGustClip;

        [Header("Audio Source References")]
        [Tooltip("2D AudioSource for ambient wind.")]
        [SerializeField] private AudioSource _windAudioSource;

        [Tooltip("3D AudioSource placed at the ground video plane for city traffic.")]
        [SerializeField] private AudioSource _trafficAudioSource;

        [Tooltip("2D AudioSource for UI chimes and alert cues.")]
        [SerializeField] private AudioSource _sfxAudioSource;

        [Tooltip("AudioSource for structural creaks and gusts.")]
        [SerializeField] private AudioSource _creakAudioSource;

        [Header("Volume Balances")]
        [SerializeField, Range(0f, 1f)] private float _windVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _trafficVolume = 0.85f;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.90f;
        [SerializeField, Range(0f, 1f)] private float _creakVolume = 0.80f;

        [Header("Ground Audio Target")]
        [SerializeField] private Transform _groundTarget;

        private void Awake()
        {
            ResolveReferences();
            InitializeAudioSources();
        }

        private void Start()
        {
            StartAmbientLoops();
        }

        /// <summary>
        /// Resolves missing clips, transforms, and AudioSources.
        /// </summary>
        /// <summary>
        /// Blends the ambience between outdoors (0) and sealed indoors (1).
        ///
        /// Driven by the balcony door. Opening it lifts the muffling and lets the city in,
        /// which is the single strongest moment in the sequence: the drop is revealed by
        /// sound arriving before the participant has even looked down. Wind is attenuated
        /// harder than traffic because wind is a near-field cue that a closed door removes
        /// almost entirely, while traffic still carries up from the street.
        /// </summary>
        public void SetIndoorAttenuation(float indoor)
        {
            indoor = Mathf.Clamp01(indoor);

            if (_windAudioSource != null)
            {
                _windAudioSource.volume = _windVolume * Mathf.Lerp(1f, 0.06f, indoor);
                ApplyLowPass(_windAudioSource, Mathf.Lerp(22000f, 520f, indoor));
            }

            if (_trafficAudioSource != null)
            {
                _trafficAudioSource.volume = _trafficVolume * Mathf.Lerp(1f, 0.28f, indoor);
                ApplyLowPass(_trafficAudioSource, Mathf.Lerp(22000f, 900f, indoor));
            }

            if (_creakAudioSource != null)
            {
                _creakAudioSource.volume = Mathf.Lerp(1f, 0.3f, indoor);
            }
        }

        /// <summary>
        /// Adds the filter on first use rather than requiring it to be wired in the scene.
        /// </summary>
        private static void ApplyLowPass(AudioSource source, float cutoffHz)
        {
            var filter = source.GetComponent<AudioLowPassFilter>();
            if (filter == null)
            {
                filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            }

            filter.cutoffFrequency = cutoffHz;
        }

        public void ResolveReferences()
        {
#if UNITY_EDITOR
            if (_windBaseLoopClip == null)
                _windBaseLoopClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Wind_base_loop.mp3");

            if (_cityTrafficClip == null)
                _cityTrafficClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/City_traffic.mp3");

            if (_bellChimeClip == null)
                _bellChimeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Bell_Chime.mp3");

            if (_warningBeepClip == null)
                _warningBeepClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Warning_beep.mp3");

            if (_cableCreakClip == null)
                _cableCreakClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Metal_creak.mp3");

            if (_windGustClip == null)
                _windGustClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Acrophobia Exposure Therapy/Wind_gust_strong.mp3");
#endif

            if (_groundTarget == null)
            {
                var groundQuad = GameObject.Find("Ground_Video_Projection_Quad") ?? GameObject.Find("Ground_Video_Projection");
                if (groundQuad != null)
                {
                    _groundTarget = groundQuad.transform;
                }
            }
        }

        private void InitializeAudioSources()
        {
            // 1. Wind AudioSource (2D Loop on self / player head)
            if (_windAudioSource == null)
            {
                _windAudioSource = gameObject.AddComponent<AudioSource>();
            }
            _windAudioSource.spatialBlend = 0f; // 2D Stereo
            _windAudioSource.loop = true;
            _windAudioSource.playOnAwake = false;
            _windAudioSource.volume = _windVolume;
            if (_windBaseLoopClip != null) _windAudioSource.clip = _windBaseLoopClip;

            // 2. Traffic AudioSource (3D Loop on Ground_Video_Projection)
            if (_trafficAudioSource == null)
            {
                if (_groundTarget != null)
                {
                    _trafficAudioSource = _groundTarget.GetComponent<AudioSource>();
                    if (_trafficAudioSource == null)
                    {
                        _trafficAudioSource = _groundTarget.gameObject.AddComponent<AudioSource>();
                    }
                }
                else
                {
                    // Fallback to local audio source with 3D settings
                    _trafficAudioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (_trafficAudioSource != null)
            {
                _trafficAudioSource.spatialBlend = 1f; // Full 3D
                _trafficAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                _trafficAudioSource.minDistance = 25f;
                _trafficAudioSource.maxDistance = 350f;
                _trafficAudioSource.dopplerLevel = 0f;
                _trafficAudioSource.loop = true;
                _trafficAudioSource.playOnAwake = false;
                _trafficAudioSource.volume = _trafficVolume;
                if (_cityTrafficClip != null) _trafficAudioSource.clip = _cityTrafficClip;
            }

            // 3. SFX AudioSource (2D One-Shot for UI & alerts)
            if (_sfxAudioSource == null)
            {
                _sfxAudioSource = gameObject.AddComponent<AudioSource>();
            }
            _sfxAudioSource.spatialBlend = 0f;
            _sfxAudioSource.loop = false;
            _sfxAudioSource.playOnAwake = false;
            _sfxAudioSource.volume = _sfxVolume;

            // 4. Creak AudioSource
            if (_creakAudioSource == null)
            {
                _creakAudioSource = gameObject.AddComponent<AudioSource>();
            }
            _creakAudioSource.spatialBlend = 0.35f; // Slight spatial presence
            _creakAudioSource.loop = false;
            _creakAudioSource.playOnAwake = false;
            _creakAudioSource.volume = _creakVolume;
        }

        /// <summary>
        /// Starts playback of the continuous ambient soundscapes.
        /// </summary>
        public void StartAmbientLoops()
        {
            if (_windAudioSource != null && _windBaseLoopClip != null && !_windAudioSource.isPlaying)
            {
                _windAudioSource.clip = _windBaseLoopClip;
                _windAudioSource.Play();
            }

            if (_trafficAudioSource != null && _cityTrafficClip != null && !_trafficAudioSource.isPlaying)
            {
                _trafficAudioSource.clip = _cityTrafficClip;
                _trafficAudioSource.Play();
            }
        }

        /// <summary>
        /// Plays the pleasant tier-completion bell chime.
        /// </summary>
        public void PlayChime()
        {
            if (_sfxAudioSource != null && _bellChimeClip != null)
            {
                _sfxAudioSource.pitch = 1.0f;
                _sfxAudioSource.PlayOneShot(_bellChimeClip, _sfxVolume);
            }
        }

        /// <summary>
        /// Plays the high-urgency safety warning beep during acute anxiety or postural freeze.
        /// </summary>
        public void PlayWarningBeep()
        {
            if (_sfxAudioSource != null && _warningBeepClip != null)
            {
                _sfxAudioSource.pitch = 1.0f;
                _sfxAudioSource.PlayOneShot(_warningBeepClip, _sfxVolume);
            }
        }

        /// <summary>
        /// Plays the physical steel creak sound when the platform moves or sways.
        /// </summary>
        public void PlayCableCreak(float volumeMultiplier = 1.0f)
        {
            if (_creakAudioSource != null && _cableCreakClip != null)
            {
                _creakAudioSource.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
                _creakAudioSource.PlayOneShot(_cableCreakClip, Mathf.Clamp01(_creakVolume * volumeMultiplier));
            }
        }

        /// <summary>
        /// Plays a strong wind gust sound during sudden cable jerk perturbations.
        /// </summary>
        public void PlayWindGust(float volumeMultiplier = 1.0f)
        {
            if (_creakAudioSource != null && _windGustClip != null)
            {
                _creakAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
                _creakAudioSource.PlayOneShot(_windGustClip, Mathf.Clamp01(_creakVolume * volumeMultiplier));
            }
        }

        /// <summary>
        /// Dynamically adjusts the wind loop intensity according to exposure tier (e.g. Higher tiers have slightly stronger wind).
        /// </summary>
        public void SetTierWindIntensity(int tier)
        {
            if (_windAudioSource == null) return;
            float targetVol = Mathf.Clamp(_windVolume + (tier - 1) * 0.08f, 0.2f, 1.0f);
            _windAudioSource.volume = targetVol;
        }
    }
}
