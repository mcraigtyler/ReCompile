using Compile.Engine.Cards;
using TMPro;
using UnityEngine;

namespace Compile.Client.UI
{
    // A large, readable view of one card with its text. On a tablet the cards on the table are too small
    // to read, so this is the main way to read them. Shown while hovering or long-pressing, or pinned.
    public sealed class CardZoom : MonoBehaviour
    {
        static readonly Vector2 ArtSize = new Vector2(372f, 521f) * 1.15f;
        const float TextWidth = 560f;

        RectTransform _panel;
        UnityEngine.UI.Image _art;
        RectTransform _artRect;
        RectTransform _textBox;
        TextMeshProUGUI _text;
        ButtonSkin _close;
        bool _wide = true;

        public bool Pinned { get; private set; }
        public bool Visible => _panel.gameObject.activeSelf;
        public object Subject { get; private set; }

        public void Init(RectTransform root)
        {
            _panel = Ui.Rect("CardZoom", root);
            _panel.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = true;

            _artRect = Ui.Rect("Art", _panel);
            _artRect.sizeDelta = ArtSize;
            _art = _artRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            _art.preserveAspect = true;
            _art.raycastTarget = false;

            _textBox = Ui.Rect("Text", _panel);
            _textBox.sizeDelta = new Vector2(TextWidth, ArtSize.y);
            Ui.Backdrop(_textBox, Palette.PanelFill, Palette.AccentDim, shadow: false);
            _text = Ui.Text(_textBox, "", TextStyle.Body);
            _text.fontSize = 32f;
            _text.richText = true;
            Ui.Stretch(_text.rectTransform, 36f, 30f);

            _close = Ui.Button(_textBox, "Close", ButtonStyle.Secondary, Hide);
            var closeRect = (RectTransform)_close.transform;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 0f);
            closeRect.anchoredPosition = new Vector2(-24f, 24f);
            closeRect.sizeDelta = new Vector2(200f, Ui.ControlHeight);

            Layout(true);
            _panel.gameObject.SetActive(false);
        }

        public void ShowCard(object subject, Sprite art, CardDefinition card, bool faceUp, bool hiddenFromViewer, bool pinned) =>
            Show(subject, art, CardDescription.ForCard(card, faceUp, hiddenFromViewer).Combined, pinned);

        public void ShowProtocol(object subject, Sprite art, ProtocolDefinition protocol, bool compiled, bool pinned) =>
            Show(subject, art, CardDescription.ForProtocol(protocol, compiled).Combined, pinned);

        void Show(object subject, Sprite art, string text, bool pinned)
        {
            if (Pinned && !pinned && Subject != subject)
                return;     // a pinned card stays until it's closed

            Subject = subject;
            Pinned = pinned;
            _art.sprite = art;
            _text.text = text;
            _close.gameObject.SetActive(pinned);
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
        }

        // Hides a transient (hover) zoom of this subject; pinned zooms stay.
        public void Release(object subject)
        {
            if (!Pinned && Subject == subject)
                Hide();
        }

        public void Hide()
        {
            Pinned = false;
            Subject = null;
            _panel.gameObject.SetActive(false);
        }

        void Update()
        {
            var wide = Screen.width >= Screen.height;
            if (wide != _wide)
                Layout(wide);
        }

        // Wide screens: art and text side by side at the left. Tall screens: stacked in the middle.
        void Layout(bool wide)
        {
            _wide = wide;
            if (wide)
            {
                _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0f, 0.5f);
                _panel.anchoredPosition = new Vector2(40f, 40f);
                _panel.sizeDelta = new Vector2(ArtSize.x + 24f + TextWidth, ArtSize.y);
                Place(_artRect, new Vector2(0f, 0.5f), Vector2.zero);
                Place(_textBox, new Vector2(0f, 0.5f), new Vector2(ArtSize.x + 24f, 0f));
            }
            else
            {
                _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
                _panel.anchoredPosition = new Vector2(0f, 60f);
                _panel.sizeDelta = new Vector2(Mathf.Max(ArtSize.x, TextWidth), ArtSize.y * 2f + 24f);
                Place(_artRect, new Vector2(0.5f, 1f), Vector2.zero);
                Place(_textBox, new Vector2(0.5f, 0f), Vector2.zero);
            }
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 position)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
        }
    }
}
