using RescueSim.Audio;
using UnityEngine;
using UnityEngine.XR;

namespace RescueSim.Player
{
    /// <summary>Coughing and a gentle haptic pulse as exposure builds. No camera shake or forced movement.</summary>
    public class ExposureFeedback : MonoBehaviour
    {
        [SerializeField] PlayerVitals vitals;
        [SerializeField] SpatialSound cough;
        [SerializeField, Range(0.1f, 0.9f)] float threshold = 0.4f;
        [SerializeField, Min(1f)] float interval = 4f;

        float _timer;
        readonly System.Collections.Generic.List<InputDevice> _devices = new System.Collections.Generic.List<InputDevice>();

        public void Configure(PlayerVitals v, SpatialSound s) { vitals = v; cough = s; }

        void Update()
        {
            if (vitals == null || vitals.Exposure < threshold) { _timer = 0.5f; return; }
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = Mathf.Lerp(interval, interval * 0.4f, Mathf.InverseLerp(threshold, 1f, vitals.Exposure));
            cough?.PlayOneShot(SpatialSound.Cue.Cough);
            Pulse(0.25f + 0.4f * vitals.Exposure, 0.15f);
        }

        void Pulse(float amplitude, float duration)
        {
            _devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, _devices);
            foreach (var d in _devices)
                if (d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse) d.SendHapticImpulse(0, amplitude, duration);
        }
    }
}
