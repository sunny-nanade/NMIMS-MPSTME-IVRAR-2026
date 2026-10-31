using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Group10Gestures.Core;

namespace Group10Gestures.Providers
{
    /// <summary>
    /// Receives 21 MediaPipe hand landmarks streamed as JSON over UDP from the Python sender.
    /// Operates asynchronous network ingestion on a background thread and marshals to the Unity main thread.
    /// </summary>
    public class WebcamHandProvider : MonoBehaviour, IHandProvider
    {
        public string ProviderName => "Webcam (UDP 5005)";

        [SerializeField] private GestureConfig _config;

        private HandData _leftHand = new HandData(Handedness.Left);
        private HandData _rightHand = new HandData(Handedness.Right);

        // Double buffer for thread safety
        private readonly object _dataLock = new object();
        private HandData _incomingLeft = new HandData(Handedness.Left);
        private HandData _incomingRight = new HandData(Handedness.Right);
        private bool _hasNewData = false;

        private UdpClient _udpClient;
        private Thread _receiveThread;
        private volatile bool _isRunning = false;

        private float _lastLeftPacketTime = -100f;
        private float _lastRightPacketTime = -100f;
        private double _lastPacketTimestampMs = 0;

        public double LatencyMs { get; private set; }

        public void Initialize(GestureConfig config)
        {
            _config = config;
            StartUdpListener();
        }

        private void Start()
        {
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<GestureConfig>();
            }
            if (!_isRunning)
            {
                StartUdpListener();
            }
        }

        private void StartUdpListener()
        {
            if (_isRunning) return;

            int port = _config != null ? _config.udpPort : 5005;
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _isRunning = true;

                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "WebcamHandProvider_UDP"
                };
                _receiveThread.Start();
                Debug.Log($"[WebcamHandProvider] Bound UDP listener on 0.0.0.0:{port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebcamHandProvider] Failed to bind UDP port {port}: {ex.Message}");
            }
        }

        private void ReceiveLoop()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning && _udpClient != null)
            {
                try
                {
                    byte[] rawData = _udpClient.Receive(ref remoteEndPoint);
                    if (rawData == null || rawData.Length == 0) continue;

                    string jsonStr = Encoding.UTF8.GetString(rawData);
                    ParseJsonPayload(jsonStr);
                }
                catch (SocketException sex)
                {
                    if (!_isRunning) break;
                    Debug.LogWarning($"[WebcamHandProvider] Socket error: {sex.Message}");
                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    if (!_isRunning) break;
                    Debug.LogWarning($"[WebcamHandProvider] Parse error: {ex.Message}");
                }
            }
        }

        private class JsonHand
        {
            public string label = null;
            public float[][] lm = null;
        }

        private class JsonPayload
        {
            public double t = 0;
            public JsonHand[] hands = null;
        }

        private void ParseJsonPayload(string jsonStr)
        {
            try
            {
                JsonPayload payload = JsonConvert.DeserializeObject<JsonPayload>(jsonStr);
                if (payload == null || payload.hands == null) return;

                lock (_dataLock)
                {
                    _lastPacketTimestampMs = payload.t;
                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    LatencyMs = Math.Max(0, nowMs - payload.t);

                    bool leftFound = false;
                    bool rightFound = false;

                    foreach (var h in payload.hands)
                    {
                        if (h.lm == null || h.lm.Length < 21) continue;

                        bool isRight = string.Equals(h.label, "Right", StringComparison.OrdinalIgnoreCase);
                        HandData target = isRight ? _incomingRight : _incomingLeft;

                        target.IsTracked = true;
                        target.Timestamp = payload.t;

                        for (int i = 0; i < 21; i++)
                        {
                            float[] coords = h.lm[i];
                            if (coords != null && coords.Length >= 3)
                            {
                                // MediaPipe coords: X right [0,1], Y down [0,1], Z depth
                                float normX = coords[0];
                                float normY = coords[1];
                                float normZ = coords[2];

                                Vector3 scale = _config != null ? _config.webcamWorldScale : new Vector3(1.2f, 0.9f, 1.0f);
                                Vector3 offset = _config != null ? _config.webcamWorldOffset : new Vector3(0f, 0f, 0.7f);

                                // Map normalized coordinates to meters in Unity camera space
                                float unityX = (normX - 0.5f) * scale.x + offset.x;
                                float unityY = -(normY - 0.5f) * scale.y + offset.y;
                                float unityZ = -normZ * scale.z + offset.z;

                                target.Joints[i] = new Vector3(unityX, unityY, unityZ);
                            }
                        }

                        if (isRight) rightFound = true;
                        else leftFound = true;
                    }

                    if (!leftFound) _incomingLeft.IsTracked = false;
                    if (!rightFound) _incomingRight.IsTracked = false;

                    _hasNewData = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebcamHandProvider] JSON parse exception: {ex.Message}");
            }
        }

        public void UpdateProvider()
        {
            float now = Time.time;
            float timeout = _config != null ? _config.trackingTimeoutSec : 0.5f;

            lock (_dataLock)
            {
                if (_hasNewData)
                {
                    if (_incomingLeft.IsTracked)
                    {
                        _leftHand.CopyFrom(_incomingLeft);
                        _lastLeftPacketTime = now;
                    }
                    if (_incomingRight.IsTracked)
                    {
                        _rightHand.CopyFrom(_incomingRight);
                        _lastRightPacketTime = now;
                    }
                    _hasNewData = false;
                }
            }

            // Check tracking timeout
            if (now - _lastLeftPacketTime > timeout)
            {
                _leftHand.IsTracked = false;
            }
            if (now - _lastRightPacketTime > timeout)
            {
                _rightHand.IsTracked = false;
            }
        }

        public bool IsTracked(Handedness handedness)
        {
            return handedness == Handedness.Left ? _leftHand.IsTracked : _rightHand.IsTracked;
        }

        public Vector3[] GetJoints(Handedness handedness)
        {
            HandData hand = handedness == Handedness.Left ? _leftHand : _rightHand;
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return hand.Joints;

            // Transform into world space
            Vector3[] worldJoints = new Vector3[21];
            for (int i = 0; i < 21; i++)
            {
                worldJoints[i] = cam.TransformPoint(hand.Joints[i]);
            }
            return worldJoints;
        }

        public Vector3 GetJoint(Handedness handedness, int jointIndex)
        {
            if (jointIndex < 0 || jointIndex >= 21) return Vector3.zero;
            HandData hand = handedness == Handedness.Left ? _leftHand : _rightHand;
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return hand.Joints[jointIndex];

            return cam.TransformPoint(hand.Joints[jointIndex]);
        }

        public double GetTimestamp(Handedness handedness)
        {
            return handedness == Handedness.Left ? _leftHand.Timestamp : _rightHand.Timestamp;
        }

        public void Shutdown()
        {
            _isRunning = false;
            if (_udpClient != null)
            {
                try { _udpClient.Close(); } catch { }
                _udpClient = null;
            }
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                try { _receiveThread.Abort(); } catch { }
                _receiveThread = null;
            }
            Debug.Log("[WebcamHandProvider] Shutdown complete.");
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }
    }
}
