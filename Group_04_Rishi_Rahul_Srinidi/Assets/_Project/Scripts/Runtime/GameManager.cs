using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CSER
{
    public enum GameState { Intro, Playing, Won, Lost }

    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        // Difficulty survives the scene reload triggered by "change difficulty / restart".
        public static bool Experienced;
        static bool skipIntroOnReload;

        [Tooltip("1-based chapter this scene plays (see GameData.Chapters)")]
        public int chapter = 2;
        public ChapterInfo Chapter => GameData.Chapters[chapter - 1];
        public bool HasTimer => Chapter.normalMinutes > 0;

        public GameState State { get; private set; } = GameState.Intro;
        public float Elapsed { get; private set; }
        public float TimeLimit => (Experienced ? Chapter.experiencedMinutes : Chapter.normalMinutes) * 60f;
        public float Exfil01 => HasTimer ? Mathf.Clamp01(Elapsed / TimeLimit) : 0;
        public int HintsUsed { get; private set; }
        public int Mistakes { get; private set; }
        public int[] DoorCode { get; private set; }
        /// Stops the chapter clock (the final encryption + data reveal can't be lost to the timer mid-sequence).
        public bool TimerPaused { get; set; }

        readonly HashSet<TaskId> done = new HashSet<TaskId>();
        readonly Dictionary<TaskId, int> hintTier = new Dictionary<TaskId, int>();

        public event Action<TaskId> TaskCompleted;
        public event Action<GameState> StateChanged;
        public event Action<int> AreaCompleted;

        public int DoneCount => done.Count;
        public bool IsDone(TaskId t) => done.Contains(t);

        // clear the static when the scene unloads, so later scenes never call into a destroyed instance
        void OnDestroy() { if (I == this) I = null; }

        void Awake()
        {
            I = this;
            var rng = new System.Random(Environment.TickCount);
            DoorCode = new[] { rng.Next(1, 10), rng.Next(0, 10), rng.Next(0, 10), rng.Next(0, 10) };
        }

        void Start()
        {
            AuditInteraction();
            RunState.SetCheckpoint(chapter);
            if (skipIntroOnReload)
            {
                skipIntroOnReload = false;
                SetState(GameState.Playing);
            }
            else SetState(GameState.Intro);
        }

        void Update()
        {
            if (State != GameState.Playing || TimerPaused) return;
            Elapsed += Time.deltaTime;
            if (HasTimer && Elapsed >= TimeLimit) Lose();
        }

        /// Every interactive canvas must keep its ISDK ray interaction, or nothing on it can be clicked in VR.
        static void AuditInteraction()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (!c.GetComponent<UnityEngine.UI.GraphicRaycaster>()) continue;
                bool ok = c.GetComponentInChildren<Oculus.Interaction.RayInteractable>(true);
                if (ok) Debug.Log($"[CSER] {c.name}: ray interaction OK");
                else Debug.LogError($"[CSER] {c.name} has no ISDK ray interaction: its buttons can't be clicked in VR");
            }
        }

        public void SetState(GameState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        public void BeginPlaying()
        {
            if (State == GameState.Intro) SetState(GameState.Playing);
        }

        public bool Complete(TaskId t)
        {
            if (State == GameState.Won || State == GameState.Lost || !done.Add(t)) return false;
            Sfx.Play(Sfx.I ? Sfx.I.success : null);
            Sfx.Haptic(0.5f, 0.1f);
            if (GameData.HasPoint(t))
            {
                StoryProgress.AddPoint(t);
                var lp = GameData.Point(t);
                GameUI.I?.Toast("Learning point unlocked", $"<b>{lp.title}</b>\n{lp.text}", Palette.Mint, 9f);
            }
            TaskCompleted?.Invoke(t);
            for (int i = 0; i < GameData.Areas.Length; i++)
            {
                var area = GameData.Areas[i];
                if (Array.IndexOf(area.tasks, t) < 0) continue;
                if (Array.TrueForAll(area.tasks, done.Contains))
                {
                    Sfx.Play(Sfx.I ? Sfx.I.bong : null);
                    AreaCompleted?.Invoke(i);
                }
            }
            if (Array.TrueForAll(Chapter.tasks, x => done.Contains(x.id))) Win();
            return true;
        }

        public bool AreaDone(int i) => Array.TrueForAll(GameData.Areas[i].tasks, done.Contains);

        public bool AllContainedExcept(params TaskId[] except)
        {
            foreach (var info in Chapter.tasks)
                if (Array.IndexOf(except, info.id) < 0 && !done.Contains(info.id)) return false;
            return true;
        }

        public void Mistake(string message)
        {
            if (State == GameState.Playing) Mistakes++;
            Sfx.Play(Sfx.I ? Sfx.I.error : null);
            if (!string.IsNullOrEmpty(message)) GameUI.I?.Caption(message, Palette.Red);
        }

        /// The task the hint system and the objective line point at: the first unfinished one in play order.
        public TaskId CurrentObjective()
        {
            foreach (var info in Chapter.tasks)
                if (!done.Contains(info.id)) return info.id;
            return Chapter.tasks[Chapter.tasks.Length - 1].id;
        }

        public string NextHint(out TaskId task, out int tier)
        {
            task = CurrentObjective();
            hintTier.TryGetValue(task, out tier);
            var hints = GameData.Info(task).hints;
            var text = hints[Mathf.Min(tier, hints.Length - 1)];
            if (tier < hints.Length) HintsUsed++;
            hintTier[task] = tier + 1;
            return text;
        }

        public int HintTierFor(TaskId t) => hintTier.TryGetValue(t, out var v) ? v : 0;

        void Win()
        {
            StoryProgress.Complete(chapter, Elapsed, HintsUsed, Mistakes);
            SetState(GameState.Won);
            Sfx.Play(Sfx.I ? Sfx.I.win : null);
        }

        void Lose()
        {
            Elapsed = TimeLimit;
            Narrator.I?.Stop();
            SetState(GameState.Lost);
            Sfx.Play(Sfx.I ? Sfx.I.glitch : null);
        }

        public void Restart(bool skipIntro)
        {
            skipIntroOnReload = skipIntro;
            SceneFlow.Load(SceneManager.GetActiveScene().name);
        }

        public void SetDifficulty(bool experienced)
        {
            if (experienced == Experienced) return;
            Experienced = experienced;
            if (State != GameState.Intro) Restart(true);
        }

        public static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0, seconds);
            return $"{(int)(seconds / 60):00}:{(int)(seconds % 60):00}";
        }

        // ---- test helpers (callable from the editor / MCP for automated play-throughs) ----
        public void DebugComplete(string task)
        {
            if (Enum.TryParse(task, out TaskId t)) Complete(t);
        }

        /// Presses the first active UI button whose label contains the given text (automated play-tests).
        public string DebugClick(string label)
        {
            foreach (var b in FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Exclude))
            {
                var t = b.GetComponentInChildren<TMPro.TMP_Text>();
                if (t && t.text.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0 && b.interactable)
                {
                    b.onClick.Invoke();
                    return "clicked " + t.text;
                }
            }
            return "no button " + label;
        }

        /// Drops an item into a zone as if the player released it there (automated play-tests).
        public void DebugDrop(string item, string zone)
        {
            var i = GameObject.Find(item);
            var z = GameObject.Find(zone);
            if (i && z)
            {
                if (i.TryGetComponent<GrabItem>(out var g))
                {
                    g.EverHeld = true;
                    g.Teleport(z.transform.position, i.transform.rotation);
                }
                else i.transform.position = z.transform.position;
            }
        }

        /// Jumps the chapter clock to a few seconds before the limit (game-over tests).
        public void DebugNearlyOutOfTime(float secondsLeft) { if (HasTimer) Elapsed = Mathf.Max(Elapsed, TimeLimit - secondsLeft); }

        public static string StatusLine() => I ? I.DebugStatus() : "no GameManager";

        public string DebugStatus()
        {
            var sb = new System.Text.StringBuilder($"{State} t={Elapsed:0} hints={HintsUsed} mistakes={Mistakes} code={string.Concat(DoorCode)} done=");
            foreach (var t in done) sb.Append(t).Append(',');
            return sb.ToString();
        }

        public void DebugCompleteAllButEscape()
        {
            var last = Chapter.tasks[Chapter.tasks.Length - 1].id;
            foreach (var info in Chapter.tasks)
                if (info.id != last) Complete(info.id);
        }
    }
}
