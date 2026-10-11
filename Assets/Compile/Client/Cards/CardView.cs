using System;
using Compile.Client.Table;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Compile.Client.Cards
{
    // A card lying on the table: a face with a drop shadow, which can glide to a new spot and flip.
    public sealed class CardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public static readonly Vector2 SizePx = new Vector2(372f, 521f);
        const float HoverLift = 1.1f;

        RectTransform _rect;
        UnityEngine.UI.Image _face;
        UnityEngine.UI.Image _shadow;
        UnityEngine.UI.Image _glow;

        Vector2 _fromPos, _toPos;
        float _fromRot, _toRot, _fromScale, _toScale;
        float _moveT = 1f, _moveSeconds;

        Sprite _pendingFace;
        float _flipT = 1f;
        const float FlipSeconds = 0.32f;

        bool _hovered;
        float _hoverAmount;

        public RectTransform Rect => _rect;
        public string Protocol { get; set; }
        public bool Interactive { get; set; }
        public bool IsMoving => _moveT < 1f;

        public event Action<CardView> Clicked;
        public event Action<CardView, bool> Hovered;

        public static CardView Create(Transform parent, Sprite face, string name = "Card")
        {
            var rt = UI.Ui.Rect(name, parent);
            rt.sizeDelta = SizePx;

            var view = rt.gameObject.AddComponent<CardView>();
            view._rect = rt;

            view._shadow = Child(rt, "Shadow", face, new Color(0f, 0f, 0f, 0.5f));
            view._shadow.rectTransform.anchoredPosition = new Vector2(10f, -16f);

            view._glow = Child(rt, "Glow", UI.Ui.Theme.chamferFrame, UI.Palette.Accent);
            view._glow.type = UnityEngine.UI.Image.Type.Sliced;
            view._glow.rectTransform.sizeDelta = SizePx + new Vector2(28f, 28f);
            view._glow.enabled = false;

            view._face = Child(rt, "Face", face, Color.white);
            view._face.raycastTarget = true;
            view._face.preserveAspect = true;

            view._toScale = view._fromScale = 1f;
            return view;
        }

        static UnityEngine.UI.Image Child(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var rt = UI.Ui.Rect(name, parent);
            rt.sizeDelta = SizePx;
            var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public void Place(Vector2 position, float rotation, float scale)
        {
            _moveT = 1f;
            _toPos = position;
            _toRot = rotation;
            _toScale = scale;
            Apply(position, rotation, scale);
        }

        public void MoveTo(Vector2 position, float rotation, float scale, float seconds)
        {
            _fromPos = _rect.anchoredPosition;
            _fromRot = _rect.localEulerAngles.z;
            _fromScale = _toScale;
            _toPos = position;
            _toRot = rotation;
            _toScale = scale;
            _moveSeconds = Mathf.Max(0.01f, seconds);
            _moveT = 0f;
        }

        public void Flip(Sprite newFace)
        {
            _pendingFace = newFace;
            _flipT = 0f;
        }

        public void SetHighlight(bool on) => _glow.enabled = on;

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _hoverAmount = Mathf.MoveTowards(_hoverAmount, _hovered && Interactive ? 1f : 0f, dt * 8f);

            Vector2 pos = _toPos;
            float rot = _toRot, scale = _toScale;
            if (_moveT < 1f)
            {
                _moveT = Mathf.Min(1f, _moveT + dt / _moveSeconds);
                var k = Ease.OutCubic(_moveT);
                pos = Vector2.Lerp(_fromPos, _toPos, k);
                rot = Mathf.LerpAngle(_fromRot, _toRot, k);
                scale = Mathf.Lerp(_fromScale, _toScale, k);
                // Arc up off the table while travelling.
                scale *= 1f + 0.12f * Mathf.Sin(k * Mathf.PI);
            }

            scale *= Mathf.Lerp(1f, HoverLift, _hoverAmount);

            var flipX = 1f;
            if (_flipT < 1f)
            {
                _flipT = Mathf.Min(1f, _flipT + dt / FlipSeconds);
                if (_flipT >= 0.5f && _pendingFace != null)
                {
                    _face.sprite = _shadow.sprite = _pendingFace;
                    _pendingFace = null;
                }
                flipX = Mathf.Abs(Mathf.Cos(_flipT * Mathf.PI));
            }

            Apply(pos, rot, scale, flipX);
        }

        void Apply(Vector2 position, float rotation, float scale, float flipX = 1f)
        {
            _rect.anchoredPosition = position;
            _rect.localEulerAngles = new Vector3(0f, 0f, rotation);
            _rect.localScale = new Vector3(scale * flipX, scale, 1f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            if (Interactive)
                _rect.SetAsLastSibling();
            Hovered?.Invoke(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Hovered?.Invoke(this, false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Interactive)
                Clicked?.Invoke(this);
        }
    }
}
