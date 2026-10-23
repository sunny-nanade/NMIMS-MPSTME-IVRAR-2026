using UnityEngine;

namespace Fractured_Final
{
    /// <summary>One signal beacon of the beacon game (a grabbable coloured barrel). Built by EarCheckController.</summary>
    public class BeaconVisual : MonoBehaviour
    {
        [SerializeField] int beaconIndex = 1;
        [SerializeField] string colorName = "Red";
        [SerializeField] Color tint = Color.red;

        public int Index { get => beaconIndex; set => beaconIndex = value; }
        public string ColorName { get => colorName; set => colorName = value; }
        public Color Tint { get => tint; set => tint = value; }
        /// <summary>Slot number (1 to 6) the beacon is locked into, or 0 while it is loose.</summary>
        public int PlacedSlot { get; set; }
        public Grabbable Grabbable { get; set; }
    }
}
