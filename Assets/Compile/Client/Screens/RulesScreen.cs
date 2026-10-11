using Compile.Client.App;
using Compile.Client.UI;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    public sealed class RulesScreen : MenuScreen
    {
        const float ViewportHeight = 680f;

        protected override float Width => 1300f;

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> man compile", TextStyle.Label);
            Ui.Text(panel, "How to play", TextStyle.Heading);
            BuildScrollView(panel, App.RulesText);
            Ui.Button(panel, "Back", ButtonStyle.Secondary, () => App.Nav.Go(new TitleScreen()));
        }

        // ScrollRect > Viewport (Mask + Image) > Content (VerticalLayoutGroup + ContentSizeFitter).
        static void BuildScrollView(RectTransform parent, string text)
        {
            var scroll = Ui.Rect("Rules", parent);
            Ui.Size(scroll, height: ViewportHeight);
            Ui.Backdrop(scroll, Palette.PanelInset, Color.clear, shadow: false);

            var viewport = Ui.Rect("Viewport", scroll);
            Ui.Stretch(viewport, 8f, 8f);
            var viewportImage = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
            viewportImage.color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;

            var content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Ui.VStack(content, 0f, padX: 40, padY: 32);
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            var body = Ui.Text(content, text, TextStyle.Body);
            body.fontSize = 32f;
            body.richText = true;

            var rect = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            rect.viewport = viewport;
            rect.content = content;
            rect.horizontal = false;
            rect.vertical = true;
            rect.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            rect.scrollSensitivity = 40f;
        }
    }
}
