using System;
using UnityEngine;
using Acrophobia.AdaptiveFSM;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Redresses the one room shell so each tier reads as a different place.
    ///
    /// Building five distinct environments would mean five sets of geometry, props and
    /// materials - and until textures exist they would all read as the same grey box
    /// anyway, which defeats the point. Instead the shell is reused and the dressing
    /// changes: props swap, surfaces swap, and what lies beyond the opening is already
    /// differentiated by the catwalk tiers. Bedroom, office, plant room, stairwell, then
    /// the open terrace with no room at all.
    ///
    /// That carries the fiction the hierarchy needs - the participant is being relocated
    /// upward between sessions - without paying for five environments.
    ///
    /// Materials are optional on every entry. Unassigned slots leave the existing surface
    /// alone, so this works with placeholder concrete today and with real textures the
    /// moment they are dropped in, without touching this code.
    /// </summary>
    public class TierEnvironmentDresser : MonoBehaviour
    {
        [Serializable]
        public class TierDressing
        {
            [Tooltip("Label for readability in the inspector and the telemetry.")]
            public string Label = "Untitled";

            [Tooltip("Prop group enabled for this tier. All other groups are disabled.")]
            public GameObject PropGroup;

            [Tooltip("Optional. Leave empty to keep the current floor surface.")]
            public Material FloorMaterial;

            [Tooltip("Optional. Leave empty to keep the current wall surface.")]
            public Material WallMaterial;
        }

        [Tooltip("One entry per tier, in order. Entry 0 is tier 1.")]
        [SerializeField] private TierDressing[] _dressings = new TierDressing[5];

        [Tooltip("Floor pieces repainted when a dressing specifies a floor material.")]
        [SerializeField] private Renderer[] _floorRenderers;

        [Tooltip("Wall pieces repainted when a dressing specifies a wall material.")]
        [SerializeField] private Renderer[] _wallRenderers;

        [SerializeField] private AdaptiveExposureFSM _fsm;
        [SerializeField] private Transform _apartmentRoot;

        private int _appliedTier = -1;

        public int AppliedTier => _appliedTier;

        public string CurrentLabel =>
            _appliedTier >= 1 && _appliedTier <= _dressings.Length
                ? _dressings[_appliedTier - 1].Label
                : "none";

        public event Action<int, string> OnDressingApplied;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced += ApplyDressing;
                _fsm.OnTierSteppedBack += ApplyDressing;
            }
        }

        private void OnDisable()
        {
            if (_fsm != null)
            {
                _fsm.OnTierAdvanced -= ApplyDressing;
                _fsm.OnTierSteppedBack -= ApplyDressing;
            }
        }

        private void Start()
        {
            ApplyDressing(_fsm != null ? _fsm.CurrentTier : 1);
        }

        public void ResolveReferences()
        {
            if (_fsm == null) _fsm = FindAnyObjectByType<AdaptiveExposureFSM>();

            if (_apartmentRoot == null)
            {
                var found = GameObject.Find("Apartment_Interior");
                if (found != null) _apartmentRoot = found.transform;
            }
        }

        /// <summary>
        /// Switches the room to the given tier's dressing. Safe to call repeatedly.
        /// </summary>
        public void ApplyDressing(int tier)
        {
            if (_dressings == null || _dressings.Length == 0)
            {
                return;
            }

            int index = Mathf.Clamp(tier - 1, 0, _dressings.Length - 1);

            // Only one prop group is ever live, so a tier cannot inherit the previous
            // room's furniture.
            for (int i = 0; i < _dressings.Length; i++)
            {
                var d = _dressings[i];
                if (d == null || d.PropGroup == null)
                {
                    continue;
                }

                bool shouldBeActive = i == index;
                if (d.PropGroup.activeSelf != shouldBeActive)
                {
                    d.PropGroup.SetActive(shouldBeActive);
                }
            }

            TierDressing dressing = _dressings[index];
            if (dressing != null)
            {
                ApplyMaterial(_floorRenderers, dressing.FloorMaterial);
                ApplyMaterial(_wallRenderers, dressing.WallMaterial);
            }

            _appliedTier = tier;
            OnDressingApplied?.Invoke(tier, dressing != null ? dressing.Label : "none");
        }

        private static void ApplyMaterial(Renderer[] renderers, Material material)
        {
            // A null material means "leave this surface as it is", which is what keeps
            // the system usable before any textures exist.
            if (material == null || renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterial = material;
                }
            }
        }

        /// <summary>
        /// Rebuilds the renderer lists from the room hierarchy by name convention.
        /// Called from editor tooling after the shell is regenerated.
        /// </summary>
        public void CollectRenderers()
        {
            ResolveReferences();

            if (_apartmentRoot == null)
            {
                return;
            }

            var floors = new System.Collections.Generic.List<Renderer>();
            var walls = new System.Collections.Generic.List<Renderer>();

            foreach (Renderer r in _apartmentRoot.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;

                if (n == "Floor" || n == "Ceiling")
                {
                    floors.Add(r);
                }
                else if (n.StartsWith("Wall_"))
                {
                    walls.Add(r);
                }
            }

            _floorRenderers = floors.ToArray();
            _wallRenderers = walls.ToArray();
        }

        public void SetDressings(TierDressing[] dressings)
        {
            _dressings = dressings;
        }
    }
}
