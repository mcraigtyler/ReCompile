using TMPro;
using UnityEngine;

namespace Compile.Client.UI
{
    // Colors a Ui.Button for its style and state (normal, selected, disabled), including its label,
    // which the Button's own color tint doesn't reach.
    public sealed class ButtonSkin : MonoBehaviour
    {
        UnityEngine.UI.Button _button;
        UnityEngine.UI.Image _fill;
        UnityEngine.UI.Image _frame;
        TextMeshProUGUI _label;
        ButtonStyle _style;
        bool _selected;

        public UnityEngine.UI.Button Button => _button;
        public TextMeshProUGUI Label => _label;

        public bool Interactable
        {
            get => _button.interactable;
            set
            {
                _button.interactable = value;
                Refresh();
            }
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Refresh();
            }
        }

        public void Init(UnityEngine.UI.Button button, UnityEngine.UI.Image fill, UnityEngine.UI.Image frame,
            TextMeshProUGUI label, ButtonStyle style)
        {
            _button = button;
            _fill = fill;
            _frame = frame;
            _label = label;
            _style = style;
            Refresh();
        }

        public void SetLabel(string text) => _label.text = text;

        void Refresh()
        {
            Color fill, frame, text;
            if (_selected || _style == ButtonStyle.Primary)
            {
                fill = Palette.Accent;
                frame = Color.clear;
                text = Palette.OnAccent;
            }
            else if (_style == ButtonStyle.Secondary)
            {
                fill = Palette.SecondaryFill;
                frame = Palette.CyanDim;
                text = Palette.Ink;
            }
            else
            {
                fill = Color.clear;
                frame = Color.clear;
                text = Palette.Muted;
            }

            // The Button tints the fill on hover/press; the base color lives in the ColorBlock.
            _fill.color = Color.white;
            var colors = _button.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Brighten(fill);
            colors.selectedColor = fill;
            colors.pressedColor = Color.Lerp(fill, Color.black, 0.25f);
            colors.disabledColor = new Color(fill.r, fill.g, fill.b, fill.a * 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            _button.colors = colors;

            var enabled = _button.interactable;
            _frame.color = enabled ? frame : new Color(frame.r, frame.g, frame.b, frame.a * 0.35f);
            _label.color = enabled ? text : new Color(text.r, text.g, text.b, 0.4f);
        }

        static Color Brighten(Color c) =>
            c.a < 0.01f ? new Color(1f, 1f, 1f, 0.08f) : new Color(Mathf.Min(1f, c.r * 1.12f + 0.04f), Mathf.Min(1f, c.g * 1.12f + 0.04f),
                Mathf.Min(1f, c.b * 1.12f + 0.04f), Mathf.Min(1f, c.a + 0.08f));
    }
}
