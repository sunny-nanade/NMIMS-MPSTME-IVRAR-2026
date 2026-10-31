using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace RescueSim.EditorTools
{
    /// <summary>
    /// Idempotent project configuration for OpenXR (Standalone/Editor) and Meta Quest (Android).
    /// Run from the menu: Rescue Sim / Configure XR and Android.
    /// </summary>
    public static class XRProjectSetup
    {
        const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string PerTargetAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        [MenuItem("Rescue Sim/Configure XR and Android")]
        public static void ConfigureAll()
        {
            ConfigureLoader(BuildTargetGroup.Standalone);
            ConfigureLoader(BuildTargetGroup.Android);

            ConfigureFeatures(BuildTargetGroup.Standalone, quest: false);
            ConfigureFeatures(BuildTargetGroup.Android, quest: true);

            ConfigureAndroidPlayer();

            AssetDatabase.SaveAssets();
            Debug.Log("[RescueSim] XR + Android configuration applied. Check Project Settings > XR Plug-in Management to confirm.");
        }

        static void ConfigureLoader(BuildTargetGroup group)
        {
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(PerTargetAssetPath);
            if (perTarget == null)
            {
                Debug.LogError($"[RescueSim] Missing {PerTargetAssetPath}. Open Project Settings > XR Plug-in Management once, then rerun.");
                return;
            }

            if (!perTarget.HasSettingsForBuildTarget(group))
                perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;

            var manager = perTarget.ManagerSettingsForBuildTarget(group);
            bool ok = XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderType, group);
            if (!ok)
                Debug.LogError($"[RescueSim] Could not assign OpenXR loader for {group}.");

            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
        }

        static void ConfigureFeatures(BuildTargetGroup group, bool quest)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogError($"[RescueSim] No OpenXR settings for {group}.");
                return;
            }

            Enable<OculusTouchControllerProfile>(settings);
            Enable<MetaQuestTouchPlusControllerProfile>(settings);
            Enable<MetaQuestTouchProControllerProfile>(settings);
            if (quest)
            {
                Enable<MetaQuestFeature>(settings);
                // Controller-free play: OpenXR hand tracking (joint poses) plus Meta's aim/pinch extension. The XR Hands package
                // adds the Android manifest entries (hand tracking feature + permission) at build time.
                Enable<UnityEngine.XR.Hands.OpenXR.HandTracking>(settings);
                Enable<UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim>(settings);
            }

            EditorUtility.SetDirty(settings);
        }

        static void Enable<T>(OpenXRSettings settings) where T : OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
            {
                Debug.LogWarning($"[RescueSim] OpenXR feature {typeof(T).Name} not found.");
                return;
            }
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        static void ConfigureAndroidPlayer()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ProjectInfo.AndroidPackageId);
            PlayerSettings.productName = ProjectInfo.ProductName;
            PlayerSettings.companyName = "RescueSim";

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            PlayerSettings.runInBackground = true;
        }
    }
}
