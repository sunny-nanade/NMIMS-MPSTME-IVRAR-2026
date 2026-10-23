using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// Audits every clip of the voice manifest (length, channels, import settings, peak, RMS, clipping, silence, placeholder) and applies the import rules:
    /// every clip decompresses on load with preload on and no background loading (no delay on first play; ambience needs readable samples for the mono down-mix),
    /// nothing is forced to ambisonic. Writes Docs/Reports/audit_clips.md.
    /// </summary>
    public static class ClipAudit
    {
        public const string ReportPath = "Assets/_Project/Docs/Reports/audit_clips.md";

        [MenuItem("DeepSleep/Audit/Clips: report and fix import settings")]
        public static void Run()
        {
            var assets = Resources.Load<DeepSleepAssets>("DeepSleepAssets");
            var sb = new StringBuilder();
            sb.AppendLine("# Clip audit"); sb.AppendLine();
            sb.AppendLine($"Date: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}"); sb.AppendLine();
            sb.AppendLine("| ID | file | len s | ch | load | preload | bg | peak | clipped samples | RMS dBFS | note |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            int fixedCount = 0, problems = 0;
            foreach (var e in assets.audio)
            {
                if (e.clip == null) { sb.AppendLine($"| {e.id} | - | - | - | - | - | - | - | - | - | NULL CLIP |"); problems++; continue; }
                string path = AssetDatabase.GetAssetPath(e.clip);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                string note = e.placeholder ? "PLACEHOLDER" : "";
                if (imp != null)
                {
                                        var want = AudioClipLoadType.DecompressOnLoad; // ambience too: the mono down-mix and the loudness scan read the samples
                    var s = imp.defaultSampleSettings;
                    bool changed = false;
                    if (s.loadType != want) { s.loadType = want; changed = true; }
                    if (!s.preloadAudioData) { s.preloadAudioData = true; changed = true; }
                    if (imp.loadInBackground) { imp.loadInBackground = false; changed = true; }
                    if (imp.ambisonic) { imp.ambisonic = false; changed = true; }
                    if (changed)
                    {
                        imp.defaultSampleSettings = s;
                        if (imp.ContainsSampleSettingsOverride("Android")) { var o = imp.GetOverrideSampleSettings("Android"); o.loadType = want; o.preloadAudioData = true; imp.SetOverrideSampleSettings("Android", o); }
                        imp.SaveAndReimport(); fixedCount++; note += " import fixed";
                    }
                }
                var c = e.clip;
                float peak = 0, clipped = 0; double sum = 0; int n = 0;
                try
                {
                    var d = new float[c.samples * c.channels];
                    if (c.GetData(d, 0)) { foreach (var x in d) { float a = Mathf.Abs(x); if (a > peak) peak = a; if (a >= 0.999f) clipped++; sum += x * x; } n = d.Length; }
                }
                catch { }
                double rms = n > 0 ? System.Math.Sqrt(sum / n) : 0;
                string rmsText = rms > 1e-6 ? (20 * System.Math.Log10(rms)).ToString("F1") : "SILENT";
                if (rms <= 1e-6) { note += " SILENT"; problems++; }
                if (clipped > 2) { note += " CLIPPED"; problems++; }
                if (c.length < 0.15f) { note += " VERY SHORT"; problems++; }
                var ss = imp != null ? imp.defaultSampleSettings : default;
                sb.AppendLine($"| {e.id} | {Path.GetFileName(path)} | {c.length:F2} | {c.channels} | {ss.loadType} | {ss.preloadAudioData} | {(imp != null && imp.loadInBackground)} | {peak:F2} | {clipped} | {rmsText} | {note.Trim()} |");
            }
            sb.AppendLine();
            sb.AppendLine($"Import settings fixed on {fixedCount} clips; {problems} problems (silent, clipped, very short or missing).");
            File.WriteAllText(ReportPath, sb.ToString());
            AssetDatabase.SaveAssets();
            Debug.Log($"[DeepSleep] Clip audit: {fixedCount} import fixes, {problems} problems. See {ReportPath}");
        }
    }
}
