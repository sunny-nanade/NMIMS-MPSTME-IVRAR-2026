using RescueSim.Audio;
using RescueSim.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Interaction
{
    /// <summary>Completes an objective the first time the player grabs this object (e.g. the rescue buoy).</summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PickupObjective : MonoBehaviour
    {
        [SerializeField] MissionController mission;
        [SerializeField] string objectiveId = "equip";
        [SerializeField] string message = "";
        [SerializeField] SpatialSound sound;
        [Tooltip("Hidden once picked up (e.g. a floating hint ring around the item).")]
        [SerializeField] GameObject[] hideOnPickup;

        XRGrabInteractable _grab;
        public bool PickedUp { get; private set; }

        public void Configure(MissionController m, string id, string msg) { mission = m; objectiveId = id; message = msg; }

        void Awake() { _grab = GetComponent<XRGrabInteractable>(); }
        void OnEnable() { _grab.selectEntered.AddListener(OnGrabbed); }
        void OnDisable() { _grab.selectEntered.RemoveListener(OnGrabbed); }

        void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs _) => Collect();

        /// <summary>Public so tests (and non-VR fallbacks) can trigger the pickup.</summary>
        public void Collect()
        {
            if (PickedUp) return;
            if (mission != null && mission.State != MissionState.Active) return;
            PickedUp = true;
            if (hideOnPickup != null) foreach (var g in hideOnPickup) if (g != null) g.SetActive(false);
            sound?.PlayOneShot(SpatialSound.Cue.Confirm);
            if (mission != null) mission.CompleteObjective(objectiveId, string.IsNullOrEmpty(message) ? null : message);
        }
    }
}
