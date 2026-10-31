using System;
using System.Collections.Generic;
using UnityEngine;

namespace RescueSim.Audio
{
    /// <summary>
    /// Generates all sound effects in code so the project has no licensed or missing audio dependencies.
    /// Clips are cached after first use. Loops use whole numbers of cycles / crossfades so they do not click.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 24000;
        static readonly Dictionary<SpatialSound.Cue, AudioClip> Cache = new Dictionary<SpatialSound.Cue, AudioClip>();

        public static AudioClip Get(SpatialSound.Cue cue)
        {
            if (Cache.TryGetValue(cue, out var existing) && existing != null) return existing;
            var clip = Build(cue);
            Cache[cue] = clip;
            return clip;
        }

        static AudioClip Build(SpatialSound.Cue cue)
        {
            switch (cue)
            {
                case SpatialSound.Cue.Click: return Make("click", Click());
                case SpatialSound.Cue.Clunk: return Make("clunk", Clunk());
                case SpatialSound.Cue.Confirm: return Make("confirm", Tones(new[] { 660f, 880f }, 0.09f));
                case SpatialSound.Cue.Success: return Make("success", Tones(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.13f));
                case SpatialSound.Cue.Failure: return Make("failure", Sweep(330f, 150f, 0.7f));
                case SpatialSound.Cue.Warning: return Make("warning", Tones(new[] { 440f, 440f }, 0.18f, 0.08f));
                case SpatialSound.Cue.Alarm: return Make("alarm", Alarm(), loop: true);
                case SpatialSound.Cue.DistressWhistle: return Make("whistle", Whistle());
                case SpatialSound.Cue.Hiss: return Make("hiss", Hiss(), loop: true);
                case SpatialSound.Cue.Crackle: return Make("crackle", Crackle(), loop: true);
                case SpatialSound.Cue.ElectricBuzz: return Make("buzz", Buzz(), loop: true);
                case SpatialSound.Cue.AmbientHum: return Make("hum", Hum(), loop: true);
                case SpatialSound.Cue.Spray: return Make("spray", Spray(), loop: true);
                case SpatialSound.Cue.Cough: return Make("cough", Cough());
                case SpatialSound.Cue.Rain: return Make("rain", Rain(), loop: true);
                case SpatialSound.Cue.Rumble: return Make("rumble", Rumble(), loop: true);
                case SpatialSound.Cue.WaterLap: return Make("waterlap", WaterLap(), loop: true);
                case SpatialSound.Cue.Wind: return Make("wind", Wind(), loop: true);
                default: return Make("silence", new float[SampleRate / 10]);
            }
        }

