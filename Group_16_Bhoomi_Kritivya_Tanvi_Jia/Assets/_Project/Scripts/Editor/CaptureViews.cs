using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RescueSim.EditorTools
{
    /// <summary>Renders a handful of fixed viewpoints of the open scene to PNGs (Builds/Shots) for visual review without entering play mode.</summary>
    public static class CaptureViews
    {
        struct V { public string name; public Vector3 pos; public Vector3 look; public float fov; public V(string n, Vector3 p, Vector3 l, float f = 70f) { name = n; pos = p; look = l; fov = f; } }

        [MenuItem("Rescue Sim/Debug/Capture Views of Open Scene")]
        public static void Capture()
        {
            var scene = EditorSceneManager.GetActiveScene();
            V[] views;
            switch (scene.name)
            {
                case "EarthquakeRescue":
                    views = new[]
                    {
                        new V("start", new Vector3(0, 1.7f, -12), new Vector3(0, 1.5f, 10)),
                        new V("door", new Vector3(0, 1.7f, 3), new Vector3(0, 1.6f, 14)),
                        new V("corridor", new Vector3(0, 1.7f, 9), new Vector3(0, 1.0f, 17)),
                        new V("debris", new Vector3(0, 1.7f, 11.5f), new Vector3(0, 1.0f, 15.5f), 80),
                        new V("westroom", new Vector3(-3, 1.7f, 12.2f), new Vector3(-7, 1.0f, 13)),
                        new V("eastroom", new Vector3(3.2f, 1.7f, 19.8f), new Vector3(7, 1.0f, 21)),
                        new V("aerial", new Vector3(14, 12, -16), new Vector3(0, 0, 12), 60),
                        new V("rescuezone", new Vector3(0, 1.7f, 6), new Vector3(0, 1.5f, -4), 80),
                        new V("amira", new Vector3(-3.6f, 1.6f, 11.6f), new Vector3(-6.2f, 0.4f, 10.4f), 65),
                        new V("ben", new Vector3(4.2f, 1.6f, 20.4f), new Vector3(6.4f, 0.4f, 22.2f), 65),
                    };
                    break;
                case "FloodEvacuation":
                    views = new[]
                    {
                        new V("start", new Vector3(0, 1.7f, -8), new Vector3(0, 1.0f, 20)),
                        new V("buoy", new Vector3(0, 1.7f, -5), new Vector3(1.8f, 1.1f, -2)),
                        new V("mid", new Vector3(0, 1.7f, 16), new Vector3(0, 1.0f, 40)),
                        new V("porchE", new Vector3(3.5f, 1.7f, 16), new Vector3(8, 1.0f, 19.3f), 80),
                        new V("ramp", new Vector3(0, 1.7f, 36), new Vector3(0, 2.0f, 55), 75),
                        new V("safezone", new Vector3(0, 3.7f, 50), new Vector3(0, 3.0f, 60), 80),
                        new V("aerial", new Vector3(22, 20, -14), new Vector3(0, 0, 24), 60),
                        new V("van", new Vector3(0, 1.7f, 0), new Vector3(-3.4f, 1.2f, -9.5f), 80),
                        new V("lena", new Vector3(5.6f, 1.7f, 17.6f), new Vector3(8f, 0.9f, 19.3f), 65),
                        new V("omar", new Vector3(-5.6f, 1.7f, 30.6f), new Vector3(-8f, 0.9f, 32.3f), 65),
                    };
                    break;
                case "TriageSimulation":
                    views = new[]
                    {
                        new V("yard", new Vector3(0, 1.7f, 0.5f), new Vector3(0, 1.0f, 10), 75),
                        new V("aerial", new Vector3(0, 16, -8), new Vector3(0, 0, 8), 65),
                        new V("close", new Vector3(-3.5f, 1.7f, 5.5f), new Vector3(-3.5f, 0.3f, 8.5f), 70),
                        new V("panel", new Vector3(0, 1.7f, 7), new Vector3(0, 1.6f, 9.2f), 70),
                    };
                    break;
                case "FactoryRescue":
                    views = new[]
                    {
                        new V("dana", new Vector3(-14.4f, 1.6f, -4.6f), new Vector3(-16.8f, 0.4f, -7f), 65),
                        new V("marcus", new Vector3(-13.2f, 1.6f, -26.2f), new Vector3(-15.5f, 0.4f, -28.5f), 65),
                        new V("priya", new Vector3(-8.6f, 1.6f, -28.8f), new Vector3(-6.8f, 0.4f, -31f), 65),
                        new V("start", new Vector3(-11.4f, 1.7f, 24.5f), new Vector3(-11.4f, 1.2f, 5), 75),
                    };
                    break;
                case "MainMenu":
                    views = new[] { new V("menu", new Vector3(0, 1.7f, 0), new Vector3(0, 1.7f, 2.3f), 75) };
                    break;
                default:
                    views = new[] { new V("view", new Vector3(0, 1.7f, 0), new Vector3(0, 1.7f, 10)) };
                    break;
            }

            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Shots");
            Directory.CreateDirectory(dir);
            Camera src = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { src = c; break; }
            var go = new GameObject("CaptureCam");
            var cam = go.AddComponent<Camera>();
            if (src != null) { cam.clearFlags = src.clearFlags; cam.backgroundColor = src.backgroundColor; }
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            foreach (var v in views)
            {
                go.transform.position = v.pos;
                go.transform.rotation = Quaternion.LookRotation((v.look - v.pos).normalized, Vector3.up);
                cam.fieldOfView = v.fov;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{scene.name}_{v.name}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            Debug.Log($"[CaptureViews] Wrote {views.Length} views to {dir}");
        }
    }
}
