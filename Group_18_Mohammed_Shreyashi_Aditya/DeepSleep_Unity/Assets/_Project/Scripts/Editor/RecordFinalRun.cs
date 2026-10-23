using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// DeepSleep > Record Final Run (Editor only). Runs PlaythroughBot for participant 4 (S-A then M-B) in real time with a
    /// caption and records the Game view with the Unity Recorder: Movie, MP4 H.264, 1920 x 1080, 30 fps constant, game audio on.
    /// The play-mode side (PlayCheckRunner scenario "record") and this class talk through SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class RecordFinalRun
    {
        const int Width = 1920, Height = 1080, Fps = 30;
        static RecorderController _ctl;
        static bool _stopRequested;

        static RecordFinalRun()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingPlayMode && _ctl != null && _ctl.IsRecording()) _ctl.StopRecording();
                if (s == PlayModeStateChange.EnteredEditMode) { _ctl = null; _stopRequested = false; }
            };
        }

        public static string OutputBase => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Recordings", "DeepSleep_FinalRun_v3_" + DateTime.Now.ToString("yyyyMMdd")));

        [MenuItem("DeepSleep/Record Final Run")]
        public static void Record()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[DeepSleep] Stop play mode first, then use Record Final Run."); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(OutputBase));
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(OutputBase), Path.GetFileName(OutputBase) + "*")) File.Delete(f);
            EditorUtility.audioMasterMute = false; // the Game view must not be muted
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.SetBool("DS_record_on", true);
            SessionState.SetString("DS_record_path", OutputBase);
            // the recorded run: participant 4, human pace, no coverage extras, real time, a caption, timeline written to timeline_record.csv
            SessionState.SetInt("DS_bot_first", 4); SessionState.SetInt("DS_bot_last", 4); SessionState.SetBool("DS_bot_cov", false);
            SessionState.SetBool("DS_bot_timeline", true); SessionState.SetString("DS_bot_timeline_name", "record");
            SessionState.SetFloat("DS_bot_ts", 1f); SessionState.SetBool("DS_bot_pace", true); SessionState.SetBool("DS_bot_plain", true); SessionState.SetBool("DS_bot_human", true);
            PlayCheckLauncher.Launch("record", ProjectSetupTools.ScenePath("SCN_Main"), "Assets/_Project/Docs/Reports/final_record.md");
        }

        static void Tick()
        {
            if (!SessionState.GetBool("DS_record_on", false) || !EditorApplication.isPlaying) return;
            if (_ctl == null && SessionState.GetBool("DS_record_ready", false) && !SessionState.GetBool("DS_record_started", false)) StartRecorder();
            if (_ctl != null && SessionState.GetBool("DS_record_end", false) && !_stopRequested)
            {
                _stopRequested = true;
                if (_ctl.IsRecording()) _ctl.StopRecording();
            }
            if (_ctl != null && _stopRequested && !_ctl.IsRecording() && !SessionState.GetBool("DS_record_stopped", false))
            {
                SessionState.SetBool("DS_record_stopped", true);
                SessionState.SetBool("DS_record_on", false);
                Debug.Log("[DeepSleep] Recording finished: " + SessionState.GetString("DS_record_path", "") + ".mp4");
            }
        }

        static void StartRecorder()
        {
            var cs = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var mv = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            mv.name = "DeepSleep Final Run";
            mv.Enabled = true;
            mv.EncoderSettings = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4, EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
            mv.CaptureAudio = true;
            mv.ImageInputSettings = new GameViewInputSettings { OutputWidth = Width, OutputHeight = Height };
            mv.OutputFile = SessionState.GetString("DS_record_path", "").Replace('\\', '/');
            mv.FrameRate = Fps;
            mv.FrameRatePlayback = FrameRatePlayback.Constant;
            mv.CapFrameRate = true;
            cs.AddRecorderSettings(mv);
            cs.SetRecordModeToManual();
            cs.FrameRate = Fps;
            cs.CapFrameRate = true;
            cs.FrameRatePlayback = FrameRatePlayback.Constant;
            _ctl = new RecorderController(cs);
            _ctl.PrepareRecording();
            bool ok = _ctl.StartRecording();
            SessionState.SetBool("DS_record_started", ok);
            Debug.Log($"[DeepSleep] Recorder started: {ok}, {Width}x{Height} @ {Fps} fps constant, MP4 H.264, audio {mv.CaptureAudio}, file {mv.OutputFile}.mp4");
        }
    }
}
