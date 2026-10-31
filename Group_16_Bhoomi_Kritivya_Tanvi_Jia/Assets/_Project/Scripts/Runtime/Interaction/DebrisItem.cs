using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RescueSim.Interaction
{
    /// <summary>A heavy-ish loose object (barrel, cabinet) that can be grabbed and moved out of the collapsed passage.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public class DebrisItem : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float massKg = 18f;

        void Awake()
        {
            var rb = GetComponent<Rigidbody>();
            rb.mass = massKg;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}
