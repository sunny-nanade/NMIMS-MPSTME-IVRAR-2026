using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Experimenter menu: both grips + menu (Editor F10). Participant ID, restart, export, profiler on/off,
    /// debrief free text, and (Editor only) Jump to Stage.
    /// </summary>
    public class ExperimenterPanel : MonoBehaviour
    {
        public static ExperimenterPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        [SerializeField] float distance = 1.2f;
        [SerializeField] bool allowJumpInEditor = true;

        GameObject _root;
        TextMeshPro _idLabel, _stageLabel, _noteLabel, _statusLabel, _profLabel;
        int _jumpIndex;
        string _note = "";
        TouchScreenKeyboard _kb;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable() { if (InputService.Exists) InputService.Instance.TextTyped += OnTyped; }
        void OnDisable() { if (InputService.Exists) InputService.Instance.TextTyped -= OnTyped; }

        void Start() { if (InputService.Exists) { InputService.Instance.TextTyped -= OnTyped; InputService.Instance.TextTyped += OnTyped; } }

        void OnTyped(char c)
        {
            if (!IsOpen) return;
            if (c == '\b') { if (_note.Length > 0) _note = _note.Substring(0, _note.Length - 1); }
            else if (c == '\r' || c == '\n') { CommitNote(); }
            else if (!char.IsControl(c) && _note.Length < 300) _note += c;
            RefreshLabels();
        }

        void Update()
        {
            if (!InputService.Exists) return;
            if (InputService.Instance.ExperimenterComboPressed) Toggle();
            if (_kb != null && _kb.active) { _note = _kb.text; RefreshLabels(); }
            else if (_kb != null && _kb.status == TouchScreenKeyboard.Status.Done) { _note = _kb.text; _kb = null; CommitNote(); }
        }

        public void Toggle()
        {
            if (_root == null) Build();
            bool open = !_root.activeSelf;
            _root.SetActive(open);
            if (open)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 f = cam.transform.forward; f.y = 0; f.Normalize();
                    _root.transform.position = cam.transform.position + f * distance;
                    _root.transform.rotation = Quaternion.LookRotation(f);
                }
                PauseManager.Instance?.SetPaused(true);
                RefreshLabels();
            }
            else PauseManager.Instance?.SetPaused(false);
            DS.Log("Experimenter panel " + (open ? "opened" : "closed"));
        }

        void Build()
        {
            _root = new GameObject("ExperimenterPanel");
            DontDestroyOnLoad(_root);
            var t = _root.transform;
            WorldUI.Panel(t, "Back", new Vector2(1.1f, 1.0f), new Color(0.04f, 0.05f, 0.08f), new Vector3(0, 1.5f, 0.02f));
            WorldUI.Text(t, "EXPERIMENTER", 1.4f, new Vector3(0, 1.93f, 0), new Vector2(1f, 0.1f));
            _idLabel = WorldUI.Text(t, "", 1.1f, new Vector3(0, 1.80f, 0), new Vector2(1f, 0.08f));
            WorldUI.Button(t, "-10", new Vector2(0.14f, 0.07f), new Vector3(-0.42f, 1.72f, 0), b => ChangeId(-10), 0.8f);
            WorldUI.Button(t, "-1", new Vector2(0.14f, 0.07f), new Vector3(-0.26f, 1.72f, 0), b => ChangeId(-1), 0.8f);
            WorldUI.Button(t, "+1", new Vector2(0.14f, 0.07f), new Vector3(0.26f, 1.72f, 0), b => ChangeId(1), 0.8f);
            WorldUI.Button(t, "+10", new Vector2(0.14f, 0.07f), new Vector3(0.42f, 1.72f, 0), b => ChangeId(10), 0.8f);

            WorldUI.Button(t, "Restart", new Vector2(0.3f, 0.08f), new Vector3(-0.35f, 1.6f, 0), b => Restart(), 0.9f);
            WorldUI.Button(t, "Export", new Vector2(0.3f, 0.08f), new Vector3(0f, 1.6f, 0), b => DoExport(), 0.9f);
            WorldUI.Button(t, "Profiler", new Vector2(0.3f, 0.08f), new Vector3(0.35f, 1.6f, 0), b => ToggleProfiler(), 0.9f);
            _profLabel = WorldUI.Text(t, "", 0.8f, new Vector3(0.35f, 1.53f, 0), new Vector2(0.4f, 0.05f));

#if UNITY_EDITOR
            if (allowJumpInEditor)
            {
                WorldUI.Text(t, "Jump to stage (Editor only)", 0.8f, new Vector3(0, 1.46f, 0), new Vector2(1f, 0.05f));
                WorldUI.Button(t, "<", new Vector2(0.1f, 0.07f), new Vector3(-0.4f, 1.38f, 0), b => CycleStage(-1), 0.9f);
                _stageLabel = WorldUI.Text(t, "", 1.0f, new Vector3(-0.1f, 1.38f, 0), new Vector2(0.5f, 0.07f));
                WorldUI.Button(t, ">", new Vector2(0.1f, 0.07f), new Vector3(0.2f, 1.38f, 0), b => CycleStage(1), 0.9f);
                WorldUI.Button(t, "Go", new Vector2(0.15f, 0.07f), new Vector3(0.38f, 1.38f, 0), b => JumpNow(), 0.9f);
            }
#endif

            WorldUI.Text(t, "Debrief note (type on keyboard, Enter to save)", 0.75f, new Vector3(0, 1.30f, 0), new Vector2(1f, 0.05f));
            _noteLabel = WorldUI.Text(t, "", 0.8f, new Vector3(0, 1.23f, 0), new Vector2(1f, 0.1f));
            WorldUI.Button(t, "Type on device keyboard", new Vector2(0.5f, 0.07f), new Vector3(0, 1.15f, 0), b => OpenDeviceKeyboard(), 0.8f);
            _statusLabel = WorldUI.Text(t, "", 0.8f, new Vector3(0, 1.08f, 0), new Vector2(1f, 0.05f));
            WorldUI.Button(t, "Close", new Vector2(0.25f, 0.07f), new Vector3(0, 1.02f, 0), b => Toggle(), 0.9f);
            _root.SetActive(false);
        }

        void RefreshLabels()
        {
            if (_root == null) return;
            _idLabel.text = $"Participant {StudyConfig.Data.participant_id}   order {ConditionRandomizer.OrderLabel(StudyConfig.ParticipantNumber)}";
            if (_stageLabel != null) _stageLabel.text = ((Stage)_jumpIndex).ToString();
            _noteLabel.text = string.IsNullOrEmpty(_note) ? "(empty)" : _note;
            _profLabel.text = ProfilerCapture.Enabled ? "ON" : "OFF";
        }

        void ChangeId(int d)
        {
            int n = Mathf.Max(0, StudyConfig.ParticipantNumber + d);
            StudyConfig.Data.participant_id = n.ToString();
            StudyConfig.Save();
            RefreshLabels();
        }

        void Restart()
        {
            _statusLabel.text = "Restarting...";
            VoiceBus.CancelAll();
            EgressBlockController.ResetStatics();
            SessionManager.Instance.ResetSession();
            TelemetryLogger.Instance.ResetAll();
            SaveLoad.Instance?.ResetDebrief();
            if (OxygenSystem.Instance != null) OxygenSystem.Instance.ResetTimer();
            Toggle();
            SceneFlow.Instance.LoadSceneForJump(DS.SceneSetup);
        }

        void DoExport()
        {
            string p = SaveLoad.Instance != null ? SaveLoad.Instance.Export() : "";
            _statusLabel.text = "Exported";
            DS.Log("Export requested from experimenter panel: " + p);
        }

        void ToggleProfiler()
        {
            ProfilerCapture.Enabled = !ProfilerCapture.Enabled;
            RefreshLabels();
        }

        void CycleStage(int d)
        {
            int n = Enum.GetValues(typeof(Stage)).Length;
            _jumpIndex = (_jumpIndex + d + n) % n;
            RefreshLabels();
        }

#if UNITY_EDITOR
        void JumpNow()
        {
            StageJump.Jump((Stage)_jumpIndex);
            Toggle();
        }
#endif

        void OpenDeviceKeyboard()
        {
            if (TouchScreenKeyboard.isSupported) _kb = TouchScreenKeyboard.Open(_note, TouchScreenKeyboardType.Default);
            else _statusLabel.text = "No device keyboard; type on PC keyboard";
        }

        void CommitNote()
        {
            if (SaveLoad.Instance != null) SaveLoad.Instance.Debrief.free_text = _note;
            _statusLabel.text = "Note saved";
            DS.Log("Debrief note saved");
        }
    }
}
