using System.Collections.Generic;
using System.Linq;
using CSER;
using UnityEditor.Events;
using UnityEngine;

namespace CSER.EditorTools
{
    public static partial class EscapeRoomBuilder
    {
        /// Fred's desk drawers (chapter 2): the desk model's own drawer meshes slide open on click. Pure set dressing:
        /// a few tidy personal items per drawer, parented to the drawer, no physics and no puzzle data.
        static void FredDrawers()
        {
            Transform desk = null;
            foreach (Transform t in env)
                if (t.name.StartsWith("metal_office_desk") && t.position.x > 0) desk = t;   // Fred's desk is on the right
            if (!desk) { log.Add("Fred's desk not found: no drawers"); return; }

            var drawers = new List<Transform>();
            foreach (Transform c in desk)
                if (c.name.Contains("_drawer_") && c.GetComponent<Renderer>()) drawers.Add(c);
            // top drawers first, so the contents table below reads top to bottom
            drawers = drawers.OrderByDescending(d => d.GetComponent<Renderer>().bounds.center.y).ThenBy(d => d.GetComponent<Renderer>().bounds.center.x).ToList();

            for (int i = 0; i < drawers.Count; i++)
            {
                var d = drawers[i];
                var b = d.GetComponent<Renderer>().bounds;
                // Model() marks the whole desk static: a statically batched drawer would not move, so it is made
                // dynamic again and lit from the light probes (MarkStatic skips SlideDrawer hierarchies)
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(d.gameObject, 0);
                if (d.TryGetComponent<MeshRenderer>(out var mr)) mr.receiveGI = UnityEngine.ReceiveGI.LightProbes;
                var col = d.gameObject.AddComponent<BoxCollider>();
                col.center = d.InverseTransformPoint(b.center);
                var ls = d.InverseTransformVector(b.size);
                col.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                // the desk faces the chair (-z): drawers pull out toward the player
                var slide = d.gameObject.AddComponent<SlideDrawer>();
                slide.openOffset = d.parent.InverseTransformVector(new Vector3(0, 0, -Mathf.Min(0.28f, b.size.z * 0.7f)));
                var click = MakeClickable(d.gameObject, null);
                UnityEventTools.AddPersistentListener(click.onClick, slide.Toggle);
                FillDrawer(d, b, i);
                log.Add($"Drawer {d.name}: {b.min:F2} - {b.max:F2}");
            }
        }

        static void FillDrawer(Transform drawer, Bounds b, int index)
        {
            var contents = new GameObject("Contents").transform;
            contents.SetParent(drawer, false);
            // front part of the drawer: the part that comes out from under the desktop when it opens
            contents.SetPositionAndRotation(new Vector3(b.center.x, b.min.y + 0.012f, b.min.z + 0.15f), Quaternion.identity);
            var s = contents.lossyScale;
            contents.localScale = new Vector3(contents.localScale.x / s.x, contents.localScale.y / s.y, contents.localScale.z / s.z);
            float w = b.size.x * 0.32f, dz = 0.08f;   // keep items well inside the drawer walls
            var ink = new Color(0.12f, 0.12f, 0.16f);

            GameObject Item(string name, Vector3 pos, Vector3 size, Color c, float yaw = 0)
            {
                var go = LocalBox(name, contents, pos + Vector3.up * size.y / 2, size, Mat("Drawer_" + name, c, 0.3f), false);
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                return go;
            }
            void Text(Transform on, string text, Vector2 size)
            {
                // flat on top, readable from the chair side; 1.5 mm above the surface (no z-fighting)
                var top = on.position + Vector3.up * (on.lossyScale.y / 2 + 0.0015f);
                Label(on, text, top, on.rotation * Quaternion.Euler(90, 0, 0), size, ink, world: true);
            }

            switch (index)
            {
                case 0: // stationery
                    Text(Item("PaperStack", new Vector3(-w * 0.4f, 0, 0), new Vector3(0.15f, 0.02f, 0.2f), new Color(0.95f, 0.94f, 0.9f)).transform, "Meeting notes", new Vector2(0.12f, 0.04f));
                    Item("Pen", new Vector3(w * 0.6f, 0, -dz * 0.3f), new Vector3(0.012f, 0.012f, 0.14f), new Color(0.15f, 0.3f, 0.75f), 10);
                    Item("StickyPad", new Vector3(w * 0.6f, 0, dz * 0.5f), new Vector3(0.076f, 0.015f, 0.076f), new Color(0.98f, 0.88f, 0.3f), -8);
                    break;
                case 1: // personal
                    var frame = Item("FamilyPhoto", new Vector3(-w * 0.3f, 0, 0), new Vector3(0.16f, 0.012f, 0.12f), new Color(0.35f, 0.22f, 0.12f), 6).transform;
                    LocalBox("Photo", frame, new Vector3(0, 0.55f, 0), new Vector3(0.85f, 0.1f, 0.8f), Mat("Drawer_PhotoPrint", new Color(0.55f, 0.72f, 0.85f), 0.6f), false);
                    Text(frame, "Lake day 2025 - the whole family", new Vector2(0.13f, 0.05f));
                    Text(Item("Chocolate", new Vector3(w * 0.65f, 0, 0), new Vector3(0.06f, 0.012f, 0.14f), new Color(0.38f, 0.2f, 0.1f), -5).transform, "CHOC", new Vector2(0.05f, 0.03f));
                    break;
                case 2: // keepsakes
                    Text(Item("BirthdayCard", new Vector3(-w * 0.35f, 0, 0), new Vector3(0.15f, 0.006f, 0.1f), new Color(0.95f, 0.75f, 0.8f), 4).transform, "Happy birthday Fred!", new Vector2(0.13f, 0.05f));
                    Item("GlassesCase", new Vector3(w * 0.6f, 0, 0), new Vector3(0.06f, 0.035f, 0.15f), new Color(0.15f, 0.15f, 0.18f));
                    break;
                case 3:
                    Text(Item("BusinessCards", new Vector3(0, 0, 0), new Vector3(0.09f, 0.025f, 0.055f), new Color(0.97f, 0.97f, 0.97f)).transform, "F. Jansen", new Vector2(0.07f, 0.03f));
                    Item("Mints", new Vector3(w * 0.7f, 0, dz * 0.3f), new Vector3(0.05f, 0.015f, 0.08f), new Color(0.3f, 0.7f, 0.55f), 12);
                    break;
                default: // lower drawers: a single hanging folder, most of the drawer left empty
                    Text(Item("Folder", new Vector3(0, 0, 0), new Vector3(0.22f, 0.01f, 0.28f), new Color(0.3f, 0.45f, 0.3f)).transform, index == 4 ? "Receipts 2025" : "Manuals", new Vector2(0.18f, 0.05f));
                    break;
            }
        }
    }
}
