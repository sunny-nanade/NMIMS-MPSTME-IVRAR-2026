using RescueSim.Core;
using UnityEngine;

namespace RescueSim.Interaction
{
    /// <summary>
    /// Completes an objective when the player's head enters a box-shaped zone (e.g. "enter the building").
    /// Pure containment test, no physics, polled at a low rate.
    /// </summary>
    public class ZoneObjective : MonoBehaviour
    {
        [SerializeField] MissionController mission;
        [SerializeField] Transform head;
        [SerializeField] string objectiveId = "enter";
        [SerializeField] Vector3 size = new Vector3(3f, 3f, 3f);
        [SerializeField] string message = "";
        [SerializeField, Min(0.05f)] float pollInterval = 0.2f;
        [Tooltip("Optional objective that must already be complete (keeps the checklist in a sensible order).")]
        [SerializeField] string requiresObjective = "";

        float _timer;
        public bool Reached { get; private set; }

        public void Configure(MissionController m, Transform h, string id, Vector3 boxSize, string msg, string requires = "")
        {
            mission = m; head = h; objectiveId = id; size = boxSize; message = msg; requiresObjective = requires;
        }

        public bool Contains(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            Vector3 h = size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        void Update()
        {
            if (Reached || mission == null || head == null || mission.State != MissionState.Active) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = pollInterval;
            if (!string.IsNullOrEmpty(requiresObjective) && mission.Objectives != null && !mission.Objectives.IsComplete(requiresObjective)) return;
            Evaluate(head.position);
        }

        /// <summary>Public for tests: treat <paramref name="position"/> as the player's head position.</summary>
        public void Evaluate(Vector3 position)
        {
            if (Reached || mission == null) return;
            if (!Contains(position)) return;
            Reached = true;
            mission.CompleteObjective(objectiveId, string.IsNullOrEmpty(message) ? null : message);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
