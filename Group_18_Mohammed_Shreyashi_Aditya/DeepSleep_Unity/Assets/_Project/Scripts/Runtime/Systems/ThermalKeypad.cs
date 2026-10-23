using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Thermal keypad: nine numbered keys, three of them heat-glow (7 red = least hot, 3 white, 5 blue = hottest). Press coolest to hottest: 7, 3, 5.
    /// Wrong sequence: 2 s lockout. After 3 failures the glowing keys pulse for 4 s. Idle for 30 s plays R041.
    /// Uses PokeTarget (controller tip) and RayTarget (ray click) on every key.
    /// </summary>
    public class ThermalKeypad : MonoBehaviour
    {
        public enum State { Inactive, Ready, Locked, Solved }

        [SerializeField] int[] sequence = { 7, 3, 5 };
        [SerializeField] Color[] heatColors =
        {
            new Color(1f, 0.1f, 0.05f), new Color(1f, 1f, 1f), new Color(0.15f, 0.4f, 1f)
        };
        [SerializeField] float glowIntensity = 2.5f;
        [SerializeField] float labelHeight = 0.024f;
        [SerializeField] float plateSize = 0.034f;
        [SerializeField] float lockoutSeconds = 2f;
        [SerializeField] int failsBeforeHint = 3;
        [SerializeField] float hintPulseSeconds = 4f;
        [SerializeField] float idleSeconds = 30f;
        [SerializeField] float wrongVoiceCooldownSeconds = 30f;
        [SerializeField] float pressDepth = 0.0015f;
        [SerializeField] float pressDebounceSeconds = 0.25f;

        public State Current { get; private set; } = State.Inactive;
        public int WrongAttempts { get; private set; }
        public float SolveTime { get; private set; }
        public int[] Sequence => sequence;
        public event Action Solved;

        readonly Transform[] _keys = new Transform[10];
        readonly Renderer[] _renderers = new Renderer[10];
        readonly Material[] _mats = new Material[10];
        readonly GameObject[] _plates = new GameObject[10];
        readonly Material[] _plateMats = new Material[10];
        TMP_Text _display;
        int _pos;
        float _lastPressTime = -10f;
        float _beginTime, _lastActivity, _lastWrongVoice = -999f;
        Coroutine _pulse;

        public Transform Key(int n) { return n >= 1 && n <= 9 ? _keys[n] : null; }

        void Awake()
        {
            // the asset's own Keypad/KeypadButton scripts read input directly and are not used
            foreach (var c in GetComponentsInChildren<MonoBehaviour>(true))
                if (c != null && c.GetType().Name == "KeypadButton" || c != null && c.GetType().Name == "Keypad") Destroy(c);
            _display = GetComponentInChildren<TMP_Text>(true);
            for (int n = 1; n <= 9; n++)
            {
                var k = FindDeep(transform, "bttn" + n);
                if (k == null) { DS.Warn("Keypad key bttn" + n + " not found"); continue; }
                _keys[n] = k;
                var r = k.GetComponent<Renderer>();
                _renderers[n] = r;
                if (r != null) { _mats[n] = r.material; _mats[n].EnableKeyword("_EMISSION"); _mats[n].SetColor("_EmissionColor", Color.black); }
                WorldUI.SetLayer(k.gameObject, "Interactable");
                var bc = k.GetComponent<BoxCollider>();
                if (bc != null) bc.size = new Vector3(bc.size.x * 0.6f, bc.size.y * 0.6f, bc.size.z); // neighbouring key colliders overlapped (3.6 cm wide at 2.8 cm pitch)
                var poke = k.GetComponent<PokeTarget>() ?? k.gameObject.AddComponent<PokeTarget>();
                var ray = k.GetComponent<RayTarget>() ?? k.gameObject.AddComponent<RayTarget>();
                int num = n;
                poke.Poked += (_, __) => OnKey(num);
                ray.Down += (_, __) => OnKey(num);
            }
            BuildKeyFaces();
            SetDisplay("----", new Color(0.4f, 0.8f, 1f));
        }

        /// <summary>Number labels on all nine keys and a flat, unlit heat plate (red / white / blue) on the three hot keys, so the glow reads in any light.</summary>
        void BuildKeyFaces()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var root = new GameObject("KeyFaces").transform;
            root.SetParent(transform, false);
            for (int n = 1; n <= 9; n++)
            {
                var k = _keys[n];
                if (k == null) continue;
                int heat = System.Array.IndexOf(sequence, n);
                Vector3 front = -transform.forward; // keys face the player: the keypad's local -Z
                var holder = new GameObject("Face_" + n).transform;
                holder.SetParent(root, false);
                holder.SetPositionAndRotation(k.position + front * 0.0085f, transform.rotation);
                if (heat >= 0 && heat < heatColors.Length)
                {
                    var plate = WorldUI.Panel(holder, "HeatPlate", new Vector2(plateSize, plateSize), Color.white, Vector3.zero);
                    var m = new Material(unlit);
                    plate.GetComponent<MeshRenderer>().sharedMaterial = m;
                    _plates[n] = plate; _plateMats[n] = m;
                    plate.SetActive(false);
                }
                bool darkText = heat >= 0 && heat < heatColors.Length && heatColors[heat].grayscale > 0.6f;
                var t = WorldUI.Text(holder, n.ToString(), WorldUI.Cm(labelHeight), new Vector3(0, 0, -0.002f), new Vector2(0.04f, 0.04f),
                    TextAlignmentOptions.Center, darkText ? new Color(0.05f, 0.05f, 0.08f) : new Color(0.92f, 0.95f, 1f));
                t.fontStyle = FontStyles.Bold;
            }
        }

        void Start()
        {
            var sm = SessionManager.Instance;
            if (sm != null && sm.Current >= Stage.KeypadSolved) { Current = State.Solved; ApplyGlow(0f); SetDisplay("OK", Color.green); }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        /// <summary>Called by EarCheckController when the Ear Check is finished.</summary>
        public void Begin()
        {
            if (Current != State.Inactive) return;
            Current = State.Ready; _pos = 0; WrongAttempts = 0;
            _beginTime = Time.time; _lastActivity = Time.time;
            ApplyGlow(1f);
            SetDisplay("----", new Color(1f, 0.7f, 0.2f));
            VoiceBus.Request("R040");
            DS.Log("Keypad ready");
        }

        void Update()
        {
            if (Current == State.Ready && Time.time - _lastActivity >= idleSeconds)
            {
                _lastActivity = Time.time;
                VoiceBus.Request("R041");
            }
        }

        void ApplyGlow(float k)
        {
            for (int i = 0; i < sequence.Length && i < heatColors.Length; i++)
            {
                int n = sequence[i];
                if (n < 1 || n > 9) continue;
                if (_mats[n] != null) _mats[n].SetColor("_EmissionColor", heatColors[i] * glowIntensity * k);
                if (_plates[n] != null)
                {
                    _plates[n].SetActive(k > 0.01f);
                    float b = Mathf.Clamp(0.45f + 0.55f * k, 0f, 1f); // the plate stays readable at rest and brightens on the hint pulse
                    var c = heatColors[i] * b; c.a = 1f;
                    _plateMats[n].SetColor("_BaseColor", c); _plateMats[n].color = c;
                }
            }
        }

        void SetDisplay(string text, Color c)
        {
            if (_display == null) return;
            _display.text = text; _display.color = c;
        }

        void OnKey(int n)
        {
            // a poke and a ray click on the same key can arrive together: count one press
            if (Time.time - _lastPressTime < pressDebounceSeconds) return;
            _lastPressTime = Time.time;
            if (_keys[n] != null) StartCoroutine(PressAnim(_keys[n]));
            if (Current == State.Inactive || Current == State.Solved) { SfxEvents.Raise(SfxEvent.KeyPress, _keys[n].position, n - 1); return; }
            if (Current == State.Locked) return;
            _lastActivity = Time.time;
            SfxEvents.Raise(SfxEvent.KeyPress, _keys[n].position, n - 1);
            if (n == sequence[_pos])
            {
                _pos++;
                SetDisplay(new string('*', _pos).PadRight(sequence.Length, '-'), new Color(0.4f, 1f, 0.5f));
                if (_pos >= sequence.Length) Solve();
            }
            else Fail();
        }

        IEnumerator PressAnim(Transform k)
        {
            Vector3 p = k.localPosition;
            k.localPosition = p + new Vector3(0, 0, pressDepth);
            yield return new WaitForSeconds(0.12f);
            k.localPosition = p;
        }

        void Fail()
        {
            WrongAttempts++;
            _pos = 0;
            Current = State.Locked;
            SfxEvents.Raise(SfxEvent.WrongSequence, transform.position);
            if (Time.time - _lastWrongVoice >= wrongVoiceCooldownSeconds) { _lastWrongVoice = Time.time; VoiceBus.Request("H004"); }
            DS.Log($"Keypad wrong sequence #{WrongAttempts}");
            StartCoroutine(Lockout());
            if (failsBeforeHint > 0 && WrongAttempts % failsBeforeHint == 0)
            {
                if (_pulse != null) StopCoroutine(_pulse);
                _pulse = StartCoroutine(HintPulse());
            }
        }

        IEnumerator Lockout()
        {
            SetDisplay("LOCK", new Color(1f, 0.2f, 0.2f));
            yield return new WaitForSeconds(lockoutSeconds);
            if (Current == State.Locked) { Current = State.Ready; _lastActivity = Time.time; SetDisplay("----", new Color(1f, 0.7f, 0.2f)); }
        }

        IEnumerator HintPulse()
        {
            float t = 0f;
            while (t < hintPulseSeconds)
            {
                t += Time.deltaTime;
                ApplyGlow(0.9f + 0.9f * Mathf.Sin(t * Mathf.PI * 4f));
                yield return null;
            }
            if (Current != State.Solved) ApplyGlow(1f);
            _pulse = null;
        }

        void Solve()
        {
            Current = State.Solved;
            SolveTime = Time.time - _beginTime;
            if (_pulse != null) { StopCoroutine(_pulse); _pulse = null; }
            ApplyGlow(0f);
            SetDisplay("OK", Color.green);
            TelemetryLogger.Log("keypad_time_s", SolveTime);
            TelemetryLogger.Log("keypad_wrong_attempts", WrongAttempts);
            SfxEvents.Raise(SfxEvent.TerminalBoot, transform.position);
            VoiceBus.Request("R043");
            DS.Log($"Keypad solved in {SolveTime:F1} s with {WrongAttempts} wrong attempts");
            var sm = SessionManager.Instance;
            if (sm != null) sm.TryAdvance(Stage.KeypadSolved);
            Solved?.Invoke();
        }
    }
}
