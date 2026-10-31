using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace CSER
{
    public enum AlertState { Normal, High, Critical, Clear }

    /// Central alert state for a level: NORMAL -> HIGH (orange pulse + beep) -> CRITICAL (red pulse + alarm) ->
    /// CLEAR (lights fade back, success cue, "proceed" unlocked). Driven by task / timer events from GameManager and
    /// scene-scoped, so a level reload resets it. Lights follow via LightAlertController; any Light named or tagged
    /// "AlertLight" gets one automatically when none is configured. Wire per-level behaviour with the UnityEvents.
    public class AlertStateManager : MonoBehaviour
    {
        public static AlertStateManager I { get; private set; }

        public AlertState State { get; private set; } = AlertState.Normal;

        [Header("Timer thresholds (fraction of the chapter time used)")]
        [Range(0f, 1f)] public float highAt = 0.6f;
        [Range(0f, 1f)] public float criticalAt = 0.85f;
        [Header("Audio")]
        [Range(0f, 1f)] public float masterVolume = 1f;
        public float highVolume = 0.25f;
        public float criticalVolume = 0.5f;
        [Header("Events (wire per level)")]
        public UnityEvent onNormal, onHigh, onCritical, onAllClear;

        const string FlashPref = "cser.reduceFlash";
        static readonly List<LightAlertController> controllers = new List<LightAlertController>();

        /// Accessibility: pulsing lights become steady colours.
        public bool ReduceFlashing
        {
            get => PlayerPrefs.GetInt(FlashPref, 0) == 1;
            set => PlayerPrefs.SetInt(FlashPref, value ? 1 : 0);
        }

        AudioSource beepSrc, alarmSrc, cueSrc;
        AudioClip successClip;
        float evalTimer;
        bool cleared;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += (_, __) => Ensure();
            Ensure();
        }

        static void Ensure()
        {
            if (I || !FindAnyObjectByType<GameManager>()) return;
            new GameObject("AlertStateManager").AddComponent<AlertStateManager>();
        }

        public static void Register(LightAlertController c)
        {
            if (!controllers.Contains(c)) controllers.Add(c);
        }

        public static void Unregister(LightAlertController c) => controllers.Remove(c);

        void Awake()
        {
            I = this;
            beepSrc = MakeSource(MakeBeep(), 0f);
            alarmSrc = MakeSource(MakeAlarm(), 0f);
            cueSrc = gameObject.AddComponent<AudioSource>();
            cueSrc.playOnAwake = false;
            cueSrc.spatialBlend = 0f;
            successClip = MakeSuccess();
        }

        void Start()
        {
            // lights the level author didn't configure: pick up everything called / tagged "AlertLight"
            if (controllers.Count == 0) AutoFindLights();
            var gm = GameManager.I;
            if (!gm) return;
            gm.StateChanged += OnGameState;
            gm.TaskCompleted += OnTask;
            Evaluate();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            var gm = GameManager.I;
            if (!gm) return;
            gm.StateChanged -= OnGameState;
            gm.TaskCompleted -= OnTask;
        }

        void AutoFindLights()
        {
            var found = new HashSet<Light>();
            try
            {
                foreach (var go in GameObject.FindGameObjectsWithTag("AlertLight"))
                    if (go.TryGetComponent<Light>(out var l)) found.Add(l);
            }
            catch (UnityException) { /* tag not defined in this project: fall back to names */ }
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l.name.Contains("AlertLight")) found.Add(l);
            foreach (var l in found)
                if (!l.GetComponent<LightAlertController>()) l.gameObject.AddComponent<LightAlertController>();
        }

        void Update()
        {
            // timer chapters escalate as the clock runs down
            evalTimer += Time.deltaTime;
            if (evalTimer > 0.5f)
            {
                evalTimer = 0;
                Evaluate();
            }
            float dt = Time.deltaTime * 2f;   // ~0.5 s crossfade
            Fade(beepSrc, State == AlertState.High ? highVolume : 0f, dt);
            Fade(alarmSrc, State == AlertState.Critical ? criticalVolume : 0f, dt);
        }

        void Fade(AudioSource s, float target, float step)
        {
            s.volume = Mathf.MoveTowards(s.volume, target * masterVolume, step);
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && s.isPlaying) s.Stop();
        }

        void OnGameState(GameState s) => Evaluate();
        void OnTask(TaskId t) => Evaluate();

        /// Derives the alert level from the game: timer fraction, an unresolved incident, win / loss.
        void Evaluate()
        {
            var gm = GameManager.I;
            if (!gm) return;
            AlertState s = AlertState.Normal;
            switch (gm.State)
            {
                case GameState.Won: s = AlertState.Clear; break;
                case GameState.Lost: s = AlertState.Critical; break;
                case GameState.Playing:
                    if (gm.HasTimer)
                    {
                        float f = gm.Exfil01;
                        if (f >= criticalAt) s = AlertState.Critical;
                        else if (f >= highAt) s = AlertState.High;
                    }
                    // incident declared but not yet contained (chapter 1 infected laptop)
                    if (s == AlertState.Normal && gm.IsDone(TaskId.SOC_Escalate) && !gm.IsDone(TaskId.SOC_Malware)) s = AlertState.High;
                    break;
            }
            SetState(s);
        }

        /// Manual control (UnityEvents, other scripts). The next Evaluate may override it.
        public void SetState(AlertState s)
        {
            if (s == State) return;
            State = s;
            switch (s)
            {
                case AlertState.Normal: onNormal?.Invoke(); break;
                case AlertState.High: onHigh?.Invoke(); break;
                case AlertState.Critical: onCritical?.Invoke(); break;
                case AlertState.Clear:
                    if (!cleared)
                    {
                        cleared = true;
                        cueSrc.PlayOneShot(successClip, 0.7f * masterVolume);
                        onAllClear?.Invoke();
                    }
                    break;
            }
        }

        public void SetNormal() => SetState(AlertState.Normal);
        public void SetHigh() => SetState(AlertState.High);
        public void SetCritical() => SetState(AlertState.Critical);

        // ---------------------------------------------------------------- procedural audio (no assets needed)

        AudioSource MakeSource(AudioClip clip, float volume)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // level-wide alarm: same loudness wherever the player stands
            s.volume = volume;
            return s;
        }

        /// Medium warning: two soft beeps per second.
        static AudioClip MakeBeep()
        {
            const int rate = 22050;
            int n = rate;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float tt = t % 0.5f;
                float env = tt < 0.14f ? Mathf.Clamp01(tt * 150) * Mathf.Clamp01((0.14f - tt) * 150) : 0f;
                d[i] = Mathf.Sin(2 * Mathf.PI * 880 * t) * 0.5f * env;
            }
            var c = AudioClip.Create("AlertBeep", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// Urgent two-tone wail, seamless loop.
        static AudioClip MakeAlarm()
        {
            const int rate = 22050;
            int n = rate;
            var d = new float[n];
            float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float f = 700 + 350 * Mathf.Sin(2 * Mathf.PI * t);   // 1 s sweep, loops cleanly
                ph += 2 * Mathf.PI * f / rate;
                d[i] = (Mathf.Sin(ph) + 0.3f * Mathf.Sin(2 * ph)) * 0.45f;
            }
            var c = AudioClip.Create("AlertAlarm", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// Rising "all clear" arpeggio.
        static AudioClip MakeSuccess()
        {
            const int rate = 22050;
            int n = (int)(rate * 1.1f);
            var d = new float[n];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                int idx = Mathf.Min(3, (int)(t / 0.14f));
                float lt = t - idx * 0.14f;
                float env = Mathf.Exp(-lt * 4f) * Mathf.Clamp01(lt * 300);
                d[i] = Mathf.Sin(2 * Mathf.PI * notes[idx] * t) * 0.4f * env;
            }
            var c = AudioClip.Create("AlertClear", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
