using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Turns the Building Block camera/interaction rig in SCN_Setup into PF_PlayerRig and reuses it in every scene.</summary>
    public static class RigPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/PF_PlayerRig.prefab";

        [MenuItem("DeepSleep/Setup/Build PF_PlayerRig")]
        public static void Build()
        {
            if (File.Exists(PrefabPath)) { Debug.Log("[DeepSleep] PF_PlayerRig already exists"); return; }
            var scene = EditorSceneManager.OpenScene(ProjectSetupTools.ScenePath("SCN_Setup"));
            GameObject rig = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.name == "[BuildingBlock] Camera Rig") rig = r;
            }
            if (rig == null) { Debug.LogError("[DeepSleep] Building Block rig not found in SCN_Setup"); return; }

            // remove the sample objects of the Building Blocks (distance grab, ray cube, hand grab cube, sample audio source)
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r == rig) continue;
                if (r.name.StartsWith("[BuildingBlock]") && r.name != "[BuildingBlock] Controller Buttons Mapper" && r.name != "[BuildingBlock] Haptics")
                    Object.DestroyImmediate(r);
            }

            var emu = rig.GetComponent<OVRHeadsetEmulator>();
            if (emu != null) Object.DestroyImmediate(emu);

            rig.name = "PF_PlayerRig";
            if (rig.GetComponent<PlayerRig>() == null) rig.AddComponent<PlayerRig>();
            var loco = rig.GetComponent<PlayerLocomotion>();
            if (loco == null) loco = rig.AddComponent<PlayerLocomotion>();
            var so = new SerializedObject(loco);
            so.FindProperty("vignetteMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/M_Vignette.mat");
            so.ApplyModifiedProperties();
            if (rig.GetComponent<EditorFallbackRig>() == null) rig.AddComponent<EditorFallbackRig>();

            foreach (Hand h in new[] { Hand.Left, Hand.Right })
            {
                string n = "HandInteractor_" + h;
                var existing = rig.transform.Find(n);
                if (existing != null) continue;
                var go = new GameObject(n);
                go.transform.SetParent(rig.transform, false);
                var hi = go.AddComponent<HandInteractor>();
                var hso = new SerializedObject(hi);
                hso.FindProperty("Side").enumValueIndex = (int)h;
                hso.ApplyModifiedProperties();
            }

            // keep the raycast layers clean: the whole rig lives on Ignore Raycast
            foreach (var t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            StripDistanceGrab(rig);
            Directory.CreateDirectory("Assets/_Project/Prefabs");
            PrefabUtility.SaveAsPrefabAssetAndConnect(rig, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[DeepSleep] PF_PlayerRig created");
        }

        /// <summary>No distance grab in this project: strip the SDK distance-grab interactors and their reticle drawers.</summary>
        public static int StripDistanceGrab(GameObject root)
        {
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root.transform) continue;
                string gn = t.name;
                if (gn.Contains("DistanceGrab") || gn.Contains("DistanceHandGrab") || gn.Contains("Reticle"))
                { try { Object.DestroyImmediate(t.gameObject); n++; } catch { } }
            }
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    if (tn.StartsWith("Reticle") || tn.Contains("DistanceHandGrab") || tn.Contains("DistanceGrab") || tn.Contains("DistanceInteractor") || tn.Contains("MoveTowardsTarget") || tn == "ActiveStateUnityEventWrapper" || tn == "InteractorActiveState" || tn == "ActiveStateGroup")
                    {
                        // other components may depend on it; destroy those first on later passes
                        try { Object.DestroyImmediate(c, true); n++; } catch { }
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// The ISDK interactor web (distance grab, reticles, active-state graphs) is interlinked and not used: all
        /// interaction goes through InputService + HandInteractor. Keep only the OVR camera rig and add plain controller models.
        /// </summary>
        [MenuItem("DeepSleep/Setup/Slim PF_PlayerRig (remove ISDK interactors)")]
        public static void SlimPrefab()
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            var isdk = contents.transform.Find("[BuildingBlock] OVRComprehensiveInteractionRig");
            if (isdk != null) Object.DestroyImmediate(isdk.gameObject);
            // the Building Block rig already carries SDK controller visuals under the controller anchors; remove any duplicates added earlier
            foreach (var n in new[] { "TrackingSpace/LeftHandAnchor/OVRControllerPrefab", "TrackingSpace/RightHandAnchor/OVRControllerPrefab" })
            {
                var dup = contents.transform.Find(n);
                if (dup != null) Object.DestroyImmediate(dup.gameObject);
            }
            foreach (var t in contents.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[DeepSleep] PF_PlayerRig slimmed: ISDK interaction rig removed, controller visuals added");
        }

        /// <summary>Replace the rig instance in the open scene with a fresh PF_PlayerRig.</summary>
        public static void RefreshRigInScene()
        {
            foreach (var r in Object.FindObjectsByType<PlayerRig>()) Object.DestroyImmediate(r.gameObject);
            EnsureRigInScene();
        }

        [MenuItem("DeepSleep/Setup/Clean PF_PlayerRig Prefab")]
        public static void CleanPrefab()
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            int n = StripDistanceGrab(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log($"[DeepSleep] PF_PlayerRig cleaned: {n} distance-grab components removed");
        }

        /// <summary>Adds PF_PlayerRig at the origin of the open scene if it does not contain one yet.</summary>
        public static GameObject EnsureRigInScene()
        {
            var existing = Object.FindAnyObjectByType<PlayerRig>();
            if (existing != null) return existing.gameObject;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[DeepSleep] PF_PlayerRig missing"); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            inst.transform.position = Vector3.zero;
            return inst;
        }
    }
}
