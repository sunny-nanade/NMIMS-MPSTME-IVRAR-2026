#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Scenario "record": the final real-time run (participant 4, order S-A then M-B) while the Editor-side recorder (RecordFinalRun) captures the Game view.</summary>
    public partial class PlayCheckRunner
    {
        IEnumerator RecordRun()
        {
            AudioLevelMeter.Reset();
            gameObject.AddComponent<RecordCaption>();
            SessionState.SetBool("DS_record_started", false); SessionState.SetBool("DS_record_end", false); SessionState.SetBool("DS_record_stopped", false);
            SessionState.SetBool("DS_record_ready", true);
            yield return RealUntil(() => SessionState.GetBool("DS_record_started", false), 90f);
            Check(SessionState.GetBool("DS_record_started", false), "The Unity Recorder started (Movie, MP4 H.264, 1920 x 1080, 30 fps, constant frame rate, audio on)");
            Check(!EditorUtility.audioMasterMute, $"Game view audio is not muted (mute {EditorUtility.audioMasterMute})");
            float recStart = Time.time;
            SessionState.SetFloat("DS_record_t0", recStart);
            yield return new WaitForSeconds(2f);
            var bot = gameObject.AddComponent<PlaythroughBot>();
            yield return bot.Run(this, 4, 4);
            Check(AudioLevelMeter.ClippedBlocks == 0, $"No clipping at the listener ({AudioLevelMeter.ClippedBlocks} blocks above full scale, first at game time {AudioLevelMeter.FirstClipTime:F1} s)");
            yield return new WaitForSeconds(5f); // the plaque stays on screen for 5 s
            float len = Time.time - recStart;
            Note($"Recorded game time {len:F1} s ({len / 60f:F1} min)");
            var sb = new System.Text.StringBuilder();
            double rms = AudioLevelMeter.Samples > 0 ? System.Math.Sqrt(AudioLevelMeter.SumSquares / AudioLevelMeter.Samples) : 0;
            double db = rms > 0 ? 20 * System.Math.Log10(rms) : -200;
            sb.AppendLine($"listener_mix_mean_rms_dbfs={db:F1}");
            sb.AppendLine($"listener_mix_peak={AudioLevelMeter.Peak:F3}");
            sb.AppendLine($"blocks_above_minus80dbfs={AudioLevelMeter.LoudBlocks}/{AudioLevelMeter.Blocks}");
            sb.AppendLine($"recorded_game_seconds={len:F1}");
            sb.AppendLine($"record_t0={recStart:F2}");
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Recordings", "record_audio_level.txt")), sb.ToString());
            Check(db > -60.0 && AudioLevelMeter.Peak > 0.05f, $"The mix reaching the AudioListener is not silent (mean {db:F1} dBFS, peak {AudioLevelMeter.Peak:F2})");
            Check(len > 12f * 60f, $"The recorded run is longer than 12 minutes ({len / 60f:F1} min)");
            SessionState.SetBool("DS_record_end", true);
            yield return RealUntil(() => SessionState.GetBool("DS_record_stopped", false), 120f);
            Check(SessionState.GetBool("DS_record_stopped", false), "The recorder stopped and finalised the file");
        }
    }
}
#endif
