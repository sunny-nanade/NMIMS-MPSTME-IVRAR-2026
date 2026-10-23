using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Small audio helpers shared by the directors.</summary>
    public static class AudioUtil
    {
        static readonly Dictionary<AudioClip, float> LeadCache = new Dictionary<AudioClip, float>();

        /// <summary>Seconds of near-silence (below about -40 dBFS) at the start of a clip, minus a 20 ms pad. Voice files carry about 0.2 s of lead-in; skipping it makes the line audible at once.</summary>
        public static float LeadSilence(AudioClip clip, float thresholdLinear = 0.01f)
        {
            if (clip == null) return 0f;
            if (LeadCache.TryGetValue(clip, out float v)) return v;
            float lead = 0f;
            try
            {
                int ch = Mathf.Max(1, clip.channels);
                int n = Mathf.Min(clip.samples, clip.frequency); // look at the first second only
                var data = new float[n * ch];
                if (clip.GetData(data, 0))
                {
                    int first = -1;
                    for (int i = 0; i < n && first < 0; i++) for (int c = 0; c < ch; c++) if (Mathf.Abs(data[i * ch + c]) > thresholdLinear) { first = i; break; }
                    if (first > 0) lead = Mathf.Max(0f, (float)first / clip.frequency - 0.02f);
                }
            }
            catch (System.Exception) { lead = 0f; }
            LeadCache[clip] = lead;
            return lead;
        }

        /// <summary>Starts a clip on a source, skipping its leading silence. Returns the seconds that were skipped.</summary>
        public static float PlaySkippingLead(AudioSource src, AudioClip clip, bool skip = true)
        {
            src.clip = clip;
            float lead = skip ? LeadSilence(clip) : 0f;
            src.timeSamples = Mathf.Clamp(Mathf.RoundToInt(lead * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
            src.Play();
            return lead;
        }

        static readonly Dictionary<AudioClip, AudioClip> MonoCache = new Dictionary<AudioClip, AudioClip>();

        /// <summary>Mono down-mix of a clip (cached). Returns the clip itself when it is already mono or cannot be read.</summary>
        public static AudioClip ToMono(AudioClip src)
        {
            if (src == null || src.channels <= 1) return src;
            if (MonoCache.TryGetValue(src, out var c) && c != null) return c;
            AudioClip result = src;
            try
            {
                var data = new float[src.samples * src.channels];
                if (src.GetData(data, 0))
                {
                    var mono = new float[src.samples];
                    for (int i = 0; i < src.samples; i++)
                    {
                        float s = 0f; for (int ch = 0; ch < src.channels; ch++) s += data[i * src.channels + ch];
                        mono[i] = s / src.channels;
                    }
                    result = AudioClip.Create(src.name + "_mono", src.samples, 1, src.frequency, false);
                    result.SetData(mono, 0);
                }
                else DS.Warn($"Could not read {src.name} for the mono down-mix; using it as imported");
            }
            catch (System.Exception e) { DS.Warn($"Mono down-mix of {src.name} failed: {e.Message}"); }
            MonoCache[src] = result;
            return result;
        }
    }
}
