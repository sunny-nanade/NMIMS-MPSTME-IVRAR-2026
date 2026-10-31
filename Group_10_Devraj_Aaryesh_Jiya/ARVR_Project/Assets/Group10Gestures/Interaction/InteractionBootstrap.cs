using UnityEngine;
using Group10Gestures.Core;

namespace Group10Gestures.Interaction
{
    /// <summary>
    /// Adds the room-drawing + trial-logging layer automatically when a scene containing the
    /// Group 10 GameManager starts. No scene editing and no changes to existing scripts needed.
    /// To remove the feature, delete the Assets/Group10Gestures/Interaction folder.
    /// </summary>
    public static class InteractionBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (Object.FindAnyObjectByType<GameManager>() == null) return;
            if (Object.FindAnyObjectByType<RoomBuilderController>() != null) return;

            var go = new GameObject("Group10_Interaction");
            go.AddComponent<RoomBuilderController>();
            go.AddComponent<TrialLogger>();
            go.AddComponent<XRGuidePanel>(); // headset guide (only shows while the Meta XR provider is active)
            Debug.Log("[Group10] Interaction layer started (room drawing + trial logger + headset guide).");
        }
    }
}
