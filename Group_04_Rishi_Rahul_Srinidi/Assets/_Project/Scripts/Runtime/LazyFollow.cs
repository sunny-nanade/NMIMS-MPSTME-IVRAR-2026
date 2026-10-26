using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Yaw-only lazy follow used for HUD toasts and captions so text stays readable without being glued to the face.
    public class LazyFollow : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0, -0.15f, 0.9f);
        public float speed = 6f;

        void LateUpdate()
        {
            if (!target) return;
            var fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var pos = target.position + rot * offset;
            float k = 1 - Mathf.Exp(-speed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, pos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(transform.position - target.position, Vector3.up), k);
        }

        public void Snap()
        {
            if (!target) return;
            var fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            transform.position = target.position + rot * offset;
            transform.rotation = Quaternion.LookRotation(transform.position - target.position, Vector3.up);
        }
    }
}
