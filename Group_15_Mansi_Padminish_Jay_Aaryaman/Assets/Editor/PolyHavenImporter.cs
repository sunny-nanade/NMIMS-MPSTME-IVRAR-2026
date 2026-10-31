using System;
using UnityEditor;
using UnityEngine;

namespace Acrophobia.EditorTools
{
    /// <summary>
    /// Auto-configures Poly Haven PBR textures dropped anywhere under Assets/Textures/.
    ///
    /// Classification is driven by Poly Haven's filename suffixes, which are underscore
    /// delimited tokens (concrete_floor_02_arm_1k.jpg -> tokens: concrete, floor, 02, arm, 1k).
    /// Tokens are matched exactly rather than by substring, so a name like "marble" or
    /// "alarm_panel" cannot be mistaken for an ARM map.
    ///
    /// Already-configured assets are skipped via a stamp written into the importer's
    /// User Data, so re-imports and library rebuilds stay fast and any manual override
    /// you make afterwards is preserved. To force a re-apply, bump PipelineVersion or
    /// clear the User Data field at the bottom of the texture's Inspector.
    ///
    /// Scope note: HDRIs under Assets/HDRIs/ are deliberately NOT touched. Equirectangular
    /// sky maps need cube shape, uncapped size and HDR-aware compression, which would be
    /// wrong for every rule below. See Assets/Editor/README-textures.md.
    /// </summary>
    public class PolyHavenImporter : AssetPostprocessor
    {
        private const string TextureRoot = "Assets/Textures/";

        // Bump this to force every texture under Assets/Textures/ to be reconfigured.
        private const int PipelineVersion = 1;
        private const string StampPrefix = "polyhaven-pipeline-v";

        private const int DefaultMaxSize = 1024;
        private const TextureImporterFormat AndroidFormat = TextureImporterFormat.ASTC_6x6;
        private const TextureImporterFormat StandaloneFormat = TextureImporterFormat.BC7;

        private const string AndroidPlatform = "Android";
        private const string StandalonePlatform = "Standalone";

        private enum MapKind
        {
            Unrecognized,
            BaseColor,
            NormalOpenGL,
            NormalDirectX,
            ArmPacked,
            LinearData,
            Displacement
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            TextureImporter importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            string stamp = StampPrefix + PipelineVersion;
            if (string.Equals(importer.userData, stamp, StringComparison.Ordinal))
            {
                return;
            }

            string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            MapKind kind = Classify(fileName);

            if (kind == MapKind.Unrecognized)
            {
                Debug.Log(
                    $"[PolyHavenImporter] No Poly Haven map suffix recognised in '{fileName}' " +
                    "- import settings left untouched. Expected one of: _diff, _nor_gl, _arm, " +
                    "_rough, _ao, _metal, _disp.",
                    AssetDatabase.LoadAssetAtPath<Texture>(assetPath));
                return;
            }

            if (kind == MapKind.Displacement)
            {
                Debug.LogWarning(
                    $"[PolyHavenImporter] '{fileName}' is a displacement map. This project does " +
                    "not use displacement or tessellation - nothing samples it, and on Quest it " +
                    "is wasted texture memory. Delete it unless you have a specific plan for it.",
                    AssetDatabase.LoadAssetAtPath<Texture>(assetPath));
            }

            if (kind == MapKind.NormalDirectX)
            {
                Debug.LogWarning(
                    $"[PolyHavenImporter] '{fileName}' looks like a DirectX-convention normal map " +
                    "(_nor_dx). Unity expects OpenGL convention (green channel up). Download the " +
                    "_nor_gl variant instead, or the surface lighting will read inverted.",
                    AssetDatabase.LoadAssetAtPath<Texture>(assetPath));
            }

            ApplyTypeAndColorSpace(importer, kind);
            ApplyCommonSettings(importer);
            ApplyPlatformSettings(importer);

            importer.userData = stamp;
        }

        /// <summary>
        /// Maps a Poly Haven filename to a map kind using exact underscore-delimited tokens.
        /// Normal-map variants are tested before the generic cases because "_nor_gl" and
        /// "_nor_dx" both carry a "nor" token.
        /// </summary>
        private static MapKind Classify(string fileName)
        {
            string[] tokens = fileName.ToLowerInvariant().Split('_');

            if (HasToken(tokens, "nor") && HasToken(tokens, "gl")) return MapKind.NormalOpenGL;
            if (HasToken(tokens, "nor") && HasToken(tokens, "dx")) return MapKind.NormalDirectX;
            if (HasToken(tokens, "nor") || HasToken(tokens, "normal")) return MapKind.NormalOpenGL;

            if (HasToken(tokens, "arm")) return MapKind.ArmPacked;

            if (HasToken(tokens, "diff") || HasToken(tokens, "diffuse") ||
                HasToken(tokens, "albedo") || HasToken(tokens, "col")) return MapKind.BaseColor;

            if (HasToken(tokens, "rough") || HasToken(tokens, "roughness") ||
                HasToken(tokens, "ao") || HasToken(tokens, "metal") ||
                HasToken(tokens, "metallic")) return MapKind.LinearData;

            if (HasToken(tokens, "disp") || HasToken(tokens, "displacement") ||
                HasToken(tokens, "height")) return MapKind.Displacement;

            return MapKind.Unrecognized;
        }

        private static bool HasToken(string[] tokens, string token)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (string.Equals(tokens[i], token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyTypeAndColorSpace(TextureImporter importer, MapKind kind)
        {
            switch (kind)
            {
                case MapKind.BaseColor:
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    break;

                case MapKind.NormalOpenGL:
                case MapKind.NormalDirectX:
                    importer.textureType = TextureImporterType.NormalMap;
                    // Guard against Unity treating the map as a heightmap to convert.
                    importer.convertToNormalmap = false;
                    break;

                case MapKind.ArmPacked:
                case MapKind.LinearData:
                case MapKind.Displacement:
                    // Packed AO / Roughness / Metallic and every single-channel mask are
                    // data, not colour. sRGB encoding here would skew the values.
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = false;
                    break;
            }
        }

        private static void ApplyCommonSettings(TextureImporter importer)
        {
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = DefaultMaxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        private static void ApplyPlatformSettings(TextureImporter importer)
        {
            SetPlatform(importer, AndroidPlatform, AndroidFormat);
            SetPlatform(importer, StandalonePlatform, StandaloneFormat);
        }

        private static void SetPlatform(
            TextureImporter importer, string platform, TextureImporterFormat format)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.name = platform;
            settings.overridden = true;
            settings.maxTextureSize = DefaultMaxSize;
            settings.format = format;
            settings.compressionQuality = (int)TextureCompressionQuality.Normal;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
