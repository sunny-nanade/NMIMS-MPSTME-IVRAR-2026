using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>Object the player can grip-grab (tag Grabbable). No distance grab.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        public static readonly List<Grabbable> All = new List<Grabbable>();

        [SerializeField] Material outlineMaterial;
        [SerializeField] bool respawnIfFallen = true;
        [SerializeField] float respawnBelowY = -2f;
        [SerializeField] float respawnBeyondMeters = 6f;

        public bool IsHeld { get; private set; }
        public HandInteractor Holder { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool Hovered { get; private set; }

        public event Action<Grabbable, Hand> Grabbed;
        public event Action<Grabbable, Hand> Released;

        Vector3 _startPos; Quaternion _startRot;
        Renderer[] _renderers;
        Collider[] _colliders;
        bool _wasKinematic;

        public Material OutlineMaterial { get => outlineMaterial; set => outlineMaterial = value; }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            _startPos = transform.position; _startRot = transform.rotation;
            _renderers = GetComponentsInChildren<Renderer>();
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // a thrown or dropped object never tunnels through the floor
            if (outlineMaterial == null && DeepSleepAssets.Instance != null) outlineMaterial = DeepSleepAssets.Instance.outlineMaterial;
            try { gameObject.tag = "Grabbable"; } catch { }
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); if (Hovered) SetHover(false); }

        void Update()
        {
            if (respawnIfFallen && !IsHeld && (transform.position.y < respawnBelowY || (transform.position - _startPos).magnitude > respawnBeyondMeters))
            {
                Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
                transform.SetPositionAndRotation(_startPos, _startRot);
            }
        }

        public float DistanceTo(Vector3 point)
        {
            float best = float.MaxValue;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                Vector3 cp = c.ClosestPoint(point);
                best = Mathf.Min(best, Vector3.Distance(cp, point));
            }
            return best == float.MaxValue ? Vector3.Distance(transform.position, point) : best;
        }

        public void SetHover(bool on)
        {
            if (Hovered == on || outlineMaterial == null) { Hovered = on; return; }
            Hovered = on;
            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                var mats = new List<Material>(r.sharedMaterials);
                if (on) { if (!mats.Contains(outlineMaterial)) mats.Add(outlineMaterial); }
                else mats.Remove(outlineMaterial);
                r.sharedMaterials = mats.ToArray();
            }
        }

        public void BeginGrab(HandInteractor h)
        {
            IsHeld = true; Holder = h;
            _wasKinematic = Body.isKinematic;
            Body.isKinematic = true;
            // a held object must not push the player's capsule or block the teleport arc: its colliders are off while it is in the hand
            _colliders = GetComponentsInChildren<Collider>();
            foreach (var c in _colliders) if (c != null) c.enabled = false;
            Grabbed?.Invoke(this, h.Side);
        }

        public void EndGrab(HandInteractor h, Vector3 velocity)
        {
            IsHeld = false; Holder = null;
            if (_colliders != null) foreach (var c in _colliders) if (c != null) c.enabled = true;
            Body.isKinematic = _wasKinematic;
            if (!Body.isKinematic) { Body.linearVelocity = velocity; }
            Released?.Invoke(this, h.Side);
        }

        /// <summary>Makes the current pose the one ResetToStart and the fall respawn return to.</summary>
        public void RememberPose() { _startPos = transform.position; _startRot = transform.rotation; }

        public void ForceRelease()
        {
            if (Holder != null) Holder.Release();
        }

        public void ResetToStart()
        {
            ForceRelease();
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            transform.SetPositionAndRotation(_startPos, _startRot);
            // keep the rigidbody (and its interpolation) in step with the transform
            Body.position = _startPos; Body.rotation = _startRot;
            Physics.SyncTransforms();
        }
    }
}
