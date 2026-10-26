using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CSER.EditorTools
{
    /// Renders fixed viewpoints of each scene to PNG (Screenshots/ next to Assets) for visual checks, in edit or play mode.
    public static class ViewCapture
    {
        static readonly Dictionary<string, (string name, Vector3 pos, Vector3 euler)[]> Views = new Dictionary<string, (string, Vector3, Vector3)[]>
        {
            ["MainMenu"] = new[]
            {
                ("menu_front", new Vector3(0, 1.6f, 0), new Vector3(5, 0, 0)),
                ("menu_side", new Vector3(-2.2f, 1.8f, -2.2f), new Vector3(12, 40, 0)),
            },
            ["Ch1_NightShift"] = new[]
            {
                ("soc_start", new Vector3(0, 1.6f, -0.45f), new Vector3(14, 0, 0)),
                ("soc_overview", new Vector3(-3.6f, 2.7f, -2.7f), new Vector3(26, 48, 0)),
                ("soc_door", new Vector3(1.4f, 1.6f, -0.6f), new Vector3(6, 100, 0)),
                ("soc_laptop", new Vector3(-2.3f, 1.25f, 0.45f), new Vector3(30, 8, 0)),
            },
            ["Ch2_FredsOffice"] = new[]
            {
                ("off_start", new Vector3(0, 1.6f, -0.95f), new Vector3(12, 0, 0)),
                ("off_desk", new Vector3(0.72f, 1.45f, 0.05f), new Vector3(28, 0, 0)),
                ("off_door", new Vector3(0.4f, 1.6f, -0.6f), new Vector3(8, 105, 0)),
                ("off_left", new Vector3(0.2f, 1.55f, -0.5f), new Vector3(22, -110, 0)),
                ("off_drawers", new Vector3(0.82f, 1.15f, -0.45f), new Vector3(42, 0, 0)),
                ("off_screen", new Vector3(0.72f, 1.17f, 0.22f), new Vector3(0, 0, 0)),
            },
            ["Ch3_ServerRoom"] = new[]
            {
                ("srv_start", new Vector3(2.55f, 1.6f, 0), new Vector3(8, -90, 0)),
                ("srv_implant", new Vector3(-0.68f, 1.45f, -0.25f), new Vector3(18, 0, 0)),
                ("srv_cart", new Vector3(2.4f, 1.5f, 0.3f), new Vector3(18, 5, 0)),
                ("srv_overview", new Vector3(3.3f, 2.7f, -2.3f), new Vector3(28, -60, 0)),
            },
        };

        [MenuItem("CSER/Capture Views (open scene)")]
        public static void CaptureAll()
        {
            var scene = EditorSceneManager.GetActiveScene().name;
            if (!Views.TryGetValue(scene, out var views)) return;
            foreach (var v in views) Capture(v.name, v.pos, Quaternion.Euler(v.euler));
            Debug.Log("[CSER] Captured views of " + scene + " to " + Dir);
        }

        [MenuItem("CSER/Capture Views (all scenes)")]
        public static void CaptureEveryScene()
        {
            foreach (var name in Views.Keys)
            {
                var path = $"Assets/_Project/Scenes/{name}.unity";
                if (!File.Exists(path)) continue;
                EditorSceneManager.OpenScene(path);
                CaptureAll();
            }
        }

        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots"));

        public static void Capture(string name, Vector3 pos, Quaternion rot, int w = 1280, int h = 800, float fov = 75)
        {
            Directory.CreateDirectory(Dir);
            var go = new GameObject("CaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderPostProcessing = true;
            cam.allowHDR = true;
            cam.clearFlags = RenderSettings.skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }

        /// Capture from the XR head pose (play mode) so world-space UI is seen as the player sees it.
        [MenuItem("CSER/Capture Head View")]
        public static void CaptureHead()
        {
            var eye = GameObject.Find("CenterEyeAnchor");
            if (!eye) { Debug.LogWarning("No CenterEyeAnchor"); return; }
            Capture("head_" + System.DateTime.Now.ToString("HHmmss"), eye.transform.position, eye.transform.rotation);
        }
    }
}
