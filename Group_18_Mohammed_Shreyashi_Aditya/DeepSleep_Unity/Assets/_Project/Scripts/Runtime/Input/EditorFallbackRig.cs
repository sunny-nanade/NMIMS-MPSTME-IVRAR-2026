using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Used only when no headset is present (Editor, bot, tests). Puts the camera at EYE_HEIGHT above the floor,
    /// applies mouse/arrow look, moves the hand anchors to the virtual hands, and shows a key legend.
    /// Never active on a headset; the whole body is compiled out of device builds (the empty class stays so PF_PlayerRig keeps its component).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class EditorFallbackRig : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] float eyeHeight = DS.EYE_HEIGHT;
        [SerializeField] bool showLegend = true;
        [SerializeField] float pitchLimit = 80f;

        PlayerRig _rig;
        float _pitch;
        float _yaw;

        static readonly string[] Legend =
        {
            "EDITOR FALLBACK RIG",
            "WASD  move (free-walk)",
            "Alt/MMB + mouse or arrows  look",
            "Q / R  snap turn 45",
            "Hold E, release  teleport (aim with mouse)",
            "Left click  trigger / poke / ray click",
            "Right click (hold)  grip / grab",
            "F  menu (pause)    F10  experimenter menu",
        };

        bool _ovrDisabled;

        void Awake() { _rig = GetComponent<PlayerRig>(); }

        void DisableOvrTracking()
        {
            // no headset: stop OVRCameraRig from writing a simulated head pose over the fallback camera height
            if (_ovrDisabled) return;
            var cr = GetComponentInChildren<OVRCameraRig>(true);
            var _ = _rig.Head; var __ = _rig.LeftHand; var ___ = _rig.RightHand; // cache anchors before removing the component
            if (cr != null) Destroy(cr);
            _ovrDisabled = true;
        }

        void OnEnable() { Application.onBeforeRender += EnforceHeight; }
        void OnDisable() { Application.onBeforeRender -= EnforceHeight; }

        // last word before rendering: nothing else may change the camera height in fallback mode
        void EnforceHeight()
        {
            if (_rig == null || !Active || _rig.Head == null || !_ovrDisabled) return;
            var p = _rig.Head.localPosition;
            if (Mathf.Abs(p.y - eyeHeight) > 0.0001f) _rig.Head.localPosition = new Vector3(0f, eyeHeight, 0f);
        }

        bool Active => InputService.Exists && InputService.Instance.CurrentSource != InputService.Source.Headset;

        void LateUpdate()
        {
            if (_rig == null || _rig.Head == null || !Active) return;
            var input = InputService.Instance;
            DisableOvrTracking();
            if (_rig.Head.parent != null) _rig.Head.parent.localPosition = Vector3.zero;

            if (!(PauseManager.Instance != null && PauseManager.Instance.IsPaused))
            {
                Vector2 look = input.FallbackLook;
                if (look.sqrMagnitude > 0f)
                {
                    _rig.RotateAroundHead(look.x);
                    _pitch = Mathf.Clamp(_pitch - look.y, -pitchLimit, pitchLimit);
                }
            }
            // camera at natural standing eye height above the floor (rig root is on the floor)
            _rig.Head.localPosition = new Vector3(0f, eyeHeight, 0f);
            _rig.Head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            if (_rig.RightHand != null)
            {
                _rig.RightHand.SetPositionAndRotation(input.HandPosition(Hand.Right), input.HandRotation(Hand.Right));
            }
            if (_rig.LeftHand != null)
            {
                _rig.LeftHand.SetPositionAndRotation(input.HandPosition(Hand.Left), input.HandRotation(Hand.Left));
            }
        }

        void OnGUI()
        {
            if (!showLegend || !Active || !Application.isEditor) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            float y = 6;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(4, 4, 330, 18 * Legend.Length + 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            foreach (var l in Legend) { GUI.Label(new Rect(8, y, 320, 20), l, style); y += 18; }
        }
#endif
    }
}
