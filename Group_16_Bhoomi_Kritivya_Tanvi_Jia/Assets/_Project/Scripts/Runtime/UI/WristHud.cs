using System.Text;
using RescueSim.Core;
using RescueSim.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RescueSim.UI
{
    /// <summary>
    /// Mission status panel that hovers by the left hand and fades in when the player raises it into view.
    /// Shows time, exposure and the objective checklist.
    /// </summary>
    public class WristHud : MonoBehaviour
    {
        [SerializeField] MissionController mission;
        [SerializeField] PlayerVitals vitals;
        [SerializeField] Transform head;
        [SerializeField] Transform hand;
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text objectivesText;
        [SerializeField] Image exposureFill;
        [SerializeField] Vector3 handOffset = new Vector3(0f, 0.13f, 0f);
        [SerializeField, Range(10f, 90f)] float showAngle = 55f;
        [SerializeField, Min(0.2f)] float showDistance = 0.8f;

        readonly StringBuilder _sb = new StringBuilder(256);
        int _lastSecond = -1;
        bool _dirty = true;

        public void Configure(MissionController m, PlayerVitals v, Transform h, Transform handT, CanvasGroup g, TMP_Text timer, TMP_Text objectives, Image exposure)
        {
            mission = m; vitals = v; head = h; hand = handT; group = g; timerText = timer; objectivesText = objectives; exposureFill = exposure;
        }

        void Awake()
        {
            if (exposureFill != null && exposureFill.sprite == null) exposureFill.sprite = UIFactory.WhiteSprite;
        }

        void OnEnable()
        {
            if (mission != null && mission.Objectives != null) mission.Objectives.ObjectiveChanged += OnObjectiveChanged;
            _dirty = true;
        }

        void OnDisable()
        {
            if (mission != null && mission.Objectives != null) mission.Objectives.ObjectiveChanged -= OnObjectiveChanged;
        }

        void OnObjectiveChanged(ObjectiveState _) { _dirty = true; }

        void LateUpdate()
        {
            if (mission == null || head == null || hand == null || group == null) return;

            Vector3 target = hand.position + handOffset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            Vector3 away = transform.position - head.position; away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);

            Vector3 to = transform.position - head.position;
            bool inView = to.magnitude < showDistance && Vector3.Angle(head.forward, to) < showAngle;
            bool visible = inView && (mission.State == MissionState.Active || mission.State == MissionState.Paused);
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            if (group.alpha <= 0.01f) return;

            int sec = Mathf.CeilToInt(mission.TimeRemaining);
            if (sec != _lastSecond && timerText != null)
            {
                _lastSecond = sec;
                timerText.text = $"{sec / 60:00}:{sec % 60:00}";
                timerText.color = sec <= 60 ? UIFactory.Bad : UIFactory.TextMain;
            }
            if (exposureFill != null && vitals != null)
            {
                exposureFill.fillAmount = vitals.Exposure;
                exposureFill.color = Color.Lerp(UIFactory.Good, UIFactory.Bad, vitals.Exposure);
            }
            if (_dirty) { _dirty = false; RebuildObjectives(); }
        }

        void RebuildObjectives()
        {
            if (objectivesText == null || mission.Objectives == null) return;
            _sb.Clear();
            foreach (var o in mission.Objectives.Objectives)
            {
                bool done = o.Status == ObjectiveStatus.Complete;
                bool failed = o.Status == ObjectiveStatus.Failed;
                string color = done ? "#4DE680" : failed ? "#FF5A4D" : "#EAF0F7";
                string mark = done ? "[x]" : failed ? "[!]" : "[ ]";
                _sb.Append("<color=").Append(color).Append('>').Append(mark).Append(' ');
                if (done) _sb.Append("<s>");
                _sb.Append(o.Title);
                if (done) _sb.Append("</s>");
                if (!string.IsNullOrEmpty(o.Progress) && !done) _sb.Append("  ").Append(o.Progress);
                _sb.Append("</color>\n");
            }
            objectivesText.text = _sb.ToString();
        }
    }
}
