using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// The Trail: after the keypad is solved, at most 24 emissive arrow decals fade in along one route (no forks) from beside the terminal
    /// to the wall panel opposite the main door. Arriving at the panel slides it away; the service door behind it opens when the player pulls the handle (about 1.0 m).
    /// </summary>
    public class BreachTrail : MonoBehaviour
    {
        public enum State { Idle, TrailVisible, PanelOpen, DoorOpen }

        [SerializeField] Transform[] waypoints;
        [SerializeField] Transform wallPanel;
        [SerializeField] Transform serviceDoor;
        [SerializeField] Transform handle;
        [SerializeField] int maxDecals = 24;
        [SerializeField] float decalSpacing = 0.3f;
        [SerializeField] float decalFadeInterval = 0.12f;
        [SerializeField] float revealRadius = 1.1f;
        [SerializeField] float panelSlideMeters = 1.5f;
        [SerializeField] float panelSlideSeconds = 1.5f;
        [SerializeField] float grabRadius = 0.2f;
        [SerializeField] float pullMeters = 0.15f;
        [SerializeField] float doorSlideMeters = 0.95f;
        [SerializeField] float doorSlideSeconds = 1.5f;
        [SerializeField] Color decalColor = new Color(0.2f, 1f, 0.75f);

        public State Current { get; private set; } = State.Idle;
        public List<Transform> Decals { get; } = new List<Transform>();
        public float TrailTime { get; private set; }
        public Transform[] Waypoints { get => waypoints; set => waypoints = value; }
        public Transform Handle => handle;

        float _start;
        bool _pulling; Hand _hand; Vector3 _pullStart; Vector3 _handleHome;
        Mesh _arrow;

        void Start()
        {
            if (handle != null) { _handleHome = handle.localPosition; handle.gameObject.SetActive(false); }
            var sm = SessionManager.Instance;
            if (sm == null) return;
            sm.StageChanged += OnStage;
            if (sm.Current == Stage.KeypadSolved) StartTrail();
            else if (sm.Current >= Stage.TrailFollowed) JumpToOpen();
        }

        void OnDestroy() { if (SessionManager.Instance != null) SessionManager.Instance.StageChanged -= OnStage; }

        void OnStage(Stage from, Stage to) { if (to == Stage.KeypadSolved && isActiveAndEnabled) StartTrail(); }

        public void StartTrail()
        {
            if (Current != State.Idle) return;
            Current = State.TrailVisible; _start = Time.time;
            BuildDecals();
            StartCoroutine(FadeIn());
            VoiceBus.Request("R050");
            DS.Log($"Trail visible: {Decals.Count} decals");
        }

        void JumpToOpen()
        {
            Current = State.DoorOpen;
            if (wallPanel != null) wallPanel.position += new Vector3(0, 0, -panelSlideMeters);
            if (serviceDoor != null) serviceDoor.position += new Vector3(0, 0, -doorSlideMeters);
        }

        Mesh ArrowMesh()
        {
            if (_arrow != null) return _arrow;
            _arrow = new Mesh { name = "TrailArrow" };
            _arrow.vertices = new[] { new Vector3(0, 0, 0.11f), new Vector3(-0.1f, 0, -0.11f), new Vector3(0, 0, -0.04f), new Vector3(0.1f, 0, -0.11f) };
            _arrow.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _arrow.RecalculateNormals();
            return _arrow;
        }

        void BuildDecals()
        {
            if (waypoints == null || waypoints.Length < 2) return;
            var pts = new List<Vector3>();
            foreach (var w in waypoints) pts.Add(new Vector3(w.position.x, 0.015f, w.position.z));
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            float spacing = Mathf.Max(decalSpacing, total / Mathf.Max(1, maxDecals - 1));
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = decalColor };
            mat.SetColor("_BaseColor", decalColor);
            var root = new GameObject("TrailDecals").transform;
            root.SetParent(transform, false);
            float carry = 0f; int seg = 1; Vector3 cur = pts[0];
            while (seg < pts.Count && Decals.Count < maxDecals)
            {
                Vector3 to = pts[seg]; float d = Vector3.Distance(cur, to);
                Vector3 dir = d > 0.0001f ? (to - cur) / d : Vector3.forward;
                float need = spacing - carry;
                if (d >= need)
                {
                    cur += dir * need; carry = 0f;
                    AddDecal(root, mat, cur, dir);
                }
                else { carry += d; cur = to; seg++; }
            }
            if (Decals.Count == 0) AddDecal(root, mat, pts[0], (pts[1] - pts[0]).normalized);
        }

        void AddDecal(Transform root, Material mat, Vector3 pos, Vector3 dir)
        {
            var g = new GameObject("Decal_" + Decals.Count);
            g.transform.SetParent(root, false);
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z), Vector3.up);
            g.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            g.transform.localScale = Vector3.zero;
            Decals.Add(g.transform);
        }

        IEnumerator FadeIn()
        {
            foreach (var d in Decals)
            {
                float t = 0f;
                while (t < decalFadeInterval) { t += Time.deltaTime; d.localScale = Vector3.one * Mathf.Clamp01(t / decalFadeInterval); yield return null; }
                d.localScale = Vector3.one;
            }
        }

        void Update()
        {
            if (Current == State.TrailVisible) CheckArrival();
        }

        // grip edges are read after every Update (like HandInteractor) so the press is never missed
        void LateUpdate()
        {
            if (Current == State.PanelOpen) CheckPull();
        }

        /// <summary>A floor point 0.6 m in front of the wall panel (inside the reveal radius). The last waypoint itself touches the wall, where a teleport is refused.</summary>
        public Vector3 PanelStandPoint()
        {
            var w = waypoints; Vector3 last = w[w.Length - 1].position, prev = w[w.Length - 2].position;
            Vector3 dir = last - prev; dir.y = 0; dir.Normalize();
            Vector3 p = last - dir * 0.6f; p.y = 0f; return p;
        }

        void CheckArrival()
        {
            var rig = PlayerRig.Instance;
            if (rig == null || rig.Head == null || wallPanel == null) return;
            Vector3 d = rig.Head.position - wallPanel.position; d.y = 0;
            if (d.magnitude > revealRadius) return;
            Current = State.PanelOpen;
            StartCoroutine(Slide(wallPanel, new Vector3(0, 0, -panelSlideMeters), panelSlideSeconds, () =>
            {
                if (handle != null) handle.gameObject.SetActive(true);
            }));
            SfxEvents.Raise(SfxEvent.ServiceDoor, wallPanel.position);
            DS.Log("Wall panel opening");
        }

        void CheckPull()
        {
            if (handle == null || !handle.gameObject.activeSelf || !InputService.Exists) return;
            var input = InputService.Instance;
            if (!_pulling)
            {
                foreach (Hand h in new[] { Hand.Right, Hand.Left })
                {
                    if (!input.GripDown(h)) continue;
                    if (Vector3.Distance(input.HandPosition(h), handle.position) <= grabRadius)
                    {
                        _pulling = true; _hand = h; _pullStart = input.HandPosition(h);
                        Haptics.Pulse(h, 0.4f, 0.06f);
                        break;
                    }
                }
                return;
            }
            if (!input.Grip(_hand)) { _pulling = false; handle.localPosition = _handleHome; return; }
            // pull direction: from the door toward the room (west, -x)
            float disp = Vector3.Dot(input.HandPosition(_hand) - _pullStart, Vector3.left);
            handle.localPosition = _handleHome + new Vector3(0, 0, 0) ; // handle visual follows via world offset below
            handle.position = handle.parent.TransformPoint(_handleHome) + Vector3.left * Mathf.Clamp(disp, 0f, pullMeters + 0.03f);
            if (disp >= pullMeters)
            {
                _pulling = false;
                OpenDoor();
            }
        }

        void OpenDoor()
        {
            Current = State.DoorOpen;
            TrailTime = Time.time - _start;
            TelemetryLogger.Log("trail_time_s", TrailTime);
            SfxEvents.Raise(SfxEvent.ServiceDoor, serviceDoor != null ? serviceDoor.position : transform.position);
            VoiceBus.Request("R052");
            if (serviceDoor != null) StartCoroutine(Slide(serviceDoor, new Vector3(0, 0, -doorSlideMeters), doorSlideSeconds, null));
            var sm = SessionManager.Instance;
            if (sm != null) sm.TryAdvance(Stage.TrailFollowed);
            DS.Log($"Service door open after {TrailTime:F1} s");
        }

        IEnumerator Slide(Transform t, Vector3 delta, float seconds, System.Action done)
        {
            Vector3 a = t.position, b = a + delta; float e = 0f;
            while (e < seconds) { e += Time.deltaTime; t.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, e / seconds)); yield return null; }
            t.position = b;
            done?.Invoke();
        }
    }
}
