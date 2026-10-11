using TMPro;
using UnityEngine;

namespace Compile.Client.UI
{
    // Fonts, sprites, and materials the UI is built from. Created by Compile.Editor.ClientSetup.
    [CreateAssetMenu(menuName = "Compile/Theme")]
    public sealed class Theme : ScriptableObject
    {
        [Header("Fonts")]
        public TMP_FontAsset displayFont;   // Chakra Petch Bold
        public TMP_FontAsset headingFont;   // Chakra Petch SemiBold
        public TMP_FontAsset bodyFont;      // Chakra Petch Regular
        public TMP_FontAsset monoFont;      // Share Tech Mono

        [Header("UI sprites (9-sliced)")]
        public Sprite chamferFill;
        public Sprite chamferFrame;

        [Header("Table")]
        public Sprite playMat;
        public Sprite cardBack;
        public Sprite controlNeutral;
        public Material tableMaterial;
        public Material spriteUnlitMaterial;
    }

    public static class Palette
    {
        public static readonly Color Ink = Hex(0xECE6F7);
        public static readonly Color Muted = Hex(0xA397BA);
        public static readonly Color Accent = Hex(0xE58BFF);
        public static readonly Color AccentDim = Hex(0xE58BFF, 0.55f);
        public static readonly Color Cyan = Hex(0xBFF4F4);
        public static readonly Color CyanDim = Hex(0xBFF4F4, 0.45f);
        public static readonly Color PanelFill = Hex(0x100B1C, 0.94f);
        public static readonly Color PanelInset = Hex(0x1C1430, 0.95f);
        public static readonly Color SecondaryFill = Hex(0x251A3B);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color OnAccent = Hex(0x1A0F24);
        public static readonly Color Ghost = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color Success = Hex(0x7CF2C4);
        public static readonly Color Danger = Hex(0xFF7A96);

        static Color Hex(uint rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }
}
