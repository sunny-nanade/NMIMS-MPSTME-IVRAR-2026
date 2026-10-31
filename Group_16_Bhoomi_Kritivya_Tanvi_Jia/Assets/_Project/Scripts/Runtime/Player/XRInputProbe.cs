using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace RescueSim.Player
{
    /// <summary>
    /// Diagnostic that proves headset tracking and controller input are arriving (Meta XR Simulator, XR Device Simulator or a real headset).
    /// Logs button edges and a periodic head-pose line with the "[XRProbe]" prefix; exposes the latest state for tests.
    /// </summary>
    public class XRInputProbe : MonoBehaviour
    {
        [SerializeField] bool logEvents = true;
        [SerializeField, Min(1f)] float poseLogInterval = 10f;

        static readonly string[] Buttons = { "triggerPressed", "gripPressed", "primaryButton", "secondaryButton", "thumbstickClicked", "menu" };
        readonly bool[][] _prev = { new bool[Buttons.Length], new bool[Buttons.Length] };
        float _nextPoseLog;
        float _nextUiScan;
        NearFarInteractor[] _interactors = System.Array.Empty<NearFarInteractor>();
        string[] _lastUiHit = new string[0];
        bool _loggedSetup;

        public bool HeadsetPresent { get; private set; }
        public bool HeadsetTracked { get; private set; }
        public Vector3 HeadsetPosition { get; private set; }
        public bool LeftPresent { get; private set; }
        public bool RightPresent { get; private set; }
        public int ButtonEdgeCount { get; private set; }

        void Update()
        {
            var hmd = InputSystem.GetDevice<XRHMD>();
            HeadsetPresent = hmd != null;
            if (hmd != null)
            {
                HeadsetTracked = hmd.isTracked.isPressed;
                HeadsetPosition = hmd.devicePosition.ReadValue();
            }
            else HeadsetTracked = false;

            var left = XRController.leftHand;
            var right = XRController.rightHand;
            LeftPresent = left != null; RightPresent = right != null;
            Poll(left, 0, "LeftHand");
            Poll(right, 1, "RightHand");

            PollUi();

            if (logEvents && Time.unscaledTime >= _nextPoseLog)
            {
                _nextPoseLog = Time.unscaledTime + poseLogInterval;
                Debug.Log($"[XRProbe] HMD present={HeadsetPresent} tracked={HeadsetTracked} pos={HeadsetPosition:F2} | left={LeftPresent} right={RightPresent}");
            }
        }

        /// <summary>Logs which UI object each ray currently hits (only on change), plus a one-time EventSystem summary.</summary>
        float _nextDetail; bool _doDetail;
        void PollUi()
        {
            if (!logEvents) return;
            if (Time.unscaledTime >= _nextUiScan)
            {
                _nextUiScan = Time.unscaledTime + 2f;
                if (_interactors.Length == 0 || System.Array.Exists(_interactors, i => i == null))
                {
                    _interactors = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
                    _lastUiHit = new string[_interactors.Length];
                }
                if (!_loggedSetup && Time.unscaledTime > 3f)
                {
                    _loggedSetup = true;
                    var es = EventSystem.current;
                    Debug.Log($"[XRProbe] UI setup: eventSystem={(es != null)} module={(es != null && es.currentInputModule != null ? es.currentInputModule.GetType().Name : "none")} xrModule={(FindFirstObjectByType<XRUIInputModule>() != null)} " +
                              $"mainCamera={(Camera.main != null)} nearFarInteractors={_interactors.Length} uiRaycasters={FindObjectsByType<TrackedDeviceGraphicRaycaster>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");
                }
            }
            _doDetail = Time.unscaledTime >= _nextDetail;
            if (_doDetail) _nextDetail = Time.unscaledTime + 5f;
            for (int i = 0; i < _interactors.Length; i++)
            {
                var it = _interactors[i];
                if (it == null) continue;
                string hit = "-";
                bool reg = it.TryGetUIModel(out var model);
                if (reg && model.currentRaycast.isValid && model.currentRaycast.gameObject != null)
                    hit = model.currentRaycast.gameObject.name;
                if (_doDetail)
                {
                    var bgo = GameObject.Find("Button_Begin Mission");
                    if (bgo != null)
                    {
                        var g = bgo.GetComponent<UnityEngine.UI.Graphic>();
                        var cv = g.canvas;
                        Debug.Log($"[XRProbe] BeginBtn: depth={g.depth} raycastTarget={g.raycastTarget} cull={g.canvasRenderer.cull} layer={bgo.layer} pos={bgo.transform.position:F2} fwd={bgo.transform.forward:F2} canvasWorldCam={(cv.worldCamera != null)} " +
                                  $"raycaster={(cv.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>() != null)} cgBlocks={cv.GetComponent<CanvasGroup>()?.blocksRaycasts}");
                    }
                    Debug.Log($"[XRProbe] {it.transform.parent?.name}/{it.name} detail: uiEnabled={it.enableUIInteraction} registered={reg} far={it.enableFarCasting} hasSel={it.hasSelection} " +
                              (reg && model.raycastPoints != null && model.raycastPoints.Count > 1 ? $"ray0={model.raycastPoints[0]:F2} rayN={model.raycastPoints[model.raycastPoints.Count-1]:F2} n={model.raycastPoints.Count} " : "noRayPoints ") +
                              (reg ? $"modelPos={model.position:F2} mask={(int)model.raycastLayerMask} type={model.interactionType}" : ""));
                }
                if (hit != _lastUiHit[i])
                {
                    _lastUiHit[i] = hit;
                    Debug.Log($"[XRProbe] {it.transform.parent?.name}/{it.name} UI hit: {hit}");
                }
            }
        }

        void Poll(InputDevice device, int side, string label)
        {
            if (device == null) return;
            for (int i = 0; i < Buttons.Length; i++)
            {
                var b = device[Buttons[i]] as ButtonControl;
                if (b == null) continue;
                bool now = b.isPressed;
                if (now != _prev[side][i])
                {
                    _prev[side][i] = now;
                    ButtonEdgeCount++;
                    if (logEvents) Debug.Log($"[XRProbe] {label} {Buttons[i]} {(now ? "PRESSED" : "released")}");
                }
            }
        }
    }
}
