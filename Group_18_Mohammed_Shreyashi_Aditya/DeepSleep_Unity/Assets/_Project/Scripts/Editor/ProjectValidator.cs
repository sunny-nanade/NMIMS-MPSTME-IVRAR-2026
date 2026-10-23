using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    /// <summary>DeepSleep > Validate Project. Writes Docs/Reports/validate_latest.md and logs a summary.</summary>
    public static class ProjectValidator
    {
        public const string ReportPath = "Assets/_Project/Docs/Reports/validate_latest.md";

        class Result
        {
            public List<string> failures = new List<string>();
            public List<string> passes = new List<string>();
            public void Ok(string s) => passes.Add(s);
            public void Fail(string s) => failures.Add(s);
        }

        [MenuItem("DeepSleep/Validate Project")]
        public static void Validate() { ValidateAndReturn(); }

        public static bool ValidateAndReturn()
        {
            var r = new Result();
            CheckTagsLayers(r);
            CheckScenes(r);
            CheckPrefabs(r);
            CheckManifest(r);
            CheckInputReaders(r);
            CheckRigHeight(r);
            CheckScripts(r);
            CheckLayoutFiles(r);
            CheckScenePieces(r);
            CheckPrefabNames(r);
            CheckDocs(r);
            CheckPolish(r);

            var sb = new StringBuilder();
            sb.AppendLine("# Validate Project");
            sb.AppendLine();
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Result: {(r.failures.Count == 0 ? "PASS" : "FAIL")} ({r.passes.Count} checks passed, {r.failures.Count} failed)");
            sb.AppendLine();
            if (r.failures.Count > 0) { sb.AppendLine("## Failures"); foreach (var f in r.failures) sb.AppendLine("- " + f); sb.AppendLine(); }
            sb.AppendLine("## Passed");
            foreach (var p in r.passes) sb.AppendLine("- " + p);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString());
            AssetDatabase.ImportAsset(ReportPath);

            if (r.failures.Count == 0) Debug.Log($"[DeepSleep] Validate Project: PASS ({r.passes.Count} checks)");
            else
            {
                Debug.LogWarning($"[DeepSleep] Validate Project: FAIL ({r.failures.Count} problems). See {ReportPath}");
                foreach (var f in r.failures) Debug.LogWarning("[DeepSleep] Validate: " + f);
            }
            return r.failures.Count == 0;
        }

        static void CheckTagsLayers(Result r)
        {
            var tags = new HashSet<string>(UnityEditorInternal.InternalEditorUtility.tags);
            foreach (var t in ProjectSetupTools.Tags)
                if (tags.Contains(t)) r.Ok($"Tag {t} present"); else r.Fail($"Tag missing: {t}");
            foreach (var l in ProjectSetupTools.Layers)
                if (LayerMask.NameToLayer(l) >= 0) r.Ok($"Layer {l} present"); else r.Fail($"Layer missing: {l}");
        }

        static void CheckScenes(Result r)
        {
            var build = EditorBuildSettings.scenes;
            string[] expected = { "SCN_Main" }; // one scene; the old four scene names are section names now
            if (build.Length != expected.Length) r.Fail($"Build Settings has {build.Length} scenes, expected {expected.Length}");
            for (int i = 0; i < expected.Length && i < build.Length; i++)
            {
                if (!build[i].path.EndsWith(expected[i] + ".unity")) r.Fail($"Build index {i} is {build[i].path}, expected {expected[i]}");
                else r.Ok($"Build index {i} = {expected[i]}");
            }
            if (EditorApplication.isPlaying) { r.Fail("Cannot scan scenes during play mode"); return; }
            var restore = SceneManager.GetActiveScene().path;
            foreach (var bs in build)
            {
                if (!File.Exists(bs.path)) { r.Fail("Scene file missing: " + bs.path); continue; }
                var scene = EditorSceneManager.OpenScene(bs.path, OpenSceneMode.Single);
                int missingScripts = 0, missingRefs = 0;
                var details = new List<string>();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        int ms = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        if (ms > 0) { missingScripts += ms; details.Add($"missing script on {HPath(t)}"); }
                        foreach (var c in t.GetComponents<Component>())
                        {
                            if (c == null) continue;
                            missingRefs += CountMissingRefs(c, HPath(t), details);
                        }
                    }
                string name = System.IO.Path.GetFileNameWithoutExtension(bs.path);
                if (missingScripts == 0 && missingRefs == 0) r.Ok($"{name}: no missing scripts or references");
                else
                {
                    r.Fail($"{name}: {missingScripts} missing scripts, {missingRefs} missing references");
                    for (int i = 0; i < Mathf.Min(8, details.Count); i++) r.Fail($"  {name}: {details[i]}");
                }
            }
            if (!string.IsNullOrEmpty(restore)) EditorSceneManager.OpenScene(restore);
        }

        static string HPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static int CountMissingRefs(Component c, string path, List<string> details)
        {
            int n = 0;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = true;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                // SDK controller models leave renderer material slots empty on purpose (OVRControllerHelper assigns them at runtime)
                if (path.Contains("Controller Tracking") && it.propertyPath.StartsWith("m_Materials")) continue;
                if (it.objectReferenceValue == null && it.objectReferenceEntityIdValue != default(UnityEngine.EntityId))
                {
                    n++;
                    details.Add($"missing reference {c.GetType().Name}.{it.propertyPath} on {path}");
                }
            }
            return n;
        }

        static void CheckPrefabs(Result r)
        {
            foreach (var g in AssetDatabase.FindAssets("t:prefab", new[] { "Assets/_Project/Prefabs" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p); // read the asset itself: loading the contents runs OVRCameraRig.Awake and the SDK warns that a second rig exists
                int ms = 0, mr = 0; var details = new List<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    ms += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    foreach (var c in t.GetComponents<Component>()) if (c != null) mr += CountMissingRefs(c, HPath(t), details);
                }
                if (ms == 0 && mr == 0) r.Ok($"Prefab {System.IO.Path.GetFileName(p)} clean");
                else { r.Fail($"Prefab {p}: {ms} missing scripts, {mr} missing references"); foreach (var d in details) r.Fail("  " + d); }
            }
        }

        static void CheckManifest(Result r)
        {
            var lib = AssetDatabase.LoadAssetAtPath<DeepSleepAssets>(AudioLibraryBuilder.LibraryPath);
            if (lib == null) { r.Fail("DeepSleepAssets.asset missing"); return; }
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(AudioLibraryBuilder.ManifestPath);
            if (manifest == null) { r.Fail("voice_manifest.csv missing"); return; }
            lib.InvalidateCache();
            int unresolved = 0, total = 0;
            var lines = manifest.text.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string id = line.Split(',')[0];
                total++;
                if (!lib.Has(id)) { unresolved++; r.Fail("Manifest ID not resolved: " + id); }
            }
            if (unresolved == 0) r.Ok($"All {total} manifest audio IDs resolved");
            if (lib.mixer == null) r.Fail("MIX_Main missing from library"); else r.Ok("MIX_Main present");
        }

        static void CheckInputReaders(Result r)
        {
            var rx = new Regex(@"OVRInput\.(Get|GetDown|GetUp)|UnityEngine\.Input\.|[^A-Za-z]Input\.(Get|mouse|GetKey)|Keyboard\.current|Mouse\.current|Gamepad\.current|InputAction|InputSystem\.");
            var offenders = new List<string>();
            foreach (var f in Directory.GetFiles("Assets/_Project/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string name = System.IO.Path.GetFileName(f);
                if (name == "InputService.cs") continue;
                if (f.Replace('\\', '/').Contains("/Editor/")) continue;
                string text = File.ReadAllText(f);
                if (rx.IsMatch(text)) offenders.Add(name);
            }
            if (offenders.Count == 0) r.Ok("No script outside InputService reads input directly");
            else r.Fail("Scripts reading input directly: " + string.Join(", ", offenders));
        }


        static readonly string[] RequiredScripts =
        {
            "InputService", "EditorFallbackRig", "SessionManager", "SceneFlow", "PauseManager", "ConditionRandomizer", "TelemetryLogger", "ExperimenterPanel",
            "OxygenSystem", "EmergencyLighting", "EarCheckController", "ThermalKeypad", "BreachTrail", "CoolantRack", "MazeBuilder", "SmokeVisibility",
            "EgressBlockController", "PathTracker", "MentorDirector", "AudioConditionSwitch", "NarrativeVoiceDirector", "HalcyonDirector", "SfxDirector",
            "QuestionnaireUI", "SaveLoad", "ProfilerCapture", "PlaythroughBot",
        };

        static void CheckScripts(Result r)
        {
            var asm = typeof(DS).Assembly;
            var missing = new List<string>();
            foreach (var n in RequiredScripts) if (asm.GetType("Fractured_Final." + n) == null) missing.Add(n);
            if (missing.Count == 0) r.Ok($"All {RequiredScripts.Length} required scripts exist (plus ProjectValidator)"); else r.Fail("Missing scripts: " + string.Join(", ", missing));
            foreach (var py in new[] { "MazeLayoutVerifier.py", "analysis.py", "generate_figures.py" })
                if (File.Exists("Tools/" + py)) r.Ok("Tools/" + py + " present"); else r.Fail("Missing Tools/" + py);
        }

        static void CheckLayoutFiles(Result r)
        {
            foreach (var n in new[] { "A", "B" })
            {
                string path = $"Assets/_Project/Data/MazeLayout{n}.json";
                if (!File.Exists(path)) { r.Fail("Missing " + path); continue; }
                try
                {
                    var l = MazeLayout.Parse(File.ReadAllText(path));
                    var m = l.Verify();
                    bool ok = m.steps == 18 && m.unique && m.junctions == 6 && m.deadEnds == 3 && m.misleading >= 2 && Mathf.Abs(l.Data.shortestMeters - 36f) < 0.01f;
                    if (ok) r.Ok($"Layout {n}: 36 m shortest path, 6 junctions, 3 dead ends, {m.misleading} misleading"); else r.Fail($"Layout {n} does not meet the specification ({m.steps} steps, {m.junctions} junctions, {m.deadEnds} dead ends, {m.misleading} misleading)");
                }
                catch (Exception e) { r.Fail($"Layout {n} unreadable: {e.Message}"); }
            }
        }

        static void CheckScenePieces(Result r)
        {
            if (EditorApplication.isPlaying) return;
            var required = new Dictionary<string, string[]>
            {
                { "SCN_Setup", new[] { "SetupController", "PlayerRig" } },
                { "SCN_MedicalBay", new[] { "AwakeningController", "EarCheckController", "ThermalKeypad", "BreachTrail", "CoolantRack", "QuestionnaireUI", "BlockLauncher", "EmergencyLighting", "PlayerRig", "OxygenDisplay" } },
                { "SCN_SmokeRoom", new[] { "MazeBuilder", "EgressBlockController", "MentorDirector", "PathTracker", "AudioConditionSwitch", "SmokeVisibility" } },
                { "SCN_HallwayDebrief", new[] { "QuestionnaireUI", "EmergencyLighting", "PlayerRig" } },
            };
            var restore = SceneManager.GetActiveScene().path;
            // one scene (SCN_Main) holds the Setup, Medical Bay and Hallway sections; the smoke room is the prefab PF_SmokeSection (Resources)
            var names = new HashSet<string>();
            var objs = new HashSet<string>();
            void Collect(GameObject root)
            {
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) names.Add(mb.GetType().Name);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) objs.Add(t.name);
            }
            const string mainPath = "Assets/_Project/Scenes/SCN_Main.unity";
            if (!File.Exists(mainPath)) { r.Fail("Scene missing: " + mainPath); return; }
            var scene = EditorSceneManager.OpenScene(mainPath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects()) Collect(root);
            var smokePrefab = Resources.Load<GameObject>("PF_SmokeSection");
            if (smokePrefab != null) Collect(smokePrefab); else r.Fail("Resources/PF_SmokeSection.prefab missing");
            foreach (var kv in required)
            {
                var miss = new List<string>();
                foreach (var n in kv.Value) if (!names.Contains(n)) miss.Add(n);
                if (kv.Key == "SCN_MedicalBay") foreach (var n in new[] { "PlayerSpawn", "CoreRoomSpawn", "HalcyonSpeaker", "WallPanel", "ServiceDoor", "MainDoor_Welded" }) if (!objs.Contains(n)) miss.Add(n);
                if (kv.Key == "SCN_SmokeRoom") foreach (var n in new[] { "SmokeEntrySpawn", "MazeRoot_A", "MazeRoot_B" }) if (!objs.Contains(n)) miss.Add(n);
                if (kv.Key == "SCN_HallwayDebrief") foreach (var n in new[] { "HallwaySpawn", "Plaque" }) if (!objs.Contains(n)) miss.Add(n);
                if (miss.Count == 0) r.Ok($"{kv.Key} section has its required components and markers"); else r.Fail($"{kv.Key} section is missing: {string.Join(", ", miss)}");
            }
            if (!string.IsNullOrEmpty(restore)) EditorSceneManager.OpenScene(restore);
        }

        /// <summary>Polish checks: no missing material or shader in prefabs (magenta), voice clips preloaded, voice level reference equals the quietest file.</summary>
        static void CheckPolish(Result r)
        {
            int bad = MaterialAudit.Audit(false, "Assets/_Project/Docs/Reports/audit_materials_prefabs.md");
            if (bad == 0) r.Ok("No prefab renderer has a missing or URP-incompatible material"); else r.Fail($"{bad} prefab renderer slots have a missing or incompatible material (see audit_materials_prefabs.md)");
            var late = new List<string>();
            float minPeak = 99f;
            var assets = Resources.Load<DeepSleepAssets>("DeepSleepAssets");
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) continue;
                var ss = imp.defaultSampleSettings;
                if (!ss.preloadAudioData || ss.loadType == AudioClipLoadType.Streaming || imp.loadInBackground) late.Add(System.IO.Path.GetFileName(p));
            }
            if (late.Count == 0) r.Ok("Every audio clip is preloaded and not streamed (no late voice starts)"); else r.Fail("Clips that load late: " + string.Join(", ", late));
            if (assets != null)
            {
                foreach (var e in assets.audio)
                    if (e.clip != null && !e.placeholder && e.id.Length == 4 && (e.id[0] == 'R' || e.id[0] == 'H')) minPeak = Mathf.Min(minPeak, VoiceLevels.PeakDb(e.clip));
                if (Mathf.Abs(minPeak - VoiceLevels.ReferencePeakDb) <= 0.2f) r.Ok($"VoiceLevels reference peak {VoiceLevels.ReferencePeakDb} dB matches the quietest voice file ({minPeak:F1} dB)");
                else r.Fail($"VoiceLevels.ReferencePeakDb is {VoiceLevels.ReferencePeakDb} but the quietest voice file peaks at {minPeak:F1} dB: update the constant");
            }
        }

        static void CheckPrefabNames(Result r)
        {
            var names = new List<string> { "PF_PlayerRig", "PF_Keypad", "PF_ServerRack", "PF_Cable_01", "PF_Cable_02", "PF_Cable_03" };
            var missing = new List<string>();
            foreach (var n in names) if (!File.Exists($"Assets/_Project/Prefabs/{n}.prefab")) missing.Add(n);
            if (missing.Count == 0) r.Ok($"All {names.Count} named prefabs exist"); else r.Fail("Missing prefabs: " + string.Join(", ", missing));
        }

        static void CheckDocs(Result r)
        {
            var docs = new[] { "PROGRESS.md", "KNOWN_ISSUES.md", "DECISIONS_APPLIED.md", "ASSET_NAMES.md", "EXPERIMENTER.md", "TEST_PROTOCOL.md", "AUDIO_TRIGGERS.md", "SCENE_FLOW.md" };
            var missing = new List<string>();
            foreach (var d in docs) if (!File.Exists("Assets/_Project/Docs/" + d)) missing.Add(d);
            if (missing.Count == 0) r.Ok("All project documents exist"); else r.Fail("Missing documents: " + string.Join(", ", missing));
        }

        static void CheckRigHeight(Result r)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabBuilder.PrefabPath);
            if (prefab == null) { r.Fail("PF_PlayerRig missing"); return; }
            var rig = prefab.GetComponent<PlayerRig>();
            if (rig == null) { r.Fail("PF_PlayerRig has no PlayerRig"); return; }
            var so = new SerializedObject(rig);
            float eh = so.FindProperty("eyeHeight").floatValue;
            if (Mathf.Abs(eh - DS.EYE_HEIGHT) < 0.001f) r.Ok($"PlayerRig eye height = {eh} m"); else r.Fail($"PlayerRig eyeHeight is {eh}, expected {DS.EYE_HEIGHT}");
            if (Mathf.Abs(prefab.transform.position.y) < 0.001f) r.Ok("Rig root sits on the floor (y = 0)"); else r.Fail("Rig root is not at y = 0");
            foreach (var comp in prefab.GetComponentsInChildren<OVRManager>(true))
            {
                if (comp.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel) r.Ok("OVRManager tracking origin = Floor");
                else r.Fail("OVRManager tracking origin is " + comp.trackingOriginType + " (set to Floor at runtime by PlayerRig; fix prefab default)");
            }
        }
    }
}
