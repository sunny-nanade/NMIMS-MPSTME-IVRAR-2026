using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Group10Gestures.Core;

namespace Group10Gestures.Providers
{
    /// <summary>
    /// Auto-detects runtime hand-tracking capability at startup.
    /// Selects Meta XR Hand Tracking if an XR runtime/simulator is present, or falls back to Webcam MediaPipe.
    /// Supports runtime force-switching with the F1 key.
    /// Acts as an IHandProvider proxy forwarding tracking queries to the active provider.
    /// </summary>
    public class HandProviderSelector : MonoBehaviour, IHandProvider
    {
        public enum ProviderType
        {
            MetaXR,
            Webcam
        }

        [Header("Configuration")]
        [SerializeField] private GestureConfig _config;
        [SerializeField] private ProviderType _initialProvider = ProviderType.Webcam;
        [SerializeField] private bool _autoDetectAtStartup = true;

        [Header("Provider References")]
        [SerializeField] private MetaXRHandProvider _metaXRProvider;
        [SerializeField] private WebcamHandProvider _webcamProvider;

        public event Action<IHandProvider> OnProviderChanged;

        private IHandProvider _activeProvider;
        public IHandProvider ActiveProvider => _activeProvider;
        public ProviderType CurrentType { get; private set; }

        public string ProviderName => _activeProvider != null ? _activeProvider.ProviderName : "None";

        private void Awake()
        {
            EnsureProvidersExist();
        }

        private void Start()
        {
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<GestureConfig>();
            }

            if (_autoDetectAtStartup)
            {
                bool xrAvailable = CheckXrHandTrackingAvailable();
                if (xrAvailable)
                {
                    SwitchProvider(ProviderType.MetaXR);
                }
                else
                {
                    Debug.Log("[HandProviderSelector] No XR hand tracking detected at startup. Auto-falling back to Webcam MediaPipe.");
                    SwitchProvider(ProviderType.Webcam);
                }
            }
            else
            {
                SwitchProvider(_initialProvider);
            }
        }

        private void EnsureProvidersExist()
        {
            if (_metaXRProvider == null)
            {
                _metaXRProvider = GetComponent<MetaXRHandProvider>();
                if (_metaXRProvider == null) _metaXRProvider = gameObject.AddComponent<MetaXRHandProvider>();
            }

            if (_webcamProvider == null)
            {
                _webcamProvider = GetComponent<WebcamHandProvider>();
                if (_webcamProvider == null) _webcamProvider = gameObject.AddComponent<WebcamHandProvider>();
            }
        }

        private bool CheckXrHandTrackingAvailable()
        {
            try
            {
                if (OVRPlugin.GetHandTrackingEnabled() || (OVRManager.instance != null && OVRManager.isHmdPresent))
                {
                    Debug.Log("[HandProviderSelector] Meta XR Hand Tracking runtime detected!");
                    return true;
                }
            }
            catch (Exception)
            {
                // Native plugin not present or runtime absent
            }

            return false;
        }

        private void Update()
        {
            // Keyboard F1 force-switch (Unity New Input System)
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                ToggleProvider();
            }

            if (_activeProvider != null)
            {
                _activeProvider.UpdateProvider();
            }
        }

        public void ToggleProvider()
        {
            ProviderType target = CurrentType == ProviderType.MetaXR ? ProviderType.Webcam : ProviderType.MetaXR;
            SwitchProvider(target);
        }

        public void SwitchProvider(ProviderType type)
        {
            CurrentType = type;
            EnsureProvidersExist();

            if (type == ProviderType.MetaXR)
            {
                _activeProvider = _metaXRProvider;
                _metaXRProvider.enabled = true;
            }
            else
            {
                _activeProvider = _webcamProvider;
                _webcamProvider.enabled = true;
            }

            _activeProvider.Initialize(_config);
            Debug.Log($"[HandProviderSelector] Switched active provider to: {_activeProvider.ProviderName}");
            OnProviderChanged?.Invoke(_activeProvider);
        }

        #region IHandProvider Proxy Forwarding

        public void Initialize(GestureConfig config)
        {
            _config = config;
            if (_activeProvider != null) _activeProvider.Initialize(config);
        }

        public void UpdateProvider()
        {
            if (_activeProvider != null) _activeProvider.UpdateProvider();
        }

        public bool IsTracked(Handedness handedness)
        {
            return _activeProvider != null && _activeProvider.IsTracked(handedness);
        }

        public Vector3[] GetJoints(Handedness handedness)
        {
            return _activeProvider != null ? _activeProvider.GetJoints(handedness) : new Vector3[21];
        }

        public Vector3 GetJoint(Handedness handedness, int jointIndex)
        {
            return _activeProvider != null ? _activeProvider.GetJoint(handedness, jointIndex) : Vector3.zero;
        }

        public double GetTimestamp(Handedness handedness)
        {
            return _activeProvider != null ? _activeProvider.GetTimestamp(handedness) : 0;
        }

        public void Shutdown()
        {
            if (_metaXRProvider != null) _metaXRProvider.Shutdown();
            if (_webcamProvider != null) _webcamProvider.Shutdown();
        }

        #endregion
    }
}
