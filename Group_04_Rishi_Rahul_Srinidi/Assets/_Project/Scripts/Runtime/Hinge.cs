using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Hinged lid / door that swings open once.
    public class Hinge : MonoBehaviour
    {
        public Vector3 openEuler = new Vector3(-100, 0, 0);
        public float duration = 0.8f;
        public bool IsOpen { get; private set; }
        Quaternion closed;

        void Awake() => closed = transform.localRotation;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Sfx.PlayAt(Sfx.I ? Sfx.I.open : null, transform.position);
            StartCoroutine(Swing(closed * Quaternion.Euler(openEuler)));
        }

        IEnumerator Swing(Quaternion target)
        {
            Quaternion from = transform.localRotation;
            for (float t = 0; t < 1; t += Time.deltaTime / duration)
            {
                transform.localRotation = Quaternion.Slerp(from, target, 1 - Mathf.Pow(1 - t, 3));
                yield return null;
            }
            transform.localRotation = target;
        }
    }
}
