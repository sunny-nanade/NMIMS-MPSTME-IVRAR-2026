using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CSER.EditorTools
{
    public static class SceneDebug
    {
        static void Open(string name) => UnityEditor.SceneManagement.EditorSceneManager.OpenScene($"Assets/_Project/Scenes/{name}.unity");

        [MenuItem("CSER/Debug/Open Scene/Main Menu")] static void O0() => Open("MainMenu");
        [MenuItem("CSER/Debug/Open Scene/Chapter 1")] static void O1() => Open("Ch1_NightShift");
        [MenuItem("CSER/Debug/Open Scene/Chapter 2")] static void O2() => Open("Ch2_FredsOffice");
        [MenuItem("CSER/Debug/Open Scene/Chapter 3")] static void O3() => Open("Ch3_ServerRoom");

        /// Logs every renderer whose bounds cover a large horizontal area (finds stray planes/slabs).
        [MenuItem("CSER/Debug/List Large Renderers")]
        public static void ListLarge()
        {
            var sb = new System.Text.StringBuilder("[CSER] Large renderers:\n");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).OrderByDescending(r => r.bounds.size.x * r.bounds.size.z))
            {
                var b = r.bounds;
                if (b.size.x < 1.5f || b.size.z < 1.5f) continue;
                sb.AppendLine($"{GetPath(r.transform)}  center={b.center}  size={b.size}  mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")}");
            }
            Debug.Log(sb.ToString());
        }

        /// Counts the interaction components in the open scene (grab / ray / poke / teleport / body) and prints each grab item.
        [MenuItem("CSER/Debug/Interaction Report")]
        public static void InteractionReport()
        {
            int F<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include).Length;
            var sb = new System.Text.StringBuilder($"[CSER] Interaction report ({UnityEngine.SceneManagement.SceneManager.GetActiveScene().name})\n");
            sb.AppendLine($"GrabItem={F<GrabItem>()} Grabbable={F<Oculus.Interaction.Grabbable>()} GrabInteractable={F<Oculus.Interaction.GrabInteractable>()} " +
                          $"RayInteractable={F<Oculus.Interaction.RayInteractable>()} PokeInteractable={F<Oculus.Interaction.PokeInteractable>()} " +
                          $"Clickable={F<Clickable>()} DropZone={F<DropZone>()}");
            sb.AppendLine($"Interactors: Ray={F<Oculus.Interaction.RayInteractor>()} Grab={F<Oculus.Interaction.GrabInteractor>()} " +
                          $"Poke={F<Oculus.Interaction.PokeInteractor>()} " +
                          $"Teleport={F<Oculus.Interaction.Locomotion.TeleportInteractor>()} Controllers={F<Oculus.Interaction.Input.Controller>()}");
            foreach (var g in Object.FindObjectsByType<GrabItem>(FindObjectsInactive.Include))
                sb.AppendLine($"  {GetPath(g.transform)} grabbables={g.GetComponentsInChildren<Oculus.Interaction.Grabbable>(true).Length} colliders={g.GetComponentsInChildren<Collider>(true).Length}");
            Debug.Log(sb.ToString());
        }

        /// Lists components that could move or drop the player rig (locomotion, gravity, rigidbodies, character controllers).
        [MenuItem("CSER/Debug/List Movers")]
        public static void ListMovers()
        {
            var sb = new System.Text.StringBuilder("[CSER] Movers:\n");
            foreach (var c in Object.FindObjectsByType<Component>(FindObjectsInactive.Include))
            {
                var n = c.GetType().Name;
                if (n.Contains("Locomot") || n.Contains("Gravity") || n.Contains("CharacterController") || n == "Rigidbody" && c.GetComponent<GrabItem>() == null)
                    sb.AppendLine($"  {GetPath(c.transform)} : {n} @ {c.transform.position}");
            }
            Debug.Log(sb.ToString());
        }

        static string GetPath(Transform t) => t.parent ? GetPath(t.parent) + "/" + t.name : t.name;
    }
}
