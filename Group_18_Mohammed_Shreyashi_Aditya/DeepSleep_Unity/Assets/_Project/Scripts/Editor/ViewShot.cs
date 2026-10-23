using System.IO;
using UnityEngine;

namespace Fractured_Final.EditorTools
{
    /// <summary>Renders the open scene from a point (edit mode, no play) into a PNG so scene looks can be reviewed without a headset.</summary>
    public static class ViewShot
    {
        public static readonly string OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Analysis", "screens", "review"));

        public static string Shoot(string name, Vector3 pos, float yaw, float pitch = 0f, float fov = 90f)
        {
            Directory.CreateDirectory(OutDir);
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 80f;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0f));
            var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(OutDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return path;
        }
    }
}
