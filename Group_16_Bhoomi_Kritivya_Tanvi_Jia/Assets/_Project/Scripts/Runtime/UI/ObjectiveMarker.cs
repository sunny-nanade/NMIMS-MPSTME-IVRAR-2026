using RescueSim.Core;
using TMPro;
using UnityEngine;

namespace RescueSim.UI
{
    /// <summary>
    /// A floating diamond + label above something the player needs to use. Visible only while its objective is pending
    /// and the mission is active; fades out as the player gets close so it never blocks the interaction.
    /// </summary>
    public class ObjectiveMarker : MonoBehaviour
    {
        [SerializeField] MissionController mission;
        [SerializeField] Transform head;
        [SerializeField] string objectiveId;
        [SerializeField] CanvasGroup group;
        [SerializeField] Transform diamond;
        [SerializeField] TMP_Text label;
        [SerializeField, Min(0.5f)] float hideWithin = 1.3f;
        [SerializeField, Min(2f)] float showBeyond = 3f;
        [SerializeField, Min(5f)] float maxDistance = 45f;

        Vector3 _basePos;

        public void Configure(MissionController m, Transform h, string objective, CanvasGroup g, Transform d, TMP_Text l)
        {
            mission = m; head = h; objectiveId = objective; group = g; diamond = d; label = l;
        }

        void Start() { _basePos = transform.position; }

        void LateUpdate()
        {
            if (mission == null || head == null || group == null || mission.Objectives == null) return;
            bool pending = mission.State == MissionState.Active && !mission.Objectives.IsComplete(objectiveId);
            float target = 0f;
            if (pending)
            {
                float d = Vector3.Distance(head.position, transform.position);
                float near = Mathf.InverseLerp(hideWithin, showBeyond, d);
                float far = 1f - Mathf.InverseLerp(maxDistance * 0.7f, maxDistance, d);
                target = Mathf.Clamp01(Mathf.Min(near, far));
            }
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * 4f);
            if (group.alpha <= 0.01f) return;

            transform.position = _basePos + Vector3.up * (0.08f * Mathf.Sin(Time.unscaledTime * 2.2f));
            Vector3 away = transform.position - head.position; away.y = 0f;
            if (away.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
