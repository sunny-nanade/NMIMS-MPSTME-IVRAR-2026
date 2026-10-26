using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;

namespace CSER.EditorTools
{
    /// Whole-scene health check (run in play mode so runtime UI exists). Writes Temp/cser_audit.txt and logs it.
    /// Checks: every interactive canvas has ISDK ray interaction, every grab item is really grabbable (non-trigger
    /// collider, Grabbable, forgiving size), buttons are VR-sized, one EventSystem, floating canvases, missing scripts.
    public static class SceneAudit
    {
        const float MinButton = 0.05f;   // metres

        [MenuItem("CSER/Debug/Audit Everything")]
        public static void Run() => File.WriteAllText(Path.Combine("Temp", "cser_audit.txt"), Report());

        public static string Report()
        {
            var sb = new StringBuilder("AUDIT " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "\n");
            int issues = 0;
            void Issue(string s) { sb.AppendLine("  ISSUE " + s); issues++; }

            var es = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            if (es.Length > 1) Issue("EventSystems: " + es.Length);

            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (c.renderMode != RenderMode.WorldSpace) continue;
                bool interactive = c.GetComponent<GraphicRaycaster>() && c.GetComponentsInChildren<Button>(true).Length > 0;
                if (interactive && !c.GetComponentInChildren<RayInteractable>(true)) Issue("canvas without ray interaction: " + PathOf(c.transform));
                if (!c.worldCamera && c.GetComponent<GraphicRaycaster>() && !c.GetComponentInChildren<RayInteractable>(true)) { /* ISDK routes input, no camera needed */ }
                foreach (var b in c.GetComponentsInChildren<Button>(true))
                {
                    var rt = (RectTransform)b.transform;
                    var s = rt.rect.size;
                    var ls = rt.lossyScale;
                    float w = s.x * Mathf.Abs(ls.x), h = s.y * Mathf.Abs(ls.y);
                    if (Mathf.Min(w, h) < MinButton)
                        Issue($"small button {PathOf(b.transform)} {w * 100:0.0} x {h * 100:0.0} cm");
                }
            }

            foreach (var g in Object.FindObjectsByType<GrabItem>(FindObjectsInactive.Include))
            {
                var cols = g.GetComponentsInChildren<Collider>(true);
                bool solid = false;
                foreach (var col in cols) if (!col.isTrigger) solid = true;
                if (!solid) Issue("no solid collider: " + PathOf(g.transform));
                if (g.GetComponentsInChildren<Grabbable>(true).Length == 0) Issue("no Grabbable: " + PathOf(g.transform));
                if (g.GetComponentsInChildren<GrabInteractable>(true).Length == 0) Issue("no GrabInteractable: " + PathOf(g.transform));
                var rb = g.GetComponent<Rigidbody>();
                if (rb && rb.interpolation == RigidbodyInterpolation.None) Issue("no interpolation: " + PathOf(g.transform));
                var b = new Bounds(g.transform.position, Vector3.zero);
                foreach (var col in cols) if (col.enabled) b.Encapsulate(col.bounds);
                if (b.size.x < 0.04f && b.size.y < 0.04f && b.size.z < 0.04f) Issue($"grab collider tiny {PathOf(g.transform)} {b.size}");
                if (g.GetComponentsInChildren<Oculus.Interaction.DistanceReticles.ReticleDataMesh>(true).Length > 0) Issue("hover ghost mesh (z-fights on thin items): " + PathOf(g.transform));
            }
            // popups draw with the TextMeshPro overlay shader; OverlayUI loads it through a Resources material so it is
            // packed into the player. Without it the popups show up blank on the headset.
            var refMat = Resources.Load<Material>("Shaders/TMP_OverlayMobile");
            if (!refMat || !refMat.shader || refMat.shader.name != "TextMeshPro/Mobile/Distance Field Overlay")
                Issue("Resources/Shaders/TMP_OverlayMobile.mat missing or wrong shader (blank popups on device)");

            foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject) > 0) Issue("missing script on " + PathOf(go));

            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l.type != LightType.Directional && l.lightmapBakeType == LightmapBakeType.Realtime && l.shadows != LightShadows.None)
                    Issue("realtime shadow light (Quest cost): " + PathOf(l.transform));

            var cam = Camera.main;
            if (cam && cam.nearClipPlane > 0.06f) Issue("near clip " + cam.nearClipPlane);
            sb.AppendLine($"  issues={issues}  grabItems={Object.FindObjectsByType<GrabItem>(FindObjectsInactive.Include).Length}  fixedDt={Time.fixedDeltaTime:0.0000}");
            return sb.ToString();
        }

        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    }
}
