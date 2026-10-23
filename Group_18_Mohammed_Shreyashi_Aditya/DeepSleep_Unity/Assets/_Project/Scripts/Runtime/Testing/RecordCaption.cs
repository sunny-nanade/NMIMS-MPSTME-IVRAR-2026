#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Corner caption for the recorded final run only: current stage and each voice line (speaker and ID). Also meters the mix that reaches the AudioListener.</summary>
    public class RecordCaption : MonoBehaviour
    {
        class Line { public string id, text; public float until, maxUntil; public bool stopped; }
        readonly List<Line> _lines = new List<Line>();
        GUIStyle _box, _head;
        AudioLevelMeter _meter;
        float _nextFind;

        void OnEnable() { FlowTimeline.VoiceStarted += OnVoice; FlowTimeline.VoiceStopped += OnStopped; }
        void OnDisable() { FlowTimeline.VoiceStarted -= OnVoice; FlowTimeline.VoiceStopped -= OnStopped; }

        void OnVoice(string id, float length)
        {
            string who = id[0] == 'R' ? "Rose" : "HALCYON";
            // a caption lives exactly as long as its source plays (plus a short linger); the length-based timer is only a failsafe
            _lines.Add(new Line { id = id, text = $"{who}  {id}", until = float.MaxValue, maxUntil = Time.unscaledTime + length + 2f });
        }

        void OnStopped(string id)
        {
            foreach (var l in _lines) if (l.id == id && !l.stopped) { l.stopped = true; l.until = Time.unscaledTime + 0.3f; }
        }

        void Update()
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            var l = FindAnyObjectByType<AudioListener>();
            if (l != null && (_meter == null || _meter.gameObject != l.gameObject)) _meter = AudioLevelMeter.EnsureOn(l.gameObject);
        }

        void OnGUI()
        {
            if (_box == null)
            {
                int fs = Mathf.Max(14, Screen.height / 38);
                _box = new GUIStyle(GUI.skin.box) { fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white }, padding = new RectOffset(12, 12, 4, 4) };
                _head = new GUIStyle(_box) { fontStyle = FontStyle.Bold };
            }
            var sm = SessionManager.Instance;
            string stage = sm != null ? StageLabel(sm.Current) : "";
            float h = Screen.height / 20f, w = Screen.width * 0.26f, x = 16f, y = 16f;
            GUI.Box(new Rect(x, y, w, h), "Stage: " + stage, _head);
            y += h + 4f;
            _lines.RemoveAll(l => l.until < Time.unscaledTime || l.maxUntil < Time.unscaledTime);
            foreach (var l in _lines)
            {
                GUI.Box(new Rect(x, y, w, h), l.text, _box);
                y += h + 2f;
            }
        }

        static string StageLabel(Stage s)
        {
            switch (s)
            {
                case Stage.KeypadSolved: return "Thermal Keypad solved / The Trail";
                case Stage.TrailFollowed: return "Trail followed";
                case Stage.CoolantRouted: return "Coolant Reroute";
                case Stage.Questionnaire: return "Debrief";
                default: return s.ToString();
            }
        }
    }

    /// <summary>Meters the final mix on the AudioListener object (mean and peak level), for the "recording is not silent" check.</summary>
    public class AudioLevelMeter : MonoBehaviour
    {
        public static double SumSquares; public static long Samples; public static float Peak; public static long LoudBlocks, Blocks, ClippedBlocks; public static float FirstClipTime = -1f;
        /// <summary>The meter goes after the limiter on the listener object, so it measures what is actually heard.</summary>
        public static AudioLevelMeter EnsureOn(GameObject listenerObject)
        {
            MasterLimiter.EnsureOn(listenerObject);
            var m = listenerObject.GetComponent<AudioLevelMeter>();
            return m != null ? m : listenerObject.AddComponent<AudioLevelMeter>();
        }

        // measurement window: whole-window and active (block RMS above -50 dBFS) mean square, and the peak
        static bool _win; static double _winSum, _winActSum; static long _winN, _winActN; static float _winPeak;
        public static void WinBegin() { _winSum = 0; _winN = 0; _winActSum = 0; _winActN = 0; _winPeak = 0; _win = true; }
        /// <summary>Returns (RMS dBFS over the window, RMS dBFS over active blocks, peak dBFS).</summary>
        public static (double rmsDb, double activeRmsDb, double peakDb) WinEnd()
        {
            _win = false;
            double r = _winN > 0 ? System.Math.Sqrt(_winSum / _winN) : 0, a = _winActN > 0 ? System.Math.Sqrt(_winActSum / _winActN) : 0;
            return (20 * System.Math.Log10(System.Math.Max(r, 1e-9)), 20 * System.Math.Log10(System.Math.Max(a, 1e-9)), 20 * System.Math.Log10(System.Math.Max(_winPeak, 1e-9)));
        }

        public static void Reset() { SumSquares = 0; Samples = 0; Peak = 0; LoudBlocks = 0; Blocks = 0; ClippedBlocks = 0; FirstClipTime = -1f; }

        static float _t; void Update() { _t = Time.time; }

        void OnAudioFilterRead(float[] data, int channels)
        {
            double ss = 0; float pk = 0;
            for (int i = 0; i < data.Length; i++) { float v = data[i]; ss += v * v; float a = v < 0 ? -v : v; if (a > pk) pk = a; }
            SumSquares += ss; Samples += data.Length; if (pk > Peak) Peak = pk;
            if (_win)
            {
                _winSum += ss; _winN += data.Length; if (pk > _winPeak) _winPeak = pk;
                if (data.Length > 0 && ss / data.Length > 1e-5) { _winActSum += ss; _winActN += data.Length; } // active: above -50 dBFS RMS
            }
            Blocks++; if (pk > 1f) { ClippedBlocks++; if (FirstClipTime < 0f) FirstClipTime = _t; } if (data.Length > 0 && ss / data.Length > 1e-8) LoudBlocks++; // above about -80 dBFS rms
        }
    }
}
#endif
