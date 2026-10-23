using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Loudness staging for the voice files. The files are not edited. Every R and H line is brought to the same peak level at the source
    /// (an attenuation only, so AudioSource.volume never needs to exceed 1) and the mixer groups then carry the whole voice bus up to the target level.
    /// ReferencePeakDb is the quietest voice file (R205, measured with DeepSleep > Run Check > Polish: voice levels).
    /// </summary>
    public static class VoiceLevels
    {
        public const float ReferencePeakDb = -8.2f;

        static readonly Dictionary<AudioClip, float> PeakCache = new Dictionary<AudioClip, float>();

        /// <summary>Highest absolute sample of a clip in dBFS (cached).</summary>
        public static float PeakDb(AudioClip clip)
        {
            if (clip == null) return ReferencePeakDb;
            if (PeakCache.TryGetValue(clip, out float v)) return v;
            float db = ReferencePeakDb;
            try
            {
                var data = new float[clip.samples * clip.channels];
                if (clip.GetData(data, 0))
                {
                    float p = 0f; for (int i = 0; i < data.Length; i++) { float a = data[i] < 0 ? -data[i] : data[i]; if (a > p) p = a; }
                    db = 20f * Mathf.Log10(Mathf.Max(p, 1e-6f));
                }
            }
            catch (System.Exception) { }
            PeakCache[clip] = db;
            return db;
        }

        /// <summary>Attenuation in dB (never positive) that brings this clip's peak down to ReferencePeakDb.</summary>
        public static float TrimDb(AudioClip clip) { return Mathf.Min(0f, ReferencePeakDb - PeakDb(clip)); }

        public static float TrimLinear(AudioClip clip) { return Mathf.Pow(10f, TrimDb(clip) / 20f); }
    }
}
