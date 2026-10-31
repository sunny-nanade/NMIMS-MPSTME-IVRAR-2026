using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSER
{
    /// Point-and-trigger target (ISDK RayInteractable + InteractableUnityEventWrapper added by the scene builder).
    public class Clickable : MonoBehaviour
    {
        [TextArea] public string caption;
        public UnityEvent onClick = new UnityEvent();
        InteractableUnityEventWrapper wrapper;

        void Awake()
        {
            wrapper = GetComponentInChildren<InteractableUnityEventWrapper>(true);
            if (wrapper) wrapper.WhenSelect.AddListener(Click);
        }

        public void Click()
        {
            Sfx.Haptic(0.3f, 0.04f);
            if (!string.IsNullOrEmpty(caption)) GameUI.I?.Caption(caption, Palette.Ink);
            onClick.Invoke();
        }
    }
}
