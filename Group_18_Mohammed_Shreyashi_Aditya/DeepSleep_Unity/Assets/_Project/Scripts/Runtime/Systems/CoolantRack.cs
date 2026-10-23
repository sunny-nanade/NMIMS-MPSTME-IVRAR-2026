using System.Collections;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Coolant Reroute at the server rack: red cable to port 2, blue to port 5, yellow to port 3. A wrong plug is a 3 s lockout only (the cable
    /// goes back to the table). When all three are seated the lever (about 1.1 m) can be pulled down: blast doors open, stage Escaped, then the hallway.
    /// </summary>
    public class CoolantRack : MonoBehaviour
    {
        public enum State { Waiting, Active, Solved, LeverPulled }

        [SerializeField] CoolantCable[] cables;
        [SerializeField] CoolantPort[] ports;
        [SerializeField] Transform leverHandle;
        [SerializeField] Transform blastDoor;
        [SerializeField] float plugRadius = 0.1f;
        [SerializeField] float wrongPlugLockoutSeconds = 3f;
        [SerializeField] float leverGrabRadius = 0.2f;
        [SerializeField] float leverPullMeters = 0.25f;
        [SerializeField] float blastDoorSlideMeters = 0.95f;
        [SerializeField] float hallwayDelaySeconds = 4f;
        [SerializeField] bool requireBothBlocks = true;
        [SerializeField] Vector3 posterPosition = new Vector3(1.1f, 1.62f, 2.85f); // on the north wall (inner face at 2.88) above the cable table, facing south
        [SerializeField] Vector2 posterSize = new Vector2(1.1f, 0.78f);

        public State Current { get; private set; } = State.Waiting;
        public int WrongPlugs { get; private set; }
        public bool Locked => Time.time < _lockedUntil;
        public float SolveTime { get; private set; }
        public CoolantCable[] Cables { get => cables; set => cables = value; }
        public CoolantPort[] Ports { get => ports; set => ports = value; }
        public Transform LeverHandle => leverHandle;

        float _lockedUntil, _start = -1f;
        bool _pulling; Hand _hand; Vector3 _pullStart; Vector3 _leverHome;

        void Start()
        {
            BuildPoster();
            if (leverHandle != null) { _leverHome = leverHandle.position; leverHandle.gameObject.SetActive(false); }
            foreach (var c in cables) c.Grabbable.Released += OnReleased;
            var sm = SessionManager.Instance;
            if (sm != null && sm.Current >= Stage.CoolantRouted)
            {
                // entered by a stage jump: the rack is already routed
                for (int i = 0; i < cables.Length; i++)
                {
                    var port = PortByIndex(cables[i].CorrectPort);
                    if (port != null) cables[i].PlugInto(port);
                }
                Current = sm.Current >= Stage.Escaped ? State.LeverPulled : State.Solved;
                if (leverHandle != null && Current == State.Solved) leverHandle.gameObject.SetActive(true);
            }
        }

        /// <summary>Wall poster (primitives) that shows the port grid with each port number in the colour of the cable that belongs there.</summary>
        void BuildPoster()
        {
            var board = PosterBuilder.Board(transform, "CablePoster", posterPosition, Quaternion.identity, posterSize, UiStyle.Back, UiStyle.Frame);
            PosterBuilder.Label(board, "COOLANT PORTS", 0.07f, new Vector3(0, posterSize.y * 0.5f - 0.085f, 0), new Color(0.75f, 0.9f, 1f), posterSize.x * 0.9f);
            float dx = 0.32f, dy = 0.24f;
            for (int n = 1; n <= 6; n++)
            {
                int col = (n - 1) % 3, row = (n - 1) / 3;
                var pos = new Vector3((col - 1) * dx, 0.05f - row * dy, 0);
                CoolantCable match = null;
                foreach (var c in cables) if (c != null && c.CorrectPort == n) match = c;
                PosterBuilder.Quad(board, "Cell_" + n, new Vector2(0.27f, 0.2f), new Color(0.14f, 0.16f, 0.19f), pos);
                var digitColor = match != null ? match.Tint : new Color(0.38f, 0.4f, 0.44f);
                PosterBuilder.Label(board, n.ToString(), 0.13f, pos + new Vector3(0, match != null ? 0.02f : 0f, 0), digitColor, 0.25f);
                if (match != null)
                    PosterBuilder.Label(board, match.CableName.ToUpperInvariant(), 0.032f, pos + new Vector3(0, -0.07f, 0), digitColor, 0.26f);
            }
            PosterBuilder.Label(board, "Plug each cable into the port that has its colour.", 0.035f, new Vector3(0, -posterSize.y * 0.5f + 0.06f, 0), new Color(0.8f, 0.85f, 0.9f), posterSize.x * 0.92f, false);
        }

        void OnDestroy() { foreach (var c in cables) if (c != null && c.Grabbable != null) c.Grabbable.Released -= OnReleased; }

        CoolantPort PortByIndex(int n) { foreach (var p in ports) if (p.Index == n) return p; return null; }

        bool RackAllowed()
        {
            var sm = SessionManager.Instance;
            if (sm == null || sm.Current != Stage.Block2) return false;
            return !requireBothBlocks || EgressBlockController.CompletedBlocks >= 2;
        }

        void Update()
        {
            var sm = SessionManager.Instance;
            if (Current == State.Waiting && RackAllowed() && (QuestionnaireUI.Instance == null || !QuestionnaireUI.Instance.IsOpen))
            {
                Current = State.Active; _start = Time.time;
                DS.Log("Coolant rack active");
            }
        }

        // grip edges are read after every Update (like HandInteractor) so the press is never missed
        void LateUpdate()
        {
            if (Current == State.Solved) CheckLever();
        }

        void OnReleased(Grabbable g, Hand h)
        {
            var cable = g.GetComponent<CoolantCable>();
            if (cable != null) EvaluateRelease(cable, h);
        }

        /// <summary>What happens when a cable is let go: plug it, reject it (wrong port or lockout) or ignore it.</summary>
        public void EvaluateRelease(CoolantCable cable, Hand h)
        {
            if (Current != State.Active) return;
            if (cable == null || cable.Plugged) return;
            CoolantPort best = null; float bd = plugRadius;
            foreach (var p in ports)
            {
                if (p.Occupied) continue;
                float d = Vector3.Distance(cable.PlugTip.position, p.transform.position);
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) return;
            if (Locked) { StartCoroutine(SendBack(cable, 0.2f)); return; }
            if (best.Index == cable.CorrectPort)
            {
                cable.PlugInto(best);
                SfxEvents.Raise(SfxEvent.Plug, best.transform.position, best.Index);
                Haptics.Pulse(h, 0.5f, 0.08f);
                DS.Log($"Cable {cable.CableName} plugged into port {best.Index}");
                if (AllPlugged()) Solve();
            }
            else
            {
                WrongPlugs++;
                _lockedUntil = Time.time + wrongPlugLockoutSeconds;
                SfxEvents.Raise(SfxEvent.WrongPlug, best.transform.position, best.Index);
                Haptics.Pulse(h, 0.8f, 0.2f);
                DS.Log($"Wrong plug #{WrongPlugs}: {cable.CableName} into port {best.Index}");
                StartCoroutine(SendBack(cable, 0.4f));
            }
        }

        IEnumerator SendBack(CoolantCable cable, float delay)
        {
            yield return new WaitForSeconds(delay);
            cable.ResetToTable();
        }

        bool AllPlugged() { foreach (var c in cables) if (!c.Plugged) return false; return true; }

        void Solve()
        {
            Current = State.Solved;
            SolveTime = Time.time - _start;
            SfxEvents.Raise(SfxEvent.RackSolved, transform.position);
            VoiceBus.Request("R153");
            if (leverHandle != null) leverHandle.gameObject.SetActive(true);
            var sm = SessionManager.Instance;
            if (sm != null) sm.TryAdvance(Stage.CoolantRouted);
            DS.Log($"Rack solved in {SolveTime:F1} s with {WrongPlugs} wrong plugs");
        }

        void CheckLever()
        {
            if (leverHandle == null || !InputService.Exists) return;
            var input = InputService.Instance;
            if (!_pulling)
            {
                foreach (Hand h in new[] { Hand.Right, Hand.Left })
                {
                    if (!input.GripDown(h)) continue;
                    if (Vector3.Distance(input.HandPosition(h), leverHandle.position) <= leverGrabRadius)
                    {
                        _pulling = true; _hand = h; _pullStart = input.HandPosition(h);
                        Haptics.Pulse(h, 0.4f, 0.06f);
                        VoiceBus.Request("H007");
                        break;
                    }
                }
                return;
            }
            if (!input.Grip(_hand)) { _pulling = false; leverHandle.position = _leverHome; return; }
            float disp = Vector3.Dot(input.HandPosition(_hand) - _pullStart, Vector3.down);
            leverHandle.position = _leverHome + Vector3.down * Mathf.Clamp(disp, 0f, leverPullMeters + 0.03f);
            if (disp >= leverPullMeters) { _pulling = false; PullLever(); }
        }

        void PullLever()
        {
            Current = State.LeverPulled;
            TelemetryLogger.Log("coolant_time_s", Time.time - _start);
            TelemetryLogger.Log("coolant_wrong_plugs", WrongPlugs);
            SfxEvents.Raise(SfxEvent.Lever, leverHandle.position);
            VoiceBus.Request("H008");
            VoiceBus.Request("R160");
            SfxEvents.Raise(SfxEvent.BlastDoor, blastDoor != null ? blastDoor.position : transform.position);
            var sm = SessionManager.Instance;
            if (sm != null) sm.TryAdvance(Stage.Escaped);
            StartCoroutine(Escape());
            DS.Log("Lever pulled: blast doors");
        }

        IEnumerator Escape()
        {
            if (blastDoor != null)
            {
                Vector3 a = blastDoor.position, b = a + new Vector3(0, 0, blastDoorSlideMeters); float e = 0f;
                while (e < 2.5f) { e += Time.deltaTime; blastDoor.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, e / 2.5f)); yield return null; }
            }
            yield return new WaitForSeconds(hallwayDelaySeconds);
            // the escape lines (H008, R160) finish before the debrief loads: nothing said here carries into the next stage
            float waited = 0f;
            while (waited < 15f && ((NarrativeVoiceDirector.Instance != null && NarrativeVoiceDirector.Instance.IsBusy) || (HalcyonDirector.Instance != null && HalcyonDirector.Instance.IsBusy))) { waited += Time.deltaTime; yield return null; }
            if (SceneFlow.Instance != null) SceneFlow.Instance.GoToHallway();
        }
    }
}
