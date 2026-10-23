using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Holds narrative voice lines (Rose comms, HALCYON) for a short settle time after a scene loads, until the frame time is steady.
    /// A line started during the load hitch (shader warm-up, texture upload, first-frame audio mixer work) underruns the audio buffer and crackles.
    /// The gate always opens after MaxWaitSeconds so a slow device is never silenced.
    /// </summary>
    public static class VoiceGate
    {
        public const float SettleSeconds = 0.8f;
        public const float MaxWaitSeconds = 3.5f;
        public const float SteadyFrameSeconds = 0.04f;
        public const int SteadyFrames = 20;

        static float _loadedAt = -999f;
        static int _steady;
        static int _lastFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded;
            _loadedAt = Time.unscaledTime; _steady = 0; _lastFrame = -1;
        }

        static void OnLoaded(Scene s, LoadSceneMode m) { NotifyLoaded(); }

        /// <summary>A section switch causes the same hitch as a scene load: hold narrative lines until frames are steady again.</summary>
        public static void NotifyLoaded() { _loadedAt = Time.unscaledTime; _steady = 0; }

        /// <summary>True when a narrative line may start now. Cheap; call from Update.</summary>
        public static bool Open
        {
            get
            {
                if (_lastFrame != Time.frameCount)
                {
                    _lastFrame = Time.frameCount;
                    _steady = Time.unscaledDeltaTime <= SteadyFrameSeconds ? _steady + 1 : 0;
                }
                float since = Time.unscaledTime - _loadedAt;
                if (since >= MaxWaitSeconds) return true;
                return since >= SettleSeconds && _steady >= SteadyFrames;
            }
        }
    }
}
