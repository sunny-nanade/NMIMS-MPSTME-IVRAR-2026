using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Debug helper: lays assets out in a row in a scratch scene and frames the scene view on them.</summary>
    public static class AssetGallery
    {
        public static void Show(string[] paths, float spacing, float viewDistance)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            float x = 0;
            var root = new GameObject("Gallery");
            foreach (var p in paths)
            {
                var a = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (a == null) { Debug.LogWarning("gallery: missing " + p); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(a);
                go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(x, 0, 0);
                x += spacing;
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = new Vector3(x * 0.5f, -0.01f, 0); floor.transform.localScale = new Vector3(x / 10f + 1, 1, 1);
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                sv.in2DMode = false;
                sv.pivot = new Vector3(x * 0.5f, 1.2f, 0);
                sv.rotation = Quaternion.Euler(12, 180, 0);
                sv.size = viewDistance;
                sv.Repaint();
            }
        }
    }
}
