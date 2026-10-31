using RescueSim.Audio;
using RescueSim.Core;
using TMPro;
using UnityEngine;

namespace RescueSim.UI
{
    /// <summary>Short on-screen feedback messages (objective done, mistakes, warnings) plus a matching sound cue.</summary>
    public class FeedbackToast : MonoBehaviour
    {
        [SerializeField] MissionController mission;
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text label;
        [SerializeField] SpatialSound sound;
        [SerializeField, Min(0.5f)] float visibleSeconds = 4f;

        float _timer;

        public void Configure(MissionController m, CanvasGroup g, TMP_Text l, SpatialSound s) { mission = m; group = g; label = l; sound = s; }

        void OnEnable() { if (mission != null) mission.Feedback += Show; if (group != null) group.alpha = 0f; }
        void OnDisable() { if (mission != null) mission.Feedback -= Show; }

        public void Show(string message, FeedbackKind kind)
        {
            if (label == null) return;
            label.text = message;
            label.color = kind switch
            {
                FeedbackKind.Success => UIFactory.Good,
                FeedbackKind.Warning => new Color(1f, 0.8f, 0.3f),
                FeedbackKind.Mistake => UIFactory.Bad,
                _ => UIFactory.TextMain
            };
            _timer = visibleSeconds;
            if (sound != null)
            {
                sound.PlayOneShot(kind switch
                {
                    FeedbackKind.Success => SpatialSound.Cue.Success,
                    FeedbackKind.Mistake => SpatialSound.Cue.Failure,
                    FeedbackKind.Warning => SpatialSound.Cue.Warning,
                    _ => SpatialSound.Cue.Confirm
                });
            }
        }

        void Update()
        {
            if (group == null) return;
            _timer -= Time.unscaledDeltaTime;
            float target = _timer > 0f ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * 3f);
        }
    }
}
