using UnityEditor;
using UnityEngine;

namespace Compile.Editor
{
    // Applies sprite import settings to everything under Assets/Compile/Art on first import.
    // Later changes made in the Inspector are kept; ApplyToAll re-applies the defaults on purpose.
    public class ArtImportSettings : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Compile/Art/";
        const string BoardFolder = ArtRoot + "Board/";
        const string UIFolder = ArtRoot + "UI/";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(ArtRoot) && assetImporter.importSettingsMissing)
                Apply((TextureImporter)assetImporter);
        }

        static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            // The table is viewed in perspective, so art is often drawn smaller than its source size.
            // 9-sliced UI sprites skip mipmaps, which would bleed at their slice borders.
            importer.mipmapEnabled = !importer.assetPath.StartsWith(UIFolder);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Compressed;
            // Crunch keeps the WebGL download small; GPU memory is the same as plain DXT/ASTC.
            importer.crunchedCompression = true;
            importer.compressionQuality = 60;

            // Cards are 372×521; the play mat is 1398×3788. Never downscale either.
            importer.maxTextureSize = importer.assetPath.StartsWith(BoardFolder) ? 4096 : 1024;
        }

        // Invoke with: -executeMethod Compile.Editor.ArtImportSettings.ApplyToAll
        public static void ApplyToAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot.TrimEnd('/') }))
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter importer)
                {
                    Apply(importer);
                    importer.SaveAndReimport();
                }
            }
        }
    }
}
