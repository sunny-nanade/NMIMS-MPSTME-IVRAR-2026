using UnityEngine;
using UnityEngine.Rendering;

namespace Acrophobia.ProceduralEnvironment
{
    /// <summary>
    /// Utility and controller for dynamically manipulating catwalk and environment materials
    /// in Universal Render Pipeline (URP).
    /// Enforces correct URP shader property conventions (Color properties for _BaseColor).
    /// </summary>
    public static class CatwalkMaterialController
    {
        public const string BaseColorProperty = "_BaseColor";
        public const string FallbackColorProperty = "_Color";
        public const string EmissionColorProperty = "_EmissionColor";
        public const string SurfaceProperty = "_Surface";
        public const string BlendProperty = "_Blend";
        public const string SrcBlendProperty = "_SrcBlend";
        public const string DstBlendProperty = "_DstBlend";
        public const string ZWriteProperty = "_ZWrite";
        public const string TransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";
        public const string EmissionKeyword = "_EMISSION";

        /// <summary>
        /// Reads the base color from a URP material safely.
        /// NEVER use GetFloat("_BaseColor") - _BaseColor is a Color property in URP.
        /// </summary>
        public static Color GetBaseColor(Material material)
        {
            if (material == null) return Color.white;

            if (material.HasProperty(BaseColorProperty))
            {
                return material.GetColor(BaseColorProperty);
            }
            if (material.HasProperty(FallbackColorProperty))
            {
                return material.GetColor(FallbackColorProperty);
            }

            return Color.white;
        }

        /// <summary>
        /// Sets the target alpha transparency on a URP material.
        /// Adjusts URP surface type, blend mode, zWrite, and shader keywords accordingly.
        /// </summary>
        public static void SetAlpha(Material material, float targetAlpha)
        {
            if (material == null) return;

            targetAlpha = Mathf.Clamp01(targetAlpha);

            // Always read and modify color and alpha using GetColor / SetColor
            if (material.HasProperty(BaseColorProperty))
            {
                Color c = material.GetColor(BaseColorProperty);
                c.a = targetAlpha;
                material.SetColor(BaseColorProperty, c);
            }
            else if (material.HasProperty(FallbackColorProperty))
            {
                Color c = material.GetColor(FallbackColorProperty);
                c.a = targetAlpha;
                material.SetColor(FallbackColorProperty, c);
            }

            // Configure URP transparency keywords and render queue
            if (targetAlpha < 0.999f)
            {
                material.SetFloat(SurfaceProperty, 1.0f); // 1 = Transparent
                material.SetFloat(BlendProperty, 0.0f);   // 0 = Alpha blend
                material.SetFloat(SrcBlendProperty, (float)BlendMode.SrcAlpha);
                material.SetFloat(DstBlendProperty, (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat(ZWriteProperty, 0.0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword(TransparentKeyword);
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            else
            {
                material.SetFloat(SurfaceProperty, 0.0f); // 0 = Opaque
                material.SetFloat(BlendProperty, 0.0f);
                material.SetFloat(SrcBlendProperty, (float)BlendMode.One);
                material.SetFloat(DstBlendProperty, (float)BlendMode.Zero);
                material.SetFloat(ZWriteProperty, 1.0f);
                material.renderQueue = (int)RenderQueue.Geometry;
                material.SetOverrideTag("RenderType", "Opaque");
                material.DisableKeyword(TransparentKeyword);
            }
        }

        /// <summary>
        /// Sets emission color and intensity on a URP material safely.
        /// </summary>
        public static void SetEmission(Material material, Color emissionColor, float intensity = 1.0f)
        {
            if (material == null) return;

            Color finalEmission = emissionColor * intensity;
            if (material.HasProperty(EmissionColorProperty))
            {
                material.SetColor(EmissionColorProperty, finalEmission);
            }

            if (intensity > 0.001f)
            {
                material.EnableKeyword(EmissionKeyword);
            }
            else
            {
                material.DisableKeyword(EmissionKeyword);
            }
        }
    }
}
