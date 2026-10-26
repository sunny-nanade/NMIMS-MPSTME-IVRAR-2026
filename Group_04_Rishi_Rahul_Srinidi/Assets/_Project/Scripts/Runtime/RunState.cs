using UnityEngine;

namespace CSER
{
    /// One play-through ("run"): wall-clock time from the first chapter load after the main menu, and the chapter
    /// checkpoint a retry goes back to. Static (no DontDestroyOnLoad object), so it can never be duplicated;
    /// returning to the main menu clears it.
    public static class RunState
    {
        static double startTime = -1;

        public static bool Running => startTime >= 0;

        /// Chapter whose start is the current checkpoint (0 = none yet).
        public static int Checkpoint { get; private set; }

        /// Total run time in seconds, counting intros, loads and retries (wall clock since New Game).
        public static float Seconds => Running ? (float)(Time.realtimeSinceStartupAsDouble - startTime) : 0f;

        /// Starts the clock unless a run is already in progress (continuing to the next chapter keeps it).
        public static void BeginIfIdle()
        {
            if (!Running) startTime = Time.realtimeSinceStartupAsDouble;
        }

        public static void SetCheckpoint(int chapter) => Checkpoint = chapter;

        public static void Reset()
        {
            startTime = -1;
            Checkpoint = 0;
        }

        // domain reload can be disabled in the editor: start every play session clean
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnPlay() => Reset();
    }
}
