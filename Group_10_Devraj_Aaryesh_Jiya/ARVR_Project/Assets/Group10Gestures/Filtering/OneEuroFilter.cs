using System;
using UnityEngine;

namespace Group10Gestures.Filtering
{
    /// <summary>
    /// Low-pass filter with adaptive cutoff frequency designed by Casiez, Roussel, and Vogel (CHI 2012).
    /// Effectively eliminates high-frequency jitter at low speeds while eliminating lag during fast movements.
    /// </summary>
    public class OneEuroFilter
    {
        private bool _firstTime = true;
        private float _minCutoff;
        private float _beta;
        private float _dCutoff;
        private float _xPrev;
        private float _dxPrev;
        private float _tPrev;

        public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.005f, float dCutoff = 1.0f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
            _firstTime = true;
        }

        public void UpdateParams(float minCutoff, float beta, float dCutoff)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
        }

        public void Reset()
        {
            _firstTime = true;
        }

        private static float Alpha(float rate, float cutoff)
        {
            float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
            float te = 1.0f / rate;
            return 1.0f / (1.0f + tau / te);
        }

        public float Filter(float x, float rate = 60.0f)
        {
            if (_firstTime)
            {
                _firstTime = false;
                _xPrev = x;
                _dxPrev = 0.0f;
                return x;
            }

            // Estimate the current derivative
            float dx = (x - _xPrev) * rate;
            float edx = Mathf.Lerp(_dxPrev, dx, Alpha(rate, _dCutoff));
            _dxPrev = edx;

            // Use the derivative to adapt the cutoff frequency
            float cutoff = _minCutoff + _beta * Mathf.Abs(edx);

            // Filter the given value
            float xFilt = Mathf.Lerp(_xPrev, x, Alpha(rate, cutoff));
            _xPrev = xFilt;

            return xFilt;
        }
    }

    /// <summary>
    /// Vector3 wrapper around three independent OneEuroFilters (X, Y, Z).
    /// </summary>
    public class OneEuroFilterVector3
    {
        private OneEuroFilter _filterX;
        private OneEuroFilter _filterY;
        private OneEuroFilter _filterZ;
        private float _lastTime = -1f;

        public OneEuroFilterVector3(float minCutoff = 1.0f, float beta = 0.005f, float dCutoff = 1.0f)
        {
            _filterX = new OneEuroFilter(minCutoff, beta, dCutoff);
            _filterY = new OneEuroFilter(minCutoff, beta, dCutoff);
            _filterZ = new OneEuroFilter(minCutoff, beta, dCutoff);
        }

        public void UpdateParams(float minCutoff, float beta, float dCutoff)
        {
            _filterX.UpdateParams(minCutoff, beta, dCutoff);
            _filterY.UpdateParams(minCutoff, beta, dCutoff);
            _filterZ.UpdateParams(minCutoff, beta, dCutoff);
        }

        public void Reset()
        {
            _filterX.Reset();
            _filterY.Reset();
            _filterZ.Reset();
            _lastTime = -1f;
        }

        public Vector3 Filter(Vector3 input, float currentTime)
        {
            float rate = 60.0f;
            if (_lastTime > 0f)
            {
                float dt = currentTime - _lastTime;
                if (dt > 0.0001f)
                {
                    rate = 1.0f / dt;
                }
            }
            _lastTime = currentTime;

            float x = _filterX.Filter(input.x, rate);
            float y = _filterY.Filter(input.y, rate);
            float z = _filterZ.Filter(input.z, rate);

            return new Vector3(x, y, z);
        }
    }
}
