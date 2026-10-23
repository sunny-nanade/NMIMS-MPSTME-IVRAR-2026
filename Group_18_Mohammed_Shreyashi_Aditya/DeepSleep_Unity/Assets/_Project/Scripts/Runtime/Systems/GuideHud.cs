using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// Always-available guidance. A 10 s orientation hologram when the player first wakes in the Medical Bay (controls only, no story),
    /// then a small lazy-follow goal card that shows the current objective whenever the stage or sub-step changes and again after the player stalls.
    /// In the smoke blocks the card appears once at the start and never as an idle nudge, so it cannot act as extra guidance between conditions.
    /// </summary>
    public class GuideHud : MonoBehaviour
    {
        [SerializeField] float introSeconds = 10f;
        [SerializeField] float introDelaySeconds = 2f;
        [SerializeField] float goalShowSeconds = 8f;
        [SerializeField] float idleRecallSeconds = 25f;
        [SerializeField] float introDistance = 1.4f;
        [SerializeField] float goalDistance = 1.3f;
        [SerializeField] float goalDrop = 0.5f;
        [SerializeField] float followYawDegrees = 40f;
        [SerializeField] Color tint = new Color(0.25f, 0.85f, 1f, 1f);

        public static GuideHud Instance { get; private set; }
        public bool IntroVisible => _intro != null && _introRoot.activeSelf;
        public string CurrentGoal { get; private set; } = "";
        public bool GoalVisible => _goalRoot != null && _goalRoot.activeSelf;

        class Card
        {
            public GameObject Root; public Material Mat; public TextMeshPro[] Texts; public float Reveal; public float Target;
        }

        Card _intro, _goal;
        GameObject _introRoot, _goalRoot;
        TextMeshPro _goalText;
        float _goalYaw, _lastChange, _goalShownAt;
        string _shownSession;
        bool _introRunning;
        float _introStart;
        EarCheckController _ear; ThermalKeypad _keypad; BreachTrail _trail; CoolantRack _rack; AwakeningController _awake;
        float _nextScan;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived += OnSceneArrived;
        }

        void OnDestroy() { if (SceneFlow.Instance != null) SceneFlow.Instance.SceneArrived -= OnSceneArrived; if (Instance == this) Instance = null; }

        void OnSceneArrived(string sceneName)
        {
            _ear = null; _keypad = null; _trail = null; _rack = null; _awake = null; _nextScan = 0f;
            var sm = SessionManager.Instance;
            if (sm == null) return;
            if (sceneName == DS.SceneMedicalBay && sm.Current == Stage.Awakening && _shownSession != sm.SessionId)
            {
                _shownSession = sm.SessionId;
                StartIntro();
            }
            _lastChange = Time.time;
        }

        void StartIntro()
        {
            if (_intro == null) _intro = BuildIntro();
            _introRoot = _intro.Root;
            _introRunning = true; _introStart = Time.time + introDelaySeconds;
            _introRoot.SetActive(false);
            DS.Log("GuideHud: orientation hologram queued");
        }

        // ---------- build ----------
        Material HoloMaterial(float alpha)
        {
            var src = Resources.Load<Material>("M_Hologram");
            Material m;
            if (src != null) m = new Material(src);
            else
            {
                var sh = Shader.Find("Fractured_Final/HologramPanel");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                m = new Material(sh);
            }
            m.SetColor("_Color", tint);
            if (m.HasProperty("_Alpha")) m.SetFloat("_Alpha", alpha);
            return m;
        }

        Card BuildPanel(string name, Vector2 size, float alpha)
        {
            var c = new Card();
            c.Root = new GameObject(name);
            c.Root.transform.SetParent(transform, false);
            var panel = WorldUI.Panel(c.Root.transform, "Glass", size, Color.white, Vector3.zero);
            c.Mat = HoloMaterial(alpha);
            panel.GetComponent<MeshRenderer>().sharedMaterial = c.Mat;
            return c;
        }

        Card BuildIntro()
        {
            var c = BuildPanel("OrientationHologram", new Vector2(1.7f, 1.05f), 0.30f);
            var tx = c.Root.transform;
            var title = WorldUI.Text(tx, "ORIENTATION", 2.0f, new Vector3(0, 0.40f, -0.01f), new Vector2(1.6f, 0.14f), TextAlignmentOptions.Center, tint);
            title.fontStyle = FontStyles.Bold;
            var body = WorldUI.Text(tx,
                "<color=#FFFFFF>GRIP</color>  pick things up\n" +
                "<color=#FFFFFF>TRIGGER</color>  point and press\n" +
                "<color=#FFFFFF>RIGHT STICK</color>  forward, release: teleport\n" +
                "<color=#FFFFFF>RIGHT STICK</color>  left or right: turn\n\n" +
                "Your goal always shows in front of you.\nLook around. Listen.",
                1.45f, new Vector3(0, -0.04f, -0.01f), new Vector2(1.62f, 0.8f), TextAlignmentOptions.Center, new Color(0.7f, 0.92f, 1f));
            body.lineSpacing = 6f;
            var bar = WorldUI.Panel(tx, "Timer", new Vector2(1.5f, 0.012f), tint, new Vector3(0, -0.47f, -0.01f));
            c.Texts = new[] { title, body };
            c.Root.SetActive(false);
            _introBar = bar.transform;
            return c;
        }
        Transform _introBar;

        Card BuildGoal()
        {
            var c = BuildPanel("GoalCard", new Vector2(0.95f, 0.3f), 0.34f);
            var tx = c.Root.transform;
            var tag = WorldUI.Text(tx, "CURRENT GOAL", 0.8f, new Vector3(0, 0.095f, -0.01f), new Vector2(0.9f, 0.06f), TextAlignmentOptions.Center, tint);
            tag.fontStyle = FontStyles.Bold;
            _goalText = WorldUI.Text(tx, "", 1.25f, new Vector3(0, -0.035f, -0.01f), new Vector2(0.9f, 0.17f), TextAlignmentOptions.Center, Color.white);
            c.Texts = new[] { tag, _goalText };
            c.Root.SetActive(false);
            return c;
        }

        // ---------- per-frame ----------
        void Update()
        {
            var rig = PlayerRig.Instance;
            var head = rig != null ? rig.Head : null;
            if (head == null) return;

            if (_introRunning) UpdateIntro(head);
            else if (_intro != null && _introRoot.activeSelf) _introRoot.SetActive(false);

            UpdateGoal(head);
        }

        bool BriefingPending => ObjectiveScreen.Active || (_awake != null && !_awake.Started);

        void UpdateIntro(Transform head)
        {
            if (BriefingPending) { _introStart = Time.time + introDelaySeconds; return; } // the mission briefing and Rose's first lines come first
            if (Time.time < _introStart) return;
            float t = Time.time - _introStart;
            if (!_introRoot.activeSelf)
            {
                _introRoot.SetActive(true);
                Place(_introRoot.transform, head, introDistance, -0.05f, true);
                SfxEvents.Raise(SfxEvent.UI, _introRoot.transform.position);
            }
            float reveal = Mathf.Clamp01(t / 0.7f) * Mathf.Clamp01((introSeconds - t) / 0.7f);
            Apply(_intro, reveal);
            if (_introBar != null) _introBar.localScale = new Vector3(1.5f * Mathf.Clamp01(1f - t / introSeconds), 0.012f, 1f);
            // keep it in front of the face without jitter: ease toward the gaze yaw
            Place(_introRoot.transform, head, introDistance, -0.05f, false);
            if (t >= introSeconds)
            {
                _introRunning = false; _introRoot.SetActive(false);
                _lastChange = Time.time; _goalShownAt = -999f; // show the first goal right away
                DS.Log("GuideHud: orientation hologram done");
            }
        }

        void UpdateGoal(Transform head)
        {
            var sm = SessionManager.Instance;
            if (sm == null) return;
            if (_goal == null) { _goal = BuildGoal(); _goalRoot = _goal.Root; }
            if (Time.time >= _nextScan) { _nextScan = Time.time + 0.4f; Resolve(); }

            string goal = _introRunning || BriefingPending ? null : ComputeGoal(sm);
            bool inBlock = sm.Current == Stage.Block1 || sm.Current == Stage.Block2;
            inBlock &= SceneFlow.Section == DS.SceneSmokeRoom;

            if (goal != CurrentGoal)
            {
                CurrentGoal = goal ?? "";
                _lastChange = Time.time;
                _goalShownAt = Time.time;
                if (goal != null && _goalText != null) _goalText.text = goal;
                if (goal != null) DS.Log("GuideHud: goal -> " + goal);
            }

            bool want = false;
            if (goal != null)
            {
                float since = Time.time - _goalShownAt;
                want = since < goalShowSeconds;
                if (!want && !inBlock && Time.time - _lastChange >= idleRecallSeconds)
                {
                    _goalShownAt = Time.time; _lastChange = Time.time; want = true; // stalled: show it again
                }
            }
            if (want && !_goalRoot.activeSelf) { _goalRoot.SetActive(true); Place(_goalRoot.transform, head, goalDistance, -goalDrop / goalDistance, true); _goal.Reveal = 0f; }
            if (_goalRoot.activeSelf)
            {
                float since = Time.time - _goalShownAt;
                float r = Mathf.Clamp01(since / 0.5f) * Mathf.Clamp01((goalShowSeconds - since) / 1f);
                Apply(_goal, want ? r : 0f);
                Place(_goalRoot.transform, head, goalDistance, -goalDrop / goalDistance, false);
                if (!want && _goal.Reveal <= 0.01f) _goalRoot.SetActive(false);
            }
        }

        static bool IsSceneLoaded(string n) { return SceneFlow.Section == n; }

        void Apply(Card c, float reveal)
        {
            c.Reveal = reveal;
            if (c.Mat.HasProperty("_Reveal")) c.Mat.SetFloat("_Reveal", reveal);
            foreach (var t in c.Texts) { var col = t.color; col.a = Mathf.Clamp01(reveal * 1.4f - 0.2f); t.color = col; }
        }

        /// <summary>Puts the card in front of the head at the given distance, lowered by pitchOffset (tan). Yaw eases only when the gaze leaves a dead zone.</summary>
        void Place(Transform card, Transform head, float dist, float dropTan, bool snap)
        {
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.001f) f = head.up; // looking straight down or up
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (snap) _goalYaw = yaw;
            else if (Mathf.Abs(Mathf.DeltaAngle(_goalYaw, yaw)) > followYawDegrees) _goalYaw = Mathf.MoveTowardsAngle(_goalYaw, yaw, 180f * Time.deltaTime);
            else _goalYaw = Mathf.MoveTowardsAngle(_goalYaw, yaw, 12f * Time.deltaTime);
            Vector3 dir = Quaternion.Euler(0, _goalYaw, 0) * Vector3.forward;
            Vector3 p = head.position + dir * dist; p.y = head.position.y + dropTan * dist;
            card.position = p;
            Vector3 look = card.position - head.position; look.y = 0f;
            if (look.sqrMagnitude > 0.0001f) card.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        // ---------- objective ----------
        void Resolve()
        {
            if (_awake == null) _awake = FindAnyObjectByType<AwakeningController>();
            if (_ear == null) _ear = FindAnyObjectByType<EarCheckController>();
            if (_keypad == null) _keypad = FindAnyObjectByType<ThermalKeypad>();
            if (_trail == null) _trail = FindAnyObjectByType<BreachTrail>();
            if (_rack == null) _rack = FindAnyObjectByType<CoolantRack>();
        }

        string ComputeGoal(SessionManager sm)
        {
            string scene = SceneFlow.Section;
            bool smokeLoaded = IsSceneLoaded("SCN_SmokeRoom");
            switch (sm.Current)
            {
                case Stage.Awakening:
                    if (_awake != null && !_awake.CupGrabbed) return "Pick up the cup from the tray.";
                    return "Teleport once: push the right stick forward and let go.";
                case Stage.EarCheck:
                    if (_ear != null && !_ear.Completed) return "Put the beacons on the table in the order shown on the poster.";
                    if (_keypad != null && _keypad.Current != ThermalKeypad.State.Solved) return "Use the keypad on the wall. Watch which keys glow.";
                    return "Use the keypad on the wall.";
                case Stage.KeypadSolved:
                    if (_trail != null && _trail.Current == BreachTrail.State.PanelOpen) return "Pull the handle on the hidden door.";
                    return "Follow the glowing trail across the room.";
                case Stage.TrailFollowed:
                    return smokeLoaded ? null : "Go through the open door.";
                case Stage.Block1:
                case Stage.Block2:
                    if (smokeLoaded) return "Find the way out. Listen for Rose.";
                    if (QuestionnaireUI.Instance != null && QuestionnaireUI.Instance.IsOpen) return null;
                    if (EgressBlockController.CompletedBlocks < 2) return "Go through the service door.";
                    if (_rack != null && _rack.Current == CoolantRack.State.Solved) return "Pull the lever on the rack.";
                    return "Reconnect the coolant: match each cable's colour to its port.";
                case Stage.CoolantRouted:
                    return "Pull the lever on the rack.";
                case Stage.Escaped:
                    return scene == "SCN_HallwayDebrief" ? "Walk to the console at the end of the corridor." : null;
                default:
                    return null;
            }
        }
    }
}
