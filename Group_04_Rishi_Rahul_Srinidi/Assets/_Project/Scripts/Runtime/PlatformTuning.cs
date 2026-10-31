using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CSER
{
    /// Per-platform performance settings applied to every scene: on Quest, drop full-screen post-processing
    /// and turn on foveated rendering; everywhere, match physics to the headset refresh rate so held
    /// objects don't jitter.
    public static class PlatformTuning
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded += (_, __) => Apply();
            Apply();
        }

        static void Apply()
        {
            Time.fixedDeltaTime = 1f / 72f;
            bool mobile = Application.platform == RuntimePlatform.Android;
            if (!mobile) return;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)) data.renderPostProcessing = false;
            OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High;
            OVRManager.useDynamicFoveatedRendering = true;
            // steady clocks: no thermal throttling spikes mid-chapter, and enough CPU for physics + UI rebuilds
            OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
            OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
            QualitySettings.vSyncCount = 0;   // the compositor paces frames on Quest
        }
    }
}
