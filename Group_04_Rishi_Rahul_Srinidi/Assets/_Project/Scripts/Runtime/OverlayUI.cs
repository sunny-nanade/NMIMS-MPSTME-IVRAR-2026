using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSER
{
    /// Makes a world-space canvas draw on top of the whole scene (ZTest Always, overlay render queue) so popups,
    /// warnings and hints can never disappear behind walls, screens or props. Call Apply once after the canvas
    /// content has been built; text and images added later can be covered with another Apply call.
    /// A higher queue draws over a lower one (the objective popup sits above the HUD).
    public static class OverlayUI
    {
        static readonly Dictionary<int, Material> imageMats = new Dictionary<int, Material>();
        static readonly Dictionary<(Material, int), Material> textMats = new Dictionary<(Material, int), Material>();
        static bool textLookupDone;
        static Shader textShader;

        public static void Apply(Transform root, int queue = 4000)
        {
            if (!root) return;
            if (!textLookupDone)
            {
                textLookupDone = true;
                // Resources materials guarantee the shader is packed into the player (Shader.Find alone only sees
                // shaders something else already pulled into the build)
                var refMat = Resources.Load<Material>("Shaders/TMP_OverlayMobile");
                if (!refMat) refMat = Resources.Load<Material>("Shaders/TMP_Overlay");
                textShader = refMat ? refMat.shader : null;
                if (!textShader) textShader = Shader.Find("TextMeshPro/Mobile/Distance Field Overlay");
                if (!textShader) textShader = Shader.Find("TextMeshPro/Distance Field Overlay");
                if (!textShader) Debug.LogWarning("[OverlayUI] TextMeshPro overlay shader missing from the build: popups draw without overlay");
            }
            // without the text overlay shader the panels must not go on top either, or they would cover their own text
            // (that was the blank-popup bug on Quest: the shader was stripped from the player build)
            if (!textShader) return;
            var shader = Resources.Load<Shader>("Shaders/CSER_UIOverlay");
            if (shader)
            {
                if (!imageMats.TryGetValue(queue, out var im) || !im)
                    imageMats[queue] = im = new Material(shader) { name = "CSER_UIOverlay " + queue, renderQueue = queue };
                foreach (var img in root.GetComponentsInChildren<Image>(true)) img.material = im;
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var src = t.fontSharedMaterial;
                if (!src || src.shader == textShader) continue;
                if (!textMats.TryGetValue((src, queue), out var tm) || !tm)
                {
                    tm = new Material(src) { name = src.name + " (overlay " + queue + ")" };
                    tm.shader = textShader;
                    tm.renderQueue = queue + 1;
                    textMats[(src, queue)] = tm;
                }
                t.fontSharedMaterial = tm;
            }
        }
    }
}
