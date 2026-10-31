using System.Collections.Generic;
using RescueSim.Audio;
using RescueSim.Core;
using UnityEngine;

namespace RescueSim.Interaction
{
    /// <summary>
    /// Watches a box-shaped passage. When no debris item is left inside it, the "route" objective is completed.
    /// Polling at a low rate keeps this cheap on mobile hardware.
    /// </summary>
    public class PassageMonitor : MonoBehaviour
    {
        [SerializeField] MissionController mission;
        [SerializeField] string objectiveId = ObjectiveIds.Route;
        [SerializeField] Vector3 size = new Vector3(6f, 3f, 2.5f);
        [SerializeField] List<DebrisItem> debris = new List<DebrisItem>();
        [SerializeField, Min(0.05f)] float pollInterval = 0.25f;
        [SerializeField] GameObject[] hideWhenCleared;
        [SerializeField] SpatialSound sound;

        float _timer;
        public bool IsCleared { get; private set; }
        public int ItemsInside { get; private set; }

        public void Configure(MissionController m, IEnumerable<DebrisItem> items)
        {
            mission = m; debris = new List<DebrisItem>(items);
        }

        public bool Contains(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            Vector3 h = size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        void Update()
        {
            if (IsCleared) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = pollInterval;
            Evaluate();
        }

        /// <summary>Counts debris inside the passage; completes the objective when it reaches zero. Public for tests.</summary>
        public void Evaluate()
        {
            int n = 0;
            for (int i = 0; i < debris.Count; i++)
                if (debris[i] != null && Contains(debris[i].transform.position)) n++;
            ItemsInside = n;
            bool canComplete = mission == null || mission.State == MissionState.Active;
            if (n == 0 && !IsCleared && canComplete)
            {
                IsCleared = true;
                if (hideWhenCleared != null) foreach (var g in hideWhenCleared) if (g != null) g.SetActive(false);
                sound?.PlayOneShot(SpatialSound.Cue.Success);
                if (mission != null) mission.CompleteObjective(objectiveId, "Passage cleared. The route north is open.");
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
