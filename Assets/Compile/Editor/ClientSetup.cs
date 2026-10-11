using System;
using System.IO;
using System.Linq;
using Compile.Client.App;
using Compile.Client.Cards;
using Compile.Client.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Compile.Editor
{
    // Creates the assets the client is built from: TMP essentials, font assets, generated UI sprites,
    // the card art library, the desk material, the Theme, and the Main scene. Safe to re-run; it
    // updates existing assets in place so references and GUIDs are kept.
    //
    // Batch mode (exits by itself, so don't pass -quit):
    //   Unity -batchmode -projectPath . -executeMethod Compile.Editor.ClientSetup.Run
    public static class ClientSetup
    {
        const string SettingsFolder = "Assets/Compile/Settings";
        const string ThemePath = SettingsFolder + "/Theme.asset";
        const string ArtLibraryPath = SettingsFolder + "/CardArtLibrary.asset";
        const string TableMaterialPath = SettingsFolder + "/TableSurface.mat";
        const string UiFolder = ArtImportSettings.ArtRoot + "UI";
        const string CardsFolder = ArtImportSettings.ArtRoot + "Cards";
        const string FontsFolder = "Assets/Compile/Fonts";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string SpriteUnlitPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        const string TmpFolder = "Assets/TextMesh Pro";

        // Prepopulated glyphs; anything else is added at runtime (the font assets are dynamic).
        static readonly string Glyphs =
            new string(Enumerable.Range(32, 95).Concat(Enumerable.Range(160, 96)).Select(c => (char)c).ToArray()) +
            "‘’“”–—…•·→←↑↓✓✕×";

        static double _deadline;

        public static void Run()
        {
            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                Continue();
                return;
            }

            Debug.Log("[ClientSetup] Importing TMP Essential Resources...");
            TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
            _deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += WaitForTmp;
        }

        static void WaitForTmp()
        {
            var ready = AssetDatabase.IsValidFolder(TmpFolder) && Shader.Find("TextMeshPro/Distance Field") != null;
            if (!ready && EditorApplication.timeSinceStartup < _deadline)
                return;

            EditorApplication.update -= WaitForTmp;
            if (!ready)
            {
                Debug.LogError("[ClientSetup] TMP Essential Resources did not import.");
                Finish(1);
                return;
            }
            Continue();
        }

        static void Continue()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                var (fill, frame) = GenerateUiSprites();
                var fonts = CreateFonts();
                var library = BuildArtLibrary();
                var tableMaterial = CreateTableMaterial();
                var theme = CreateTheme(fill, frame, fonts, tableMaterial);
                BuildMainScene(theme, library);
                PlayerSettings.SplashScreen.show = false;   // the page template shows its own loading bar
                PlayerSettings.SplashScreen.showUnityLogo = false;
                ArtImportSettings.ApplyToAll();
                CardAtlasGenerator.GenerateAll();
                AssetDatabase.SaveAssets();
                Debug.Log("[ClientSetup] Done.");
                Finish(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(1);
            }
        }

        static void Finish(int code)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        // ---------- UI sprites ----------

        // White chamfered panel shapes (top-left and bottom-right corners cut, like the mat's lanes),
        // 9-sliced so they stretch to any size and tinted per use.
        static (Sprite fill, Sprite frame) GenerateUiSprites()
        {
            Directory.CreateDirectory(UiFolder);
            return (ChamferSprite($"{UiFolder}/chamfer_fill.png", frame: false), ChamferSprite($"{UiFolder}/chamfer_frame.png", frame: true));
        }

        static Sprite ChamferSprite(string path, bool frame)
        {
            const int size = 96, chamfer = 22, border = 32, samples = 4;
            const float thickness = 3.5f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < samples; sy++)
                for (var sx = 0; sx < samples; sx++)
                {
                    var px = x + (sx + 0.5f) / samples;
                    var py = y + (sy + 0.5f) / samples;
                    var inside = InChamfer(px, py, size, chamfer, 0f) && (!frame || !InChamfer(px, py, size, chamfer, thickness));
                    if (inside)
                        covered++;
                }
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, covered / (float)(samples * samples)));
            }

            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // Inside a size×size square, inset by `inset`, with the top-left and bottom-right corners cut at 45°.
        // Texture y runs upward. The diagonal is moved inward by `inset` measured perpendicular to it.
        static bool InChamfer(float x, float y, int size, int chamfer, float inset)
        {
            float l = inset, b = inset, r = size - inset, t = size - inset;
            if (x < l || x > r || y < b || y > t)
                return false;
            var cut = chamfer - inset * (2f - Mathf.Sqrt(2f));
            return (x - l) + (t - y) >= cut && (r - x) + (y - b) >= cut;
        }

        // ---------- Fonts ----------

        static (TMP_FontAsset display, TMP_FontAsset heading, TMP_FontAsset body, TMP_FontAsset mono) CreateFonts() =>
            (Font($"{FontsFolder}/ChakraPetch/ChakraPetch-Bold.ttf"),
                Font($"{FontsFolder}/ChakraPetch/ChakraPetch-SemiBold.ttf"),
                Font($"{FontsFolder}/ChakraPetch/ChakraPetch-Regular.ttf"),
                Font($"{FontsFolder}/ShareTechMono/ShareTechMono-Regular.ttf"));

        static TMP_FontAsset Font(string ttfPath)
        {
            var assetPath = Path.ChangeExtension(ttfPath, null) + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
                return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            var asset = TMP_FontAsset.CreateFontAsset(font, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            asset.name = Path.GetFileNameWithoutExtension(assetPath);

            AssetDatabase.CreateAsset(asset, assetPath);
            asset.atlasTexture.name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            asset.TryAddCharacters(Glyphs, out var missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.Log($"[ClientSetup] {asset.name} has no glyphs for: {missing}");

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // ---------- Card art ----------

        static CardArtLibrary BuildArtLibrary()
        {
            var library = LoadOrCreate<CardArtLibrary>(ArtLibraryPath);
            library.missing = AssetDatabase.LoadAssetAtPath<Sprite>($"{CardsFolder}/404.jpg");
            library.protocols.Clear();

            foreach (var dir in Directory.GetDirectories(CardsFolder).Select(d => d.Replace('\\', '/')).OrderBy(d => d))
            {
                var name = Path.GetFileName(dir);
                if (name == "Control")
                    continue;

                var art = new CardArtLibrary.ProtocolArt
                {
                    protocol = name,
                    loading = AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/A.gif"),
                    compiled = AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}/B.gif"),
                };
                foreach (var file in Directory.GetFiles(dir, "*.gif").OrderBy(f => f))
                {
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file), out var value))
                        art.cards.Add(new CardArtLibrary.CardArt
                        {
                            value = value,
                            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(file.Replace('\\', '/')),
                        });
                }
                library.protocols.Add(art);
            }

            EditorUtility.SetDirty(library);
            return library;
        }

        // ---------- Materials, Theme, scene ----------

        public static Material CreateTableMaterial()
        {
            var shader = Shader.Find("Compile/TableSurface");
            if (shader == null)
                throw new InvalidOperationException("Compile/TableSurface shader not found.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(TableMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "TableSurface" };
                AssetDatabase.CreateAsset(material, TableMaterialPath);
            }
            // The shader's property defaults are the source of truth for the desk's look.
            material.shader = shader;
            var defaults = new Material(shader);
            material.CopyPropertiesFromMaterial(defaults);
            UnityEngine.Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Theme CreateTheme(Sprite fill, Sprite frame,
            (TMP_FontAsset display, TMP_FontAsset heading, TMP_FontAsset body, TMP_FontAsset mono) fonts, Material tableMaterial)
        {
            var theme = LoadOrCreate<Theme>(ThemePath);
            theme.displayFont = fonts.display;
            theme.headingFont = fonts.heading;
            theme.bodyFont = fonts.body;
            theme.monoFont = fonts.mono;
            theme.chamferFill = fill;
            theme.chamferFrame = frame;
            theme.playMat = AssetDatabase.LoadAssetAtPath<Sprite>(ArtImportSettings.ArtRoot + "Board/PlayMat.png");
            theme.cardBack = AssetDatabase.LoadAssetAtPath<Sprite>($"{CardsFolder}/Cardback.gif");
            theme.controlNeutral = AssetDatabase.LoadAssetAtPath<Sprite>($"{CardsFolder}/Control/A.gif");
            theme.tableMaterial = tableMaterial;
            theme.spriteUnlitMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteUnlitPath);

            foreach (var (field, value) in new (string, UnityEngine.Object)[]
                     {
                         ("displayFont", theme.displayFont), ("monoFont", theme.monoFont), ("chamferFill", fill),
                         ("playMat", theme.playMat), ("cardBack", theme.cardBack), ("spriteUnlitMaterial", theme.spriteUnlitMaterial),
                     })
                if (value == null)
                    throw new InvalidOperationException($"Theme.{field} could not be found.");

            EditorUtility.SetDirty(theme);
            return theme;
        }

        static void BuildMainScene(Theme theme, CardArtLibrary library)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("App");
            var app = root.AddComponent<AppRoot>();

            var so = new SerializedObject(app);
            so.FindProperty("theme").objectReferenceValue = theme;
            so.FindProperty("art").objectReferenceValue = library;
            so.FindProperty("cardsJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Compile/Data/cards.json");
            so.FindProperty("protocolsJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Compile/Data/protocols.json");
            so.FindProperty("rulesText").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Compile/Data/rules-summary.txt");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
