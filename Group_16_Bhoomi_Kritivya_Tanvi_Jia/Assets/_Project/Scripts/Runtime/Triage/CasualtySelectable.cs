using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Triage
{
    /// <summary>Lets the XR rig select a casualty (point + trigger, or grip when close) to start assessing them.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class CasualtySelectable : MonoBehaviour
    {
        [SerializeField] STARTTriageSimulationManager manager;
        [SerializeField] CasualtyProfile casualty;
        XRSimpleInteractable _interactable;

        public void Configure(STARTTriageSimulationManager m, CasualtyProfile c) { manager = m; casualty = c; }

        void Awake() { _interactable = GetComponent<XRSimpleInteractable>(); }
        void OnEnable() { _interactable.selectEntered.AddListener(OnSelect); }
        void OnDisable() { _interactable.selectEntered.RemoveListener(OnSelect); }
        void OnSelect(SelectEnterEventArgs _) { if (manager != null && casualty != null) manager.SelectCasualty(casualty); }
    }
}
