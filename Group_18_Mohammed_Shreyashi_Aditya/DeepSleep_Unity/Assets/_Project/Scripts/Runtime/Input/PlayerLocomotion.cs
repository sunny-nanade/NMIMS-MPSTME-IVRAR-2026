using System;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Teleport (right stick forward then release), 45 degree snap turn, and smooth move (SCN_SmokeRoom only).
    /// Changes only X/Z/yaw of the rig root. Lives on the same object as the CharacterController.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerLocomotion : MonoBehaviour
    {
        [SerializeField] bool teleportEnabled = true;
        [SerializeField] bool smoothMoveEnabled = false;
        [SerializeField] float snapTurnDegrees = 45f;
        [SerializeField] float maxMoveSpeed = 1.4f;
        [SerializeField] float stickThreshold = 0.7f;
        [SerializeField] float teleportRange = 12f;
        [SerializeField] float arcSpeed = 8f;
        [SerializeField] float stepDistance = 0.7f;
                [SerializeField] bool editorFreeWalk = true;
        [SerializeField] Material vignetteMaterial;
        [SerializeField, Range(0f, 1f)] float vignetteMax = 0.75f;
        [SerializeField] float vignetteSmoothing = 6f;

        public bool TeleportEnabled { get => teleportEnabled; set => teleportEnabled = value; }
        public bool SmoothMoveEnabled { get => smoothMoveEnabled; set => smoothMoveEnabled = value; }
        public int TeleportCount { get; private set; }
        public float DistanceMoved { get; private set; }
        public int WallContacts { get; private set; }

        public event Action<Vector3> Teleported;
        public event Action Stepped;
        public event Action WallTouched;

        CharacterController _cc;
        PlayerRig _rig;
        LineRenderer _line;
        GameObject _marker;
        bool _aiming;
        bool _hasTarget;
        Vector3 _target;
        bool _turnLatch;
        float _stepAccum;
        float _vig;
        float _lastWallTime;
        Renderer _vigRenderer;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _rig = GetComponent<PlayerRig>();
            _cc.radius = 0.2f; _cc.height = 1.6f; _cc.skinWidth = 0.02f; _cc.stepOffset = 0.1f;
            BuildAimVisuals();
            BuildVignette();
        }

        void BuildAimVisuals()
        {
            var go = new GameObject("TeleportAim");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.widthMultiplier = 0.015f;
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _line.material.color = new Color(0.4f, 0.9f, 1f);
            _line.enabled = false;
            _marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // primitive: teleport marker (KNOWN_ISSUES)
            _marker.name = "TeleportMarker";
            Destroy(_marker.GetComponent<Collider>());
            _marker.transform.SetParent(transform, false);
            _marker.transform.localScale = new Vector3(0.5f, 0.005f, 0.5f);
            var mr = _marker.GetComponent<Renderer>();
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.4f, 0.9f, 1f) };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _marker.SetActive(false);
        }

        void BuildVignette()
        {
            if (vignetteMaterial == null || _rig == null || _rig.Head == null) return;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); // primitive: comfort vignette quad (KNOWN_ISSUES)
            q.name = "ComfortVignette";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(_rig.Head, false);
            q.transform.localPosition = new Vector3(0, 0, 0.12f);
            q.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            _vigRenderer = q.GetComponent<Renderer>();
            _vigRenderer.sharedMaterial = vignetteMaterial;
            _vigRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _vigRenderer.enabled = false;
            _mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (!InputService.Exists || _rig == null || _rig.Head == null) return;
            var input = InputService.Instance;
            var pause = PauseManager.Instance;
            if (pause != null && pause.IsPaused) { HideAim(); return; }

            Vector2 rs = input.Thumbstick(Hand.Right);

            // follow the head with the collision capsule (room-scale)
            Vector3 hl = transform.InverseTransformPoint(_rig.Head.position);
            _cc.center = new Vector3(hl.x, _cc.height * 0.5f + _cc.skinWidth, hl.z);

            HandleSnapTurn(rs);
            HandleTeleport(input, rs);
            HandleSmoothMove(input);
            UpdateVignette();
        }

        void HandleSnapTurn(Vector2 rs)
        {
            if (Mathf.Abs(rs.y) > stickThreshold) { return; }
            if (Mathf.Abs(rs.x) > stickThreshold)
            {
                if (!_turnLatch)
                {
                    _turnLatch = true;
                    _rig.RotateAroundHead(Mathf.Sign(rs.x) * snapTurnDegrees);
                }
            }
            else if (Mathf.Abs(rs.x) < 0.3f) _turnLatch = false;
        }

        void HandleTeleport(InputService input, Vector2 rs)
        {
            if (!teleportEnabled) { HideAim(); return; }
            bool pushed = rs.y > stickThreshold;
            if (pushed)
            {
                _aiming = true;
                ComputeAim(input);
                ShowAim(input);
            }
            else if (_aiming)
            {
                _aiming = false;
                if (_hasTarget)
                {
                    _rig.TeleportHeadTo(_target);
                    TeleportCount++;
                    DS.Log($"Teleport #{TeleportCount} to {_target}");
                    SfxEvents.Raise(SfxEvent.Teleport, _target);
                    Teleported?.Invoke(_target);
                }
                HideAim();
            }
        }

        void ComputeAim(InputService input)
        {
            _hasTarget = false;
            Vector3 o = input.RayOrigin(Hand.Right);
            Vector3 d = input.RayDirection(Hand.Right);
            if (input.IsFallback || input.IsScripted)
            {
                if (Physics.Raycast(o, d, out var hit, teleportRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && IsValidFloor(hit))
                { _hasTarget = true; _target = hit.point; }
                return;
            }
            Vector3 p = o; Vector3 v = d * arcSpeed;
            const float dt = 0.04f;
            for (int i = 0; i < 60; i++)
            {
                Vector3 np = p + v * dt;
                v += Physics.gravity * dt;
                if (Physics.Linecast(p, np, out var h, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (IsValidFloor(h)) { _hasTarget = true; _target = h.point; }
                    return;
                }
                p = np;
            }
        }

        bool IsValidFloor(RaycastHit hit)
        {
            if (hit.normal.y < 0.7f) return false;
            if (hit.collider.CompareTag("Wall")) return false;
            if (Mathf.Abs(hit.point.y - transform.position.y) > 0.15f) return false; // floor level only: not table tops, beds or racks
            Vector3 d = hit.point - transform.position; d.y = 0;
            if (d.magnitude > teleportRange) return false;
            // never land inside a prop: the capsule must be free at the target
            Vector3 head = _rig != null && _rig.Head != null ? _rig.Head.position - transform.position : Vector3.zero;
            Vector3 p0 = hit.point + Vector3.up * 0.3f, p1 = hit.point + Vector3.up * 1.5f;
            foreach (var c in Physics.OverlapCapsule(p0, p1, 0.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<Grabbable>() != null) continue; // a cup or cable on the floor does not block a teleport
                if (c.gameObject.layer == 2) continue;
                return false;
            }
            return true;
        }

        void ShowAim(InputService input)
        {
            _line.enabled = true;
            Vector3 o = input.RayOrigin(Hand.Right);
            _line.SetPosition(0, o);
            _line.SetPosition(1, _hasTarget ? _target : o + input.RayDirection(Hand.Right) * 3f);
            _marker.SetActive(_hasTarget);
            if (_hasTarget) _marker.transform.position = _target + Vector3.up * 0.01f;
        }

        void HideAim()
        {
            if (_line != null) _line.enabled = false;
            if (_marker != null) _marker.SetActive(false);
        }

        void HandleSmoothMove(InputService input)
        {
            bool allowed = smoothMoveEnabled || (editorFreeWalk && input.IsFallback && Application.isEditor && !smoothLockedOutsideSmoke);
            Vector2 mv = input.Thumbstick(Hand.Left);
            if (!allowed || mv.sqrMagnitude < 0.01f) { _stepAccum = 0f; return; }
            if (!smoothMoveEnabled && !input.IsFallback) return;

            Vector3 fwd = _rig.Head.forward; fwd.y = 0; fwd.Normalize();
            Vector3 right = _rig.Head.right; right.y = 0; right.Normalize();
            Vector3 dir = (fwd * mv.y + right * mv.x);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            Vector3 delta = dir * (maxMoveSpeed * Time.deltaTime);
            Vector3 before = transform.position;
            _cc.Move(delta);
            Vector3 moved = transform.position - before; moved.y = 0;
            // keep on the floor plane
            transform.position = new Vector3(transform.position.x, before.y, transform.position.z);
            DistanceMoved += moved.magnitude;
            _stepAccum += moved.magnitude;
            if (_stepAccum >= stepDistance) { _stepAccum = 0f; SfxEvents.Raise(SfxEvent.Footstep, transform.position); Stepped?.Invoke(); }
        }

        /// <summary>Editor convenience walking is limited to scenes where teleport is on only when set false here.</summary>
        [SerializeField] bool smoothLockedOutsideSmoke = false;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider.CompareTag("Wall") || hit.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (Time.time - _lastWallTime > 0.5f)
                {
                    _lastWallTime = Time.time;
                    WallContacts++;
                    SfxEvents.Raise(SfxEvent.WallContact, hit.point);
                    WallTouched?.Invoke();
                }
            }
        }

        void UpdateVignette()
        {
            if (_vigRenderer == null) return;
            float target = 0f;
            if (smoothMoveEnabled && InputService.Instance.Thumbstick(Hand.Left).sqrMagnitude > 0.01f) target = vignetteMax;
            _vig = Mathf.Lerp(_vig, target, Time.deltaTime * vignetteSmoothing);
            _vigRenderer.enabled = _vig > 0.01f;
            _vigRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Strength", _vig);
            _vigRenderer.SetPropertyBlock(_mpb);
        }

        public void ResetCounters() { TeleportCount = 0; DistanceMoved = 0; WallContacts = 0; }

        public void ConfigureForScene(bool smoke)
        {
            smoothMoveEnabled = smoke;
            teleportEnabled = !smoke;
        }
    }
}
