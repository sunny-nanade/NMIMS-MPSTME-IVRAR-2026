using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// DeepSleep > Run Check > Smoke room screenshots. Opens SCN_SmokeRoom in edit mode, applies the SmokeVisibility settings (fog, ambient, lamp) and renders
    /// the view at the entry and in front of the nearest wall at 1 m, 2 m and 3 m, for layouts A and B. PNGs go to Analysis/screens/smoke.
    /// Returns the mean luminance of the centre of each picture so the readability can be checked by number as well as by eye.
    /// </summary>
    public static class SmokeShots
    {
        public static readonly string OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Analysis", "screens", "smoke"));

        [MenuItem("DeepSleep/Run Check/Smoke room screenshots")]
        public static void Run() { Debug.Log("[DeepSleep] " + Shoot("")); }

        public static string Shoot(string tag)
        {
            EditorSceneManager.SaveOpenScenes();
            string back = SceneManager.GetActiveScene().path;
            var sc = EditorSceneManager.OpenScene(ProjectSetupTools.ScenePath("SCN_SmokeRoom"), OpenSceneMode.Single);
            var maze = Object.FindAnyObjectByType<MazeBuilder>();
            var smoke = Object.FindAnyObjectByType<SmokeVisibility>();
            if (maze == null || smoke == null) return "SCN_SmokeRoom has no MazeBuilder / SmokeVisibility";
            Directory.CreateDirectory(OutDir);
            var sb = new System.Text.StringBuilder();
            foreach (char layout in new[] { 'A', 'B' })
            {
                maze.ActivateLayout(layout);
                Physics.SyncTransforms();
                smoke.Apply();
                var spawn = maze.ActiveRoot.transform.Find("EntrySpawn");
                Vector3 p0 = spawn != null ? spawn.position : Vector3.zero;
                var camGo = new GameObject("ShotCam"); var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 90; cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor;
                var lampGo = new GameObject("ShotLamp"); lampGo.transform.SetParent(camGo.transform, false); lampGo.transform.localPosition = new Vector3(0f, 0.1f, 0.1f);
                smoke.ConfigureLamp(lampGo.AddComponent<Light>());
                // a corridor on the shortest path with a free run of at least 3.3 m ending in a wall, so the camera can stand 1, 2 and 3 m in front of it
                Vector3 eye = new Vector3(p0.x, DS.EYE_HEIGHT, p0.z);
                Vector3 bestDir = Vector3.forward; float bestD = 0f; Vector3 wallPoint = Vector3.zero; bool found = false;
                var cells = new System.Collections.Generic.List<MazeCell>();
                foreach (var pc in maze.Layout.Data.path) cells.Add(new MazeCell(pc.c, pc.r));
                for (int c = 0; c < maze.Layout.Grid; c++) for (int r = 0; r < maze.Layout.Grid; r++) { var mc = new MazeCell(c, r); if (!cells.Contains(mc)) cells.Add(mc); }
                foreach (var mc in cells)
                {
                    Vector3 o = maze.Layout.CellCenter(mc); o.y = DS.EYE_HEIGHT;
                    foreach (var d in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                        if (Physics.Raycast(o, d, out var h, 12f, ~0, QueryTriggerInteraction.Ignore) && h.distance >= 3.3f) { bestD = h.distance; bestDir = d; wallPoint = h.point; eye = o; found = true; break; }
                    if (found) break;
                }
                if (!found) { sb.AppendLine($"layout {layout}: no corridor with a 3.3 m free run found"); Object.DestroyImmediate(camGo); continue; }
                sb.AppendLine($"layout {layout}: corridor from {eye} toward {bestDir}, free run {bestD:0.00} m");
                var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                foreach (var shot in new[] { ("entry", -1f), ("wall_1m", 1f), ("wall_2m", 2f), ("wall_3m", 3f) })
                {
                    if (shot.Item2 < 0) { camGo.transform.position = new Vector3(p0.x, DS.EYE_HEIGHT, p0.z); camGo.transform.rotation = spawn != null ? spawn.rotation : Quaternion.identity; }
                    else
                    {
                        camGo.transform.position = new Vector3(wallPoint.x, DS.EYE_HEIGHT, wallPoint.z) - bestDir * shot.Item2;
                        camGo.transform.rotation = Quaternion.LookRotation(bestDir, Vector3.up);
                    }
                    cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    string file = Path.Combine(OutDir, $"{tag}layout{layout}_{shot.Item1}.png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    // luminance statistics of the centre third: mean and spread (detail = spread)
                    var px = tex.GetPixels(rt.width / 3, rt.height / 3, rt.width / 3, rt.height / 3);
                    double sum = 0, sum2 = 0; foreach (var c in px) { double l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; sum += l; sum2 += l * l; }
                    double mean = sum / px.Length, sd = System.Math.Sqrt(System.Math.Max(0, sum2 / px.Length - mean * mean));
                    sb.AppendLine($"  {shot.Item1}: mean luminance {mean:0.000}, detail (stddev) {sd:0.0000}");
                    Object.DestroyImmediate(tex);
                }
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
            }
            smoke.Clear();
            maze.ActivateLayout('A');
            EditorSceneManager.SaveScene(sc);
            if (!string.IsNullOrEmpty(back) && back != sc.path) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            return sb.ToString();
        }
    }
}
