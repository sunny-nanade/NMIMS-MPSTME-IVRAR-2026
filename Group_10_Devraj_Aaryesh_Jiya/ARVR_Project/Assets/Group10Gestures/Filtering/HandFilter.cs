using UnityEngine;
using Group10Gestures.Core;
using Group10Gestures.Providers;

namespace Group10Gestures.Filtering
{
    /// <summary>
    /// Smooths 21 joints for Left and Right hands using 42 independent One Euro Filters.
    /// Provides noise-free joint positions to the GestureRecognizer and DrawingController.
    /// </summary>
    public class HandFilter : MonoBehaviour
    {
        [SerializeField] private GestureConfig _config;
        [SerializeField] private HandProviderSelector _providerSelector;

        private OneEuroFilterVector3[] _leftFilters = new OneEuroFilterVector3[21];
        private OneEuroFilterVector3[] _rightFilters = new OneEuroFilterVector3[21];

        private Vector3[] _filteredLeftJoints = new Vector3[21];
        private Vector3[] _filteredRightJoints = new Vector3[21];

        private bool _leftWasTracked = false;
        private bool _rightWasTracked = false;

        private void Awake()
        {
            // Fallback values (this component is added at runtime, so _config is normally null).
            // beta is in 1/(m/s): with joint positions in metres a value of 0.005 never raises the cutoff,
            // which made the hand feel ~0.5 s late. 1.5 Hz at rest / beta 25 gives ~9 Hz cutoff at 0.3 m/s.
            float minCutoff = _config != null ? _config.filterMinCutoff : 1.5f;
            float beta = _config != null ? _config.filterBeta : 25.0f;
            float dCutoff = _config != null ? _config.filterDCutoff : 1.0f;

            for (int i = 0; i < 21; i++)
            {
                _leftFilters[i] = new OneEuroFilterVector3(minCutoff, beta, dCutoff);
                _rightFilters[i] = new OneEuroFilterVector3(minCutoff, beta, dCutoff);
                _filteredLeftJoints[i] = Vector3.zero;
                _filteredRightJoints[i] = Vector3.zero;
            }
        }

        private void Start()
        {
            if (_providerSelector == null)
            {
                _providerSelector = FindAnyObjectByType<HandProviderSelector>();
            }
        }

        private void Update()
        {
            if (_providerSelector == null) return;

            float now = Time.time;
            UpdateFilterParams();

            // Process Left Hand
            bool leftTracked = _providerSelector.IsTracked(Handedness.Left);
            if (leftTracked)
            {
                if (!_leftWasTracked)
                {
                    ResetFilters(_leftFilters);
                }

                Vector3[] rawLeft = _providerSelector.GetJoints(Handedness.Left);
                for (int i = 0; i < 21; i++)
                {
                    _filteredLeftJoints[i] = _leftFilters[i].Filter(rawLeft[i], now);
                }
            }
            _leftWasTracked = leftTracked;

            // Process Right Hand
            bool rightTracked = _providerSelector.IsTracked(Handedness.Right);
            if (rightTracked)
            {
                if (!_rightWasTracked)
                {
                    ResetFilters(_rightFilters);
                }

                Vector3[] rawRight = _providerSelector.GetJoints(Handedness.Right);
                for (int i = 0; i < 21; i++)
                {
                    _filteredRightJoints[i] = _rightFilters[i].Filter(rawRight[i], now);
                }
            }
            _rightWasTracked = rightTracked;
        }

        private void UpdateFilterParams()
        {
            if (_config == null) return;
            for (int i = 0; i < 21; i++)
            {
                _leftFilters[i].UpdateParams(_config.filterMinCutoff, _config.filterBeta, _config.filterDCutoff);
                _rightFilters[i].UpdateParams(_config.filterMinCutoff, _config.filterBeta, _config.filterDCutoff);
            }
        }

        private void ResetFilters(OneEuroFilterVector3[] filters)
        {
            for (int i = 0; i < filters.Length; i++)
            {
                filters[i].Reset();
            }
        }

        public bool IsTracked(Handedness handedness)
        {
            return _providerSelector != null && _providerSelector.IsTracked(handedness);
        }

        public Vector3[] GetFilteredJoints(Handedness handedness)
        {
            return handedness == Handedness.Left ? _filteredLeftJoints : _filteredRightJoints;
        }

        public Vector3 GetFilteredJoint(Handedness handedness, int jointIndex)
        {
            if (jointIndex < 0 || jointIndex >= 21) return Vector3.zero;
            return handedness == Handedness.Left ? _filteredLeftJoints[jointIndex] : _filteredRightJoints[jointIndex];
        }
    }
}
