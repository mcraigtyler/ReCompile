// [UNITY-SKILL:SPRITEATLAS]
// [TYPE:PREBUILD]
// Generates one SpriteAtlas (V2) per Protocol plus a shared atlas before every build,
// so new or changed card art is always packed. Atlases are included in the build.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Compile.Editor
{
    public class CardAtlasGenerator : IPreprocessBuildWithReport
    {
        const string CardsFolder = ArtImportSettings.ArtRoot + "Cards";
        const string UIFolder = ArtImportSettings.ArtRoot + "UI";
        const string AtlasFolder = ArtImportSettings.ArtRoot + "Atlases";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => GenerateAll();

        // Also callable directly: -executeMethod Compile.Editor.CardAtlasGenerator.GenerateAll
        public static void GenerateAll()
        {
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            if (EditorSettings.spritePackerMode == SpritePackerMode.Disabled)
                Debug.LogError("[CardAtlas] Sprite packing is disabled; atlases will not pack.");

            Directory.CreateDirectory(AtlasFolder);

            // One atlas per Protocol folder (Loading/Compiled sides + its 6 cards).
            foreach (var dir in Directory.GetDirectories(CardsFolder).Select(d => d.Replace('\\', '/')))
            {
                var name = Path.GetFileName(dir);
                if (name == "Control")
                    continue;
                Generate(new[] { dir }, $"{AtlasFolder}/{name}.spriteatlasv2", recursive: true);
            }

            // Shared: card back, Control, missing-card art, and UI icons. The board image is too large to pack.
            Generate(new[] { CardsFolder, CardsFolder + "/Control", UIFolder },
                $"{AtlasFolder}/Shared.spriteatlasv2", recursive: false);

            AssetDatabase.SaveAssets();
        }

        static void Generate(string[] folders, string atlasPath, bool recursive)
        {
            var sprites = folders
                .Where(AssetDatabase.IsValidFolder)
                .SelectMany(f => AssetDatabase.FindAssets("t:Sprite", new[] { f })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.StartsWith("Assets/"))
                    .Where(p => recursive || Path.GetDirectoryName(p).Replace('\\', '/') == f)
                    // 9-sliced panel sprites stay unpacked: the atlas has mipmaps, which bleed at slice borders.
                    .Where(p => !Path.GetFileName(p).StartsWith("chamfer_")))
                .Distinct()
                .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(s => s != null)
                .Cast<Object>()
                .ToArray();

            if (sprites.Length == 0)
            {
                Debug.LogWarning($"[CardAtlas] No sprites for {atlasPath}");
                return;
            }

            SpriteAtlasAsset atlasAsset;
            if (File.Exists(atlasPath))
            {
                atlasAsset = SpriteAtlasAsset.Load(atlasPath);
                var existing = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                if (existing != null)
                    atlasAsset.Remove(existing.GetPackables());
            }
            else
            {
                atlasAsset = new SpriteAtlasAsset();
            }

            atlasAsset.Add(sprites);
            SpriteAtlasAsset.Save(atlasAsset, atlasPath);
            AssetDatabase.ImportAsset(atlasPath);
            Configure(atlasPath);

            Debug.Log($"[CardAtlas] {atlasPath}: {sprites.Length} sprites");
        }

        static void Configure(string atlasPath)
        {
            if (!(AssetImporter.GetAtPath(atlasPath) is SpriteAtlasImporter importer))
                return;

            var texture = importer.textureSettings;
            texture.filterMode = FilterMode.Bilinear;
            texture.generateMipMaps = true; // viewed in perspective on the table
            importer.textureSettings = texture;

            // Cards are drawn by uGUI Images, so keep rectangular packing and no rotation.
            var packing = importer.packingSettings;
            packing.padding = 4;
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.enableAlphaDilation = true;
            importer.packingSettings = packing;

            var platform = importer.GetPlatformSettings("DefaultTexturePlatform");
            platform.maxTextureSize = 2048;
            platform.textureCompression = TextureImporterCompression.Compressed;
            platform.crunchedCompression = true;    // keeps the WebGL download small
            platform.compressionQuality = 60;
            importer.SetPlatformSettings(platform);

            importer.includeInBuild = true;
            importer.SaveAndReimport();
        }
    }
}
