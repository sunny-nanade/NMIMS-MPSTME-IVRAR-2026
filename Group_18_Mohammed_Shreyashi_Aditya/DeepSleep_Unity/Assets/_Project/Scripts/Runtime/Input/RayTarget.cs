using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>A surface the controller ray can click or drag (UI buttons, sliders). Layer Interactable.</summary>
    public class RayTarget : MonoBehaviour
    {
        public bool Interactable = true;
        public event Action<RayTarget, Vector3> Down;
        public event Action<RayTarget, Vector3> Drag;
        public event Action<RayTarget, Vector3> Up;
        public event Action<RayTarget, bool> HoverChanged;

        public bool Hovered { get; private set; }
        public void RaiseDown(Vector3 p) { if (Interactable) Down?.Invoke(this, p); }
        public void RaiseDrag(Vector3 p) { if (Interactable) Drag?.Invoke(this, p); }
        public void RaiseUp(Vector3 p) { if (Interactable) Up?.Invoke(this, p); }
        public void SetHover(bool h) { if (Hovered != h) { Hovered = h; HoverChanged?.Invoke(this, h); } }
    }
}
