using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Temporary Phase 1 test area in SCN_Setup. Removed again before the end of Phase 2.</summary>
    public static class Phase1TestArea
    {
        const string RootName = "TEMP_Phase1TestArea";

        public static void Create()
        {
            var scene = EditorSceneManager.OpenScene(ProjectSetupTools.ScenePath("SCN_Setup"));
            if (GameObject.Find(RootName) != null) return;
            RigPrefabBuilder.EnsureRigInScene();
            var root = new GameObject(RootName);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "TeleportFloor"; floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0, -0.05f, 0); floor.transform.localScale = new Vector3(14, 0.1f, 14);

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "ReferenceTable_0.9m"; table.transform.SetParent(root.transform);
            table.transform.position = new Vector3(0, 0.45f, 2f); table.transform.localScale = new Vector3(1.2f, 0.9f, 0.6f);

            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cap.name = "ReferenceCapsule_1.8m"; cap.transform.SetParent(root.transform);
            cap.transform.position = new Vector3(2f, 0.9f, 2f); cap.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);

            for (int i = 0; i < 3; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "GrabCube_" + (i + 1); c.transform.SetParent(root.transform);
                c.transform.localScale = Vector3.one * 0.12f;
                c.transform.position = new Vector3(-0.3f + 0.3f * i, 0.96f, 2f);
                c.AddComponent<Rigidbody>();
                c.AddComponent<Grabbable>();
                c.tag = "Grabbable";
            }

            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.position = Vector3.zero;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Remove()
        {
            var scene = EditorSceneManager.OpenScene(ProjectSetupTools.ScenePath("SCN_Setup"));
            var r = GameObject.Find(RootName);
            if (r != null) Object.DestroyImmediate(r);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
