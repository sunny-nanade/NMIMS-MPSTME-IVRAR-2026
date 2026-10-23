#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Fractured_Final
{
    /// <summary>Audit scenario for the smoke room (section B): fog values through the real additive flow, overrides, screenshots at set distances, visibility numbers.</summary>
    public partial class PlayCheckRunner
    {
        static string ScreenDir => Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Docs", "Screens", "audit"));

        /// <summary>Renders the head camera, saves a PNG and returns mean luminance (0..1) of the centre square and of the whole frame.</summary>
        (float centre, float whole) ShotLum(string name)
        {
            var rig = PlayerRig.Instance;
            var cam = rig.Head.GetComponent<Camera>();
            Directory.CreateDirectory(ScreenDir);
            const int W = 900, H = 900;
            var rt = new RenderTexture(W, H, 24);
            var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
            var old = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(ScreenDir, name + ".png"), tex.EncodeToPNG());
            var px = tex.GetPixels();
            double c = 0, w = 0; int cn = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = px[y * W + x]; float l = 0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b;
                    w += l;
                    if (x > W * 0.4f && x < W * 0.6f && y > H * 0.4f && y < H * 0.6f) { c += l; cn++; }
                }
            RenderTexture.active = old; Destroy(rt); Destroy(tex);
            return ((float)(c / cn), (float)(w / (W * H)));
        }

        void FogRow(string where)
        {
            float vis = Mathf.Sqrt(Mathf.Log(50f)) / Mathf.Max(0.0001f, RenderSettings.fogDensity);
            Note($"[{where}] active scene {SceneManager.GetActiveScene().name}; fog={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity:F3} colour={RenderSettings.fogColor} ambient={RenderSettings.ambientLight} skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}; visibility (2 % transmittance) {(RenderSettings.fog ? vis.ToString("F2") + " m" : "n/a")}");
        }

        IEnumerator AuditSmoke()
        {
            var sp = ScriptedPlayer.Get();
            yield return sp.Until(() => PlayerRig.Instance != null && PlayerRig.Instance.Head != null && SceneFlow.Instance != null && SessionManager.Instance != null, 10f);
            StudyConfig.Data.participant_id = "4";
            var sm = SessionManager.Instance;
            var rig = PlayerRig.Instance;
            var cam = rig.Head.GetComponent<Camera>();
            var flagsBefore = cam.clearFlags; var bgBefore = cam.backgroundColor;
            yield return sp.Wait(1f);
            FogRow("medical bay before the smoke room");
            Check(!RenderSettings.fog, "Fog is off in SCN_MedicalBay before the smoke room");

            // ---------------------------------------------------------------- block 1 through the real flow
            sm.JumpTo(Stage.Block1); sm.CurrentBlock = 1;
            SceneFlow.Instance.EnterSmokeRoom();
            yield return sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active, 25f);
            var eg = EgressBlockController.Instance;
            rig = PlayerRig.Instance; cam = rig.Head.GetComponent<Camera>();
            yield return sp.Wait(1.5f);
            FogRow("smoke room, block 1");
            float vis = Mathf.Sqrt(Mathf.Log(50f)) / RenderSettings.fogDensity;
            Check(SceneManager.GetActiveScene().name == DS.SceneSmokeRoom && RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared && Mathf.Abs(vis - 2f) < 0.15f,
                $"Block 1: active scene is SCN_SmokeRoom, fog on, ExponentialSquared, density {RenderSettings.fogDensity:F3}, visibility {vis:F2} m");

            // B2 overrides
            var ud = cam.GetUniversalAdditionalCameraData();
            Note($"Camera: farClip {cam.farClipPlane} m, clearFlags {cam.clearFlags}, background {cam.backgroundColor}, renderPostProcessing {ud.renderPostProcessing}, volumeLayerMask {ud.volumeLayerMask.value}");
            Check(cam.farClipPlane > 10f, $"Far clip plane ({cam.farClipPlane} m) does not cut the fog short");
            int volumes = FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include).Length;
            Check(volumes == 0, $"No URP Volume exists in any loaded scene ({volumes}); nothing can override or hide the fog");
            Check(!ud.renderPostProcessing || volumes == 0, "Post-processing has no volume to apply");

            // scene scan: no particles, signs, arrows or lights other than the helmet lamp
            var smokeScene = SceneManager.GetSceneByName(DS.SceneSmokeRoom);
            int particles = 0, lights = 0, emissive = 0, signs = 0; var lightNames = new List<string>();
            foreach (var root in smokeScene.GetRootGameObjects())
            {
                if (!root.activeInHierarchy) continue;
                particles += root.GetComponentsInChildren<ParticleSystem>(false).Length;
                foreach (var l in root.GetComponentsInChildren<Light>(false)) { lights++; lightNames.Add(l.name); }
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.01f) { emissive++; Note($"emissive material {m.name} on {r.name}"); }
                    }
                    string n = r.name.ToLower();
                    if (n.Contains("exit") && n.Contains("sign") || n.Contains("arrow")) signs++;
                }
            }
            int sceneParticlesAnywhere = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude).Length;
            int activeLights = 0; var lampNames = new List<string>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.isActiveAndEnabled) { activeLights++; lampNames.Add(l.name + "@" + l.gameObject.scene.name); }
            Note("Active lights while in the smoke room: " + string.Join(", ", lampNames));
            Check(particles == 0, $"No particle systems in the smoke scene ({particles}); smoke is fog only");
            Check(signs == 0, "No exit signs or arrows in the smoke scene");
            Check(lights == 0, $"No lights are placed in the smoke scene ({lights}); the only light is the helmet lamp");
            Check(emissive == 0, $"No emissive materials (floor strips, panels) in the smoke scene ({emissive})");
            var lamp = rig.Head.Find("HelmetLamp");
            Check(lamp != null && lamp.GetComponent<Light>().type == LightType.Spot && Mathf.Abs(lamp.GetComponent<Light>().range - 1.5f) < 0.01f, "Helmet lamp: spot light, range 1.5 m, child of the head");
            int medBayLights = 0;
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.isActiveAndEnabled && l.gameObject.scene.name == DS.SceneMedicalBay && l.name != "HelmetLamp") medBayLights++;
            Check(medBayLights == 0, $"The hidden Medical Bay lights are off while in the smoke room ({medBayLights} active)");

            // ---------------------------------------------------------------- screenshots and visibility numbers
            eg.Mentor.enabled = false; eg.Tracker.enabled = false;
            var layout = eg.Maze.Layout;
            var entryYaw = rig.transform.eulerAngles.y;
            ShotLum("smoke_00_entry");
            // find the longest straight run to a wall
            Vector3 best = default; Vector3 bestDir = Vector3.forward; float bestDist = 0f;
            for (int c = 0; c < layout.Grid; c++)
                for (int r = 0; r < layout.Grid; r++)
                {
                    if (!layout.Used(c, r)) continue;
                    Vector3 o = layout.CellCenter(new MazeCell(c, r)) + Vector3.up * 1.0f;
                    foreach (var d in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                        if (Physics.Raycast(o, d, out var hit, 20f, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore) && hit.distance > bestDist) { bestDist = hit.distance; best = hit.point; bestDir = d; }
                }
            Note($"Longest straight run found: {bestDist:F1} m toward {bestDir}");
            Check(bestDist >= 8.5f, $"A corridor with at least 8.5 m to a wall exists for the distance shots ({bestDist:F1} m)");
            float yaw = Mathf.Atan2(bestDir.x, bestDir.z) * Mathf.Rad2Deg;
            var lum = new Dictionary<string, (float centre, float whole)>();
            float[] dists = { 8f, 6f, 4f, 3f, 2f, 1.5f, 1f };
            foreach (bool lampOn in new[] { true, false })
            {
                if (lamp != null) lamp.gameObject.SetActive(lampOn);
                foreach (float d in dists)
                {
                    Vector3 p = best - bestDir * (d + 0.0f); p.y = 0f;
                    rig.PlaceAt(p, yaw);
                    yield return sp.Frames(4); yield return sp.Wait(0.2f);
                    var m = ShotLum($"smoke_{(lampOn ? "lamp" : "nolamp")}_wall_{d:0.0}m");
                    lum[(lampOn ? "on" : "off") + d] = m;
                }
            }
            if (lamp != null) lamp.gameObject.SetActive(true);
            float refFog = lum["on8"].centre;
            Note($"Reference (wall at 8 m, lamp on): centre luminance {refFog:F4}; fog colour luminance {0.2126f * RenderSettings.fogColor.r + 0.7152f * RenderSettings.fogColor.g + 0.0722f * RenderSettings.fogColor.b:F4}");
            float lastVisible = 0f;
            foreach (float d in dists)
            {
                float con = Mathf.Abs(lum["on" + d].centre - refFog), conOff = Mathf.Abs(lum["off" + d].centre - refFog);
                Note($"wall at {d:0.0} m: contrast against fog {con:F4} with the lamp, {conOff:F4} without the lamp (visible if above 0.02)");
                if (con > 0.02f && d > lastVisible) lastVisible = d;
            }
            // proper visibility measure: with the ambient raised so that walls are bright, the fog fraction on a wall is
            // T(d) = (fogged - fog) / (unfogged - fog); theory for exponential-squared fog is exp(-(d x density)^2)
            if (lamp != null) lamp.gameObject.SetActive(false);
            var ambientBefore = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(1f, 1f, 1f);
            float[] tD = { 8f, 4f, 3f, 2.5f, 2f, 1.5f, 1f, 0.5f };
            var fogged = new Dictionary<float, float>(); var unfogged = new Dictionary<float, float>();
            foreach (bool fogOn in new[] { true, false })
            {
                RenderSettings.fog = fogOn;
                foreach (float d in tD)
                {
                    Vector3 p = best - bestDir * d; p.y = 0f;
                    rig.PlaceAt(p, yaw);
                    yield return sp.Frames(3); yield return sp.Wait(0.15f);
                    float v = ShotLum($"smoke_bright_{(fogOn ? "fog" : "nofog")}_wall_{d:0.0}m").centre;
                    if (fogOn) fogged[d] = v; else unfogged[d] = v;
                }
            }
            RenderSettings.fog = true; RenderSettings.ambientLight = ambientBefore;
            if (lamp != null) lamp.gameObject.SetActive(true);
            float dens = RenderSettings.fogDensity;
            float lFog = Mathf.Pow(fogged[8f], 2.2f); // fog blends in linear space: convert the sRGB-encoded luminance first
            var tMeasured = new Dictionary<float, float>();
            foreach (float d in tD)
            {
                float den = Mathf.Pow(unfogged[d], 2.2f) - lFog;
                float t = Mathf.Abs(den) > 0.01f ? Mathf.Clamp01((Mathf.Pow(fogged[d], 2.2f) - lFog) / den) : float.NaN;
                // the camera sits 0.65 m above the wall point used for the distance, so the line of sight is a little longer
                float los = Mathf.Sqrt(d * d + 0.65f * 0.65f);
                float theory = Mathf.Exp(-(los * dens) * (los * dens));
                tMeasured[d] = t;
                Note($"bright wall at {d:0.0} m (line of sight {los:0.00} m): fogged {fogged[d]:F3}, unfogged {unfogged[d]:F3}, fog {lFog:F3} -> transmittance {t:F3}, theory {theory:F3}");
            }
            float visMeasured = float.NaN;
            for (int i = 0; i < tD.Length - 1 && float.IsNaN(visMeasured); i++)
            {
                float d0 = tD[i], d1 = tD[i + 1]; // d0 farther, d1 nearer
                float t0 = tMeasured[d0], t1 = tMeasured[d1];
                if (!float.IsNaN(t0) && !float.IsNaN(t1) && t0 <= 0.02f && t1 >= 0.02f) visMeasured = Mathf.Lerp(d0, d1, Mathf.InverseLerp(t0, t1, 0.02f));
            }
            Note($"Measured visibility distance (transmittance 2 %, line of sight): {(float.IsNaN(visMeasured) ? "n/a" : visMeasured.ToString("F2") + " m")}; configured 2 m");
            Check(!float.IsNaN(visMeasured) && visMeasured > 1.6f && visMeasured < 2.6f, $"Rendered fog gives a visibility of {visMeasured:F2} m (target about 2 m)");
            Check(tMeasured[1f] > 0.15f && tMeasured[1f] < 0.65f && tMeasured[3f] < 0.01f && tMeasured[4f] < 0.01f, $"Measured transmittance: 1 m {tMeasured[1f]:F2}, 3 m {tMeasured[3f]:F3}, 4 m {tMeasured[4f]:F3}: walls farther than 2 m are not visible");
            float lampGain1 = Mathf.Abs(lum["on1"].centre - lum["off1"].centre), lampGain15 = Mathf.Abs(lum["on1.5"].centre - lum["off1.5"].centre), lampGain3 = Mathf.Abs(lum["on3"].centre - lum["off3"].centre);
            Note($"Lamp effect on the wall (centre luminance): at 1 m {lampGain1:F4}, at 1.5 m {lampGain15:F4}, at 3 m {lampGain3:F4}");
            Check(lampGain1 > 0.01f && lampGain3 < 0.01f, "The helmet lamp lights a wall at 1 m and has no effect at 3 m (range 1.5 m)");

            // ---------------------------------------------------------------- back, then block 2
            eg.Finish(true);
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneFlow.Instance.IsBusy, 25f);
            yield return sp.Wait(1.2f);
            rig = PlayerRig.Instance; cam = rig.Head.GetComponent<Camera>();
            FogRow("medical bay after block 1");
            Check(!RenderSettings.fog, "Fog is off again in SCN_MedicalBay after block 1");
            Check(cam.clearFlags == flagsBefore && cam.backgroundColor == bgBefore, $"Camera clear flags and background are restored after the smoke room ({cam.clearFlags}, {cam.backgroundColor})");
            sm.JumpTo(Stage.Block2); sm.CurrentBlock = 2; EgressBlockController.MarkCompleted(1);
            SceneFlow.Instance.EnterSmokeRoom();
            yield return sp.Until(() => EgressBlockController.Instance != null && EgressBlockController.Instance.Active && EgressBlockController.Instance.Block == 2, 25f);
            yield return sp.Wait(1.2f);
            FogRow("smoke room, block 2");
            float vis2 = Mathf.Sqrt(Mathf.Log(50f)) / RenderSettings.fogDensity;
            Check(SceneManager.GetActiveScene().name == DS.SceneSmokeRoom && RenderSettings.fog && RenderSettings.fogMode == FogMode.ExponentialSquared && Mathf.Abs(vis2 - 2f) < 0.15f, $"Block 2: fog is back (density {RenderSettings.fogDensity:F3}, visibility {vis2:F2} m)");
            ShotLum("smoke_block2_entry");
            EgressBlockController.Instance.Finish(true);
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneMedicalBay && !SceneFlow.Instance.IsBusy, 25f);
            SceneFlow.Instance.GoToHallway();
            yield return sp.Until(() => SceneManager.GetActiveScene().name == DS.SceneHallway && !SceneFlow.Instance.IsBusy, 25f);
            yield return sp.Wait(1f);
            FogRow("hallway");
            Check(!RenderSettings.fog, "Fog is off in SCN_HallwayDebrief after the blocks");
            sm.ResetSession(); EgressBlockController.ResetStatics();
        }
    }
}
#endif
