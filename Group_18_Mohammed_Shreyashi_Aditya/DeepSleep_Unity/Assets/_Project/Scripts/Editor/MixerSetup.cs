using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Fractured_Final.EditorTools
{
    /// <summary>
    /// Exposes one volume parameter per MIX_Main group (VolMaster, VolRose, VolHalcyon, VolSFX, VolAmbience, VolUI) so that SfxDirector can duck
    /// Ambience and SFX under the voices at runtime, and sets the base group levels of both snapshots.
    /// Uses the editor-internal mixer API through reflection (there is no public API for exposing parameters).
    /// </summary>
    public static class MixerSetup
    {
        public static readonly string[] Groups = { "Master", "Voice_Rose", "Voice_Halcyon", "SFX", "Ambience", "UI" };
        public static string ParamName(string group) { return "Vol" + (group == "Voice_Rose" ? "Rose" : group == "Voice_Halcyon" ? "Halcyon" : group); }

        [MenuItem("DeepSleep/Build/Mixer Exposed Parameters")]
        public static void Build()
        {
            var assets = Resources.Load<DeepSleepAssets>("DeepSleepAssets");
            var mixer = assets.mixer;
            var ed = typeof(Editor).Assembly;
            var tCtrl = ed.GetType("UnityEditor.Audio.AudioMixerController");
            var tParam = ed.GetType("UnityEditor.Audio.ExposedAudioParameter");
            var tGroup = ed.GetType("UnityEditor.Audio.AudioMixerGroupController");
            if (tCtrl == null || tParam == null || tGroup == null) { Debug.LogError("[DeepSleep] mixer editor API not found"); return; }
            var fields = tParam.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fGuid = null, fName = null;
            foreach (var f in fields) { if (f.Name.ToLower().Contains("guid")) fGuid = f; if (f.Name.ToLower().Contains("name")) fName = f; }
            if (fGuid == null || fName == null) { Debug.LogError("[DeepSleep] ExposedAudioParameter fields: " + string.Join(",", Array.ConvertAll(fields, f => f.Name))); return; }
            var arr = Array.CreateInstance(tParam, Groups.Length);
            for (int i = 0; i < Groups.Length; i++)
            {
                var g = mixer.FindMatchingGroups(Groups[i]);
                AudioMixerGroup grp = null;
                foreach (var x in g) if (x.name == Groups[i]) grp = x;
                var guid = tGroup.GetMethod("GetGUIDForVolume").Invoke(grp, null);
                object p = Activator.CreateInstance(tParam);
                fGuid.SetValue(p, guid);
                fName.SetValue(p, ParamName(Groups[i]));
                arr.SetValue(p, i);
            }
            tCtrl.GetProperty("exposedParameters").SetValue(mixer, arr);
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] MIX_Main: exposed " + string.Join(", ", Array.ConvertAll(Groups, ParamName)));
        }
    }
}
