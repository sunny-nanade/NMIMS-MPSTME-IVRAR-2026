using System;
using System.Collections.Generic;
using RescueSim.Hazards;
using UnityEngine;

namespace RescueSim.Player
{
    /// <summary>
    /// Tracks the player's exposure to smoke, fumes and electrical hazards (0 = clean, 1 = overcome).
    /// Exposure is a game abstraction for training feedback; it is not a medical model.
    /// </summary>
    public class PlayerVitals : MonoBehaviour
    {
        [Tooltip("Tracked head transform (the XR camera).")]
        [SerializeField] Transform head;
        [SerializeField, Range(0f, 0.2f)] float recoveryPerSecond = 0.04f;

        readonly List<ExposureSource> _sources = new List<ExposureSource>();
        bool _overcome;

        public float Exposure { get; private set; }
        public float PeakExposure { get; private set; }
        public bool IsTracking { get; set; }

        public event Action<float> ExposureChanged;
        public event Action Overcome;

        public Transform Head => head;
        public float RecoveryPerSecond { get => recoveryPerSecond; set => recoveryPerSecond = Mathf.Max(0f, value); }

        public void SetHead(Transform t) => head = t;

        public void Register(ExposureSource s) { if (s != null && !_sources.Contains(s)) _sources.Add(s); }
        public void Unregister(ExposureSource s) => _sources.Remove(s);

        /// <summary>Total exposure inflow per second at a world position from all active sources.</summary>
        public float SampleRate(Vector3 worldPosition)
        {
            float total = 0f;
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i] != null) total += _sources[i].RateAt(worldPosition);
            return total;
        }

        /// <summary>Adds a one-off exposure spike, e.g. an electric shock.</summary>
        public void AddInstant(float amount)
        {
            if (_overcome || amount <= 0f) return;
            SetExposure(Exposure + amount);
        }

        public void ResetVitals()
        {
            _overcome = false;
            PeakExposure = 0f;
            SetExposure(0f);
        }

        void Update()
        {
            if (!IsTracking || _overcome) return;
            float inflow = head != null ? SampleRate(head.position) : 0f;
            float delta = inflow > 0f ? inflow * Time.deltaTime : -recoveryPerSecond * Time.deltaTime;
            SetExposure(Exposure + delta);
        }

        void SetExposure(float value)
        {
            float v = Mathf.Clamp01(value);
            bool changed = !Mathf.Approximately(v, Exposure);
            Exposure = v;
            if (v > PeakExposure) PeakExposure = v;
            if (changed) ExposureChanged?.Invoke(Exposure);
            if (Exposure >= 1f && !_overcome)
            {
                _overcome = true;
                Overcome?.Invoke();
            }
        }
    }
}
