using UnityEngine;

namespace Fractured_Final
{
    /// <summary>One of the six coolant ports on the rack (tag Port). Its forward axis points out of the rack face.</summary>
    public class CoolantPort : MonoBehaviour
    {
        [SerializeField] int index = 1;
        public int Index { get => index; set => index = value; }
        public CoolantCable Occupant { get; set; }
        public bool Occupied => Occupant != null;
    }
}
