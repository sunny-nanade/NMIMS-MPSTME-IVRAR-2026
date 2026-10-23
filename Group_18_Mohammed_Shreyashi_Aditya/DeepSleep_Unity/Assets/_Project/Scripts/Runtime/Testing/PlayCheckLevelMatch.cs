#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        /// <summary>
        /// Level-match tool. Plays Rose's beacon call (R201) from an anchor 5 m away at six azimuths in both conditions and measures the RMS of the
        /// AudioListener output. The stereo gain trim that makes M as loud as S is 20 log10(RMS_S / RMS_M). Result: Analysis/data/level_match.json
        /// and Docs/Reports/level_match.md; apply with the menu DeepSleep > Apply Level Match.
        /// </summary>
        IEnumerator LevelMatch()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && NarrativeVoiceDirector.Instance != null, 10f);
            var rig = PlayerRig.Instance;
            string spatializer = AudioSettings.GetSpatializerPluginName();
            Note("Spatializer plugin in use: " + (string.IsNullOrEmpty(spatializer) ? "(none)" : spatializer));
            Check(spatializer == "Meta XR Audio", "The Meta XR Audio spatializer is the selected plugin");
            // let the arrival lines finish so only the test signal is in the mix, and silence the ambience
            yield return sp.Until(() => !NarrativeVoiceDirector.Instance.IsSpeaking && NarrativeVoiceDirector.Instance.QueueLength == 0 && !HalcyonDirector.Instance.IsSpeaking, 40f);
            foreach (var a in SfxDirector.Instance.AmbienceSources) if (a != null) a.volume = 0f;
            SfxDirector.Instance.enabled = false;
            NarrativeVoiceDirector.Instance.enabled = false; HalcyonDirector.Instance.enabled = false;
            yield return sp.Wait(1f);

            // the safety limiter is bypassed: the trim must come from the raw difference between the conditions, not from what the limiter does to the louder one
            var limiter = FindAnyObjectByType<AudioListener>().GetComponent<MasterLimiter>();
            if (limiter != null) limiter.enabled = false;
            var go = new GameObject("LevelMatch");
            var acs = go.AddComponent<AudioConditionSwitch>();
            acs.IgnoreTrim = true; // measure the raw difference between the conditions
            var anchor = new GameObject("LevelMatchAnchor").transform;
            acs.Bind(anchor);
            float[] distances = { 1f, 3f, 6f, 10f, 15f, 20f };
            float[] azimuths = { 0f, 90f, 180f, -90f };
            var rmsS = new double[distances.Length]; var rmsM = new double[distances.Length];
            var buf0 = new float[1024]; var buf1 = new float[1024];
            double silence = 0; int silenceN = 0;
            for (int i = 0; i < 20; i++) { AudioListener.GetOutputData(buf0, 0); foreach (var v in buf0) { silence += v * v; silenceN++; } yield return null; }
            double floorRms = System.Math.Sqrt(silence / System.Math.Max(1, silenceN));
            Note($"Output noise floor RMS {floorRms:E2}");

            foreach (var cond in new[] { GuidanceCondition.Spatial, GuidanceCondition.Stereo })
            {
                acs.Apply(cond);
                for (int di = 0; di < distances.Length; di++)
                {
                    double acc = 0;
                    foreach (float az in azimuths)
                    {
                        Vector3 fwd = Quaternion.Euler(0, rig.Head.eulerAngles.y + az, 0) * Vector3.forward;
                        anchor.position = new Vector3(rig.Head.position.x, 1.5f, rig.Head.position.z) + fwd * distances[di];
                        float len = acs.Play("R201");
                        double sum = 0; long n = 0; float t0 = Time.unscaledTime;
                        while (Time.unscaledTime - t0 < len - 0.1f)
                        {
                            AudioListener.GetOutputData(buf0, 0); AudioListener.GetOutputData(buf1, 1);
                            if (Time.unscaledTime - t0 > 0.08f) for (int i = 0; i < buf0.Length; i++) { sum += buf0[i] * buf0[i] + buf1[i] * buf1[i]; n += 2; }
                            yield return null;
                        }
                        acc += n > 0 ? System.Math.Sqrt(sum / n) : 0;
                        acs.Stop();
                        yield return sp.Wait(0.25f);
                    }
                    double mean = acc / azimuths.Length;
                    if (cond == GuidanceCondition.Spatial) rmsS[di] = mean; else rmsM[di] = mean;
                    Note($"{cond} at {distances[di]:0} m: mean RMS {mean:E3} ({20 * System.Math.Log10(System.Math.Max(mean, 1e-9)):F1} dBFS)");
                }
            }
            bool audio = true;
            for (int i = 0; i < distances.Length; i++) audio &= rmsS[i] > floorRms * 3 && rmsM[i] > floorRms * 3;
            Check(audio, "The listener output carries the test signal at every distance (an audio device is available in the Editor)");
            var trim = new double[distances.Length];
            double sumTrim = 0;
            for (int i = 0; i < distances.Length; i++) { trim[i] = audio ? 20 * System.Math.Log10(rmsS[i] / rmsM[i]) : 0; sumTrim += trim[i]; Note($"Trim for stereo at {distances[i]:0} m: {trim[i]:F2} dB"); }
            double meanTrim = sumTrim / distances.Length;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"stereo_gain_trim_db\": {meanTrim.ToString("F2", inv)},");
            sb.AppendLine($"  \"distances_m\": [{string.Join(", ", System.Array.ConvertAll(distances, d => d.ToString("F1", inv)))}],");
            sb.AppendLine($"  \"trim_db\": [{string.Join(", ", System.Array.ConvertAll(trim, d => d.ToString("F2", inv)))}],");
            sb.AppendLine($"  \"audio_available\": {(audio ? "true" : "false")},");
            sb.AppendLine($"  \"spatializer\": \"{spatializer}\",");
            sb.AppendLine("  \"clip\": \"R201\", \"azimuths_deg\": [0, 90, 180, -90]");
            sb.AppendLine("}");
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Analysis", "data"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "level_match.json"), sb.ToString());
            Object.Destroy(go); Object.Destroy(anchor.gameObject);
            SfxDirector.Instance.enabled = true; NarrativeVoiceDirector.Instance.enabled = true; HalcyonDirector.Instance.enabled = true;
            if (limiter != null) limiter.enabled = true;
        }

        static double Mean(List<double> l) { double s = 0; foreach (var v in l) s += v; return l.Count > 0 ? s / l.Count : 0; }
        static double Max(List<double> l) { double m = 0; foreach (var v in l) if (v > m) m = v; return m; }
        static double Min(List<double> l) { double m = double.MaxValue; foreach (var v in l) if (v < m) m = v; return m == double.MaxValue ? 0 : m; }
    }
}
#endif
