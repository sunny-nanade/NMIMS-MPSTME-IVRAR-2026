#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Polish check: loudness of every voice line at the AudioListener (after the master limiter), measured with the OnAudioFilterRead meter so that peaks are exact.
    /// Rose comms (2D), HALCYON (ceiling speaker), Rose guidance in the smoke room (exit door, spatial S and stereo M, several distances), each alone at slider 100 %.
    /// Targets: voice peaks between -6 and -3 dBFS, mean active RMS about -20 dBFS, smoke-room Rose within 2 dB of (and not below) Rose elsewhere in both conditions.
    /// </summary>
    public partial class PlayCheckRunner
    {
        struct LevelRow { public string group, id; public double rms, act, peak; }

        /// <summary>Waits until the voice has been silent for 0.4 s (a one-frame gap between two files does not count as the end).</summary>
        static IEnumerator StaysQuiet(System.Func<bool> speaking, float max)
        {
            float t0 = Time.unscaledTime, quiet = 0f;
            while (quiet < 0.4f && Time.unscaledTime - t0 < max) { quiet = speaking() ? 0f : quiet + Time.unscaledDeltaTime; yield return null; }
        }

        IEnumerator PolishLevels()
        {
            VoiceRules.BypassForTests = true;
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SfxDirector.Instance != null && NarrativeVoiceDirector.Instance != null && HalcyonDirector.Instance != null, 10f);
            var narr = NarrativeVoiceDirector.Instance; var hal = HalcyonDirector.Instance; var sfx = SfxDirector.Instance;
            bool limiter = UnityEditor.SessionState.GetBool("DS_levels_limiter", true);
            var listener = FindAnyObjectByType<AudioListener>();
            var meter = AudioLevelMeter.EnsureOn(listener.gameObject);
            listener.GetComponent<MasterLimiter>().enabled = limiter;
            Note("Master limiter: " + (limiter ? "on (ceiling " + listener.GetComponent<MasterLimiter>().CeilingDb + " dBFS)" : "off"));
            AudioListener.volume = 1f;
            StudyConfig.Data.participant_id = "4";
            yield return sp.Until(() => !narr.IsSpeaking && !hal.IsSpeaking && narr.QueueLength == 0, 60f);
            sfx.ManualMixer = true;
            foreach (var a in sfx.AmbienceSources) if (a != null) a.volume = 0f;
            yield return sp.Wait(1f);

            var rows = new List<LevelRow>();
            // ---------------------------------------------------------------- Rose comms
            foreach (var id in new[] { "R001", "R002", "R003", "R010", "R011", "R012", "R030", "R031", "R032", "R040", "R041", "R043", "R050", "R052", "R140", "R150", "R151", "R153", "R160", "R161" })
            {
                SetGroups(0, -80, -80, -80, -80);
                AudioLevelMeter.WinBegin();
                VoiceBus.Request(id);
                yield return sp.Until(() => narr.IsSpeaking && narr.Current == id, 4f);
                yield return StaysQuiet(() => narr.IsSpeaking, 40f);
                yield return sp.Wait(0.1f);
                var m = AudioLevelMeter.WinEnd();
                rows.Add(new LevelRow { group = "Rose comms (Medical Bay)", id = id, rms = m.rmsDb, act = m.activeRmsDb, peak = m.peakDb });
            }
            // ---------------------------------------------------------------- HALCYON
            foreach (var id in new[] { "H001", "H002", "H004", "H007", "H008" })
            {
                SetGroups(-80, 0, -80, -80, -80);
                AudioLevelMeter.WinBegin();
                VoiceBus.Request(id);
                yield return sp.Until(() => hal.IsSpeaking && hal.Current == id, 4f);
                yield return StaysQuiet(() => hal.IsSpeaking, 40f);
                yield return sp.Wait(0.1f);
                var m = AudioLevelMeter.WinEnd();
                rows.Add(new LevelRow { group = "HALCYON (ceiling speaker)", id = id, rms = m.rmsDb, act = m.activeRmsDb, peak = m.peakDb });
            }
            // ---------------------------------------------------------------- Rose guidance from the exit door, spatial and stereo
            var rig = PlayerRig.Instance;
            var go = new GameObject("PolishLevelsSwitch");
            var acs = go.AddComponent<AudioConditionSwitch>();
            var anchor = new GameObject("PolishLevelsAnchor").transform;
            acs.Bind(anchor);
            float[] distances = { 2f, 5f, 9f, 14f, 20f };
            float[] azimuths = { 0f, 180f };
            string[] block = { "R201", "R205", "R209" };
            int bi = 0;
            SetGroups(0, -80, -80, -80, -80);
            var perCond = new Dictionary<GuidanceCondition, List<double>>();
            var perCondPeak = new Dictionary<GuidanceCondition, double>();
            foreach (var cond in new[] { GuidanceCondition.Spatial, GuidanceCondition.Stereo })
            {
                perCond[cond] = new List<double>(); perCondPeak[cond] = -99;
                acs.Apply(cond);
                foreach (float d in distances)
                {
                    var dd = new List<double>(); double pk = -99;
                    foreach (float az in azimuths)
                    {
                        Vector3 fwd = Quaternion.Euler(0, rig.Head.eulerAngles.y + az, 0) * Vector3.forward;
                        anchor.position = new Vector3(rig.Head.position.x, 1.5f, rig.Head.position.z) + fwd * d;
                        foreach (var id in new[] { block[bi++ % block.Length] })
                        {
                            AudioLevelMeter.WinBegin();
                            float len = acs.Play(id);
                            float t0 = Time.unscaledTime;
                            while (acs.IsPlaying && Time.unscaledTime - t0 < len + 1f) yield return null;
                            yield return sp.Wait(0.1f);
                            var m = AudioLevelMeter.WinEnd();
                            dd.Add(m.activeRmsDb); if (m.peakDb > pk) pk = m.peakDb;
                        }
                    }
                    double mean = 0; foreach (var v in dd) mean += v; mean /= dd.Count;
                    perCond[cond].Add(mean); if (pk > perCondPeak[cond]) perCondPeak[cond] = pk;
                    rows.Add(new LevelRow { group = "Rose guidance " + (cond == GuidanceCondition.Spatial ? "S spatial" : "M stereo"), id = d.ToString("0") + " m", rms = mean, act = mean, peak = pk });
                }
                acs.Stop();
            }
            Object.Destroy(go); Object.Destroy(anchor.gameObject);

            // ---------------------------------------------------------------- summary
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string last = "";
            foreach (var r in rows)
            {
                if (r.group != last) { Note("**" + r.group + "**"); last = r.group; }
                Note($"{r.id}: active RMS {r.act:F1} dBFS, peak {r.peak:F1} dBFS");
            }
            double Mean(string g) { double s = 0; int n = 0; foreach (var r in rows) if (r.group == g) { s += r.act; n++; } return n > 0 ? s / n : -99; }
            double Max(string g) { double s = -99; foreach (var r in rows) if (r.group == g && r.peak > s) s = r.peak; return s; }
            double Min(string g) { double s = 99; foreach (var r in rows) if (r.group == g && r.peak < s) s = r.peak; return s; }
            double roseMean = Mean("Rose comms (Medical Bay)"), halMean = Mean("HALCYON (ceiling speaker)");
            double sMean = Mean("Rose guidance S spatial"), mMean = Mean("Rose guidance M stereo");
            Note($"SUMMARY rose_comms_mean_active_rms={roseMean.ToString("F1", inv)} peak_min={Min("Rose comms (Medical Bay)").ToString("F1", inv)} peak_max={Max("Rose comms (Medical Bay)").ToString("F1", inv)}");
            Note($"SUMMARY halcyon_mean_active_rms={halMean.ToString("F1", inv)} peak_min={Min("HALCYON (ceiling speaker)").ToString("F1", inv)} peak_max={Max("HALCYON (ceiling speaker)").ToString("F1", inv)}");
            Note($"SUMMARY smoke_S_mean_active_rms={sMean.ToString("F1", inv)} peak_max={Max("Rose guidance S spatial").ToString("F1", inv)}");
            Note($"SUMMARY smoke_M_mean_active_rms={mMean.ToString("F1", inv)} peak_max={Max("Rose guidance M stereo").ToString("F1", inv)}");
            Check(rows.Count > 0, "Levels measured for " + rows.Count + " rows");
            if (limiter)
            {
                Check(Min("Rose comms (Medical Bay)") >= -6.5 && Max("Rose comms (Medical Bay)") <= -2.8, "Rose comms peaks between -6 and -3 dBFS (" + Min("Rose comms (Medical Bay)").ToString("F1", inv) + " to " + Max("Rose comms (Medical Bay)").ToString("F1", inv) + ")");
                Check(Min("HALCYON (ceiling speaker)") >= -6.5 && Max("HALCYON (ceiling speaker)") <= -2.8, "HALCYON peaks between -6 and -3 dBFS (" + Min("HALCYON (ceiling speaker)").ToString("F1", inv) + " to " + Max("HALCYON (ceiling speaker)").ToString("F1", inv) + ")");
                Check(roseMean >= -22f && roseMean <= -17f, "Rose comms mean active RMS about -20 dBFS (" + roseMean.ToString("F1", inv) + ")");
                Check(sMean >= roseMean - 2.0 && mMean >= roseMean - 2.0, $"Smoke-room Rose (S {sMean:F1}, M {mMean:F1}) is within 2 dB of Rose elsewhere ({roseMean:F1}) or louder");
                Check(System.Math.Abs(sMean - mMean) <= 1.0, $"S and M are level-matched ({sMean:F1} vs {mMean:F1} dBFS)");
            }
            sfx.ManualMixer = false;
        }
    }
}
#endif
