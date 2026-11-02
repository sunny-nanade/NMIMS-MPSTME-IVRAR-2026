using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusQuest
{
    public class InteractableObject : MonoBehaviour
    {
        public string prompt = "Read information";

        [TextArea]
        public string information;

        public LiftController lift;
        public bool cabinPanel;
        public MenuBoard menuBoard;
        public Coupon coupon;
        public ServingCounter servingCounter;
        public FoodTray foodTray;
        public FacilityInformationPanel facilityPanel;
        public MissionEventInteractable missionEvent;
        public HingedCampusDoor campusDoor;
        public CampusServiceInteractable service;

        private XRSimpleInteractable xrInteractable;

        private void Awake()
        {
            xrInteractable = GetComponent<XRSimpleInteractable>();

            if (xrInteractable == null)
                xrInteractable = gameObject.AddComponent<XRSimpleInteractable>();

            xrInteractable.selectEntered.AddListener(OnXRSelectEntered);
        }

        private void OnDestroy()
        {
            if (xrInteractable != null)
                xrInteractable.selectEntered.RemoveListener(OnXRSelectEntered);
        }

        private void OnXRSelectEntered(SelectEnterEventArgs args)
        {
            Use();
        }

        public void Use()
        {
            if (service != null)
            {
                service.Use();
                return;
            }

            if (campusDoor != null)
            {
                campusDoor.Toggle();
                return;
            }

            if (facilityPanel != null)
            {
                facilityPanel.Open();
                return;
            }

            if (missionEvent != null)
            {
                missionEvent.Open();
                return;
            }

            if (menuBoard != null)
            {
                menuBoard.Open();
                return;
            }

            if (coupon != null)
            {
                coupon.Take();
                return;
            }

            if (servingCounter != null)
            {
                servingCounter.HandOver();
                return;
            }

            if (foodTray != null)
            {
                foodTray.Take();
                return;
            }

            if (lift != null)
            {
                if (cabinPanel)
                    lift.RequestPanel();
                else
                    lift.Call();

                return;
            }

            DesktopHUD.Instance.ShowInfo(prompt, information);
        }
    }
}