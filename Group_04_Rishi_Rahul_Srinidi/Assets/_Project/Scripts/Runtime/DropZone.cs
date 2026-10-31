using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Trigger volume that accepts some item kinds and bounces the rest back with an explanation.
    public class DropZone : MonoBehaviour
    {
        public string zoneName = "Zone";
        public ItemKind[] accepts;
        public Transform intake;
        public event Action<GrabItem> Accepted;

        void OnTriggerStay(Collider other)
        {
            var item = other.GetComponentInParent<GrabItem>();
            if (!item || item.Consumed || item.Deposited || item.IsHeld || !item.EverHeld) return;
            if (Array.IndexOf(accepts, item.kind) >= 0)
            {
                Accepted?.Invoke(item);
            }
            else if (item.CanBeRejected)
            {
                var head = GameUI.I && GameUI.I.head ? GameUI.I.head.position : transform.position + Vector3.back;
                item.Eject(GetComponent<Collider>().bounds, head);
                if (item.kind != ItemKind.Prop)
                    GameManager.I?.Mistake(string.IsNullOrEmpty(item.wrongZoneHint) ? $"That doesn't belong in the {zoneName}." : item.wrongZoneHint);
            }
        }
    }
}
