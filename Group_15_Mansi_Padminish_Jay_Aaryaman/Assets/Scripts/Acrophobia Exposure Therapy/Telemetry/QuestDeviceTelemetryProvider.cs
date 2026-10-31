using System;
using UnityEngine;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// Captures Meta Quest 2 hardware, tracking, and controller telemetry without GC allocations.
    /// Provides real-time data for HMD motion dynamics (velocity, acceleration), controller tracking,
    /// button/trigger inputs, and device status (FPS, battery, tracking health).
    /// </summary>
    public class QuestDeviceTelemetryProvider : MonoBehaviour
    {
        [Header("Tracking References")]
        [SerializeField] private Transform _centerEyeAnchor;
        [SerializeField] private Transform _leftControllerAnchor;
        [SerializeField] private Transform _rightControllerAnchor;

        // --- HMD Dynamics ---
        private Vector3 _prevHeadPos;
        private Vector3 _headLinearVelocity;
        private Vector3 _prevHeadVelocity;
        private Vector3 _headAcceleration;
        private Vector3 _headAngularVelocity;
        private Vector3 _prevHeadEuler;
        private bool _hasPrevHeadPose;

        // --- Controller Dynamics ---
        private Vector3 _prevLeftPos;
        private Vector3 _leftControllerVelocity;
        private Vector3 _prevRightPos;
        private Vector3 _rightControllerVelocity;
        private bool _hasPrevControllerPos;

        // --- Performance & Battery Metrics ---
        private float _fpsAccumulator;
        private int _fpsFrameCount;
        private float _currentFps;
        private float _fpsUpdateTimer;
        private const float FpsUpdateInterval = 0.5f;

        private float _batteryLevel = -1f;
        private int _batteryStatusInt = 0; // 0=Unknown, 1=Charging, 2=Discharging, 3=NotCharging, 4=Full
        private float _batteryCheckTimer;
        private const float BatteryCheckInterval = 5f;

        // --- Public Properties for Telemetry Sampling ---
        public Vector3 HeadLinearVelocity => _headLinearVelocity;
        public float HeadSpeed => _headLinearVelocity.magnitude;
        public Vector3 HeadAcceleration => _headAcceleration;
        public float HeadAccelerationMagnitude => _headAcceleration.magnitude;
        public Vector3 HeadAngularVelocity => _headAngularVelocity;

        public Vector3 LeftControllerPosition => _leftControllerAnchor != null ? _leftControllerAnchor.position : Vector3.zero;
        public Vector3 LeftControllerRotation => _leftControllerAnchor != null ? _leftControllerAnchor.eulerAngles : Vector3.zero;
        public Vector3 LeftControllerVelocity => _leftControllerVelocity;

        public Vector3 RightControllerPosition => _rightControllerAnchor != null ? _rightControllerAnchor.position : Vector3.zero;
        public Vector3 RightControllerRotation => _rightControllerAnchor != null ? _rightControllerAnchor.eulerAngles : Vector3.zero;
        public Vector3 RightControllerVelocity => _rightControllerVelocity;

        public float LeftIndexTrigger { get; private set; }
        public float LeftGripTrigger { get; private set; }
        public Vector2 LeftThumbstick { get; private set; }
        public bool LeftPrimaryButton { get; private set; } // X button on Quest 2
        public bool LeftSecondaryButton { get; private set; } // Y button on Quest 2

        public float RightIndexTrigger { get; private set; }
        public float RightGripTrigger { get; private set; }
        public Vector2 RightThumbstick { get; private set; }
        public bool RightPrimaryButton { get; private set; } // A button on Quest 2
        public bool RightSecondaryButton { get; private set; } // B button on Quest 2

        public float CurrentFps => _currentFps;
        public float BatteryLevel => _batteryLevel;
        public int BatteryStatus => _batteryStatusInt;
        public bool IsUserPresent { get; private set; } = true;
        public bool IsHmdTrackingValid { get; private set; } = true;
        public bool IsLeftControllerConnected { get; private set; }
        public bool IsRightControllerConnected { get; private set; }

        private void Awake()
        {
            ResolveReferences();
            UpdateBattery();
        }

        public void ResolveReferences()
        {
            if (_centerEyeAnchor == null)
            {
                var cam = Camera.main;
                if (cam != null) _centerEyeAnchor = cam.transform;
                else
                {
                    var found = GameObject.Find("CenterEyeAnchor");
                    if (found != null) _centerEyeAnchor = found.transform;
                }
            }

            if (_leftControllerAnchor == null)
            {
                var left = GameObject.Find("LeftControllerAnchor");
                if (left != null) _leftControllerAnchor = left.transform;
                else
                {
                    var leftHand = GameObject.Find("LeftHandAnchor");
                    if (leftHand != null) _leftControllerAnchor = leftHand.transform;
                }
            }

            if (_rightControllerAnchor == null)
            {
                var right = GameObject.Find("RightControllerAnchor");
                if (right != null) _rightControllerAnchor = right.transform;
                else
                {
                    var rightHand = GameObject.Find("RightHandAnchor");
                    if (rightHand != null) _rightControllerAnchor = rightHand.transform;
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0.00001f) return;

            UpdateHmdDynamics(dt);
            UpdateControllerDynamics(dt);
            UpdateInputs();
            UpdatePerformance(dt);
            UpdateTrackingStatus();
        }

        private void UpdateHmdDynamics(float dt)
        {
            if (_centerEyeAnchor == null) return;

            Vector3 currentPos = _centerEyeAnchor.position;
            Vector3 currentEuler = _centerEyeAnchor.eulerAngles;

            if (_hasPrevHeadPose)
            {
                Vector3 newVelocity = (currentPos - _prevHeadPos) / dt;
                _headAcceleration = (newVelocity - _prevHeadVelocity) / dt;
                _headLinearVelocity = newVelocity;
                _prevHeadVelocity = newVelocity;

                // Angular velocity with 360 wrap-around protection
                float dx = Mathf.DeltaAngle(_prevHeadEuler.x, currentEuler.x) / dt;
                float dy = Mathf.DeltaAngle(_prevHeadEuler.y, currentEuler.y) / dt;
                float dz = Mathf.DeltaAngle(_prevHeadEuler.z, currentEuler.z) / dt;
                _headAngularVelocity = new Vector3(dx, dy, dz);
            }
            else
            {
                _hasPrevHeadPose = true;
                _headLinearVelocity = Vector3.zero;
                _headAcceleration = Vector3.zero;
                _headAngularVelocity = Vector3.zero;
                _prevHeadVelocity = Vector3.zero;
            }

            _prevHeadPos = currentPos;
            _prevHeadEuler = currentEuler;
        }

        private void UpdateControllerDynamics(float dt)
        {
            if (_leftControllerAnchor != null)
            {
                Vector3 leftPos = _leftControllerAnchor.position;
                if (_hasPrevControllerPos)
                {
                    _leftControllerVelocity = (leftPos - _prevLeftPos) / dt;
                }
                _prevLeftPos = leftPos;
            }

            if (_rightControllerAnchor != null)
            {
                Vector3 rightPos = _rightControllerAnchor.position;
                if (_hasPrevControllerPos)
                {
                    _rightControllerVelocity = (rightPos - _prevRightPos) / dt;
                }
                _prevRightPos = rightPos;
            }

            _hasPrevControllerPos = true;
        }

        private void UpdateInputs()
        {
            try
            {
                // Left Touch Controller
                LeftIndexTrigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
                LeftGripTrigger = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger);
                LeftThumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                LeftPrimaryButton = OVRInput.Get(OVRInput.Button.Three); // X
                LeftSecondaryButton = OVRInput.Get(OVRInput.Button.Four); // Y
                IsLeftControllerConnected = OVRInput.IsControllerConnected(OVRInput.Controller.LTouch);

                // Right Touch Controller
                RightIndexTrigger = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);
                RightGripTrigger = OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger);
                RightThumbstick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                RightPrimaryButton = OVRInput.Get(OVRInput.Button.One); // A
                RightSecondaryButton = OVRInput.Get(OVRInput.Button.Two); // B
                IsRightControllerConnected = OVRInput.IsControllerConnected(OVRInput.Controller.RTouch);
            }
            catch (Exception)
            {
                // Graceful fallback if OVRInput is unavailable in mock simulator
            }
        }

        private void UpdatePerformance(float dt)
        {
            _fpsAccumulator += 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            _fpsFrameCount++;
            _fpsUpdateTimer += dt;

            if (_fpsUpdateTimer >= FpsUpdateInterval)
            {
                _currentFps = _fpsAccumulator / Mathf.Max(1, _fpsFrameCount);
                _fpsAccumulator = 0f;
                _fpsFrameCount = 0;
                _fpsUpdateTimer = 0f;
            }

            _batteryCheckTimer += dt;
            if (_batteryCheckTimer >= BatteryCheckInterval)
            {
                _batteryCheckTimer = 0f;
                UpdateBattery();
            }
        }

        private void UpdateBattery()
        {
            try
            {
                _batteryLevel = SystemInfo.batteryLevel;
                switch (SystemInfo.batteryStatus)
                {
                    case UnityEngine.BatteryStatus.Charging: _batteryStatusInt = 1; break;
                    case UnityEngine.BatteryStatus.Discharging: _batteryStatusInt = 2; break;
                    case UnityEngine.BatteryStatus.NotCharging: _batteryStatusInt = 3; break;
                    case UnityEngine.BatteryStatus.Full: _batteryStatusInt = 4; break;
                    default: _batteryStatusInt = 0; break;
                }
            }
            catch (Exception)
            {
                _batteryLevel = -1f;
                _batteryStatusInt = 0;
            }
        }

        private void UpdateTrackingStatus()
        {
            try
            {
                IsUserPresent = OVRPlugin.userPresent;
                IsHmdTrackingValid = OVRNodeStateProperties.IsHmdPresent();
            }
            catch (Exception)
            {
                IsUserPresent = true;
                IsHmdTrackingValid = true;
            }
        }
    }
}
