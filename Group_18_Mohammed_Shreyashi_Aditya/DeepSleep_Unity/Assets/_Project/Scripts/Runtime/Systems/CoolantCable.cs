using System.Collections.Generic;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// A coolant cable (PF_Cable_01..03). Red goes to port 2, blue to port 5, yellow to port 3.
    /// The look is built here from a bent tube and two plugs (primitives, see KNOWN_ISSUES), with one capsule collider so the cable lies on tables and the floor.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    [DefaultExecutionOrder(-20)]
    public class CoolantCable : MonoBehaviour
    {
        [SerializeField] string cableName = "Red";
        [SerializeField] Color tint = Color.red;
        [SerializeField] int correctPort = 2;
        [SerializeField] Transform plugTip;
        [SerializeField] float length = 0.5f;
        [SerializeField] float wireRadius = 0.011f;
        [SerializeField] float colliderRadius = 0.03f;

        public string CableName => cableName;
        public Color Tint => tint;
        public int CorrectPort { get => correctPort; set => correctPort = value; }
        public Transform PlugTip => plugTip != null ? plugTip : transform;
        public Grabbable Grabbable { get; private set; }
        public bool Plugged { get; private set; }
        public CoolantPort Port { get; private set; }

        void Awake()
        {
            Grabbable = GetComponent<Grabbable>();
            BuildVisuals();
        }

        void Start() { SettleOnSurface(); }

        void BuildVisuals()
        {
            // the imported model was replaced by the cable built here
            var old = transform.Find("Model");
            if (old != null) Destroy(old.gameObject);
            var oldVis = transform.Find("CableVisual");
            if (oldVis != null) Destroy(oldVis.gameObject);

            float half = length * 0.5f;
            var vis = new GameObject("CableVisual").transform;
            vis.SetParent(transform, false);

            // wire: a gentle S-bend along local Z
            var path = new List<Vector3>();
            const int n = 16;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float z = Mathf.Lerp(-half + 0.045f, half - 0.045f, t);
                path.Add(new Vector3(Mathf.Sin(t * Mathf.PI * 2f) * 0.035f, Mathf.Sin(t * Mathf.PI) * 0.012f, z));
            }
            var wireMat = MeshFactory.Lit(tint, 0.65f, 0f, tint * 0.25f);
            var wire = new GameObject("Wire");
            wire.transform.SetParent(vis, false);
            wire.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Tube("CableWire", path, wireRadius, 10, true);
            wire.AddComponent<MeshRenderer>().sharedMaterial = wireMat;

            // two plugs: a coloured boot and a metal connector, the second end is the plug tip
            var metal = MeshFactory.Lit(new Color(0.78f, 0.8f, 0.84f), 0.85f, 0.9f);
            var bootMat = MeshFactory.Lit(tint * 0.6f, 0.4f, 0f, tint * 0.15f);
            for (int end = 0; end < 2; end++)
            {
                float sign = end == 0 ? -1f : 1f;
                Vector3 pEnd = path[end == 0 ? 0 : n];
                AddBox(vis, "Boot" + end, new Vector3(pEnd.x, pEnd.y, sign * (half - 0.07f)), new Vector3(0.034f, 0.022f, 0.05f), bootMat);
                AddBox(vis, "Connector" + end, new Vector3(pEnd.x, pEnd.y, sign * (half - 0.022f)), new Vector3(0.022f, 0.009f, 0.044f), metal);
            }

            var capsule = GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 2; capsule.radius = colliderRadius; capsule.height = length; capsule.center = new Vector3(0, 0.004f, 0);

            // plug tip at the far connector, pointing along local +Z (PlugInto turns it into the rack)
            if (plugTip == null)
            {
                var tip = transform.Find("PlugTip");
                plugTip = tip != null ? tip : new GameObject("PlugTip").transform;
            }
            plugTip.SetParent(transform, false);
            plugTip.localPosition = new Vector3(path[n].x, path[n].y, half);
            plugTip.localRotation = Quaternion.identity;

            var rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.mass = 0.15f; rb.linearDamping = 0.2f; rb.angularDamping = 0.5f; rb.interpolation = RigidbodyInterpolation.Interpolate; }
        }

        static void AddBox(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // primitive: cable plug (KNOWN_ISSUES)
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>Lowers the cable from where the scene placed it onto the surface below, so it starts lying on the table.</summary>
        void SettleOnSurface()
        {
            if (Plugged || Grabbable.IsHeld) return;
            var rb = Grabbable.Body;
            var origin = transform.position + Vector3.up * 0.3f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); // RaycastAll is unordered: the nearest surface is the table, not the floor
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<Grabbable>() != null) continue;
                var p = transform.position; p.y = h.point.y + colliderRadius + 0.004f;
                rb.position = p; transform.position = p;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                Grabbable.RememberPose();
                return;
            }
        }

        /// <summary>Snap the plug tip onto the port and freeze the cable there.</summary>
        public void PlugInto(CoolantPort port)
        {
            Plugged = true; Port = port; port.Occupant = this;
            Grabbable.ForceRelease();
            Grabbable.Body.isKinematic = true;
            Grabbable.enabled = false; // no longer grabbable once seated
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            // the tip points into the rack, then the tip is moved onto the socket
            transform.rotation = Quaternion.FromToRotation(PlugTip.forward, -port.transform.forward) * transform.rotation;
            transform.position += port.transform.position - PlugTip.position;
            Grabbable.Body.position = transform.position; Grabbable.Body.rotation = transform.rotation;
        }

        public void Unplug()
        {
            if (Port != null) Port.Occupant = null;
            Plugged = false; Port = null;
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            Grabbable.enabled = true;
            Grabbable.ResetToStart();
        }

        public void ResetToTable() { Grabbable.ResetToStart(); }
    }
}
