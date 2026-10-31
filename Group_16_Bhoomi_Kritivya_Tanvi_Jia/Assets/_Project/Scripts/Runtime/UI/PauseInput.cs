using UnityEngine;
using UnityEngine.InputSystem;

namespace RescueSim.UI
{
    /// <summary>Toggles pause from the left controller menu button (or Escape for simulator/keyboard testing).</summary>
    public class PauseInput : MonoBehaviour
    {
        [SerializeField] MissionUI ui;
        InputAction _pause;

        public void Configure(MissionUI u) { ui = u; }

        void OnEnable()
        {
            _pause = new InputAction("Pause", InputActionType.Button);
            _pause.AddBinding("<XRController>{LeftHand}/menuButton");
            _pause.AddBinding("<OculusTouchController>{LeftHand}/menu");
            _pause.AddBinding("<MetaQuestTouchPlusController>{LeftHand}/menu");
            _pause.AddBinding("<Keyboard>/escape");
            _pause.performed += OnPerformed;
            _pause.Enable();
        }

        void OnDisable()
        {
            if (_pause == null) return;
            _pause.performed -= OnPerformed;
            _pause.Disable();
            _pause.Dispose();
            _pause = null;
        }

        void OnPerformed(InputAction.CallbackContext _) { if (ui != null) ui.TogglePause(); }
    }
}
