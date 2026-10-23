using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// Indexes Assets/_Project/Audio by ID (never by extension), generates placeholders for missing trigger IDs,
    /// writes Data/voice_manifest.csv, builds MIX_Main and rebuilds Resources/DeepSleepAssets.asset.
    /// </summary>
    public static class AudioLibraryBuilder
    {
        public const string AudioDir = "Assets/_Project/Audio";
        public const string PlaceholderDir = "Assets/_Project/Audio/_Placeholders";
        public const string ManifestPath = "Assets/_Project/Data/voice_manifest.csv";
        public const string MixerPath = "Assets/_Project/Mixers/MIX_Main.mixer";
        public const string LibraryPath = "Assets/_Project/Resources/DeepSleepAssets.asset";
        public const string MaterialsDir = "Assets/_Project/Materials";

        public struct Trigger
        {
            public string id, stage, voice, anchor;
            public Trigger(string id, string stage, string voice, string anchor) { this.id = id; this.stage = stage; this.voice = voice; this.anchor = anchor; }
        }

        // Source of truth for the trigger tables (PHASE 6). stage = the stage during which the line is valid.
        public static readonly Trigger[] Triggers =
        {
            new Trigger("R001","Setup","Rose","Setup calibration: first line"),
            new Trigger("R002","Setup","Rose","Setup calibration: second line"),
            new Trigger("R003","Setup","Rose","Setup calibration: last line"),
            new Trigger("R010","Awakening","Rose","Enter SCN_MedicalBay"),
            new Trigger("R011","Awakening","Rose","Cup tray reachable"),
            new Trigger("R012","Awakening","Rose","5 s after R011"),
            new Trigger("R030","EarCheck","Rose","Start of Ear Check"),
            new Trigger("R031","EarCheck","Rose","After each Ear Check trial (cycle 3 variants)"),
            new Trigger("R032","EarCheck","Rose","End of Ear Check"),
            new Trigger("R040","EarCheck","Rose","Start of keypad"),
            new Trigger("R041","EarCheck","Rose","Keypad idle 30 s"),
            new Trigger("R043","KeypadSolved","Rose","Terminal boot"),
            new Trigger("R050","KeypadSolved","Rose","Trail fades in"),
            new Trigger("R052","TrailFollowed","Rose","Service door opens"),
            new Trigger("R150","Block2","Rose","Arrive at rack (CoreRoomSpawn)"),
            new Trigger("R151","Block2","Rose","10 s after R150"),
            new Trigger("R153","CoolantRouted","Rose","Rack solved"),
            new Trigger("R160","CoolantRouted","Rose","Blast doors start"),
            new Trigger("R140","Block1","Rose","Questionnaire opens"),
            new Trigger("R161","Questionnaire","Rose","End of debrief"),
            new Trigger("R201","Block2","Rose","Block beacon call every 4 s (3 variants)"),
            new Trigger("R202","Block2","Rose","Junction call: turn (block only)"),
            new Trigger("R203","Block2","Rose","Junction call: turn (block only)"),
            new Trigger("R204","Block2","Rose","Junction call: turn (block only)"),
            new Trigger("R205","Block2","Rose","Dead end (block only)"),
            new Trigger("R207","Block2","Rose","Idle nudge 20 s (block only)"),
            new Trigger("R208","Block2","Rose","Near exit (block only)"),
            new Trigger("R209","Block2","Rose","Finish (block only)"),
            new Trigger("R210","Block2","Rose","Block timeout (block only)"),
            new Trigger("H001","Awakening","HALCYON","Enter SCN_MedicalBay"),
            new Trigger("H002","Awakening","HALCYON","20 s after H001"),
            new Trigger("H004","EarCheck","HALCYON","Wrong keypad sequence (max once per 30 s)"),
            new Trigger("H007","CoolantRouted","HALCYON","Lever"),
            new Trigger("H008","CoolantRouted","HALCYON","Lever"),
            new Trigger("S001","EarCheck","SFX","Ear Check ping"),
            new Trigger("S002","Setup","SFX","Calibration tones"),
            new Trigger("S020","Block2","SFX","Wall contact"),
            new Trigger("S021","Block2","SFX","Footsteps on smooth move"),
            new Trigger("S022","Block2","SFX","Exit door"),
            new Trigger("S040","EarCheck","SFX","Key press (9 pitches)"),
            new Trigger("S041","EarCheck","SFX","Wrong keypad sequence"),
            new Trigger("S042","KeypadSolved","SFX","Terminal boot"),
            new Trigger("S050","TrailFollowed","SFX","Service door"),
            new Trigger("S060","CoolantRouted","SFX","Plug / unplug"),
            new Trigger("S062","CoolantRouted","SFX","Wrong plug"),
            new Trigger("S063","CoolantRouted","SFX","Rack solved and lever"),
            new Trigger("S080","Escaped","SFX","Blast doors"),
            new Trigger("S090","Questionnaire","SFX","Teleport"),
            new Trigger("S091","Questionnaire","SFX","UI"),
            new Trigger("A001","Awakening","Ambience","SCN_MedicalBay and SCN_HallwayDebrief"),
            new Trigger("A004","Block2","Ambience","SCN_SmokeRoom (identical in both ears)"),
            new Trigger("A006","Block2","Ambience","SCN_SmokeRoom (identical in both ears)"),
            new Trigger("A005","Questionnaire","Ambience","SCN_HallwayDebrief"),
        };

        static readonly Regex IdRx = new Regex(@"^([RHSA]\d{3})", RegexOptions.IgnoreCase);

        public static Dictionary<string, string> IndexFiles()
        {
            var map = new Dictionary<string, string>();
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.StartsWith(PlaceholderDir)) continue;
                var m = IdRx.Match(Path.GetFileNameWithoutExtension(p));
                if (!m.Success) continue;
                string id = m.Groups[1].Value.ToUpperInvariant();
                if (!map.ContainsKey(id)) map[id] = p;
            }
            return map;
        }

        [MenuItem("DeepSleep/Tools/Index Audio And Build Manifest")]
        public static void BuildAll()
        {
            Directory.CreateDirectory("Assets/_Project/Data");
            Directory.CreateDirectory("Assets/_Project/Resources");
            var files = IndexFiles();

            // placeholders for trigger IDs without a real file
            var missing = new List<Trigger>();
            foreach (var t in Triggers) if (!files.ContainsKey(t.id)) missing.Add(t);
            if (missing.Count > 0) Directory.CreateDirectory(PlaceholderDir);
            foreach (var t in missing)
            {
                string path = $"{PlaceholderDir}/{t.id}.wav";
                if (!File.Exists(path)) WritePlaceholderWav(path, t.voice == "Rose" || t.voice == "HALCYON");
            }
            AssetDatabase.Refresh();

            // manifest
            var sb = new StringBuilder();
            sb.AppendLine("ID,file,stage,voice,source_anchor");
            var done = new HashSet<string>();
            foreach (var t in Triggers)
            {
                done.Add(t.id);
                string file = files.TryGetValue(t.id, out var f) ? f : $"{PlaceholderDir}/{t.id}.wav";
                sb.AppendLine($"{t.id},{Csv(file)},{t.stage},{t.voice},{Csv(t.anchor)}");
            }
            var unused = new List<string>(files.Keys); unused.Sort();
            foreach (var id in unused)
            {
                if (done.Contains(id)) continue;
                string voice = id[0] == 'R' ? "Rose" : id[0] == 'H' ? "HALCYON" : id[0] == 'S' ? "SFX" : "Ambience";
                sb.AppendLine($"{id},{Csv(files[id])},none,{voice},unused (no trigger)");
            }
            File.WriteAllText(ManifestPath, sb.ToString());
            AssetDatabase.ImportAsset(ManifestPath);

            WritePlaceholderIssues(missing);
            RebuildLibrary();
            Debug.Log($"[DeepSleep] Audio indexed: {files.Count} files, {missing.Count} placeholders, manifest written");
        }

        static string Csv(string s) => s.Contains(",") ? "\"" + s + "\"" : s;

        static void WritePlaceholderIssues(List<Trigger> missing)
        {
            var sb = new StringBuilder();
            if (missing.Count == 0) sb.AppendLine("None. Every trigger ID has a real file.");
            foreach (var m in missing) sb.AppendLine($"- {m.id} ({m.voice}, {m.anchor}): generated 0.5 s placeholder `Audio/_Placeholders/{m.id}.wav`. Replace with a real file named {m.id}.* (real files win automatically).");
            DocBlock.Update("KNOWN_ISSUES.md", "placeholders", "## Placeholder audio\n" + sb);
        }

        // ------------------------------------------------------------------ placeholder wav
        static void WritePlaceholderWav(string path, bool silence)
        {
            const int rate = 44100; int n = rate / 2;
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++)
                {
                    float env = Mathf.Min(1f, Mathf.Min(i / 2000f, (n - i) / 2000f));
                    float v = silence ? 0f : Mathf.Sin(2 * Mathf.PI * 440f * i / rate) * 0.25f * env;
                    w.Write((short)(v * 32767));
                }
            }
        }

        // ------------------------------------------------------------------ materials, mixer, library
        [MenuItem("DeepSleep/Tools/Rebuild Asset Library")]
        public static void RebuildLibrary()
        {
            Directory.CreateDirectory("Assets/_Project/Resources");
            Directory.CreateDirectory(MaterialsDir);
            Directory.CreateDirectory("Assets/_Project/Mixers");

            var outline = EnsureMaterial("M_HoverOutline", "Fractured_Final/HoverOutline");
            var fade = EnsureMaterial("M_ComfortFade", "Fractured_Final/ComfortFade");
            var vig = EnsureMaterial("M_Vignette", "Fractured_Final/ComfortFade");
            var unlit = EnsureMaterial("M_Unlit", "Universal Render Pipeline/Unlit");
            var mixer = EnsureMixer();

            var lib = AssetDatabase.LoadAssetAtPath<DeepSleepAssets>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<DeepSleepAssets>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.audio.Clear();
            var files = IndexFiles();
            foreach (var kv in files)
                lib.audio.Add(new DeepSleepAssets.AudioEntry { id = kv.Key, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(kv.Value), placeholder = false });
            if (Directory.Exists(PlaceholderDir))
                foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { PlaceholderDir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    string id = Path.GetFileNameWithoutExtension(p).ToUpperInvariant();
                    if (files.ContainsKey(id)) continue;
                    lib.audio.Add(new DeepSleepAssets.AudioEntry { id = id, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p), placeholder = true });
                }
            lib.mixer = mixer;
            lib.outlineMaterial = outline; lib.fadeMaterial = fade; lib.vignetteMaterial = vig; lib.unlitMaterial = unlit;
            lib.voiceManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            lib.mazeLayoutA = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/MazeLayoutA.json");
            lib.mazeLayoutB = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/MazeLayoutB.json");
            lib.InvalidateCache();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] Asset library rebuilt (" + lib.audio.Count + " clips)");
        }

        static Material EnsureMaterial(string name, string shaderName)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shaderName);
            if (sh == null) { Debug.LogError("[DeepSleep] Shader not found: " + shaderName); return m; }
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            return m;
        }

        static AudioMixer EnsureMixer()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (existing != null) return existing;
            var ctrlType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
            var grpType = Type.GetType("UnityEditor.Audio.AudioMixerGroupController, UnityEditor");
            if (ctrlType == null) { Debug.LogError("[DeepSleep] AudioMixerController API not found"); return null; }
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var ctrl = ctrlType.GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { MixerPath });
            var master = ctrlType.GetProperty("masterGroup", F).GetValue(ctrl);
            var create = ctrlType.GetMethod("CreateNewGroup", F);
            var addChild = ctrlType.GetMethod("AddChildToParent", F);
            object ambience = null;
            foreach (var n in new[] { "Voice_Rose", "Voice_Halcyon", "SFX", "Ambience", "UI" })
            {
                var g = create.Invoke(ctrl, new object[] { n, false });
                addChild.Invoke(ctrl, new object[] { g, master });
                if (n == "Ambience") ambience = g;
            }
            // snapshots: Normal and Ducked (Ambience -8 dB under voices)
            var snapsProp = ctrlType.GetProperty("snapshots", F);
            var snaps = (Array)snapsProp.GetValue(ctrl);
            var normal = snaps.GetValue(0) as UnityEngine.Object;
            normal.name = "Normal";
            ctrlType.GetMethod("CloneNewSnapshotFromTarget", F).Invoke(ctrl, new object[] { false });
            snaps = (Array)snapsProp.GetValue(ctrl);
            var ducked = snaps.GetValue(snaps.Length - 1) as UnityEngine.Object;
            ducked.name = "Ducked";
            var snapType = Type.GetType("UnityEditor.Audio.AudioMixerSnapshotController, UnityEditor");
            grpType.GetMethod("SetValueForVolume", F).Invoke(ambience, new object[] { ctrl, ducked, -8f });
            EditorUtility.SetDirty(ctrl as UnityEngine.Object);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath);
            return AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        }
    }

    /// <summary>Maintains named blocks inside a markdown doc under Assets/_Project/Docs.</summary>
    public static class DocBlock
    {
        public static string DocPath(string file) => "Assets/_Project/Docs/" + file;

        public static void Update(string file, string block, string content)
        {
            Directory.CreateDirectory("Assets/_Project/Docs");
            string path = DocPath(file);
            string start = $"<!-- {block}:start -->", end = $"<!-- {block}:end -->";
            string text = File.Exists(path) ? File.ReadAllText(path) : "# " + Path.GetFileNameWithoutExtension(file).Replace('_', ' ') + "\n\n";
            string body = start + "\n" + content.TrimEnd() + "\n" + end;
            int i = text.IndexOf(start, StringComparison.Ordinal), j = text.IndexOf(end, StringComparison.Ordinal);
            if (i >= 0 && j > i) text = text.Substring(0, i) + body + text.Substring(j + end.Length);
            else text = text.TrimEnd() + "\n\n" + body + "\n";
            File.WriteAllText(path, text);
        }

        public static void Append(string file, string line)
        {
            Directory.CreateDirectory("Assets/_Project/Docs");
            string path = DocPath(file);
            File.AppendAllText(path, line.TrimEnd() + "\n");
        }
    }
}
