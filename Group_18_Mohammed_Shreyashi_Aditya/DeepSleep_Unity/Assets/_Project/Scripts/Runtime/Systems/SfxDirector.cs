using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Sound effects, ambience per scene, and the ambience duck under voices (-8 dB through the Normal / Ducked snapshots of MIX_Main).
    /// SfxEvent to clip: UI S091, Teleport S090, Footstep S021, WallContact S020, ExitDoor S022, KeyPress S040 (9 pitches), WrongSequence S041,
    /// TerminalBoot S042, ServiceDoor S050, Plug / Unplug S060, WrongPlug S062, RackSolved / Lever S063, BlastDoor S080.
    /// EarPing and Calibration are played by their own scripts (spatial pings and the setup tones) and ignored here.
    /// </summary>
    public class SfxDirector : MonoBehaviour
    {
        public static SfxDirector Instance { get; private set; }

        [SerializeField] int poolSize = 8;
        [SerializeField] float sfxVolume = 1f;
        [SerializeField] float ambienceVolume = 1f;
        [SerializeField] float ambienceFadeSeconds = 1.5f;
        [SerializeField] float duckInSeconds = 0.15f;
        [SerializeField] float duckOutSeconds = 0.5f;
        [Header("Mixer levels (dB) written to the exposed MIX_Main parameters; the duck is applied under every voice line")]
        [SerializeField] float roseBaseDb = 15f;
        [SerializeField] float smokeRoseBoostDb = 4f; // Rose is louder in the smoke blocks; the same in both conditions, so the level match holds
        [SerializeField] float halcyonBaseDb = 5.5f;
        [SerializeField] float ambienceBaseDb = 0f;
        [SerializeField] float sfxBaseDb = 0f;
        [SerializeField] float ambienceDuckDb = -8f;
        [SerializeField] float sfxDuckDb = -10f;
        [Header("Every SFX and ambience clip is scaled to these RMS levels (dBFS, at group 0 dB) so no clip can mask a voice")]
        [SerializeField] float sfxTargetRmsDb = -30f;
        [SerializeField] float ambienceTargetRmsDb = -36f;
        [SerializeField] float footstepMinGap = 0.25f;

        public static readonly Dictionary<SfxEvent, string> ClipFor = new Dictionary<SfxEvent, string>
        {
            { SfxEvent.UI, "S091" }, { SfxEvent.Teleport, "S090" }, { SfxEvent.Footstep, "S021" }, { SfxEvent.WallContact, "S020" },
            { SfxEvent.ExitDoor, "S022" }, { SfxEvent.KeyPress, "S040" }, { SfxEvent.WrongSequence, "S041" }, { SfxEvent.TerminalBoot, "S042" },
            { SfxEvent.ServiceDoor, "S050" }, { SfxEvent.Plug, "S060" }, { SfxEvent.Unplug, "S060" }, { SfxEvent.WrongPlug, "S062" },
            { SfxEvent.RackSolved, "S063" }, { SfxEvent.Lever, "S063" }, { SfxEvent.BlastDoor, "S080" },
        };

        public bool IsDucked { get; private set; }
        public List<string> Played { get; } = new List<string>();
        public string[] AmbienceIds { get; private set; } = new string[0];
        public IReadOnlyList<AudioSource> AmbienceSources => _amb;
        public AudioSource LastSource { get; private set; }

        AudioSource[] _pool; int _next;
        readonly List<AudioSource> _amb = new List<AudioSource>();
        float _duckUntil, _lastFoot;
        readonly HashSet<string> _warned = new HashSet<string>();
        AudioMixerGroup _sfxGroup, _uiGroup, _ambGroup;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            var assets = DeepSleepAssets.Instance;
            if (assets != null) { _sfxGroup = assets.Group("SFX"); _uiGroup = assets.Group("UI"); _ambGroup = assets.Group("Ambience"); }
            _pool = new AudioSource[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("Sfx_" + i); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.outputAudioMixerGroup = _sfxGroup;
                a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 1f; a.maxDistance = 25f;
                _pool[i] = a;
            }
        }

        void OnEnable()
        {
            SfxEvents.Raised += OnSfx;
            if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived += OnArrived;
        }

        void OnDisable()
        {
            SfxEvents.Raised -= OnSfx;
            if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived -= OnArrived;
        }

        void Start()
        {
            if (SceneFlow.Instance != null) { SceneFlow.Instance.SceneArrived -= OnArrived; SceneFlow.Instance.SceneArrived += OnArrived; }
            ApplyAmbienceFor(SceneFlow.Section);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ SFX
        /// <summary>Effect files that are much longer than the event they serve: play only a window of them (seconds, random start).</summary>
        static readonly Dictionary<string, (float seconds, bool randomStart)> Windows = new Dictionary<string, (float, bool)>
        {
            { "S060", (1.2f, false) },   // plug / unplug: the file is 21 s long
            { "S021", (0.5f, true) },    // footsteps: one step out of a 7.6 s walking recording
        };

        void OnSfx(SfxEvent e, Vector3 pos, int index)
        {
            if (!ClipFor.TryGetValue(e, out var id)) return;
            if (e == SfxEvent.Footstep) { if (Time.time - _lastFoot < footstepMinGap) return; _lastFoot = Time.time; }
            var clip = DeepSleepAssets.Get(id);
            if (clip == null) { WarnOnce(id); return; }
            bool flat = e == SfxEvent.UI || e == SfxEvent.Teleport || e == SfxEvent.Footstep || pos == Vector3.zero;
            var a = _pool[_next]; _next = (_next + 1) % _pool.Length;
            a.Stop();
            a.clip = clip;
            a.outputAudioMixerGroup = e == SfxEvent.UI ? _uiGroup : _sfxGroup;
            a.spatialBlend = flat ? 0f : 1f;
            a.spatialize = false; // plain 3D panning: the spatializer is reserved for the Condition S voice and the Ear Check pings
            a.pitch = e == SfxEvent.KeyPress ? Mathf.Pow(2f, (Mathf.Clamp(index, 0, 8) - 4) / 12f) : 1f;
            a.volume = sfxVolume * NormalizedVolume(clip, sfxTargetRmsDb);
            if (!flat) a.transform.position = pos; else a.transform.localPosition = Vector3.zero;
            if (Windows.TryGetValue(id, out var w))
            {
                float start = w.randomStart ? Random.Range(0f, Mathf.Max(0f, clip.length - w.seconds - 0.1f)) : 0f;
                a.time = start;
                a.Play();
                a.SetScheduledEndTime(AudioSettings.dspTime + w.seconds);
            }
            else a.Play();
            LastSource = a;
            Played.Add(id);
        }

        readonly Dictionary<AudioClip, float> _rmsDb = new Dictionary<AudioClip, float>();

        /// <summary>RMS of a clip in dBFS (cached). Used to scale every effect and ambience clip to a common level.</summary>
        public float ClipRmsDb(AudioClip clip)
        {
            if (clip == null) return -60f;
            if (_rmsDb.TryGetValue(clip, out float v)) return v;
            float db = -30f;
            try
            {
                var data = new float[clip.samples * clip.channels];
                if (clip.GetData(data, 0))
                {
                    double sum = 0; foreach (var x in data) sum += x * x;
                    db = 10f * Mathf.Log10((float)Mathf.Max(1e-12f, (float)(sum / Mathf.Max(1, data.Length))));
                }
            }
            catch { }
            _rmsDb[clip] = db;
            return db;
        }

        /// <summary>Volume that brings the clip to the target RMS, never above 1.</summary>
        public float NormalizedVolume(AudioClip clip, float targetDb)
        {
            return Mathf.Clamp01(Mathf.Pow(10f, (targetDb - ClipRmsDb(clip)) / 20f));
        }

        void WarnOnce(string id) { if (_warned.Add(id)) DS.Warn("SfxDirector: no clip for " + id); }

        // ------------------------------------------------------------------ ducking
        /// <summary>Called whenever a voice line starts: ambience goes down by 8 dB for the length of the line.</summary>
        public static void Duck(float seconds)
        {
            if (Instance != null) Instance.DuckFor(seconds);
        }

        public void DuckFor(float seconds)
        {
            _duckUntil = Mathf.Max(_duckUntil, Time.time + Mathf.Max(0.2f, seconds) + 0.2f);
            SetDucked(true);
        }

        void SetDucked(bool on) { IsDucked = on; }

        public float AmbienceDb { get; private set; }
        public float RoseBaseDb { get => roseBaseDb; set => roseBaseDb = value; }
        public float HalcyonBaseDb { get => halcyonBaseDb; set => halcyonBaseDb = value; }
        public float SfxDb { get; private set; }
        public float AmbienceBaseDb { get => ambienceBaseDb; set => ambienceBaseDb = value; }
        public float SfxBaseDb { get => sfxBaseDb; set => sfxBaseDb = value; }
        public float AmbienceDuckDb { get => ambienceDuckDb; set => ambienceDuckDb = value; }
        public float SfxDuckDb { get => sfxDuckDb; set => sfxDuckDb = value; }

        /// <summary>Moves the exposed Ambience and SFX volumes toward base or base + duck (attack 0.15 s, release 0.5 s).</summary>
        /// <summary>Audit / level tools: hold the mixer at the values they set instead of ducking.</summary>
        public bool ManualMixer { get; set; }

        void UpdateMixer()
        {
            if (ManualMixer) return;
            var mixer = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.mixer : null;
            if (mixer == null) return;
            float targetA = ambienceBaseDb + (IsDucked ? ambienceDuckDb : 0f);
            float targetS = sfxBaseDb + (IsDucked ? sfxDuckDb : 0f);
            float dt = Time.unscaledDeltaTime;
            float rateIn = Mathf.Abs(ambienceDuckDb) / Mathf.Max(0.02f, duckInSeconds), rateOut = Mathf.Abs(ambienceDuckDb) / Mathf.Max(0.02f, duckOutSeconds);
            AmbienceDb = Mathf.MoveTowards(AmbienceDb, targetA, (targetA < AmbienceDb ? rateIn : rateOut) * dt);
            float rateInS = Mathf.Abs(sfxDuckDb) / Mathf.Max(0.02f, duckInSeconds), rateOutS = Mathf.Abs(sfxDuckDb) / Mathf.Max(0.02f, duckOutSeconds);
            SfxDb = Mathf.MoveTowards(SfxDb, targetS, (targetS < SfxDb ? rateInS : rateOutS) * dt);
            mixer.SetFloat("VolAmbience", AmbienceDb);
            mixer.SetFloat("VolSFX", SfxDb);
            mixer.SetFloat("VolUI", SfxDb);
            mixer.SetFloat("VolRose", roseBaseDb + (SceneFlow.Section == DS.SceneSmokeRoom ? smokeRoseBoostDb : 0f));
            mixer.SetFloat("VolHalcyon", halcyonBaseDb);
        }

        /// <summary>Current group levels as read back from the mixer (what is really applied), for the audit.</summary>
        public string GroupLevelsDb()
        {
            var mixer = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.mixer : null;
            if (mixer == null) return "no mixer";
            string o = "";
            foreach (var n in new[] { "VolMaster", "VolRose", "VolHalcyon", "VolSFX", "VolAmbience", "VolUI" }) { mixer.GetFloat(n, out float v); o += n + "=" + v.ToString("F1") + " dB "; }
            return o;
        }

        void Update()
        {
            if (IsDucked && Time.time >= _duckUntil) SetDucked(false);
            UpdateMixer();
        }

        // ------------------------------------------------------------------ ambience
        
        void OnArrived(string scene)
        {
            ApplyAmbienceFor(scene.Contains(":") ? DS.SceneMedicalBay : scene);
        }

        public static string[] AmbienceFor(string sceneName)
        {
            switch (sceneName)
            {
                case DS.SceneMedicalBay: return new[] { "A001" };
                case DS.SceneSmokeRoom: return new[] { "A004", "A006" };
                case DS.SceneHallway: return new[] { "A001", "A005" };
                default: return new string[0];
            }
        }

        public void ApplyAmbienceFor(string sceneName)
        {
            var ids = AmbienceFor(sceneName);
            if (SameIds(ids, AmbienceIds)) return;
            AmbienceIds = ids;
            bool mono = sceneName == DS.SceneSmokeRoom;
            _fading.AddRange(_amb); _amb.Clear();
            foreach (var id in ids)
            {
                var clip = DeepSleepAssets.Get(id);
                if (clip == null) { WarnOnce(id); continue; }
                var go = new GameObject("Amb_" + id); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>();
                a.clip = mono ? AudioUtil.ToMono(clip) : clip; // smoke room ambience is identical in both ears
                a.loop = true; a.spatialBlend = 0f; a.spatialize = false; a.volume = 0f; a.outputAudioMixerGroup = _ambGroup;
                a.Play();
                _amb.Add(a);
                _ambTarget[a] = ambienceVolume * NormalizedVolume(a.clip, ambienceTargetRmsDb);
                Played.Add(id);
            }
            if (_fadeCo == null) _fadeCo = StartCoroutine(FadeLoop());
        }

        static bool SameIds(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        readonly List<AudioSource> _fading = new List<AudioSource>();
        readonly Dictionary<AudioSource, float> _ambTarget = new Dictionary<AudioSource, float>();
        public float AmbienceTargetVolume(AudioSource a) { return _ambTarget.TryGetValue(a, out float v) ? v : 0f; }
        Coroutine _fadeCo;

        System.Collections.IEnumerator FadeLoop()
        {
            while (true)
            {
                float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, ambienceFadeSeconds);
                bool busy = false;
                foreach (var a in _amb) { if (a == null) continue; float tv = AmbienceTargetVolume(a); if (a.volume < tv - 0.0001f) { a.volume = Mathf.MoveTowards(a.volume, tv, step * Mathf.Max(0.05f, tv)); busy = true; } }
                for (int i = _fading.Count - 1; i >= 0; i--)
                {
                    var a = _fading[i];
                    if (a == null) { _fading.RemoveAt(i); continue; }
                    a.volume = Mathf.MoveTowards(a.volume, 0f, step * 0.2f);
                    if (a.volume <= 0.0001f) { Destroy(a.gameObject); _fading.RemoveAt(i); } else busy = true;
                }
                if (!busy) break;
                yield return null;
            }
            _fadeCo = null;
        }
    }
}
