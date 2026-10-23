using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Safety limiter on the final mix. Sits on the AudioListener object (OnAudioFilterRead runs after the mixer), so the voice groups can be
    /// mixed hot without ever clipping: peaks are held at the ceiling (-3 dBFS by default), gain recovers over about 150 ms.
    /// Bootstrap adds one MasterLimiterHost to the persistent systems; it puts a MasterLimiter on whichever AudioListener is live.
    /// </summary>
    public class MasterLimiter : MonoBehaviour
    {
        [SerializeField] float ceilingDb = -3f;
        [SerializeField] float releaseSeconds = 0.15f;

        public static float LastGainReductionDb { get; private set; }
        /// <summary>Samples above the ceiling that were reduced since the last Reset (test hook).</summary>
        public static long LimitedBlocks;

        float _gain = 1f, _ceiling = 0.7079f, _releaseCoef = 0.9998f;

        public float CeilingDb { get => ceilingDb; set { ceilingDb = value; Recalc(); } }

        void OnEnable() { Recalc(); }

        void Recalc()
        {
            _ceiling = Mathf.Pow(10f, ceilingDb / 20f);
            float sr = Mathf.Max(8000, AudioSettings.outputSampleRate);
            _releaseCoef = Mathf.Exp(-1f / (sr * Mathf.Max(0.01f, releaseSeconds)));
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels < 1) return;
            float g = _gain, c = _ceiling, rc = _releaseCoef; bool limited = false;
            for (int i = 0; i < data.Length; i += channels)
            {
                float p = 0f;
                for (int ch = 0; ch < channels; ch++) { float a = data[i + ch]; if (a < 0f) a = -a; if (a > p) p = a; }
                float need = p * g > c ? c / p : 1f;
                if (need < g) { g = need; limited = true; }
                else g = 1f - (1f - g) * rc; // recover toward unity
                for (int ch = 0; ch < channels; ch++) data[i + ch] *= g;
            }
            _gain = g;
            if (limited) LimitedBlocks++;
            LastGainReductionDb = g < 0.999f ? 20f * Mathf.Log10(g) : 0f;
        }

        /// <summary>Makes sure the given listener object carries exactly one limiter (always the first audio filter on it).</summary>
        public static MasterLimiter EnsureOn(GameObject listenerObject)
        {
            var l = listenerObject.GetComponent<MasterLimiter>();
            if (l == null) l = listenerObject.AddComponent<MasterLimiter>();
            return l;
        }
    }

    /// <summary>Keeps a MasterLimiter on the live AudioListener across scene loads.</summary>
    public class MasterLimiterHost : MonoBehaviour
    {
        float _next;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var l = FindAnyObjectByType<AudioListener>();
            if (l != null && l.GetComponent<MasterLimiter>() == null) MasterLimiter.EnsureOn(l.gameObject);
        }
    }
}
