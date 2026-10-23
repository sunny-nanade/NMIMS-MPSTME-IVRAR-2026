#if UNITY_EDITOR // stage jumps are a development tool, not part of a participant build
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>Editor / experimenter jump: loads the scenes that belong to a stage and sets the stage.</summary>
    public static class StageJump
    {
        public static string SceneFor(Stage s)
        {
            switch (s)
            {
                case Stage.Setup: return DS.SceneSetup;
                case Stage.Awakening:
                case Stage.EarCheck:
                case Stage.KeypadSolved:
                case Stage.TrailFollowed:
                case Stage.Block1:
                case Stage.Block2:
                case Stage.CoolantRouted: return DS.SceneMedicalBay;
                default: return DS.SceneHallway;
            }
        }

        public static void Jump(Stage s)
        {
            var sm = SessionManager.Instance; var sf = SceneFlow.Instance;
            if (sm == null || sf == null) { DS.Warn("Jump: systems not ready"); return; }
            VoiceBus.CancelAll(); // nothing said for the old stage may carry over into the new one
            sm.JumpTo(s);
            sm.CurrentBlock = s == Stage.Block1 ? 1 : (s >= Stage.Block2 ? 2 : 0);
            if (s == Stage.Block2) EgressBlockController.MarkCompleted(1);
            if (s >= Stage.CoolantRouted) EgressBlockController.MarkCompleted(2);
            if (OxygenSystem.Instance != null)
            {
                bool smoke = s == Stage.Block1 || s == Stage.Block2;
                if (s == Stage.Setup) OxygenSystem.Instance.ResetTimer();
                else if (!smoke) OxygenSystem.Instance.Resume(); // a jump out of the smoke room must not leave the oxygen paused
                if (s >= Stage.Awakening && s < Stage.Escaped && !OxygenSystem.Instance.Running) OxygenSystem.Instance.StartCountdown();
            }
            if (s == Stage.Block1 || s == Stage.Block2)
                sf.LoadSceneForJump(DS.SceneMedicalBay, () => sf.EnterSmokeRoom());
            else if (s == Stage.CoolantRouted)
                sf.LoadSceneForJump(DS.SceneMedicalBay, () => sf.PlaceAtSpawn("CoreRoomSpawn"));
            else
                sf.LoadSceneForJump(SceneFor(s));
        }
    }
}
#endif
