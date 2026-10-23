using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>
    /// World-space questionnaires, answered with the controller ray or by poking.
    /// Raw NASA-TLX (six 0 to 100 sliders) after each smoke-room block in SCN_MedicalBay; SUS (10 items, 1 to 5), a preference question and the
    /// mission-log plaque in SCN_HallwayDebrief. The panel appears in front of the player when it opens.
    /// </summary>
    public class QuestionnaireUI : MonoBehaviour
    {
        public static QuestionnaireUI Instance { get; private set; }
        public enum Mode { None, Tlx, Sus, Preference, Done }

        [SerializeField] Vector2 size = new Vector2(1.5f, 1.05f);
        [SerializeField] float distance = 0.8f;
        [SerializeField] TextMeshPro plaque;
        [SerializeField] float hallwayStartDelaySeconds = 2.5f;

        public Mode Current { get; private set; } = Mode.None;
        public bool IsOpen => _root != null && _root.activeSelf;
        public int Page { get; private set; }
        public int TlxBlock { get; private set; }
        public readonly float[] TlxValues = new float[6];
        public readonly int[] SusValues = new int[10];
        public float SusScore { get; private set; }
        public float TlxRawMean { get; private set; }
        public string PreferenceChoice { get; private set; } = "";
        public string PlaqueText => plaque != null ? plaque.text : "";
        public bool Finished { get; private set; }
        public bool AnsweredCurrent => _answered;
        public int PendingValue => _pendingValue;

        public static readonly string[] TlxNames = { "Mental demand", "Physical demand", "Temporal demand", "Performance", "Effort", "Frustration" };
        public static readonly string[] TlxFields = { "TLX_mental", "TLX_physical", "TLX_temporal", "TLX_performance", "TLX_effort", "TLX_frustration" };
        static readonly string[] TlxQuestions =
        {
            "How mentally demanding was the task?",
            "How physically demanding was the task?",
            "How hurried or rushed was the pace of the task?",
            "How successful were you in accomplishing what you were asked to do?",
            "How hard did you have to work to accomplish your level of performance?",
            "How insecure, discouraged, irritated, stressed and annoyed were you?",
        };
        static readonly string[] TlxLow = { "Very low", "Very low", "Very low", "Perfect", "Very low", "Very low" };
        static readonly string[] TlxHigh = { "Very high", "Very high", "Very high", "Failure", "Very high", "Very high" };
        public static readonly string[] SusItems =
        {
            "I think that I would like to use this system frequently.",
            "I found the system unnecessarily complex.",
            "I thought the system was easy to use.",
            "I think that I would need the support of a technical person to be able to use this system.",
            "I found the various functions in this system were well integrated.",
            "I thought there was too much inconsistency in this system.",
            "I would imagine that most people would learn to use this system very quickly.",
            "I found the system very cumbersome to use.",
            "I felt very confident using the system.",
            "I needed to learn a lot of things before I could get going with this system.",
        };

        GameObject _root;
        Transform _content;
        WorldUI.WorldButton _next;
        bool _answered;
        int _pendingValue;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            RefreshPlaque();
            if (SceneFlow.Section == DS.SceneHallway) StartCoroutine(HallwayFlow());
        }

        // ------------------------------------------------------------------ entry points
        public static void BeginTlx(int block)
        {
            var q = FindAnyObjectByType<QuestionnaireUI>();
            if (q == null) { DS.Warn("QuestionnaireUI not found for TLX"); return; }
            q.StartTlx(block);
        }

        public void StartTlx(int block)
        {
            TlxBlock = block; Current = Mode.Tlx; Page = 0;
            if (block == 1) VoiceBus.Request("R140");
            Open();
            BuildTlxPage();
        }

        IEnumerator HallwayFlow()
        {
            yield return new WaitForSeconds(hallwayStartDelaySeconds);
            var sm = SessionManager.Instance;
            if (sm != null && sm.Current == Stage.Escaped) sm.TryAdvance(Stage.Questionnaire);
            Current = Mode.Sus; Page = 0;
            Open();
            BuildSusPage();
        }

        // ------------------------------------------------------------------ panel
        void Open()
        {
            if (_root == null)
            {
                _root = new GameObject("QuestionnairePanel");
                _root.transform.SetParent(transform, false);
                UiStyle.Panel(_root.transform, "Back", size, Vector3.zero);
                var c = new GameObject("Content"); c.transform.SetParent(_root.transform, false); _content = c.transform;
            }
            var rig = PlayerRig.Instance;
            if (rig != null && rig.Head != null)
            {
                Vector3 f = rig.Head.forward; f.y = 0; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
                Vector3 p = rig.Head.position + f * distance; p.y = rig.Head.position.y - 0.12f;
                _root.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f, Vector3.up));
            }
            _root.SetActive(true);
        }

        void Close() { if (_root != null) _root.SetActive(false); Current = Mode.None; }

        void Clear()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _next = null; _answered = false;
        }

        TextMeshPro Label(string text, float glyphMeters, Vector3 pos, Vector2 box, Color? col = null, TextAlignmentOptions al = TextAlignmentOptions.Center)
        {
            var t = WorldUI.Text(_content, text, WorldUI.Cm(glyphMeters), pos + new Vector3(0, 0, -0.005f), box, al, col);
            return t;
        }

        void AddNext(string label, System.Action onNext, bool needsAnswer)
        {
            _next = WorldUI.Button(_content, label, new Vector2(0.44f, 0.11f), new Vector3(0, -size.y * 0.4f, -0.01f), b => onNext(), WorldUI.Cm(0.05f));
            _next.Interactable = !needsAnswer;
        }

        // ------------------------------------------------------------------ TLX
        void BuildTlxPage()
        {
            Clear();
            Label($"Block {TlxBlock} of 2  -  question {Page + 1} of 6", 0.035f, new Vector3(0, size.y * 0.43f, 0), new Vector2(size.x * 0.9f, 0.08f), new Color(0.6f, 0.8f, 1f));
            Label(TlxNames[Page], 0.07f, new Vector3(0, size.y * 0.30f, 0), new Vector2(size.x * 0.9f, 0.12f), new Color(0.5f, 0.85f, 1f));
            Label(TlxQuestions[Page], 0.05f, new Vector3(0, size.y * 0.12f, 0), new Vector2(size.x * 0.85f, 0.25f));
            var s = WorldUI.Slider(_content, "TLX", 1.0f, new Vector3(-0.05f, -0.12f, -0.01f), 0, 100);
            s.ValueLabel.fontSize = WorldUI.Cm(0.04f);
            s.Changed += v => { _answered = true; _pendingValue = (int)v; if (_next != null) _next.Interactable = true; };
            Label(TlxLow[Page], 0.035f, new Vector3(-0.55f, -0.2f, 0), new Vector2(0.3f, 0.06f), null, TextAlignmentOptions.Left);
            Label(TlxHigh[Page], 0.035f, new Vector3(0.45f, -0.2f, 0), new Vector2(0.3f, 0.06f), null, TextAlignmentOptions.Right);
            AddNext(Page < 5 ? "Next" : "Finish", TlxNext, true);
        }

        void TlxNext()
        {
            if (!_answered) return;
            TlxValues[Page] = _pendingValue;
            TelemetryLogger.Log(TlxFields[Page], TlxValues[Page], TlxBlock);
            if (Page < 5) { Page++; BuildTlxPage(); return; }
            float sum = 0; foreach (var v in TlxValues) sum += v;
            TlxRawMean = sum / 6f;
            TelemetryLogger.Log("TLX_raw_mean", TlxRawMean, TlxBlock);
            DS.Log($"TLX block {TlxBlock}: raw mean {TlxRawMean:F1}");
            int block = TlxBlock;
            Close();
            var sm = SessionManager.Instance;
            if (block == 1) { if (sm != null) sm.TryAdvance(Stage.Block2); }
            else
            {
                VoiceBus.Request("R150");
                StartCoroutine(DelayedVoice("R151", 10f));
            }
        }

        static IEnumerator DelayedVoice(string id, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            VoiceBus.Request(id);
        }

        // ------------------------------------------------------------------ SUS
        void BuildSusPage()
        {
            Clear();
            Label($"Question {Page + 1} of 10", 0.035f, new Vector3(0, size.y * 0.43f, 0), new Vector2(size.x * 0.9f, 0.08f), new Color(0.6f, 0.8f, 1f));
            Label(SusItems[Page], 0.055f, new Vector3(0, size.y * 0.2f, 0), new Vector2(size.x * 0.85f, 0.3f));
            for (int i = 1; i <= 5; i++)
            {
                int v = i;
                float x = (i - 3) * 0.26f;
                var b = WorldUI.Button(_content, i.ToString(), new Vector2(0.2f, 0.13f), new Vector3(x, -0.1f, -0.01f), null, WorldUI.Cm(0.07f));
                b.Clicked += _ => { _answered = true; _pendingValue = v; if (_next != null) _next.Interactable = true; HighlightSus(v); };
            }
            Label("Strongly disagree", 0.032f, new Vector3(-0.52f, -0.23f, 0), new Vector2(0.4f, 0.06f), null, TextAlignmentOptions.Left);
            Label("Strongly agree", 0.032f, new Vector3(0.52f, -0.23f, 0), new Vector2(0.4f, 0.06f), null, TextAlignmentOptions.Right);
            AddNext(Page < 9 ? "Next" : "Finish", SusNext, true);
        }

        void HighlightSus(int v)
        {
            foreach (var b in _content.GetComponentsInChildren<WorldUI.WorldButton>())
            {
                if (b == _next) continue;
                bool sel = b.Label != null && b.Label.text == v.ToString();
                var r = b.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = WorldUI.ColorMaterial(sel ? new Color(0.15f, 0.55f, 0.35f) : b.Normal);
            }
        }

        void SusNext()
        {
            if (!_answered) return;
            SusValues[Page] = _pendingValue;
            if (Page < 9) { Page++; BuildSusPage(); return; }
            SusScore = ScoreSus(SusValues);
            TelemetryLogger.Log("SUS_score", SusScore, 1);
            TelemetryLogger.Log("SUS_score", SusScore, 2);
            DS.Log($"SUS score {SusScore:F1}");
            Current = Mode.Preference;
            BuildPreferencePage();
        }

        /// <summary>Standard SUS scoring: odd items contribute (answer - 1), even items (5 - answer); the sum is multiplied by 2.5 (0 to 100).</summary>
        public static float ScoreSus(int[] answers)
        {
            int sum = 0;
            for (int i = 0; i < 10; i++) sum += (i % 2 == 0) ? answers[i] - 1 : 5 - answers[i];
            return sum * 2.5f;
        }

        // ------------------------------------------------------------------ preference + finish
        void BuildPreferencePage()
        {
            Clear();
            Label("One last question", 0.035f, new Vector3(0, size.y * 0.43f, 0), new Vector2(size.x * 0.9f, 0.08f), new Color(0.6f, 0.8f, 1f));
            Label("In which block was Rose's voice easier to follow?", 0.055f, new Vector3(0, size.y * 0.2f, 0), new Vector2(size.x * 0.85f, 0.3f));
            string[] opts = { "Block 1", "Block 2", "No difference" };
            for (int i = 0; i < 3; i++)
            {
                string o = opts[i];
                var b = WorldUI.Button(_content, o, new Vector2(0.4f, 0.13f), new Vector3((i - 1) * 0.46f, -0.08f, -0.01f), null, WorldUI.Cm(0.045f));
                b.Clicked += _ => { _answered = true; PreferenceChoice = o; if (_next != null) _next.Interactable = true; };
            }
            AddNext("Finish", FinishDebrief, true);
        }

        void FinishDebrief()
        {
            if (!_answered) return;
            var save = SaveLoad.Instance;
            if (save != null) { save.Debrief.preference = PreferenceChoice; }
            RefreshPlaque();
            if (save != null) { save.Debrief.plaque = PlaqueText; }
            Clear();
            Label("Thank you", 0.08f, new Vector3(0, size.y * 0.25f, 0), new Vector2(size.x * 0.9f, 0.14f), new Color(0.5f, 0.9f, 0.6f));
            Label("Your mission log is on the wall ahead. Please tell the experimenter that you have finished.", 0.05f, new Vector3(0, 0f, 0), new Vector2(size.x * 0.85f, 0.4f));
            Current = Mode.Done; Finished = true;
            VoiceBus.Request("R161");
            if (save != null) save.Export();
        }

        // ------------------------------------------------------------------ plaque
        static float BlockSeconds(int block)
        {
            var tel = TelemetryLogger.Instance;
            if (tel != null && tel.Rows.TryGetValue(block, out var row) && row.TryGetValue("evac_time_s", out var v)
                && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) return f;
            return 0f;
        }

        public void RefreshPlaque()
        {
            float t1 = BlockSeconds(1), t2 = BlockSeconds(2);
            string text = SaveLoad.Plaque(StudyConfig.Data.participant_id, t1, t2);
            if (plaque != null) plaque.text = text;
            var save = SaveLoad.Instance;
            if (save != null) { save.Debrief.block1_time_s = t1; save.Debrief.block2_time_s = t2; save.Debrief.plaque = text; }
        }
    }
}
