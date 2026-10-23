using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Beacon game (stage EarCheck). Six coloured beacons stand on a supply table. A poster on the south wall shows the colour order;
    /// the player carries each beacon to the matching numbered slot on the second table. A correct beacon locks in (slot turns green),
    /// a wrong one is sent back to the supply table (slot flashes red, no penalty beyond the count). No spatial audio is involved.
    /// Everything is built from primitives in code (see KNOWN_ISSUES). Completing the game starts the keypad.
    /// </summary>
    public class EarCheckController : MonoBehaviour
    {
        [SerializeField] ThermalKeypad keypad;
        [SerializeField] Vector3 supplyTableCenter = new Vector3(-1.95f, 0f, -1.25f);
        [SerializeField] Vector3 slotTableCenter = new Vector3(-0.55f, 0f, -1.25f);
        [SerializeField] Vector3 posterCenter = new Vector3(-1.25f, 1.75f, -2.85f);
        [SerializeField] Vector2 posterSize = new Vector2(1.5f, 0.85f);
        [SerializeField] float tableHeight = 0.9f;
        [SerializeField] float slotSpacing = 0.22f;
        [SerializeField] float snapRadius = 0.11f;
        [SerializeField] float sendBackDelaySeconds = 0.5f;
        [SerializeField] float flashSeconds = 0.8f;
        [SerializeField] int shuffleSeed = -1;

        public static readonly string[] ColorNames = { "Red", "Green", "Blue", "Yellow", "Purple", "White" };
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.12f, 0.1f), new Color(0.1f, 0.9f, 0.25f), new Color(0.15f, 0.35f, 1f),
            new Color(1f, 0.9f, 0.1f), new Color(0.7f, 0.2f, 0.95f), new Color(0.95f, 0.95f, 1f)
        };

        public bool Active { get; private set; }
        public bool Completed { get; private set; }
        public int PlacedCount { get; private set; }
        public int WrongPlacements { get; private set; }
        public float SolveTime { get; private set; }
        public BeaconVisual[] Beacons { get; private set; } = new BeaconVisual[0];
        /// <summary>Slot centres on the table top, slot 1 first (slot 1 is the leftmost as seen from the player's side of the table).</summary>
        public Transform[] Slots { get; private set; } = new Transform[0];
        /// <summary>Colour index (into ColorNames) that belongs in each slot.</summary>
        public int[] Order { get; private set; } = new int[0];
        public ThermalKeypad Keypad { get => keypad; set => keypad = value; }

        Transform _station;
        Material[] _slotMats;
        BeaconVisual[] _inSlot;
        float _begin;
        bool _started;

        void Start()
        {
            Build();
            var sm = SessionManager.Instance;
            if (sm != null)
            {
                sm.StageChanged -= OnStage; sm.StageChanged += OnStage;
                if (sm.Current == Stage.EarCheck) Begin();
                else if (sm.Current > Stage.EarCheck) { Completed = true; LockAll(); }
            }
        }

        void OnDestroy() { if (SessionManager.Instance != null) SessionManager.Instance.StageChanged -= OnStage; }

        void OnStage(Stage from, Stage to) { if (to == Stage.EarCheck && isActiveAndEnabled) Begin(); }

        public void Begin()
        {
            if (_started) return;
            _started = true; Active = true; _begin = Time.time;
            foreach (var b in Beacons) if (b.Grabbable != null) b.Grabbable.enabled = true;
            VoiceBus.Request("R030");
            DS.Log("Beacon game started, order: " + string.Join(" ", System.Array.ConvertAll(Order, i => ColorNames[i])));
        }

        // ---------- evaluation ----------
        void OnReleased(Grabbable g, Hand h)
        {
            if (!Active || Completed) return;
            var b = g.GetComponent<BeaconVisual>();
            if (b == null || b.PlacedSlot != 0) return;
            int slot = NearestFreeSlot(g.transform.position);
            if (slot < 0) { StartCoroutine(RecoverFromFloor(b)); return; }
            if (Order[slot] == b.Index) Lock(b, slot, h);
            else
            {
                WrongPlacements++;
                SfxEvents.Raise(SfxEvent.WrongPlug, Slots[slot].position, slot + 1);
                Haptics.Pulse(h, 0.8f, 0.2f);
                DS.Log($"Beacon {b.ColorName} on slot {slot + 1} is wrong (#{WrongPlacements})");
                StartCoroutine(Flash(slot, new Color(1f, 0.1f, 0.05f)));
                StartCoroutine(SendBack(b));
            }
        }

        int NearestFreeSlot(Vector3 p)
        {
            int best = -1; float bd = snapRadius;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (_inSlot[i] != null) continue;
                Vector3 d = p - Slots[i].position;
                if (d.y < -0.06f || d.y > 0.3f) continue; // must be above the table, not under it or far above
                d.y = 0;
                if (d.magnitude < bd) { bd = d.magnitude; best = i; }
            }
            return best;
        }

        void Lock(BeaconVisual b, int slot, Hand h)
        {
            b.PlacedSlot = slot + 1; _inSlot[slot] = b; PlacedCount++;
            var g = b.Grabbable;
            g.ForceRelease();
            g.Body.isKinematic = true;
            g.transform.SetPositionAndRotation(Slots[slot].position, Quaternion.identity);
            g.Body.position = g.transform.position; g.Body.rotation = g.transform.rotation;
            g.enabled = false;
            foreach (var c in b.GetComponents<Collider>()) c.enabled = false;
            _slotMats[slot].SetColor("_BaseColor", new Color(0.1f, 0.95f, 0.3f)); _slotMats[slot].color = new Color(0.1f, 0.95f, 0.3f);
            SfxEvents.Raise(SfxEvent.Plug, Slots[slot].position, slot + 1);
            Haptics.Pulse(h, 0.5f, 0.08f);
            VoiceBus.Request("R031");
            DS.Log($"Beacon {b.ColorName} locked into slot {slot + 1} ({PlacedCount}/{Slots.Length})");
            if (PlacedCount >= Slots.Length) Finish();
        }

        /// <summary>A beacon let go on the floor goes back to the supply table after a moment, so nobody has to crouch for it.</summary>
        IEnumerator RecoverFromFloor(BeaconVisual b)
        {
            yield return new WaitForSeconds(2.5f);
            if (b.PlacedSlot == 0 && !b.Grabbable.IsHeld && b.transform.position.y < tableHeight - 0.2f) b.Grabbable.ResetToStart();
        }

        IEnumerator SendBack(BeaconVisual b)
        {
            yield return new WaitForSeconds(sendBackDelaySeconds);
            if (b.PlacedSlot == 0) b.Grabbable.ResetToStart();
        }

        IEnumerator Flash(int slot, Color c)
        {
            var m = _slotMats[slot];
            Color rest = new Color(0.16f, 0.17f, 0.2f);
            for (int i = 0; i < 3; i++)
            {
                m.SetColor("_BaseColor", c); m.color = c; yield return new WaitForSeconds(flashSeconds / 6f);
                if (_inSlot[slot] != null) yield break;
                m.SetColor("_BaseColor", rest); m.color = rest; yield return new WaitForSeconds(flashSeconds / 6f);
            }
        }

        void Finish()
        {
            Active = false; Completed = true;
            SolveTime = Time.time - _begin;
            VoiceBus.Request("R032");
            TelemetryLogger.Log("beacon_game_time_s", SolveTime);
            TelemetryLogger.Log("beacon_wrong_placements", WrongPlacements);
            // the old pointing-error columns stay in the CSV: 0 means "not measured" (no spatial task any more), the time column holds the mean seconds per beacon
            TelemetryLogger.Log("earcheck_mean_error_deg", 0f);
            TelemetryLogger.Log("earcheck_mean_rt_s", SolveTime / Mathf.Max(1, Slots.Length));
            DS.Log($"Beacon game complete in {SolveTime:F1} s with {WrongPlacements} wrong placements");
            if (keypad != null) keypad.Begin();
        }

        /// <summary>Entered by a stage jump past the game: the beacons already sit in their slots.</summary>
        void LockAll()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                BeaconVisual b = null;
                foreach (var x in Beacons) if (x.Index == Order[i]) b = x;
                if (b == null) continue;
                b.PlacedSlot = i + 1; _inSlot[i] = b; PlacedCount++;
                b.Grabbable.Body.isKinematic = true;
                b.transform.SetPositionAndRotation(Slots[i].position, Quaternion.identity);
                foreach (var c in b.GetComponents<Collider>()) c.enabled = false;
                _slotMats[i].SetColor("_BaseColor", new Color(0.1f, 0.95f, 0.3f)); _slotMats[i].color = new Color(0.1f, 0.95f, 0.3f);
            }
        }

        // ---------- construction ----------
        void Build()
        {
            _station = new GameObject("BeaconStation").transform;
            _station.SetParent(transform, false);

            int n = ColorNames.Length;
            var rng = new System.Random(shuffleSeed >= 0 ? shuffleSeed : StudyConfig.Data.seed);
            Order = Shuffled(n, rng);
            var home = Shuffled(n, rng); // which spot on the supply table each beacon starts on

            var tableMat = MeshFactory.Lit(new Color(0.34f, 0.37f, 0.42f), 0.35f, 0.5f);
            var topMat = MeshFactory.Lit(new Color(0.62f, 0.65f, 0.7f), 0.5f, 0.3f);
            BuildTable("SupplyTable", supplyTableCenter, new Vector2(1.45f, 0.5f), tableMat, topMat);
            BuildTable("SlotTable", slotTableCenter, new Vector2(1.45f, 0.5f), tableMat, topMat);

            // slots: number 1 is the leftmost slot from the player's side (standing north of the table, facing the poster)
            float top = tableHeight;
            Slots = new Transform[n]; _slotMats = new Material[n]; _inSlot = new BeaconVisual[n];
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            for (int i = 0; i < n; i++)
            {
                float x = slotTableCenter.x + ((n - 1) * 0.5f - i) * slotSpacing;
                var pos = new Vector3(x, top, slotTableCenter.z);
                var slot = new GameObject("Slot_" + (i + 1)).transform;
                slot.SetParent(_station, false); slot.position = pos;
                Slots[i] = slot;
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // primitive: slot ring (KNOWN_ISSUES)
                ring.name = "Ring"; Destroy(ring.GetComponent<Collider>());
                ring.transform.SetParent(slot, false); ring.transform.localPosition = new Vector3(0, 0.0005f, 0); ring.transform.localScale = new Vector3(0.17f, 0.0015f, 0.17f);
                ring.GetComponent<MeshRenderer>().sharedMaterial = WorldUI.ColorMaterial(new Color(0.85f, 0.88f, 0.95f));
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Disc"; Destroy(disc.GetComponent<Collider>());
                disc.transform.SetParent(slot, false); disc.transform.localPosition = new Vector3(0, 0.0015f, 0); disc.transform.localScale = new Vector3(0.145f, 0.0015f, 0.145f);
                _slotMats[i] = new Material(unlit); _slotMats[i].SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.2f)); _slotMats[i].color = new Color(0.16f, 0.17f, 0.2f);
                disc.GetComponent<MeshRenderer>().sharedMaterial = _slotMats[i];
                var label = WorldUI.Text(slot, (i + 1).ToString(), WorldUI.Cm(0.06f), new Vector3(0, 0.006f, 0), new Vector2(0.14f, 0.14f), TMPro.TextAlignmentOptions.Center, new Color(0.9f, 0.95f, 1f));
                label.fontStyle = TMPro.FontStyles.Bold;
                label.transform.localRotation = Quaternion.Euler(90f, 180f, 0f); // flat, readable from the player's side
            }

            // beacons on the supply table
            Beacons = new BeaconVisual[n];
            for (int i = 0; i < n; i++)
            {
                int spot = home[i];
                int col = spot % 3, row = spot / 3;
                var pos = new Vector3(supplyTableCenter.x + (1 - col) * 0.38f, tableHeight + 0.005f, supplyTableCenter.z + (row == 0 ? 0.1f : -0.1f));
                Beacons[i] = BuildBeacon(i, pos);
            }

            BuildPoster();
        }

        static int[] Shuffled(int n, System.Random rng)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = a[i]; a[i] = a[j]; a[j] = t; }
            return a;
        }

        void BuildTable(string name, Vector3 center, Vector2 size, Material body, Material top)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Cube); // primitive: table (KNOWN_ISSUES)
            t.name = name; t.transform.SetParent(_station, false);
            t.transform.localScale = new Vector3(size.x - 0.06f, tableHeight - 0.05f, size.y - 0.06f);
            t.transform.position = new Vector3(center.x, (tableHeight - 0.05f) * 0.5f, center.z);
            t.GetComponent<MeshRenderer>().sharedMaterial = body;
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name + "_Top"; slab.transform.SetParent(_station, false);
            slab.transform.localScale = new Vector3(size.x, 0.05f, size.y);
            slab.transform.position = new Vector3(center.x, tableHeight - 0.025f, center.z);
            slab.GetComponent<MeshRenderer>().sharedMaterial = top; // its collider is the surface the beacons rest on
        }

        BeaconVisual BuildBeacon(int index, Vector3 pos)
        {
            var go = new GameObject("Beacon_" + ColorNames[index]);
            go.transform.SetParent(_station, false);
            go.transform.position = pos;
            Color c = Colors[index];
            var bodyMat = MeshFactory.Lit(c, 0.55f, 0.1f, c * 0.25f);
            var dark = MeshFactory.Lit(new Color(0.18f, 0.19f, 0.22f), 0.4f, 0.6f);
            var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit")); glow.SetColor("_BaseColor", c); glow.color = c;

            Prim(go.transform, PrimitiveType.Cylinder, "Base", new Vector3(0, 0.012f, 0), new Vector3(0.1f, 0.012f, 0.1f), dark);
            Prim(go.transform, PrimitiveType.Cylinder, "Body", new Vector3(0, 0.07f, 0), new Vector3(0.082f, 0.05f, 0.082f), bodyMat);
            Prim(go.transform, PrimitiveType.Cylinder, "Band", new Vector3(0, 0.07f, 0), new Vector3(0.088f, 0.008f, 0.088f), dark);
            Prim(go.transform, PrimitiveType.Sphere, "Lens", new Vector3(0, 0.128f, 0), new Vector3(0.07f, 0.05f, 0.07f), glow);

            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.07f, 0); cap.radius = 0.055f; cap.height = 0.15f;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.25f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var vis = go.AddComponent<BeaconVisual>();
            vis.Index = index; vis.ColorName = ColorNames[index]; vis.Tint = c;
            var g = go.AddComponent<Grabbable>();
            g.enabled = false; // loose beacons cannot be taken until the game starts
            g.Released += OnReleased;
            vis.Grabbable = g;
            return vis;
        }

        static void Prim(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material m)
        {
            var p = GameObject.CreatePrimitive(type); // primitive: beacon parts (KNOWN_ISSUES)
            p.name = name; Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(parent, false);
            p.transform.localPosition = localPos; p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        void BuildPoster()
        {
            // faces north (toward the player at the slot table): forward points at the south wall
            var board = PosterBuilder.Board(_station, "BeaconPoster", posterCenter, Quaternion.Euler(0, 180, 0), posterSize, UiStyle.Back, UiStyle.Frame);
            PosterBuilder.Label(board, "SIGNAL SEQUENCE", 0.075f, new Vector3(0, posterSize.y * 0.5f - 0.085f, 0), new Color(0.75f, 0.9f, 1f), posterSize.x * 0.9f);
            int n = Order.Length;
            float spacing = (posterSize.x - 0.2f) / n;
            for (int i = 0; i < n; i++)
            {
                // viewer's right is local +X, and slot 1 is the leftmost: x grows with the slot number
                float x = (i - (n - 1) * 0.5f) * spacing;
                PosterBuilder.Label(board, (i + 1).ToString(), 0.085f, new Vector3(x, 0.17f, 0), new Color(0.9f, 0.95f, 1f), spacing);
                PosterBuilder.Circle(board, "Dot_" + (i + 1), spacing * 0.78f, new Color(0.9f, 0.92f, 1f), new Vector3(x, -0.0f, 0));
                PosterBuilder.Circle(board, "Colour_" + (i + 1), spacing * 0.66f, Colors[Order[i]], new Vector3(x, -0.0f, -0.001f));
                PosterBuilder.Label(board, ColorNames[Order[i]].ToUpperInvariant(), 0.04f, new Vector3(x, -0.17f, 0), Colors[Order[i]], spacing);
            }
            PosterBuilder.Label(board, "Put the beacons on the table in this order, left to right.", 0.04f, new Vector3(0, -posterSize.y * 0.5f + 0.07f, 0), new Color(0.8f, 0.85f, 0.9f), posterSize.x * 0.94f, false);
        }
    }
}
