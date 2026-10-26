using System;
using UnityEngine;

namespace CSER
{
    /// Guides the player through a chapter, one objective at a time: narration on each step,
    /// the guide beacon over the next object (or over the drop target while an item is held),
    /// and the objective line on the wrist/HUD. Steps are completed by the scene's puzzle logic.
    public class ChapterDirector : MonoBehaviour
    {
        [Serializable]
        public class Step
        {
            public TaskId task;
            public Transform[] targets;      // first active one gets the beacon
            public Transform dropTarget;     // beacon moves here while one of the targets is held
            public string label = "Here";
            public string dropLabel = "Drop it here";
            public float revealDelay;        // seconds before the beacon appears (keeps "find it" steps a puzzle)
        }

        public Step[] steps = new Step[0];
        public bool guideEnabled = true;

        TaskId? current;
        float stepStart;
        string pendingVoice;
        string ChapterKey => "ch" + GameManager.I.chapter;

        void Start()
        {
            GameManager.I.StateChanged += OnState;
            GameManager.I.TaskCompleted += OnTask;
            if (Narrator.I) Narrator.I.GroupFinished += OnVoiceFinished;
        }

        void OnState(GameState s)
        {
            if (s == GameState.Playing)
            {
                Speak("intro_" + ChapterKey, true);
                Enter(GameManager.I.CurrentObjective());
            }
            else if (s == GameState.Won || s == GameState.Lost)
            {
                GuideBeacon.I?.Hide();
                current = null;
                if (s == GameState.Won) Speak("outro_" + ChapterKey, true);
            }
        }

        void OnTask(TaskId t)
        {
            if (GameManager.I.State != GameState.Playing) return;
            var next = GameManager.I.CurrentObjective();
            if (next != current) Enter(next);
        }

        void Enter(TaskId t)
        {
            current = t;
            stepStart = Time.time;
            var info = GameData.Info(t);
            var chapter = GameManager.I.Chapter;
            int index = Array.FindIndex(chapter.tasks, x => x.id == t) + 1;
            GameUI.I?.SetObjective(index, chapter.tasks.Length, info.label);
            Speak(t.ToString(), false);
        }

        /// Chapter intros/outros interrupt; step lines wait for the current line to finish.
        void Speak(string key, bool interrupt)
        {
            if (!Narrator.I || !Narrator.I.Has(key)) return;
            if (interrupt || !Narrator.I.Speaking)
            {
                pendingVoice = null;
                Narrator.I.Play(key);
            }
            else pendingVoice = key;
        }

        void OnVoiceFinished()
        {
            if (pendingVoice == null) return;
            var k = pendingVoice;
            pendingVoice = null;
            Narrator.I.Play(k);
        }

        void Update()
        {
            if (!GuideBeacon.I) return;
            if (!guideEnabled || !Guide.Enabled || current == null || GameManager.I.State != GameState.Playing) { GuideBeacon.I.Hide(); return; }
            var step = Array.Find(steps, s => s.task == current.Value);
            if (step == null || Time.time - stepStart < step.revealDelay) { GuideBeacon.I.Hide(); return; }
            if (step.dropTarget)
                foreach (var t in step.targets)
                    if (t && t.TryGetComponent<GrabItem>(out var g) && g.IsHeld && !g.Consumed)
                    {
                        GuideBeacon.I.Show(step.dropTarget, step.dropLabel);
                        return;
                    }
            foreach (var t in step.targets)
            {
                if (!t || !t.gameObject.activeInHierarchy) continue;
                if (t.TryGetComponent<GrabItem>(out var g) && g.Consumed) continue;
                GuideBeacon.I.Show(t, step.label);
                return;
            }
            GuideBeacon.I.Hide();
        }
    }
}
