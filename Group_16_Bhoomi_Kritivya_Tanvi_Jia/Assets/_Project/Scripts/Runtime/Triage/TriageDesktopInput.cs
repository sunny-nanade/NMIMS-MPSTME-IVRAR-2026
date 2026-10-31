using UnityEngine;
using UnityEngine.InputSystem;

namespace RescueSim.Triage
{
    /// <summary>
    /// Keyboard shortcuts for testing the exercise in the Editor without a headset. Chosen to avoid the Meta XR Simulator keys.
    /// Enter = begin, Tab = next casualty, 1-6 = examinations (walk, breathing, airway, resp. rate, perfusion, commands),
    /// 7/8/9/0 or F1-F4 = tag Green/Yellow/Red/Black, F5 = restart.
    /// </summary>
    public class TriageDesktopInput : MonoBehaviour
    {
        [SerializeField] STARTTriageSimulationManager manager;
        public void Configure(STARTTriageSimulationManager m) { manager = m; }

        static bool Pressed(Keyboard k, Key key) => k[key].wasPressedThisFrame;

        void Update()
        {
            var k = Keyboard.current;
            if (k == null || manager == null) return;
            if (Pressed(k, Key.Enter) && !manager.IsRunning && !manager.IsComplete) manager.BeginSession();
            if (Pressed(k, Key.F5)) manager.Restart();
            if (!manager.IsRunning) return;
            if (Pressed(k, Key.Tab)) manager.SelectNext();
            if (Pressed(k, Key.Digit1)) manager.Examine(ExamKind.Walking);
            if (Pressed(k, Key.Digit2)) manager.Examine(ExamKind.Breathing);
            if (Pressed(k, Key.Digit3)) manager.Examine(ExamKind.Airway);
            if (Pressed(k, Key.Digit4)) manager.Examine(ExamKind.RespiratoryRate);
            if (Pressed(k, Key.Digit5)) manager.Examine(ExamKind.Perfusion);
            if (Pressed(k, Key.Digit6)) manager.Examine(ExamKind.MentalStatus);
            if (Pressed(k, Key.Digit7) || Pressed(k, Key.F1)) manager.Assign(TriageCategory.Green);
            if (Pressed(k, Key.Digit8) || Pressed(k, Key.F2)) manager.Assign(TriageCategory.Yellow);
            if (Pressed(k, Key.Digit9) || Pressed(k, Key.F3)) manager.Assign(TriageCategory.Red);
            if (Pressed(k, Key.Digit0) || Pressed(k, Key.F4)) manager.Assign(TriageCategory.Black);
        }
    }
}
