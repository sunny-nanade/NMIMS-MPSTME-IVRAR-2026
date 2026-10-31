using UnityEngine;
using Group10Gestures.Filtering;
using Group10Gestures.Providers;
using Group10Gestures.Recognition;
using Group10Gestures.UI;

namespace Group10Gestures.Core
{
    public enum AppMode
    {
        Edit,
        Color
    }

    /// <summary>
    /// Central orchestrator and singleton for Group 10 Gesture System.
    /// Manages application modes (Edit vs Color), hand tracking lifecycle, and cross-system events.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private GestureConfig _config;

        [Header("Subsystems")]
        [SerializeField] private HandProviderSelector _providerSelector;
        [SerializeField] private HandFilter _handFilter;
        [SerializeField] private GestureRecognizer _gestureRecognizer;
        [SerializeField] private HUDController _hudController;
        [SerializeField] private HandSkeletonRenderer _skeletonRenderer;

        public AppMode CurrentMode { get; private set; } = AppMode.Edit;
        public GestureConfig Config => _config;

        public event System.Action<AppMode> OnModeChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureSubsystems();
        }

        private void EnsureSubsystems()
        {
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<GestureConfig>();
            }

            if (_providerSelector == null)
            {
                _providerSelector = GetComponentInChildren<HandProviderSelector>();
                if (_providerSelector == null) _providerSelector = gameObject.AddComponent<HandProviderSelector>();
            }

            if (_handFilter == null)
            {
                _handFilter = GetComponentInChildren<HandFilter>();
                if (_handFilter == null) _handFilter = gameObject.AddComponent<HandFilter>();
            }

            if (_gestureRecognizer == null)
            {
                _gestureRecognizer = GetComponentInChildren<GestureRecognizer>();
                if (_gestureRecognizer == null) _gestureRecognizer = gameObject.AddComponent<GestureRecognizer>();
            }

            if (_hudController == null)
            {
                _hudController = GetComponentInChildren<HUDController>();
                if (_hudController == null) _hudController = gameObject.AddComponent<HUDController>();
            }

            if (_skeletonRenderer == null)
            {
                _skeletonRenderer = GetComponentInChildren<HandSkeletonRenderer>();
                if (_skeletonRenderer == null) _skeletonRenderer = gameObject.AddComponent<HandSkeletonRenderer>();
            }
        }

        private void Start()
        {
            if (_gestureRecognizer != null)
            {
                _gestureRecognizer.OnBothHandsOpenTriggered += ToggleMode;
            }

            UpdateModeVisuals();
        }

        public void SetMode(AppMode newMode)
        {
            CurrentMode = newMode;
            UpdateModeVisuals();
            OnModeChanged?.Invoke(CurrentMode);
            Debug.Log($"[GameManager] Mode switched to: {CurrentMode}");
        }

        public void ToggleMode()
        {
            SetMode(CurrentMode == AppMode.Edit ? AppMode.Color : AppMode.Edit);
        }

        private void UpdateModeVisuals()
        {
            if (_hudController != null)
            {
                _hudController.SetModeName(CurrentMode == AppMode.Edit ? "EDIT MODE (Room Drawing)" : "COLOR MODE (Palette)");
            }
        }

        private void OnDestroy()
        {
            if (_gestureRecognizer != null)
            {
                _gestureRecognizer.OnBothHandsOpenTriggered -= ToggleMode;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
