using UnityEditor;
using UnityEngine;

namespace Compile.Editor
{
    // Applies sprite import settings to everything under Assets/Compile/Art on first import.
    // Later changes made in the Inspector are kept.
    public class ArtImportSettings : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Compile/Art/";
        const string BoardFolder = ArtRoot + "Board/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot) || !assetImporter.importSettingsMissing)
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;

            // Cards are 372×521; the board is 1398×1500. Never downscale either.
            importer.maxTextureSize = assetPath.StartsWith(BoardFolder) ? 2048 : 1024;
        }
    }
}
