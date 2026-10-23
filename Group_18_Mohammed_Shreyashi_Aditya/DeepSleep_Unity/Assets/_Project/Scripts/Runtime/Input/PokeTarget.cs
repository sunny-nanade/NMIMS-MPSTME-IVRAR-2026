using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>A pressable surface. Pressed when a PokeTip enters its collider (controller or fingertip poke).</summary>
    public class PokeTarget : MonoBehaviour
    {
        [SerializeField] float cooldown = 0.25f;
        public event Action<PokeTarget, Hand> Poked;
        public bool Interactable = true;
        float _last = -10f;

        void OnTriggerEnter(Collider other)
        {
            var tip = other.GetComponent<PokeTip>();
            if (tip == null) return;
            Press(tip.Side);
        }

        public void Press(Hand h)
        {
            if (!Interactable || Time.unscaledTime - _last < cooldown) return;
            _last = Time.unscaledTime;
            Poked?.Invoke(this, h);
            var ctrl = HandInteractor.For(h);
            if (ctrl != null) ctrl.Pulse(0.3f, 0.05f);
        }
    }
}
