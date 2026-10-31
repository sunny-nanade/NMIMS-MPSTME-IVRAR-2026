using System;
using RescueSim.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Interaction
{
    /// <summary>
    /// The facility's main power breaker. Grab/select the lever to cut power; electrical hazards react to <see cref="PowerCut"/>.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class MainBreaker : MonoBehaviour
    {
        [SerializeField] Transform lever;
        [SerializeField] Vector3 leverDownEuler = new Vector3(70f, 0f, 0f);
        [SerializeField, Min(0.05f)] float throwSeconds = 0.35f;
        [SerializeField] SpatialSound sound;
        [SerializeField] Renderer indicator;
        [SerializeField] Color liveColor = new Color(0.1f, 1f, 0.2f);
        [SerializeField] Color deadColor = new Color(0.5f, 0.05f, 0.05f);

        XRSimpleInteractable _interactable;
        Quaternion _upRot;
        Quaternion _downRot;
        float _throw;
        MaterialPropertyBlock _mpb;

        public bool IsLive { get; private set; } = true;
        public event Action PowerCut;

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            _mpb = new MaterialPropertyBlock();
            if (lever != null)
            {
                _upRot = lever.localRotation;
                _downRot = _upRot * Quaternion.Euler(leverDownEuler);
            }
            ApplyIndicator();
        }

        void OnEnable() => _interactable.selectEntered.AddListener(OnSelect);
        void OnDisable() => _interactable.selectEntered.RemoveListener(OnSelect);

        void OnSelect(SelectEnterEventArgs _) => Cut();

        /// <summary>Cuts main power (idempotent).</summary>
        public void Cut()
        {
            if (!IsLive) return;
            IsLive = false;
            sound?.PlayOneShot(SpatialSound.Cue.Clunk);
            ApplyIndicator();
            PowerCut?.Invoke();
        }

        void Update()
        {
            if (lever == null) return;
            float target = IsLive ? 0f : 1f;
            if (Mathf.Approximately(_throw, target)) return;
            _throw = Mathf.MoveTowards(_throw, target, Time.unscaledDeltaTime / throwSeconds);
            lever.localRotation = Quaternion.Slerp(_upRot, _downRot, Mathf.SmoothStep(0f, 1f, _throw));
        }

        void ApplyIndicator()
        {
            if (indicator == null) return;
            var c = IsLive ? liveColor : deadColor;
            indicator.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c * 2f);
            indicator.SetPropertyBlock(_mpb);
        }
    }
}
