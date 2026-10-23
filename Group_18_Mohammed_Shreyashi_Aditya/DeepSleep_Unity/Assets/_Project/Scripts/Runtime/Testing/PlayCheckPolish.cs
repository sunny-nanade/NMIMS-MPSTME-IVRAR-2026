#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine;

namespace Fractured_Final
{
    /// <summary>
    /// Scenario "polish": jumps through the sections and takes the standard viewpoints (free camera, same render pipeline and post-processing as the game),
    /// then samples the renderer stats (draw calls, SetPass calls, triangles) and counts the lights. Output: Docs/Screens/polish/&lt;tag&gt;/ and Docs/Reports/polish_stats_&lt;tag&gt;.md.
    /// The tag ("before" or "after") comes from SessionState DS_polish_tag.
    /// </summary>
    public partial class PlayCheckRunner
    {
        struct VP { public string name; public Vector3 pos, target; public float fov; public VP(string n, Vector3 p, Vector3 t, float f = 75f) { name = n; pos = p; target = t; fov = f; } }

        readonly StringBuilder _stats = new StringBuilder();

        string PolishDir(string tag) { return Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Docs", "Screens", "polish", tag)); }

        void PolishShot(string dir, string name, VP v, bool lamp = false)
        {
            GameObject lampGo = null;
            try
            {
                if (lamp)
                {
                    // the helmet lamp of the player, placed where the camera is, so the smoke shots show what the player sees
                    lampGo = new GameObject("PolishLamp");
                    lampGo.transform.SetPositionAndRotation(v.pos, Quaternion.LookRotation(v.target - v.pos, Vector3.up));
                    var sv = FindAnyObjectByType<SmokeVisibility>();
                    var light = lampGo.AddComponent<Light>();
                    if (sv != null) sv.ConfigureLamp(light);
                }
                Directory.CreateDirectory(dir);
                var go = new GameObject("PolishCam");
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = v.fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 80f;
                if (SceneFlow.Section == DS.SceneSmokeRoom) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor; } // what the player camera does in the smoke (SmokeVisibility): gaps in perforated wall pieces show the fog colour
                go.transform.position = v.pos; go.transform.rotation = Quaternion.LookRotation(v.target - v.pos, Vector3.up);
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; // URP takes the sample count of a camera that renders into a texture from that texture: 4x like the Quest asset
                cam.targetTexture = rt; cam.Render();
                var old = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = old; cam.targetTexture = null; Destroy(rt); Destroy(tex); Destroy(go);
            }
            catch (System.Exception e) { Note("shot failed " + name + ": " + e.Message); }
            if (lampGo != null) Destroy(lampGo);
        }

