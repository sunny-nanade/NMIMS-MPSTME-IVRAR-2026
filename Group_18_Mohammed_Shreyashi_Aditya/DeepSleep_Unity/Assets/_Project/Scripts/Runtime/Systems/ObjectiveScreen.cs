using System.Collections;
using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Mission briefing shown once, right after the Setup pages and before Rose's first line: the objective and the three tasks
    /// (beacons, maze, cables) without the solutions. It stays for holdSeconds, then fades out and the awakening starts.
    /// </summary>
    public class ObjectiveScreen : MonoBehaviour
    {
        [SerializeField] float holdSeconds = 20f;
        [SerializeField] float distance = 1.7f;
        [SerializeField] float followYawDegrees = 35f;
        [SerializeField] Vector2 size = new Vector2(2.0f, 1.25f);

        static string _shownFor;
        public static bool Active { get; private set; }
        public bool Done { get; private set; }

        Transform _root;
        TextMeshPro[] _texts;
        Transform _bar;
        float _yaw;

        /// <summary>True when the briefing has not yet been shown in this session.</summary>
        public static bool Pending(string sessionId) { return _shownFor != sessionId; }

        void OnDestroy() { if (Active && !Done) Active = false; } // a restart or section switch mid-briefing must not leave the guidance HUD waiting for it

        public static ObjectiveScreen Create(Transform parent)
        {
            var go = new GameObject("ObjectiveScreen");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ObjectiveScreen>();
        }

        public IEnumerator Show(string sessionId)
        {
            _shownFor = sessionId;
            Active = true;
            while (PlayerRig.Instance == null || PlayerRig.Instance.Head == null) yield return null;
            while (SceneFlow.Instance != null && SceneFlow.Instance.IsBusy) yield return null; // the fade-in from the Setup room finishes first
            yield return new WaitForSeconds(0.8f);

            Build();
            var head = PlayerRig.Instance.Head;
            Place(head, true);
            SfxEvents.Raise(SfxEvent.UI, _root.position);
            DS.Log("ObjectiveScreen: shown");

            float t = 0f;
            while (t < holdSeconds)
            {
                t += Time.deltaTime;
                float reveal = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((holdSeconds - t) / 0.8f);
                _root.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, reveal);
                foreach (var tx in _texts) { var c = tx.color; c.a = reveal; tx.color = c; }
                _bar.localScale = new Vector3(Mathf.Max(0.001f, size.x * 0.9f * (1f - t / holdSeconds)), 0.012f, 1f);
                Place(PlayerRig.Instance.Head, false);
                yield return null;
            }
            Destroy(_root.gameObject);
            Active = false; Done = true;
            DS.Log("ObjectiveScreen: done");
        }

        void Build()
        {
            var accent = UiStyle.Accent;
            var body = UiStyle.Body;
            _root = new GameObject("Briefing").transform;
            _root.SetParent(transform, false);
            UiStyle.Panel(_root, "Back", size, Vector3.zero);

            var list = new System.Collections.Generic.List<TextMeshPro>();
            float top = size.y * 0.5f;
            var title = WorldUI.Text(_root, "PROTOCOL: DEEP SLEEP", WorldUI.Cm(0.1f), new Vector3(0, top - 0.11f, -0.005f), new Vector2(size.x * 0.9f, 0.2f), TextAlignmentOptions.Center, accent);
            title.fontStyle = FontStyles.Bold; list.Add(title);
            list.Add(WorldUI.Text(_root, "RESEARCH VESSEL AETHELGARD  -  DECK 4", WorldUI.Cm(0.04f), new Vector3(0, top - 0.2f, -0.005f), new Vector2(size.x * 0.9f, 0.08f), TextAlignmentOptions.Center, new Color(0.55f, 0.7f, 0.8f)));

            var obj = WorldUI.Text(_root,
                "<b><color=#FFC857>OBJECTIVE</color></b>\nGet off the ship. Rose, an engineer sealed on Deck 4, will guide you over comms. " +
                "HALCYON, the ship's AI, insists everything is fine. Decide who to trust.",
                WorldUI.Cm(0.056f), new Vector3(0, top - 0.46f, -0.005f), new Vector2(size.x * 0.88f, 0.4f), TextAlignmentOptions.Center, body);
            list.Add(obj);

            string[] heads = { "1", "2", "3" };
            string[] names = { "THE BEACONS", "THE MAZE", "THE CABLES" };
            string[] lines =
            {
                "Bring the signal back. Order matters.",
                "Smoke fills the corridor. Listen closely.",
                "Reroute the coolant before it is too late."
            };
            for (int i = 0; i < 3; i++)
            {
                float y = top - 0.8f - i * 0.15f;
                WorldUI.Panel(_root, "Badge" + i, new Vector2(0.1f, 0.1f), accent, new Vector3(-size.x * 0.4f, y, -0.004f));
                var n = WorldUI.Text(_root, heads[i], WorldUI.Cm(0.07f), new Vector3(-size.x * 0.4f, y, -0.008f), new Vector2(0.1f, 0.1f), TextAlignmentOptions.Center, new Color(0.03f, 0.05f, 0.08f));
                n.fontStyle = FontStyles.Bold; list.Add(n);
                var nm = WorldUI.Text(_root, "<b>" + names[i] + "</b>", WorldUI.Cm(0.055f), new Vector3(-0.30f, y, -0.005f), new Vector2(0.62f, 0.1f), TextAlignmentOptions.Left, accent);
                list.Add(nm);
                list.Add(WorldUI.Text(_root, lines[i], WorldUI.Cm(0.044f), new Vector3(0.36f, y, -0.005f), new Vector2(1.1f, 0.1f), TextAlignmentOptions.Left, body));
            }
            _texts = list.ToArray();

            var bar = WorldUI.Panel(_root, "Timer", new Vector2(1f, 1f), accent, new Vector3(0, -top + 0.05f, -0.005f));
            _bar = bar.transform;
            _bar.localScale = new Vector3(size.x * 0.9f, 0.012f, 1f);
        }

        void Place(Transform head, bool snap)
        {
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.001f) f = head.up;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (snap) _yaw = yaw;
            else if (Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw)) > followYawDegrees) _yaw = Mathf.MoveTowardsAngle(_yaw, yaw, 90f * Time.deltaTime);
            Vector3 dir = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            Vector3 p = head.position + dir * distance; p.y = head.position.y - 0.05f;
            _root.position = p;
            Vector3 look = p - head.position; look.y = 0f;
            if (look.sqrMagnitude > 0.0001f) _root.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }
    }
}
