using System;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Cosmetic oxygen counter. 20 min. Never ends the session; at zero displays flash and nothing else happens.</summary>
    public class OxygenSystem : MonoBehaviour
    {
        public static OxygenSystem Instance { get; private set; }

        [SerializeField] float totalSeconds = 20f * 60f;

        public float Remaining { get; private set; }
        public float Fraction => totalSeconds > 0 ? Remaining / totalSeconds : 0f;
        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        public bool Depleted => Remaining <= 0f;
        public event Action Emptied;
        bool _emptyRaised;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Remaining = totalSeconds;
        }

        void OnEnable() { StartCoroutine(Hook()); }

        System.Collections.IEnumerator Hook()
        {
            while (SessionManager.Instance == null) yield return null;
            SessionManager.Instance.StageChanged -= OnStage;
            SessionManager.Instance.StageChanged += OnStage;
        }

        void OnStage(Stage from, Stage to)
        {
            if (to == Stage.Awakening && !Running) StartCountdown();
        }

        public void StartCountdown() { Running = true; Paused = false; }

        public void ResetTimer() { Remaining = totalSeconds; Running = false; Paused = false; _emptyRaised = false; }

        public void Pause() { Paused = true; }
        public void Resume() { Paused = false; }

        void Update()
        {
            if (!Running || Paused || Depleted) return;
            Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
            if (Depleted && !_emptyRaised)
            {
                _emptyRaised = true;
                DS.Log("Oxygen display reached zero (cosmetic only, nothing happens)");
                Emptied?.Invoke();
            }
        }

        public string Formatted => $"{(int)(Remaining / 60f):00}:{(int)(Remaining % 60f):00}";
    }
}
