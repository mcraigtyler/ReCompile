using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Compile.Client.UI
{
    public enum TextStyle
    {
        Title,      // big display heading
        Heading,
        Body,
        Small,
        Label,      // terminal-style eyebrow, e.g. ":\> NEW SESSION"
        Code,       // room codes
    }

    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Quiet,
    }

    // Builds uGUI elements in code from the Theme. World-space canvases use 100 canvas pixels per
    // table unit, so sizes below are in canvas pixels.
    public static class Ui
    {
        public const float PixelsPerUnit = 100f;
        public const float ControlHeight = 96f;
        const int UILayer = 5;

        static Theme _theme;

        public static Theme Theme => _theme != null ? _theme : throw new InvalidOperationException("Ui.Init has not been called.");

        public static void Init(Theme theme) => _theme = theme;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // A canvas lying in the table plane (facing the camera, which looks along +Z).
        public static Canvas WorldCanvas(string name, Transform parent, Camera camera, int sortingOrder)
        {
            var rt = Rect(name, parent);
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = sortingOrder;
            rt.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            rt.gameObject.AddComponent<CanvasGroup>();
            rt.localScale = Vector3.one / PixelsPerUnit;
            return canvas;
        }

        public static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        // Shadow, fill, and frame behind a container's content. The shadow makes panels read as
        // objects resting on the desk.
        public static void Backdrop(RectTransform target, Color fill, Color frame, bool shadow = true)
        {
            var index = 0;
            if (shadow)
            {
                var s = Layer(target, "Shadow", Theme.chamferFill, Palette.Shadow);
                s.rectTransform.offsetMin = new Vector2(10f, -26f);
                s.rectTransform.offsetMax = new Vector2(22f, -8f);
                s.transform.SetSiblingIndex(index++);
            }

            Layer(target, "Fill", Theme.chamferFill, fill).transform.SetSiblingIndex(index++);
            if (frame.a > 0f)
                Layer(target, "Frame", Theme.chamferFrame, frame).transform.SetSiblingIndex(index);
        }

        public static UnityEngine.UI.Image Layer(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent);
            Stretch(rt);
            rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, TextStyle style,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var rt = Rect(style + "Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = alignment;
            ApplyStyle(t, style);
            t.text = text;
            return t;
        }

        public static void ApplyStyle(TextMeshProUGUI t, TextStyle style)
        {
            var theme = Theme;
            t.fontStyle = FontStyles.Normal;
            t.characterSpacing = 0f;
            t.lineSpacing = 0f;
            switch (style)
            {
                case TextStyle.Title:
                    t.font = theme.displayFont;
                    t.fontSize = 116f;
                    t.characterSpacing = 6f;
                    t.color = Palette.Ink;
                    break;
                case TextStyle.Heading:
                    t.font = theme.headingFont;
                    t.fontSize = 54f;
                    t.color = Palette.Ink;
                    break;
                case TextStyle.Body:
                    t.font = theme.bodyFont;
                    t.fontSize = 36f;
                    t.lineSpacing = 8f;
                    t.color = Palette.Ink;
                    break;
                case TextStyle.Small:
                    t.font = theme.bodyFont;
                    t.fontSize = 29f;
                    t.color = Palette.Muted;
                    break;
                case TextStyle.Label:
                    t.font = theme.monoFont;
                    t.fontSize = 30f;
                    t.characterSpacing = 6f;
                    t.fontStyle = FontStyles.UpperCase;
                    t.color = Palette.Accent;
                    break;
                case TextStyle.Code:
                    t.font = theme.monoFont;
                    t.fontSize = 136f;
                    t.characterSpacing = 28f;
                    t.color = Palette.Cyan;
                    break;
            }
        }

        public static ButtonSkin Button(Transform parent, string label, ButtonStyle style, UnityAction onClick)
        {
            var rt = Rect(label.Replace(" ", "") + "Button", parent);
            var layout = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = ControlHeight;
            layout.preferredHeight = ControlHeight;

            var fill = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            fill.sprite = Theme.chamferFill;
            fill.type = UnityEngine.UI.Image.Type.Sliced;

            var frame = Layer(rt, "Frame", Theme.chamferFrame, Color.clear);
            var text = Text(rt, label, TextStyle.Heading, TextAlignmentOptions.Center);
            text.fontSize = 36f;
            text.characterSpacing = 4f;
            text.fontStyle = FontStyles.UpperCase;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(text.rectTransform, 24f, 0f);

            var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = fill;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var skin = rt.gameObject.AddComponent<ButtonSkin>();
            skin.Init(button, fill, frame, text, style);
            return skin;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, int characterLimit)
        {
            var rt = Rect("Input", parent);
            rt.gameObject.SetActive(false);
            var layout = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = ControlHeight;
            layout.preferredHeight = ControlHeight;

            var background = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.sprite = Theme.chamferFill;
            background.type = UnityEngine.UI.Image.Type.Sliced;
            background.color = Palette.PanelInset;
            Layer(rt, "Frame", Theme.chamferFrame, Palette.CyanDim);

            var area = Rect("TextArea", rt);
            Stretch(area, 30f, 10f);
            area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            var hint = Text(area, placeholder, TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            hint.color = new Color(Palette.Muted.r, Palette.Muted.g, Palette.Muted.b, 0.7f);
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(hint.rectTransform);

            var value = Text(area, "", TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(value.rectTransform);

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = value;
            input.placeholder = hint;
            input.targetGraphic = background;
            input.characterLimit = characterLimit;
            input.customCaretColor = true;
            input.caretColor = Palette.Cyan;
            input.caretWidth = 3;
            input.selectionColor = new Color(0.9f, 0.55f, 1f, 0.35f);
            input.onFocusSelectAll = true;
            input.fontAsset = value.font;
            input.pointSize = value.fontSize;

            rt.gameObject.SetActive(true);
            return input;
        }

        public static UnityEngine.UI.VerticalLayoutGroup VStack(RectTransform rt, float spacing, int padX = 0, int padY = 0,
            TextAnchor alignment = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padX, padX, padY, padY);
            v.childAlignment = alignment;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        // A horizontal row inside a vertical stack; children share the width equally.
        public static RectTransform Row(Transform parent, float spacing, string name = "Row")
        {
            var rt = Rect(name, parent);
            var h = rt.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            return rt;
        }

        public static RectTransform Spacer(Transform parent, float height)
        {
            var rt = Rect("Spacer", parent);
            var layout = rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            return rt;
        }

        public static UnityEngine.UI.LayoutElement Size(Component c, float width = -1f, float height = -1f)
        {
            if (!c.TryGetComponent<UnityEngine.UI.LayoutElement>(out var layout))
                layout = c.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            if (width >= 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
            }
            if (height >= 0f)
            {
                layout.minHeight = height;
                layout.preferredHeight = height;
            }
            return layout;
        }
    }
}
