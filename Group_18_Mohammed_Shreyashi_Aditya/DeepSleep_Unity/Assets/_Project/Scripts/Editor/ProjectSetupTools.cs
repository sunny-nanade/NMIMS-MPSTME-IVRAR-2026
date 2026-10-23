using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fractured_Final.EditorTools
{
    public static class ProjectSetupTools
    {
        public static readonly string[] Tags = { "Grabbable", "Beacon", "Port", "Wall", "Junction", "DeadEndCap", "EntryZone", "ExitZone" };
        public static readonly string[] Layers = { "Interactable", "Wall" };

        public const string ScenesDir = "Assets/_Project/Scenes";
        public static readonly string[] SceneNames = { "SCN_Setup", "SCN_MedicalBay", "SCN_SmokeRoom", "SCN_HallwayDebrief" };

        public static string ScenePath(string n) => $"{ScenesDir}/{n}.unity";

        [MenuItem("DeepSleep/Setup/Create Tags And Layers")]
        public static void CreateTagsAndLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tm.FindProperty("tags");
            foreach (var t in Tags)
            {
                bool found = false;
                for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == t) found = true;
                if (!found) { tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = t; }
            }
            var layers = tm.FindProperty("layers");
            foreach (var l in Layers)
            {
                bool found = false;
                for (int i = 8; i < 32; i++) if (layers.GetArrayElementAtIndex(i).stringValue == l) found = true;
                if (found) continue;
                for (int i = 8; i < 32; i++)
                {
                    var el = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = l; break; }
                }
            }
            tm.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] Tags and layers ensured");
        }

        [MenuItem("DeepSleep/Setup/Create Scenes And Build Settings")]
        public static void CreateScenes()
        {
            Directory.CreateDirectory(ScenesDir);
            // move SCN_Setup (keeps GUID and contents)
            string old = "Assets/Scenes/SCN_Setup.unity";
            if (File.Exists(old) && !File.Exists(ScenePath("SCN_Setup")))
            {
                string err = AssetDatabase.MoveAsset(old, ScenePath("SCN_Setup"));
                if (!string.IsNullOrEmpty(err)) Debug.LogError("[DeepSleep] Move failed: " + err);
            }
            foreach (var n in SceneNames)
            {
                if (File.Exists(ScenePath(n))) continue;
                var s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, ScenePath(n));
            }
            var list = new List<EditorBuildSettingsScene>();
            foreach (var n in SceneNames) list.Add(new EditorBuildSettingsScene(ScenePath(n), true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] Scenes created and Build Settings set");
        }
    }
}