        IEnumerator SampleStats(string label)
        {
            // three frames so the Game view has rendered the new state, then read the last frame's counters
            yield return null; yield return null; yield return null;
            int rt = 0, bakedOrMixed = 0, shadows = 0;
            foreach (var l in FindObjectsByType<Light>())
            {
                if (!l.isActiveAndEnabled || l.type == LightType.Directional && l.intensity <= 0f) continue;
                if (l.lightmapBakeType == LightmapBakeType.Realtime) rt++; else bakedOrMixed++;
                if (l.shadows != LightShadows.None && l.lightmapBakeType != LightmapBakeType.Baked) shadows++;
            }
            long tris = 0; int renderers = 0; var mats = new HashSet<Material>();
            foreach (var r in FindObjectsByType<MeshRenderer>())
            {
                if (!r.isVisible && !r.enabled) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                renderers++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) { long t = 0; for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) t += mf.sharedMesh.GetIndexCount(i) / 3; tris += t; }
                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
            }
            _stats.AppendLine($"| {label} | {UnityStats.drawCalls} | | {UnityStats.setPassCalls} | {UnityStats.triangles} | {renderers} | {tris} | {mats.Count} | {rt} | {bakedOrMixed} | {shadows} |");
        }

        IEnumerator JumpAndSettle(Stage s, float seconds)
        {
            StageJump.Jump(s);
            yield return null;
            var sf = SceneFlow.Instance;
            float t0 = Time.realtimeSinceStartup;
            while ((sf.IsBusy || Time.realtimeSinceStartup - t0 < 1f) && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < seconds) yield return null;
        }

        IEnumerator PolishCheck()
        {
            QualitySettings.SetQualityLevel(0, true); // the Quest quality level (Mobile URP asset); the Editor default is PC
            string tag = SessionState.GetString("DS_polish_tag", "before");
            string dir = PolishDir(tag);
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null, 10f);
            yield return sp.Wait(1f);
            _stats.AppendLine("| viewpoint set | draw calls | SetPass | triangles (frame) | mesh renderers | triangles (scene) | materials | realtime lights | baked/mixed lights | shadowed lights |");
            _stats.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            // ---- Setup alcove
            yield return JumpAndSettle(Stage.Setup, 3f);
            var setup = new[]
            {
                new VP("1_start_panel", new Vector3(0, 1.65f, -0.3f), new Vector3(0, 1.45f, 1.5f)),
                new VP("2_left_side", new Vector3(-2.2f, 1.65f, 0.2f), new Vector3(0, 1.4f, 1.5f)),
                new VP("3_right_side", new Vector3(2.2f, 1.65f, 0.2f), new Vector3(0, 1.4f, 1.5f)),
                new VP("4_pods", new Vector3(0, 1.65f, 1.0f), new Vector3(0, 1.2f, 3.3f)),
                new VP("5_corner_a", new Vector3(-2.7f, 2.2f, -2.2f), new Vector3(1, 1, 2.5f)),
                new VP("6_corner_b", new Vector3(2.7f, 2.2f, -2.2f), new Vector3(-1, 1, 2.5f)),
            };
            foreach (var v in setup) PolishShot(dir, "setup_" + v.name, v);
            yield return SampleStats("Setup alcove");

            // ---- Medical Bay (beacon game stage: cup done, puzzle props visible)
            yield return JumpAndSettle(Stage.EarCheck, 4f);
            var bay = new[]
            {
                new VP("1_spawn_east", new Vector3(-2f, 1.65f, 0.2f), new Vector3(2f, 1.2f, 0.2f)),
                new VP("2_beacon_station", new Vector3(-1.25f, 1.65f, -0.2f), new Vector3(-1.25f, 1.3f, -2.3f), 85f),
                new VP("3_keypad_terminal", new Vector3(-0.6f, 1.6f, 0.8f), new Vector3(0.3f, 1.1f, 0.8f), 60f),
                new VP("4_cables_rack_poster", new Vector3(1.1f, 1.65f, 0.9f), new Vector3(1.8f, 1.2f, 2.4f), 80f),
                new VP("5_overview_ne", new Vector3(3.6f, 2.5f, 2.6f), new Vector3(-1f, 1f, -1f), 85f),
                new VP("6_overview_sw", new Vector3(-3.6f, 2.5f, -2.6f), new Vector3(2f, 1f, 1f), 85f),
            };
            foreach (var v in bay) PolishShot(dir, "bay_" + v.name, v);
            yield return SampleStats("Medical Bay (beacon game)");
            var rig = PlayerRig.Instance;
            rig.PlaceAt(new Vector3(-2f, 0f, 0.2f), 90f); yield return SampleStats("Medical Bay, at spawn looking east");
            rig.PlaceAt(new Vector3(2.4f, 0f, 0.4f), 0f); yield return SampleStats("Medical Bay, at the rack looking north");

            yield return JumpAndSettle(Stage.KeypadSolved, 5f);
            PolishShot(dir, "bay_7_trail_panel", new VP("", new Vector3(0.8f, 1.65f, 0.5f), new Vector3(3.3f, 0.5f, -0.5f), 80f));
            PolishShot(dir, "bay_8_trail_floor", new VP("", new Vector3(0.0f, 1.65f, -0.2f), new Vector3(2.0f, 0f, -1.0f), 80f));
            yield return SampleStats("Medical Bay (trail)");

            yield return JumpAndSettle(Stage.CoolantRouted, 4f);
            PolishShot(dir, "bay_9_rack_lever", new VP("", new Vector3(1.9f, 1.65f, 0.6f), new Vector3(2.9f, 1.4f, 1.7f), 75f));
            PolishShot(dir, "bay_10_rack_wide", new VP("", new Vector3(0.0f, 1.9f, 0.2f), new Vector3(2.8f, 1.2f, 2.2f), 80f));
            yield return SampleStats("Medical Bay (rack solved)");

            // ---- Hallway
            yield return JumpAndSettle(Stage.Escaped, 4f);
            var hall = new[]
            {
                new VP("1_spawn_forward", new Vector3(0, 1.65f, 4.5f), new Vector3(0, 1.5f, 9f)),
                new VP("2_spawn_back", new Vector3(0, 1.65f, 4.5f), new Vector3(0, 1.5f, 0f)),
                new VP("3_console_plaque", new Vector3(0, 1.65f, 7f), new Vector3(0, 1.7f, 8.85f), 70f),
                new VP("4_side", new Vector3(-0.8f, 1.7f, 2f), new Vector3(0.8f, 1.2f, 6f)),
                new VP("5_ceiling", new Vector3(0, 2.2f, 1f), new Vector3(0, 2.0f, 8f), 85f),
                new VP("6_end_corner", new Vector3(0.7f, 1.65f, 8.5f), new Vector3(-0.5f, 1.4f, 5f), 85f),
            };
            foreach (var v in hall) PolishShot(dir, "hall_" + v.name, v);
            yield return SampleStats("Hallway");

            // ---- Smoke room (block 1)
            StageJump.Jump(Stage.Block1);
            yield return sp.Until(() => SceneFlow.Section == DS.SceneSmokeRoom && EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 40f);
            yield return sp.Wait(3f);
            var head = PlayerRig.Instance.Head;
            Vector3 o = head.position; Vector3 f = head.forward; f.y = 0; f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            var smoke = new List<VP>
            {
                new VP("1_entry_forward", o, o + f * 3f, 80f),
                new VP("2_left", o, o - r * 3f, 80f),
                new VP("3_right", o, o + r * 3f, 80f),
                new VP("4_back", o, o - f * 3f, 80f),
                new VP("5_down", o, o + f * 2f + Vector3.down * 1.2f, 80f),
            };
            var maze = FindAnyObjectByType<MazeBuilder>();
            if (maze != null && maze.ExitDoorLeaf != null)
            {
                Vector3 l = maze.ExitDoorLeaf.position;
                smoke.Add(new VP("6_exit_at_1m", l + new Vector3(0, 1.65f, -1.0f), l + new Vector3(0, 1.2f, 0), 80f));
                smoke.Add(new VP("7_exit_at_2m", l + new Vector3(0, 1.65f, -2.0f), l + new Vector3(0, 1.2f, 0), 80f));
                smoke.Add(new VP("8_exit_at_3m", l + new Vector3(0, 1.65f, -3.0f), l + new Vector3(0, 1.2f, 0), 80f));
            }
            foreach (var v in smoke) PolishShot(dir, "smoke_" + v.name, v, true);
            yield return SampleStats("Smoke room");

            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Docs", "Reports")));
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Docs", "Reports", "polish_stats_" + tag + ".md")),
                "# Render stats (" + tag + ")\n\nEditor Game view numbers (not Quest numbers; use them to compare before and after). Realtime lights count lights whose bake type is Realtime and that are active in the section.\n\n" + _stats);
            Check(true, "Polish viewpoints captured to " + dir);
        }
    }
}
#endif
#if UNITY_EDITOR
namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        /// <summary>Scenario "speckle": renders the same Medical Bay view with one suspect switched off at a time.</summary>
        IEnumerator SpeckleProbe()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null, 10f);
            yield return sp.Wait(1f);
            QualitySettings.SetQualityLevel(0, true);
            yield return JumpAndSettle(Stage.EarCheck, 4f);
            string dir = PolishDir("speckle_mobile");
            var v = new VP("x", new Vector3(1.8f, 1.65f, 0.4f), new Vector3(2.5f, 1.2f, 1.6f), 80f);
            PolishShot(dir, "0_base", v);
            var sky = RenderSettings.skybox; RenderSettings.skybox = null;
            PolishShot(dir, "1_no_sky", v); RenderSettings.skybox = sky;
            float ri = RenderSettings.reflectionIntensity; RenderSettings.reflectionIntensity = 0f;
            PolishShot(dir, "2_no_reflection", v); RenderSettings.reflectionIntensity = ri;
            foreach (var p in FindObjectsByType<ReflectionProbe>()) p.enabled = false;
            PolishShot(dir, "3_no_probes", v);
            foreach (var p in FindObjectsByType<ReflectionProbe>()) p.enabled = true;
            foreach (var ps in FindObjectsByType<ParticleSystem>()) ps.gameObject.SetActive(false);
            PolishShot(dir, "4_no_particles", v);
            var rig = PlayerRig.Instance.gameObject; rig.SetActive(false);
            PolishShot(dir, "5_no_rig", v); rig.SetActive(true);
            foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional) l.enabled = false;
            PolishShot(dir, "6_no_key_light", v);
            Check(true, "speckle probe written to " + dir);
        }
    }
}
#endif
#if UNITY_EDITOR
namespace Fractured_Final
{
    public partial class PlayCheckRunner
    {
        static Transform FindChildDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindChildDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        /// <summary>Scenario "speckle2": the smoke-room wall at 1 m with one suspect switched off at a time.</summary>
        IEnumerator SmokeSpeckleProbe()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null, 10f);
            yield return sp.Wait(1f);
            QualitySettings.SetQualityLevel(0, true);
            StageJump.Jump(Stage.Block1);
            yield return sp.Until(() => SceneFlow.Section == DS.SceneSmokeRoom && EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 40f);
            yield return sp.Wait(3f);
            string dir = PolishDir("speckle_smoke");
            var maze = FindAnyObjectByType<MazeBuilder>();
            Vector3 l = maze.ExitDoorLeaf.position;
            var v = new VP("x", l + new Vector3(0, 1.65f, -1.0f), l + new Vector3(0.6f, 1.2f, 0), 80f);
            PolishShot(dir, "0_base", v);
            foreach (var group in new[] { "Floors", "Ceilings", "Walls" })
            {
                var g = FindChildDeep(maze.ActiveRoot.transform, group);
                if (g == null) continue;
                g.gameObject.SetActive(false);
                PolishShot(dir, "off_" + group, v, true);
                g.gameObject.SetActive(true);
            }
            PolishShot(dir, "all_on_with_lamp", v, true);
            Check(true, "smoke speckle probe written to " + dir);
        }
    }
}
#endif
