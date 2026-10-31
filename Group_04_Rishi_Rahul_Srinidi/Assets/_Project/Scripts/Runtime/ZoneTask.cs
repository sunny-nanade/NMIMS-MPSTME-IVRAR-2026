using UnityEngine;

namespace CSER
{
    /// Completes a task when an accepted item is dropped into the zone (e.g. the implant into the evidence bag).
    [RequireComponent(typeof(DropZone))]
    public class ZoneTask : MonoBehaviour
    {
        public TaskId task;

        void Start()
        {
            var zone = GetComponent<DropZone>();
            zone.Accepted += item =>
            {
                item.Deposit(zone.GetComponent<Collider>().bounds);
                Sfx.PlayAt(Sfx.I ? Sfx.I.drop : null, item.transform.position);
                GameManager.I.Complete(task);
            };
        }
    }
}
