using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Compile.Client.UI
{
    // A docked panel with the text of the card under the pointer, or of the selected hand card, placed
    // where it leaves the cards in play clear. Used on the board and in the draft. A hovered card only shows once the pointer rests on it, and nothing
    // new shows while a pointer is pressed, so sweeping across cards or dragging one into a lane doesn't
    // pop anything up. On tall screens, tapping the panel hides it until the selection changes.
    public sealed class CardReader : MonoBehaviour, IPointerClickHandler
    {
        public const float HoverDelay = 0.5f;
        const float WarmSeconds = 0.3f;     // moving straight from one shown card to the next shows it at once
        const float Pad = 22f;
        const float Gap = 18f;
        const float BodySize = 28f;
        const float ComfortableBodySize = 24f;   // below this, the panel first drops the thumbnail, then grows
        const float MinBodySize = 20f;
        static readonly Vector2 WideThumb = new Vector2(120f, 168f);
        static readonly Vector2 TallThumb = new Vector2(170f, 238f);

        sealed class Entry
        {
            public object Subject;
            public Sprite Art;
            public CardDescription Text;

            public bool SameAs(Entry other) => other != null && Equals(Subject, other.Subject) && Art == other.Art &&
                                               Text.Title == other.Text.Title && Text.Body == other.Text.Body;
        }

        RectTransform _rect;
        RectTransform _panel;
        UnityEngine.UI.Image _hit;
        UnityEngine.UI.Image _thumb;
        TextMeshProUGUI _title;
        TextMeshProUGUI _body;
        Entry _resting, _hover, _pending, _shown;
        object _dismissed;
        float _pendingAt;
        float _hoverEndedAt = -10f;
        bool _wide = true;
        float _width = 440f;
        float _maxHeight = 600f;
        float _stretchHeight = 600f;
        bool _layoutDirty;

        public RectTransform Rect => _rect;

        // Height the panel takes on screen, or 0 when hidden.
        public float Height => _panel.gameObject.activeSelf ? _rect.sizeDelta.y : 0f;

        // While a hand card is dragged, nothing new is shown; on tall screens the panel also gets out of the way.
        public bool Dragging { get; set; }

        // Pins the panel's anchor and pivot to the same point of its parent, e.g. (0, 1) for top-left.
        public void Dock(Vector2 anchor, Vector2 position)
        {
            _rect.anchorMin = _rect.anchorMax = _rect.pivot = anchor;
            _rect.anchoredPosition = position;
        }

        public static CardReader Create(RectTransform parent)
        {
            var rt = Ui.Rect("CardReader", parent);
            var reader = rt.gameObject.AddComponent<CardReader>();
            reader._rect = rt;

            var panel = Ui.Rect("Panel", rt);
            Ui.Stretch(panel);
            reader._hit = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
            reader._hit.color = Color.clear;
            Ui.Backdrop(panel, Palette.PanelFill, Palette.AccentDim, shadow: false);
            reader._panel = panel;

            var thumb = Ui.Rect("Thumb", panel);
            reader._thumb = thumb.gameObject.AddComponent<UnityEngine.UI.Image>();
            reader._thumb.preserveAspect = true;
            reader._thumb.raycastTarget = false;

            reader._title = Ui.Text(panel, "", TextStyle.Heading);
            reader._title.fontSize = 34f;
            reader._body = Ui.Text(panel, "", TextStyle.Body);
            reader._body.richText = true;
            reader._body.overflowMode = TextOverflowModes.Ellipsis;

            panel.gameObject.SetActive(false);
            return reader;
        }

        // Wide screens stack the thumbnail over the text in a narrow column; tall screens put them side by side.
        // The panel keeps within maxHeight, and only grows to stretchHeight (over whatever is below it)
        // when a long card won't fit otherwise.
        public void SetLayout(bool wide, float width, float maxHeight, float stretchHeight)
        {
            stretchHeight = Mathf.Max(stretchHeight, maxHeight);
            if (wide == _wide && Mathf.Approximately(width, _width) && Mathf.Approximately(maxHeight, _maxHeight) &&
                Mathf.Approximately(stretchHeight, _stretchHeight))
                return;
            _wide = wide;
            _width = width;
            _maxHeight = maxHeight;
            _stretchHeight = stretchHeight;
            _layoutDirty = true;
            // On wide screens the panel sits beside the board and ignores the pointer, so it can never steal
            // a hover from a card it overlaps. On tall screens it covers part of the board, so a tap hides it.
            _hit.raycastTarget = !wide;
        }

        // The selected hand card; shown whenever nothing is hovered.
        public void SetResting(object subject, Sprite art, CardDescription text)
        {
            if (_resting == null || !Equals(_resting.Subject, subject))
                _dismissed = null;
            _resting = new Entry { Subject = subject, Art = art, Text = text };
        }

        public void ClearResting()
        {
            _resting = null;
            _dismissed = null;
        }

        public void Hover(object subject, Sprite art, CardDescription text)
        {
            if (Dragging || PointerPressed)
                return;
            var entry = new Entry { Subject = subject, Art = art, Text = text };
            if (_hover != null || Time.unscaledTime - _hoverEndedAt < WarmSeconds)
            {
                _hover = entry;
                _pending = null;
            }
            else
            {
                _pending = entry;
                _pendingAt = Time.unscaledTime;
            }
        }

        public void Unhover(object subject)
        {
            if (_pending != null && Equals(_pending.Subject, subject))
                _pending = null;
            if (_hover != null && Equals(_hover.Subject, subject))
                EndHover();
        }

        void EndHover()
        {
            _hover = null;
            _hoverEndedAt = Time.unscaledTime;
        }

        static bool PointerPressed
        {
            get
            {
                var pointer = UnityEngine.InputSystem.Pointer.current;
                return pointer != null && pointer.press.isPressed;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_hover != null)
                EndHover();
            else if (_resting != null)
                _dismissed = _resting.Subject;
        }

        void Update()
        {
            // Pressing anywhere ends a hover view: the press is a tap, a drag, or a long press for the big zoom.
            if (PointerPressed || Dragging)
            {
                _pending = null;
                if (_hover != null)
                    EndHover();
            }
            else if (_pending != null && Time.unscaledTime - _pendingAt >= HoverDelay)
            {
                _hover = _pending;
                _pending = null;
            }

            var want = _hover ?? (_resting != null && !Equals(_resting.Subject, _dismissed) ? _resting : null);
            if (Dragging && !_wide)
                want = null;

            if (want == null)
            {
                _shown = null;
                if (_panel.gameObject.activeSelf)
                    _panel.gameObject.SetActive(false);
                return;
            }
            if (!want.SameAs(_shown) || _layoutDirty)
                Render(want);
        }

        void Render(Entry entry)
        {
            _shown = entry;
            _layoutDirty = false;
            _panel.gameObject.SetActive(true);
            _thumb.sprite = entry.Art;
            _title.text = entry.Text.Title;
            _body.text = entry.Text.Body ?? "";
            var hasBody = _body.text.Length > 0;

            float height, limit = _maxHeight;
            if (_wide)
            {
                // Thumbnail and title side by side with the text under them; for long text, the title
                // alone above the text, and if it still doesn't fit, a taller panel.
                var thumb = WideThumb;
                var textWidth = _width - Pad * 2f;
                var withThumb = Pad + thumb.y + Gap;
                var titleHeight = _title.GetPreferredValues(_title.text, textWidth, 0f).y;
                var withoutThumb = Pad + titleHeight + 10f;
                var showThumb = !hasBody || BodyHeight(textWidth, ComfortableBodySize) <= limit - withThumb - Pad;
                if (!showThumb && BodyHeight(textWidth, ComfortableBodySize) > limit - withoutThumb - Pad)
                    limit = _stretchHeight;

                _thumb.gameObject.SetActive(showThumb);
                if (showThumb)
                {
                    Put(_thumb.rectTransform, Pad, Pad, thumb);
                    _title.alignment = TextAlignmentOptions.MidlineLeft;
                    Put(_title.rectTransform, Pad + thumb.x + Gap, Pad, new Vector2(textWidth - thumb.x - Gap, thumb.y));
                }
                else
                {
                    _title.alignment = TextAlignmentOptions.TopLeft;
                    Put(_title.rectTransform, Pad, Pad, new Vector2(textWidth, titleHeight));
                }
                var bodyTop = showThumb ? withThumb : withoutThumb;
                var bodyHeight = hasBody ? FitBody(textWidth, limit - bodyTop - Pad) : 0f;
                Put(_body.rectTransform, Pad, bodyTop, new Vector2(textWidth, bodyHeight));
                height = hasBody ? bodyTop + bodyHeight + Pad : Pad * 2f + thumb.y;
            }
            else
            {
                // Thumbnail at the left, title and text beside it.
                var thumb = TallThumb;
                var left = Pad + thumb.x + Gap;
                var textWidth = _width - left - Pad;
                _thumb.gameObject.SetActive(true);
                Put(_thumb.rectTransform, Pad, Pad, thumb);
                _title.alignment = TextAlignmentOptions.TopLeft;
                var titleHeight = _title.GetPreferredValues(_title.text, textWidth, 0f).y;
                Put(_title.rectTransform, left, Pad, new Vector2(textWidth, titleHeight));
                var bodyTop = Pad + titleHeight + 10f;
                if (hasBody && BodyHeight(textWidth, ComfortableBodySize) > limit - bodyTop - Pad)
                    limit = _stretchHeight;
                var bodyHeight = hasBody ? FitBody(textWidth, limit - bodyTop - Pad) : 0f;
                Put(_body.rectTransform, left, bodyTop, new Vector2(textWidth, bodyHeight));
                height = Mathf.Max(Pad * 2f + thumb.y, bodyTop + bodyHeight + Pad);
            }
            _rect.sizeDelta = new Vector2(_width, Mathf.Min(height, limit));
        }

        float BodyHeight(float width, float size)
        {
            _body.fontSize = size;
            return _body.GetPreferredValues(_body.text, width, 0f).y;
        }

        // Shrinks the text until it fits the space available (down to a readable minimum).
        float FitBody(float width, float maxHeight)
        {
            for (var size = BodySize; ; size -= 2f)
            {
                var height = BodyHeight(width, size);
                if (height <= maxHeight || size <= MinBodySize)
                    return Mathf.Min(height, Mathf.Max(0f, maxHeight));
            }
        }

        static void Put(RectTransform rt, float left, float top, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = size;
        }
    }
}
