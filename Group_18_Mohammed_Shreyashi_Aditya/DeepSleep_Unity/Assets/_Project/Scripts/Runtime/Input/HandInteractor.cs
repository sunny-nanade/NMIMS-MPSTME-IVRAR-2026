using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Per-hand interaction: grab (grip, near only), poke tip, UI ray.</summary>
    public class HandInteractor : MonoBehaviour
    {
        static readonly HandInteractor[] Instances = new HandInteractor[2];
        public static HandInteractor For(Hand h) => Instances[(int)h];

        public Hand Side;
        [SerializeField] float grabRadius = 0.15f;
        [SerializeField] float fallbackHoverRange = 2.5f;
        [SerializeField] float rayRange = 6f;
        [SerializeField] float pokeRadius = 0.012f;

        public Grabbable Hovered { get; private set; }
        public Grabbable Held { get; private set; }
        public RayTarget RayHover { get; private set; }

        Transform _tip;
        Rigidbody _tipBody;
        Vector3 _holdOffsetPos; Quaternion _holdOffsetRot;
        Vector3 _lastHandPos; Vector3 _vel;
        RayTarget _rayDown;
        LineRenderer _ray;
        float _holdDistance;
        int _interactableMask;

        void Awake()
        {
            Instances[(int)Side] = this;
            var tip = new GameObject("PokeTip_" + Side);
            tip.transform.SetParent(transform.parent, false);
            tip.layer = 2; // Ignore Raycast: the poke tip must never block the UI ray
            _tip = tip.transform;
            var sc = tip.AddComponent<SphereCollider>(); sc.isTrigger = true; sc.radius = pokeRadius;
            _tipBody = tip.AddComponent<Rigidbody>(); _tipBody.isKinematic = true; _tipBody.useGravity = false;
            tip.AddComponent<PokeTip>().Side = Side;

            var rgo = new GameObject("UIRay_" + Side);
            rgo.transform.SetParent(transform, false);
            _ray = rgo.AddComponent<LineRenderer>();
            _ray.widthMultiplier = 0.004f; _ray.positionCount = 2; _ray.useWorldSpace = true;
            _ray.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.6f, 0.95f, 1f) };
            _ray.enabled = false;
            _interactableMask = LayerMask.GetMask("Interactable");
        }

        void OnDestroy() { if (Instances[(int)Side] == this) Instances[(int)Side] = null; }

        void LateUpdate()
        {
            if (!InputService.Exists) return;
            var input = InputService.Instance;
            bool paused = PauseManager.Instance != null && PauseManager.Instance.IsPaused;
            if (paused) { UpdateRay(input); return; }

            Vector3 hp = input.HandPosition(Side);
            if (Time.deltaTime > 0) _vel = Vector3.Lerp(_vel, (hp - _lastHandPos) / Time.deltaTime, 0.3f);
            _lastHandPos = hp;
            _tip.position = hp; _tipBody.position = hp;

            UpdateHover(input, hp);

            if (Held == null)
            {
                if (input.GripDown(Side) && Hovered != null) Grab(Hovered, input, hp);
            }
            else
            {
                if (input.GripUp(Side) || !input.Grip(Side)) Release();
                else
                {
                    Held.transform.position = hp + input.HandRotation(Side) * _holdOffsetPos;
                    Held.transform.rotation = input.HandRotation(Side) * _holdOffsetRot;
                }
            }
            UpdateRay(input);
        }

        void UpdateHover(InputService input, Vector3 hp)
        {
            Grabbable best = null; float bd = grabRadius;
            if (Held == null)
            {
                foreach (var g in Grabbable.All)
                {
                    if (g == null || g.IsHeld) continue;
                    float d = g.DistanceTo(hp);
                    if (d < bd) { bd = d; best = g; }
                }
                if (best == null && (input.IsFallback))
                {
                    if (Physics.Raycast(input.RayOrigin(Side), input.RayDirection(Side), out var hit, fallbackHoverRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        var g = hit.collider.GetComponentInParent<Grabbable>();
                        if (g != null && !g.IsHeld) best = g;
                    }
                }
            }
            if (best != Hovered)
            {
                if (Hovered != null) Hovered.SetHover(false);
                Hovered = best;
                if (Hovered != null) Hovered.SetHover(true);
            }
        }

        void Grab(Grabbable g, InputService input, Vector3 hp)
        {
            Held = g;
            g.SetHover(false);
            Quaternion inv = Quaternion.Inverse(input.HandRotation(Side));
            _holdOffsetPos = inv * (g.transform.position - hp);
            _holdOffsetRot = inv * g.transform.rotation;
            g.BeginGrab(this);
            if (input.IsFallback) input.SetFallbackHold(Vector3.Distance(input.RayOrigin(Side), hp));
            Pulse(0.4f, 0.06f);
            SfxEvents.Raise(SfxEvent.Grab);
        }

        public void Release()
        {
            if (Held == null) return;
            var g = Held; Held = null;
            if (InputService.Exists) InputService.Instance.SetFallbackHold(-1f);
            g.EndGrab(this, _vel);
            SfxEvents.Raise(SfxEvent.Release);
        }

        void UpdateRay(InputService input)
        {
            Vector3 o = input.RayOrigin(Side), d = input.RayDirection(Side);
            RayTarget hitT = null; Vector3 hitP = o + d * rayRange;
            if (Physics.Raycast(o, d, out var hit, rayRange, _interactableMask | (1 << 0), QueryTriggerInteraction.Collide))
            {
                hitT = hit.collider.GetComponentInParent<RayTarget>();
                if (hitT != null) hitP = hit.point;
            }
            if (_rayDown != null)
            {
                // while dragging keep projecting onto the target plane
                var plane = new Plane(_rayDown.transform.forward, _rayDown.transform.position);
                if (plane.Raycast(new Ray(o, d), out float en)) hitP = o + d * en;
            }
            if (hitT != RayHover)
            {
                if (RayHover != null) RayHover.SetHover(false);
                RayHover = hitT;
                if (RayHover != null) RayHover.SetHover(true);
            }
            bool showRay = RayHover != null || _rayDown != null;
            _ray.enabled = showRay;
            if (showRay) { _ray.SetPosition(0, o); _ray.SetPosition(1, hitP); }

            if (input.TriggerDown(Side) && RayHover != null && Held == null)
            {
                _rayDown = RayHover;
                _rayDown.RaiseDown(hitP);
            }
            else if (_rayDown != null && input.Trigger(Side)) _rayDown.RaiseDrag(hitP);
            else if (_rayDown != null && !input.Trigger(Side))
            {
                _rayDown.RaiseUp(hitP);
                _rayDown = null;
            }
        }

        public void Pulse(float amplitude, float seconds)
        {
            Haptics.Pulse(Side, amplitude, seconds);
        }
    }
}
