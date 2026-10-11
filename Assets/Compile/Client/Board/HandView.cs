using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Client.Cards;
using Compile.Client.UI;
using Compile.Engine.Views;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Compile.Client.Board
{
    // The local player's hand, fanned along the bottom of the screen. Cards are selected by tapping or
    // dragged straight onto a lane; right-click flips the selected card between face-up and face-down.
    public sealed class HandView : MonoBehaviour
    {
        const float CardScale = 0.56f;
        const float Spacing = 176f;
        const float SelectedLift = 40f;
        const float RestDrop = 0.56f;     // resting cards sit this far (fraction of their height) below the screen edge

        readonly Dictionary<int, HandCard> _cards = new Dictionary<int, HandCard>();
        readonly List<int> _order = new List<int>();
        RectTransform _rect;
        CardArtLibrary _art;
        Theme _theme;

        public int Selected { get; private set; } = -1;
        public bool SelectedFaceUp { get; private set; } = true;
        public int Dragging { get; private set; } = -1;

        public event Action<int> CardTapped;
        public event Action<int> CardRightClicked;
        public event Action<int, bool> CardHovered;
        public event Action<int> CardLongPressed;
        public event Action<int> DragStarted;
        public event Action<int> DragEnded;

        public static HandView Create(RectTransform parent, CardArtLibrary art, Theme theme)
        {
            var rt = Ui.Rect("Hand", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 24f);
            rt.sizeDelta = new Vector2(1100f, CardView.SizePx.y * CardScale);
            var hand = rt.gameObject.AddComponent<HandView>();
            hand._rect = rt;
            hand._art = art;
            hand._theme = theme;
            return hand;
        }

        public RectTransform Rect => _rect;

        public void Render(IReadOnlyList<CardInfo> hand)
        {
            var ids = hand.Select(c => c.InstanceId).ToList();
            foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                Destroy(_cards[id].gameObject);
                _cards.Remove(id);
            }

            _order.Clear();
            foreach (var info in hand)
            {
                if (!_cards.TryGetValue(info.InstanceId, out var card))
                {
                    card = HandCard.Create(_rect, this, info, _art.Card(info.Protocol, info.PrintedValue), _theme);
                    // New cards come up from below the screen.
                    card.Rect.anchoredPosition = new Vector2(0f, -400f);
                    _cards[info.InstanceId] = card;
                }
                card.Info = info;
                _order.Add(info.InstanceId);
            }

            if (!_cards.ContainsKey(Selected))
                Selected = -1;
            Arrange();
        }

        public void Select(int id, bool faceUp)
        {
            Selected = id;
            SelectedFaceUp = faceUp;
            Arrange();
        }

        public void SetSelectedFaceUp(bool faceUp)
        {
            SelectedFaceUp = faceUp;
            Arrange();
        }

        public void ClearSelection() => Select(-1, true);

        public CardInfo InfoFor(int id) => _cards.TryGetValue(id, out var card) ? card.Info : null;

        // Width available to the fan; narrower on tall screens.
        public void SetWidth(float width)
        {
            _rect.sizeDelta = new Vector2(width, _rect.sizeDelta.y);
            Arrange();
        }

        void Arrange()
        {
            var count = _order.Count;
            var cardWidth = CardView.SizePx.x * CardScale;
            var spacing = count > 1 ? Mathf.Min(Spacing, (_rect.sizeDelta.x - cardWidth) / (count - 1)) : 0f;
            for (var i = 0; i < count; i++)
            {
                var card = _cards[_order[i]];
                var offset = i - (count - 1) / 2f;
                var selected = card.Info.InstanceId == Selected;
                var height = CardView.SizePx.y * CardScale;
                var position = new Vector2(offset * spacing, height / 2f - offset * offset * 5f + (selected ? SelectedLift : 0f));
                card.SetTarget(position, selected ? 0f : -offset * 3.5f, selected, height * RestDrop);
                card.SetFaceDownBadge(selected && !SelectedFaceUp);
                card.transform.SetSiblingIndex(i);
            }
            if (Selected >= 0 && _cards.TryGetValue(Selected, out var top))
                top.transform.SetAsLastSibling();
        }

        // ---- Called by HandCard ----

        internal void OnTapped(HandCard card) => CardTapped?.Invoke(card.Info.InstanceId);
        internal void OnRightClicked(HandCard card) => CardRightClicked?.Invoke(card.Info.InstanceId);
        internal void OnHovered(HandCard card, bool on) => CardHovered?.Invoke(card.Info.InstanceId, on);
        internal void OnLongPressed(HandCard card) => CardLongPressed?.Invoke(card.Info.InstanceId);

        internal void OnDragStarted(HandCard card)
        {
            Dragging = card.Info.InstanceId;
            card.transform.SetAsLastSibling();
            DragStarted?.Invoke(Dragging);
        }

        internal void OnDragEnded(HandCard card)
        {
            var id = Dragging;
            Dragging = -1;
            DragEnded?.Invoke(id);
            Arrange();
        }
    }

    public sealed class HandCard : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        const float LongPressSeconds = 0.45f;

        HandView _hand;
        UnityEngine.UI.Image _glow;
        RectTransform _badge;
        CanvasGroup _group;
        Vector2 _targetPosition;
        float _targetRotation;
        float _targetScale = 1f;
        bool _hovered, _dragging, _pressed, _longPressFired, _selected;
        float _restDrop;
        float _pressedAt;

        public RectTransform Rect { get; private set; }
        public CardInfo Info { get; set; }

        public static HandCard Create(RectTransform parent, HandView hand, CardInfo info, Sprite face, Theme theme)
        {
            var rt = Ui.Rect(info.CardId, parent);
            rt.sizeDelta = CardView.SizePx * 0.56f;
            // Positions are measured up from the bottom of the hand area (the screen edge).
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var card = rt.gameObject.AddComponent<HandCard>();
            card.Rect = rt;
            card._hand = hand;
            card.Info = info;
            card._group = rt.gameObject.AddComponent<CanvasGroup>();

            card._glow = Ui.Layer(rt, "Glow", theme.chamferFrame, Palette.Accent);
            card._glow.rectTransform.offsetMin = new Vector2(-12f, -12f);
            card._glow.rectTransform.offsetMax = new Vector2(12f, 12f);
            card._glow.enabled = false;

            var shadow = Ui.Layer(rt, "Shadow", face, new Color(0f, 0f, 0f, 0.55f));
            shadow.type = UnityEngine.UI.Image.Type.Simple;
            shadow.rectTransform.offsetMin = new Vector2(8f, -14f);
            shadow.rectTransform.offsetMax = new Vector2(8f, -14f);

            var image = Ui.Layer(rt, "Face", face, Color.white);
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = true;

            card._badge = Ui.Rect("FaceDownBadge", rt);
            card._badge.anchorMin = new Vector2(0f, 0.5f);
            card._badge.anchorMax = new Vector2(1f, 0.5f);
            card._badge.sizeDelta = new Vector2(0f, 64f);
            Ui.Backdrop(card._badge, new Color(0.06f, 0.04f, 0.1f, 0.92f), Palette.Cyan, shadow: false);
            var label = Ui.Text(card._badge, "FACE-DOWN · 2", TextStyle.Label, TextAlignmentOptions.Center);
            label.color = Palette.Cyan;
            label.fontSize = 24f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Stretch(label.rectTransform);
            card._badge.gameObject.SetActive(false);
            return card;
        }

        // Resting cards sit partly below the screen edge; hovering or selecting raises them fully.
        public void SetTarget(Vector2 position, float rotation, bool selected, float restDrop)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _selected = selected;
            _restDrop = restDrop;
            _glow.enabled = selected;
        }

        public void SetFaceDownBadge(bool on) => _badge.gameObject.SetActive(on);

        void Update()
        {
            if (_pressed && !_longPressFired && !_dragging && Time.unscaledTime - _pressedAt >= LongPressSeconds)
            {
                _longPressFired = true;
                _hand.OnLongPressed(this);
            }

            if (_dragging)
                return;

            var k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f);
            var lift = _hovered || _selected ? (_hovered ? 24f : 0f) : -_restDrop;
            Rect.anchoredPosition = Vector2.Lerp(Rect.anchoredPosition, _targetPosition + new Vector2(0f, lift), k);
            var z = Mathf.LerpAngle(Rect.localEulerAngles.z, _hovered ? 0f : _targetRotation, k);
            Rect.localEulerAngles = new Vector3(0f, 0f, z);
            var scale = Mathf.Lerp(Rect.localScale.x, _targetScale * (_hovered ? 1.08f : 1f), k);
            Rect.localScale = new Vector3(scale, scale, 1f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_dragging || _longPressFired)
                return;
            if (eventData.button == PointerEventData.InputButton.Right)
                _hand.OnRightClicked(this);
            else if (eventData.button == PointerEventData.InputButton.Left)
                _hand.OnTapped(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            _hand.OnHovered(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _hand.OnHovered(this, false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _longPressFired = false;
            _pressedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData) => _pressed = false;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            _dragging = true;
            _pressed = false;
            _group.blocksRaycasts = false;      // let the lane under the pointer receive the drop
            Rect.localEulerAngles = Vector3.zero;
            Rect.localScale = Vector3.one * 0.85f;
            _hand.OnDragStarted(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
                return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Rect.parent, eventData.position, null, out var local))
                Rect.anchoredPosition = local;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
                return;
            _dragging = false;
            _group.blocksRaycasts = true;
            _hand.OnDragEnded(this);
        }
    }
}