        static AudioClip Make(string name, float[] data, bool loop = false)
        {
            // Soft-limit to avoid clipping.
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -0.95f, 0.95f);
            var clip = AudioClip.Create("proc_" + name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---- Building blocks -------------------------------------------------------------------

        static float[] Click()
        {
            var d = new float[(int)(SampleRate * 0.06f)];
            var rng = new System.Random(1);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 90f);
                float tone = Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.5f;
                float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.4f;
                d[i] = (tone + noise) * env * 0.8f;
            }
            return d;
        }

        static float[] Clunk()
        {
            var d = new float[(int)(SampleRate * 0.35f)];
            var rng = new System.Random(2);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 14f);
                float body = Mathf.Sin(2f * Mathf.PI * (95f - 40f * t) * t) * 0.9f;
                float knock = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 70f) * 0.5f;
                d[i] = (body + knock) * env;
            }
            return d;
        }

        static float[] Tones(float[] freqs, float noteLen, float gap = 0.015f)
        {
            int n = (int)(SampleRate * (noteLen + gap));
            var d = new float[n * freqs.Length];
            for (int k = 0; k < freqs.Length; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    if (t > noteLen) continue;
                    float attack = Mathf.Min(1f, t / 0.006f);
                    float env = attack * Mathf.Exp(-t * 7f);
                    float s = Mathf.Sin(2f * Mathf.PI * freqs[k] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * freqs[k] * t);
                    d[k * n + i] = s * env * 0.45f;
                }
            }
            return d;
        }

        static float[] Sweep(float f0, float f1, float len)
        {
            var d = new float[(int)(SampleRate * len)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * f / SampleRate;
                float env = Mathf.Min(1f, i / 200f) * (1f - t);
                d[i] = (Mathf.Sin(phase) + 0.4f * Mathf.Sign(Mathf.Sin(phase))) * env * 0.35f;
            }
            return d;
        }

        static float[] Alarm()
        {
            // Two alternating tones, 0.4 s each, 1.6 s total (loops seamlessly because each segment fades at edges).
            int seg = (int)(SampleRate * 0.4f);
            var d = new float[seg * 4];
            float[] freqs = { 880f, 660f, 880f, 660f };
            for (int k = 0; k < 4; k++)
            {
                float phase = 0f;
                for (int i = 0; i < seg; i++)
                {
                    phase += 2f * Mathf.PI * freqs[k] / SampleRate;
                    float edge = Mathf.Min(1f, Mathf.Min(i, seg - i) / 240f);
                    float s = Mathf.Sin(phase) + 0.35f * Mathf.Sin(2f * phase) + 0.15f * Mathf.Sin(3f * phase);
                    d[k * seg + i] = s * edge * 0.3f;
                }
            }
            return d;
        }

        static float[] Whistle()
        {
            int n = (int)(SampleRate * 1.1f);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                // Two rising chirps.
                float local = t < 0.5f ? t / 0.5f : t < 0.6f ? 0f : (t - 0.6f) / 0.5f;
                bool voiced = t < 0.5f || t > 0.6f;
                float f = 1300f + 600f * Mathf.Clamp01(local);
                phase += 2f * Mathf.PI * f / SampleRate;
                float vib = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 22f * t);
                float env = voiced ? Mathf.Min(1f, Mathf.Min(local * 30f, (1f - Mathf.Clamp01(local)) * 14f)) : 0f;
                d[i] = Mathf.Sin(phase) * env * vib * 0.35f;
            }
            return d;
        }

        static float[] Hiss()
        {
            int n = SampleRate * 2;
            var d = new float[n];
            var rng = new System.Random(3);
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                float hp = w - prev; // crude high-pass for a hiss character
                prev = w * 0.6f;
                d[i] = hp * 0.35f;
            }
            Crossfade(d, SampleRate / 10);
            return d;
        }

        static float[] Spray()
        {
            var d = Hiss();
            for (int i = 0; i < d.Length; i++) d[i] *= 1.25f;
            return d;
        }

        static float[] Crackle()
        {
            int n = SampleRate * 3;
            var d = new float[n];
            var rng = new System.Random(4);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.05f; // low rumble bed
                d[i] = lp * 0.5f;
            }
            for (int p = 0; p < 90; p++)
            {
                int start = rng.Next(0, n - 600);
                float amp = 0.3f + (float)rng.NextDouble() * 0.6f;
                int len = 80 + rng.Next(0, 400);
                for (int i = 0; i < len; i++)
                    d[start + i] += ((float)rng.NextDouble() * 2f - 1f) * amp * Mathf.Exp(-i / 60f);
            }
            Crossfade(d, SampleRate / 8);
            return d;
        }

        static float[] Buzz()
        {
            int n = SampleRate * 2; // 100 Hz fundamental => whole cycles
            var d = new float[n];
            var rng = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float s = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 100f * t)) * 0.25f + Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.2f;
                float gate = ((i / 300) % 7 == 0) ? 1.6f : 1f; // irregular arcing
                d[i] = (s + ((float)rng.NextDouble() * 2f - 1f) * 0.05f) * gate * 0.5f;
            }
            return d;
        }

        static float[] Hum()
        {
            int n = SampleRate * 4; // 55 Hz => whole cycles
            var d = new float[n];
            var rng = new System.Random(6);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.01f;
                d[i] = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.1f + lp * 0.6f;
            }
            return d;
        }

        static float[] Cough()
        {
            int n = (int)(SampleRate * 0.5f);
            var d = new float[n];
            var rng = new System.Random(7);
            for (int burst = 0; burst < 2; burst++)
            {
                int start = burst * (int)(SampleRate * 0.22f);
                for (int i = 0; i < SampleRate * 0.14f && start + i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-t * 28f) * Mathf.Min(1f, t / 0.004f);
                    float f = 170f - 60f * t * 7f;
                    d[start + i] = (Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f + ((float)rng.NextDouble() * 2f - 1f) * 0.5f) * env * 0.6f;
                }
            }
            return d;
        }

        static float[] Rain()
        {
            int n = SampleRate * 3;
            var d = new float[n];
            var rng = new System.Random(11);
            float prev = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                float hp = w - prev; prev = w * 0.85f;
                lp += (hp - lp) * 0.55f;
                float drop = rng.NextDouble() < 0.0025 ? (float)rng.NextDouble() * 0.5f : 0f;
                d[i] = (lp * 0.2f) + drop * (float)(rng.NextDouble() * 2 - 1);
            }
            Crossfade(d, SampleRate / 8);
            return d;
        }

        static float[] Rumble()
        {
            int n = SampleRate * 4;
            var d = new float[n];
            var rng = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float w = (float)rng.NextDouble() * 2f - 1f;
                lp += (w - lp) * 0.012f;
                float swell = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 0.5f * t); // 2 whole cycles in 4 s
                d[i] = (lp * 4.2f + 0.2f * Mathf.Sin(2f * Mathf.PI * 40f * t)) * swell * 0.45f;
            }
            return d;
        }

        static float[] WaterLap()
        {
            int n = SampleRate * 4;
            var d = new float[n];
            var rng = new System.Random(21);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float w = (float)rng.NextDouble() * 2f - 1f;
                lp += (w - lp) * 0.06f;
                lp2 += (lp - lp2) * 0.2f;
                float lfo = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.75f * t); // 3 whole cycles in 4 s
                d[i] = lp2 * 2.2f * lfo * 0.5f;
            }
            Crossfade(d, SampleRate / 8);
            return d;
        }

        static float[] Wind()
        {
            int n = SampleRate * 4;
            var d = new float[n];
            var rng = new System.Random(8);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float w = (float)rng.NextDouble() * 2f - 1f;
                lp += (w - lp) * 0.03f;
                float gust = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.25f * t); // 1 whole cycle
                d[i] = lp * 2.4f * (0.35f + 0.65f * gust) * 0.4f;
            }
            Crossfade(d, SampleRate / 8);
            return d;
        }

        static void Crossfade(float[] d, int len)
        {
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                int j = d.Length - len + i;
                d[j] = d[j] * (1f - t) + d[i] * t;
            }
            // Trim the head so the looped tail+head join is continuous.
        }
    }
}
