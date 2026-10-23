using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// One smoke-room block in SCN_SmokeRoom. Picks the layout and the guidance condition from ConditionRandomizer, starts the timer when the player has faded in,
    /// ends the block at the exit door (or after 4 minutes), logs the metrics, then returns the player to SCN_MedicalBay at CoreRoomSpawn.
    /// </summary>
    public class EgressBlockController : MonoBehaviour
    {
        public static EgressBlockController Instance { get; private set; }
        public static int CompletedBlocks { get; private set; }
        public static event Action<int, bool> BlockCompleted;
        public static void ResetStatics() { CompletedBlocks = 0; }
        /// <summary>Stage jumps and tests: record that blocks up to n are done.</summary>
        public static void MarkCompleted(int n) { CompletedBlocks = Mathf.Max(CompletedBlocks, n); }

        [SerializeField] MazeBuilder maze;
        [SerializeField] PathTracker tracker;
        [SerializeField] MentorDirector mentor;
        [SerializeField] AudioConditionSwitch audioSwitch;
        [SerializeField] SmokeVisibility smoke;
        [SerializeField] GameObject standaloneRig; // inactive rig used only when this scene is opened on its own
        [SerializeField] float timeoutSeconds = 240f;
        [SerializeField] float exitHoldSeconds = 1.6f;
        [SerializeField] float exitDoorSlideSeconds = 1.2f;

        public int Block { get; private set; }
        public GuidanceCondition Condition { get; private set; }
        public char LayoutName { get; private set; }
        public bool Active { get; private set; }
        public bool Finished { get; private set; }
        public bool TimedOut { get; private set; }
        public float EvacTime { get; private set; }
        public float Elapsed => Active ? Time.time - _t0 : EvacTime;
        public MazeBuilder Maze => maze;
        public PathTracker Tracker => tracker;
        public MentorDirector Mentor => mentor;
        public AudioConditionSwitch AudioSwitch => audioSwitch;
        public SmokeVisibility Smoke => smoke;
        public float TimeoutSeconds { get => timeoutSeconds; set => timeoutSeconds = value; }

        float _t0;
        int _wall0;
        PlayerLocomotion _loco;

        void Awake()
        {
            Instance = this;
            if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived += OnArrived;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived -= OnArrived;
        }

        void Start()
        {
                        PlanAndPrepare();
        }

        void PlanAndPrepare()
        {
            var sm = SessionManager.Instance;
            if (sm != null && sm.CurrentBlock <= 0) sm.CurrentBlock = 1;
            Block = sm != null ? sm.CurrentBlock : 1;
            var plan = ConditionRandomizer.Plan(StudyConfig.ParticipantNumber, Block);
            Condition = plan.condition; LayoutName = plan.layout;
            maze.ActivateLayout(plan.layout);
            audioSwitch.Apply(plan.condition);
        }

        IEnumerator Standalone()
        {
            var sf = SceneFlow.Instance;
            while (PlayerRig.Instance == null || PlayerRig.Instance.Head == null || sf == null) { sf = SceneFlow.Instance; yield return null; }
            yield return null;
            var sm = SessionManager.Instance;
            if (sm != null) sm.EnsureAtLeast(Block == 1 ? Stage.Block1 : Stage.Block2);
            sf.ConfigureForSmoke(true);
            sf.PlaceAtSpawn("SmokeEntrySpawn");
            yield return new WaitForSeconds(0.3f);
            Begin();
        }

        void OnArrived(string scene)
        {
            if (scene != DS.SceneSmokeRoom || Active || Finished) return;
            StartCoroutine(BeginWhenReady());
        }

        IEnumerator BeginWhenReady()
        {
            while (SceneFlow.Instance != null && SceneFlow.Instance.IsBusy) yield return null;
            Begin();
        }

        public void Begin()
        {
            if (Active || Finished) return;
            PlanAndPrepare();
            smoke.Apply();
            if (TelemetryLogger.Instance != null) TelemetryLogger.Instance.BeginBlock(Block);
            if (OxygenSystem.Instance != null) OxygenSystem.Instance.Pause();
            var rig = PlayerRig.Instance;
            _loco = rig != null ? rig.GetComponent<PlayerLocomotion>() : null;
            _wall0 = _loco != null ? _loco.WallContacts : 0;
            tracker.Begin(maze.Layout);
            mentor.BeginBlock(maze.ExitAnchor);
            _t0 = Time.time; Active = true;
            DS.Log($"Block {Block} begins: layout {LayoutName}, condition {ConditionRandomizer.Short(Condition)}");
        }

        void Update()
        {
            if (!Active) return;
            var rig = PlayerRig.Instance;
            if (rig != null && rig.Head != null && maze.InsideExitZone(rig.Head.position)) { Finish(false); return; }
            if (Time.time - _t0 >= timeoutSeconds) Finish(true);
        }

        public void Finish(bool timeout)
        {
            if (!Active) return;
            Active = false; Finished = true; TimedOut = timeout;
            EvacTime = Time.time - _t0;
            tracker.End();
            mentor.EndBlock(timeout);
            SfxEvents.Raise(SfxEvent.ExitDoor, maze.ExitAnchor != null ? maze.ExitAnchor.position : Vector3.zero);
            if (maze.ExitDoorLeaf != null && !timeout) StartCoroutine(SlideOpen(maze.ExitDoorLeaf, maze.ExitHandle));

            int b = Block;
            TelemetryLogger.Log("evac_time_s", EvacTime, b);
            TelemetryLogger.Log("wrong_turns", tracker.WrongTurns, b);
            TelemetryLogger.Log("dead_end_entries", tracker.DeadEndEntries, b);
            TelemetryLogger.Log("backtracks", tracker.Backtracks, b);
            TelemetryLogger.Log("path_length_m", tracker.PathLength, b);
            TelemetryLogger.Log("path_efficiency", tracker.Efficiency, b);
            TelemetryLogger.Log("wall_contacts", (_loco != null ? _loco.WallContacts : 0) - _wall0, b);
            TelemetryLogger.Log("idle_time_s", tracker.IdleTotal, b);
            TelemetryLogger.Log("timeout_flag", timeout, b);
            TelemetryLogger.Log("mean_head_turn_latency_ms", mentor.MeanHeadTurnLatencyMs, b);
            if (TelemetryLogger.Instance != null) TelemetryLogger.Instance.EndBlock(b);
            CompletedBlocks = Mathf.Max(CompletedBlocks, b);
            DS.Log($"Block {b} {(timeout ? "timed out" : "finished")} in {EvacTime:F1} s: wrong turns {tracker.WrongTurns}, dead ends {tracker.DeadEndEntries}, backtracks {tracker.Backtracks}, efficiency {tracker.Efficiency:F2}");
            BlockCompleted?.Invoke(b, timeout);
            StartCoroutine(Leave(b, timeout));
        }

        IEnumerator SlideOpen(Transform leaf, Transform handle)
        {
            Vector3 a = leaf.position, b = a + new Vector3(0.95f, 0f, 0f);
            Vector3 ha = handle != null ? handle.position : Vector3.zero;
            float e = 0f;
            while (e < exitDoorSlideSeconds)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, e / exitDoorSlideSeconds);
                leaf.position = Vector3.Lerp(a, b, k);
                if (handle != null) handle.position = ha + (b - a) * k;
                yield return null;
            }
        }

        IEnumerator Leave(int block, bool timeout)
        {
            yield return new WaitForSeconds(exitHoldSeconds);
            // let the finish line (R209 / R210) end before the smoke scene and its audio source go away
            float waited = 0f;
            while (mentor != null && mentor.IsBusy && waited < 10f) { waited += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
            smoke.Clear();
            var sf = SceneFlow.Instance;
            if (sf != null) sf.ReturnToCore(() => PostReturn(block));
        }

        static void PostReturn(int block)
        {
            if (OxygenSystem.Instance != null) OxygenSystem.Instance.Resume();
            // the NASA-TLX opens at the rack; R140 (block 1) and R150 / R151 (after block 2's TLX) are requested by QuestionnaireUI
            QuestionnaireUI.BeginTlx(block);
        }
    }
}
