#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Editor-only recorder for the final flow check. Writes one CSV row per event:
    /// real_s, game_s, scene, stage, event, id, group, detail. Events: scene, stage, voice_request, audio_start, audio_stop, state (1 Hz), log.
    /// At Time.timeScale above 1 the voice and SFX sources get pitch = timeScale so that clip lengths match game time (test-only; 1x runs are untouched).
    /// </summary>
    public class FlowTimeline : MonoBehaviour
    {
        public static FlowTimeline Instance { get; private set; }
        /// <summary>A voice line (R or H) started playing: id and clip length in game seconds.</summary>
        public static event System.Action<string, float> VoiceStarted;
        /// <summary>A voice line (R or H) stopped playing (finished, cut, or its source was destroyed).</summary>
        public static event System.Action<string> VoiceStopped;
        public int Errors, Warnings;
        public readonly List<string> ErrorTexts = new List<string>();
        public readonly List<string> WarningTexts = new List<string>();

        class Track { public string id; public float origPitch; public float lastTime; public string group; public float startReal; public bool duckLogged; public float len; }
        readonly Dictionary<AudioSource, Track> _tracks = new Dictionary<AudioSource, Track>();
        readonly StringBuilder _sb = new StringBuilder();
        float _t0, _nextState, _nextScan;
        bool _subscribed;
        string _path;
        Stage _stage;

        public static FlowTimeline Begin(string path)
        {
            if (Instance != null) Instance.End();
            var go = new GameObject("FlowTimeline");
            DontDestroyOnLoad(go);
            var f = go.AddComponent<FlowTimeline>();
            f.Open(path);
            return f;
        }

        void Open(string path)
        {
            Instance = this; _path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            _sb.Clear(); _sb.AppendLine("real_s,game_s,scene,stage,event,id,group,detail");
            _t0 = Time.realtimeSinceStartup; Errors = Warnings = 0; ErrorTexts.Clear(); WarningTexts.Clear();
            VoiceBus.Requested += OnRequest;
            SfxEvents.Raised += OnSfx;
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _subscribed = true;
            Add("start", "", "", "timeScale=" + Time.timeScale.ToString(CultureInfo.InvariantCulture));
        }

        public void End()
        {
            if (_subscribed)
            {
                VoiceBus.Requested -= OnRequest;
                SfxEvents.Raised -= OnSfx;
                Application.logMessageReceived -= OnLog;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                if (SessionManager.Instance != null) SessionManager.Instance.StageChanged -= OnStage;
                _subscribed = false;
            }
            foreach (var kv in new List<KeyValuePair<AudioSource, Track>>(_tracks)) CloseTrack(kv.Key, kv.Value, "end");
            Add("end", "", "", "");
            if (_path != null) File.WriteAllText(_path, _sb.ToString());
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        public void Add(string ev, string id, string group, string detail)
        {
            string scene = string.IsNullOrEmpty(SceneFlow.Section) ? SceneManager.GetActiveScene().name : SceneFlow.Section; // one scene (SCN_Main): the section is what the old scene names meant
            _sb.Append(F(Time.realtimeSinceStartup - _t0)).Append(',').Append(F(Time.time)).Append(',').Append(scene).Append(',').Append(_stage).Append(',')
               .Append(ev).Append(',').Append(id).Append(',').Append(group).Append(',').Append('"').Append(detail.Replace("\"", "'").Replace("\n", " ")).Append('"').AppendLine();
        }

        void OnSfx(SfxEvent e, Vector3 pos, int idx) { Add("sfx_event", e.ToString(), "", "idx=" + idx); }
        readonly HashSet<AudioSource> _oneShot = new HashSet<AudioSource>();

        void OnRequest(string id) { Add("voice_request", id, "", ""); }

        void OnStage(Stage from, Stage to) { _stage = to; Add("stage", to.ToString(), "", "from " + from); }

        void OnSceneLoaded(Scene s, LoadSceneMode m) { Add("scene_loaded", s.name, "", m.ToString()); }
        void OnSceneUnloaded(Scene s) { Add("scene_unloaded", s.name, "", ""); }

        void OnLog(string msg, string stack, LogType t)
        {
            if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { Errors++; if (ErrorTexts.Count < 30) ErrorTexts.Add(msg.Split('\n')[0]); Add("log_error", "", "", msg.Split('\n')[0]); }
            else if (t == LogType.Warning && !msg.StartsWith(DS.Prefix) && !stack.Contains("Assets/_Project"))
            {
                // package / editor warning, not from our code: listed in the timeline but not counted
                Add("log_package_warning", "", "", msg.Split((char)10)[0]);
            }
            else if (t == LogType.Warning) { Warnings++; string w = msg.Split('\n')[0]; if (WarningTexts.Count < 30 && !WarningTexts.Contains(w)) WarningTexts.Add(w); Add("log_warning", "", "", w); }
        }

        static bool IsId(string n)
        {
            if (n == null || n.Length != 4) return false;
            char c = n[0];
            if (c != 'R' && c != 'H' && c != 'S' && c != 'A') return false;
            return char.IsDigit(n[1]) && char.IsDigit(n[2]) && char.IsDigit(n[3]);
        }

        void Update()
        {
            if (!_subscribed) return;
            var sm = SessionManager.Instance;
            if (sm != null && sm.StageChangedSubscribers >= 0 && !_hooked) { sm.StageChanged += OnStage; _hooked = true; _stage = sm.Current; }
            ScanAudio();
            if (Time.realtimeSinceStartup >= _nextState) { _nextState = Time.realtimeSinceStartup + 1f; State(); }
        }
        bool _hooked;

        void ScanAudio()
        {
            // new / restarted sources
            var all = FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
            float ts = Time.timeScale;
            // effective game-time ratio of this frame (the delta time cap keeps it below the nominal time scale)
            float ratio = ts > 1.01f && Time.unscaledDeltaTime > 0f ? Time.deltaTime / Time.unscaledDeltaTime : 1f;
            foreach (var s in all)
            {
                if (s == null) continue;
                if (s.isPlaying && s.clip == null)
                {
                    if (_oneShot.Add(s)) Add("oneshot", "", s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "none", "src=" + s.name + ";blend=" + s.spatialBlend.ToString("0.0", CultureInfo.InvariantCulture) + ";spatialize=" + s.spatialize);
                }
                else _oneShot.Remove(s);
                bool playing = s.isPlaying && s.clip != null && IsId(s.clip.name);
                if (_tracks.TryGetValue(s, out var tr))
                {
                    if (!playing || s.clip.name != tr.id || s.time + 0.001f < tr.lastTime - 0.05f)
                    {
                        CloseTrack(s, tr, "stop");
                        if (playing) OpenTrack(s, ratio);
                    }
                    else
                    {
                        tr.lastTime = s.time;
                        if (!tr.duckLogged && (tr.id[0] == 'R' || tr.id[0] == 'H') && tr.len > 1.5f && Time.realtimeSinceStartup - tr.startReal > 0.6f * (ts > 1.01f ? 1f / Mathf.Max(1f, ratio) : 1f))
                        {
                            tr.duckLogged = true;
                            var mx = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.mixer : null;
                            string dd = "";
                            if (mx != null) foreach (var pn in new[] { "VolSFX", "VolAmbience" }) if (mx.GetFloat(pn, out float vv)) dd += pn + "=" + vv.ToString("0.0", CultureInfo.InvariantCulture) + ";";
                            Add("duck_check", tr.id, tr.group, dd);
                        }
                        if (ts > 1.01f && (tr.id[0] == 'R' || tr.id[0] == 'H')) s.pitch = tr.origPitch * ratio;
                    }
                }
                else if (playing) OpenTrack(s, ratio);
            }
            // sources that were destroyed
            var gone = new List<AudioSource>();
            foreach (var kv in _tracks) if (kv.Key == null) gone.Add(kv.Key);
            foreach (var g in gone) { var gt = _tracks[g]; Add("audio_stop", gt.id, gt.group, "source destroyed"); _tracks.Remove(g); if (gt.id[0] == 'R' || gt.id[0] == 'H') VoiceStopped?.Invoke(gt.id); }
        }

        void OpenTrack(AudioSource s, float ratio)
        {
            var tr = new Track { id = s.clip.name, origPitch = s.pitch, lastTime = s.time, startReal = Time.realtimeSinceStartup, len = s.clip.length, group = s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "none" };
            _tracks[s] = tr;
            if (Time.timeScale > 1.01f && (tr.id[0] == 'R' || tr.id[0] == 'H')) s.pitch = tr.origPitch * ratio;
            string det = $"src={s.name};blend={s.spatialBlend:0.0};spatialize={s.spatialize};vol={s.volume:0.00};len={s.clip.length:0.00}";
            var mix = DeepSleepAssets.Instance != null ? DeepSleepAssets.Instance.mixer : null;
            if (mix != null)
            {
                foreach (var p in new[] { "VolRose", "VolHalcyon", "VolSFX", "VolAmbience" })
                    if (mix.GetFloat(p, out float v)) det += $";{p}={v:0.0}";
            }
            Add("audio_start", tr.id, tr.group, det);
            if (tr.id[0] == 'R' || tr.id[0] == 'H') VoiceStarted?.Invoke(tr.id, s.clip.length);
        }

        void CloseTrack(AudioSource s, Track tr, string why)
        {
            if (s != null && Time.timeScale > 1.01f && (tr.id[0] == 'R' || tr.id[0] == 'H')) s.pitch = tr.origPitch;
            Add("audio_stop", tr.id, tr.group, why + ";at=" + F(tr.lastTime));
            _tracks.Remove(s);
            if (tr.id[0] == 'R' || tr.id[0] == 'H') VoiceStopped?.Invoke(tr.id);
        }

        void State()
        {
            var rig = PlayerRig.Instance;
            string rigS = "none";
            if (rig != null && rig.Head != null)
            {
                float h = rig.CameraHeight;
                bool floor = Physics.Raycast(rig.transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 1.5f, ~0, QueryTriggerInteraction.Ignore);
                rigS = $"head_h={h:0.000};root_y={rig.transform.position.y:0.000};floor={(floor ? hit.distance - 0.5f : -1f):0.000}";
            }
            var ox = OxygenSystem.Instance;
            string oxS = ox != null ? $"oxy_running={ox.Running};oxy_paused={ox.Paused};oxy={ox.Remaining:0.0}" : "oxy=none";
            int ps = 0; foreach (var p in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude)) if (p.isPlaying) ps++;
            string fog = $"fog={RenderSettings.fog};dens={RenderSettings.fogDensity:0.000};particles={ps}";
            float ts = Time.timeScale;
            Add("state", "", "", $"{rigS};{oxS};{fog};ts={ts:0.0};listenerPause={AudioListener.pause}");
        }
    }
}
#endif
