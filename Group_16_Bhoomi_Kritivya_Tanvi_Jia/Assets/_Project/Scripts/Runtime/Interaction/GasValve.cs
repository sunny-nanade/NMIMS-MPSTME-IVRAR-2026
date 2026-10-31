using RescueSim.Audio;
using RescueSim.Hazards;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Interaction
{
    /// <summary>Hold the grip on the valve to turn it closed. Releasing early lets pressure push it back open a little.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class GasValve : MonoBehaviour
    {
        [SerializeField] GasLeakHazard gasLeak;
        [SerializeField] Transform wheel;
        [SerializeField, Min(0.5f)] float secondsToClose = 3f;
        [SerializeField, Min(0f)] float slipPerSecond = 0.25f;
        [SerializeField] float totalTurnDegrees = 540f;
        [SerializeField] SpatialSound sound;

        XRSimpleInteractable _interactable;
        bool _holding;
        float _tickTimer;
        Quaternion _startRot;

        public float Progress { get; private set; }
        public bool IsClosed => Progress >= 1f;

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            if (wheel != null) _startRot = wheel.localRotation;
        }

        void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelect);
            _interactable.selectExited.AddListener(OnDeselect);
        }

        void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelect);
            _interactable.selectExited.RemoveListener(OnDeselect);
        }

        void OnSelect(SelectEnterEventArgs _) { _holding = true; }
        void OnDeselect(SelectExitEventArgs _) { _holding = false; }

        public void Bind(GasLeakHazard hazard) => gasLeak = hazard;

        void Update()
        {
            if (IsClosed) return;
            if (_holding)
            {
                Progress = Mathf.Min(1f, Progress + Time.deltaTime / secondsToClose);
                _tickTimer -= Time.deltaTime;
                if (_tickTimer <= 0f) { _tickTimer = 0.45f; sound?.PlayOneShot(SpatialSound.Cue.Click); }
                if (Progress >= 1f) Close();
            }
            else if (Progress > 0f)
            {
                Progress = Mathf.Max(0f, Progress - slipPerSecond * Time.deltaTime);
            }
            if (gasLeak != null) gasLeak.SetPressure(1f - Progress);
            if (wheel != null) wheel.localRotation = _startRot * Quaternion.Euler(0f, 0f, -totalTurnDegrees * Progress);
        }

        void Close()
        {
            sound?.PlayOneShot(SpatialSound.Cue.Clunk);
            if (gasLeak != null) gasLeak.Neutralize();
        }
    }
}
