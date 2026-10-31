using System;
using UnityEngine;

namespace RescueSim.Hazards
{
    /// <summary>Base class for anything the player must neutralise (fire, gas leak, live electrics).</summary>
    public abstract class Hazard : MonoBehaviour
    {
        [SerializeField] string hazardId = "hazard";
        [SerializeField] string displayName = "Hazard";
        [Tooltip("Mission objective completed when this hazard is neutralised.")]
        [SerializeField] string objectiveId = "";

        public string HazardId => hazardId;
        public string DisplayName => displayName;
        public string ObjectiveId => objectiveId;

        /// <summary>True while the hazard is still dangerous.</summary>
        public bool IsActive { get; protected set; } = true;

        public event Action<Hazard> Neutralized;

        protected void RaiseNeutralized()
        {
            if (!IsActive) return;
            IsActive = false;
            Neutralized?.Invoke(this);
        }

        /// <summary>Forces the hazard off (used by interactions and tests).</summary>
        public abstract void Neutralize();
    }
}
