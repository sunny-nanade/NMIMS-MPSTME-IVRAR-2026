using UnityEditor;
using UnityEditor.SceneManagement;

namespace RescueSim.EditorTools
{
    /// <summary>Menu shortcuts to open each campaign scene in the editor.</summary>
    public static class OpenScenes
    {
        static void Open(string path) { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path); }
        [MenuItem("Rescue Sim/Open/Main Menu")] static void Menu() => Open(SceneBuilder.MenuScenePath);
        [MenuItem("Rescue Sim/Open/Stage 1 Factory")] static void S1() => Open(SceneBuilder.MissionScenePath);
        [MenuItem("Rescue Sim/Open/Stage 2 Earthquake")] static void S2() => Open(SceneBuilder.QuakeScenePath);
        [MenuItem("Rescue Sim/Open/Triage Simulation")] static void Tri() => Open(SceneBuilder.TriageScenePath);
        [MenuItem("Rescue Sim/Open/Stage 3 Flood")] static void S3() => Open(SceneBuilder.FloodScenePath);
    }
}
