using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace RescueSim.Player
{
    /// <summary>Applies <see cref="GameSettings"/> to the XR rig's locomotion providers and audio listener.</summary>
    public class ComfortApplier : MonoBehaviour
    {
        [SerializeField] SnapTurnProvider snapTurn;
        [SerializeField] ContinuousTurnProvider continuousTurn;
        [SerializeField] ContinuousMoveProvider move;
        [SerializeField] ComfortVignette vignette;

        public void Configure(SnapTurnProvider snap, ContinuousTurnProvider cont, ContinuousMoveProvider mv, ComfortVignette vig)
        {
            snapTurn = snap; continuousTurn = cont; move = mv; vignette = vig;
        }

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        public void Apply()
        {
            bool snap = GameSettings.SnapTurn;
            if (snapTurn != null) { snapTurn.enabled = snap; snapTurn.turnAmount = GameSettings.SnapAngle; }
            if (continuousTurn != null) { continuousTurn.enabled = !snap; continuousTurn.turnSpeed = GameSettings.TurnSpeed; }
            if (move != null) move.moveSpeed = GameSettings.MoveSpeed;
            if (vignette != null) vignette.Enabled = GameSettings.ComfortVignette;
            AudioListener.volume = GameSettings.Volume;
        }
    }
}
