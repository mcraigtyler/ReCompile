using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Compile.Client.UI
{
    // Screen-space overlay for things that aren't objects on the table: whose turn it is, short
    // messages, details about the card under the pointer, and debug actions.
    public sealed class Hud : MonoBehaviour
    {
        const float ToastSeconds = 3.5f;

        RectTransform _status;
        TextMeshProUGUI _statusLabel;
        TextMeshProUGUI _statusText;
        RectTransform _toast;
        TextMeshProUGUI _toastText;
        float _toastUntil;
        RectTransform _info;
        TextMeshProUGUI _infoText;
        RectTransform _actions;
        readonly List<GameObject> _actionButtons = new List<GameObject>();

        public RectTransform Root { get; private set; }
        public CardZoom Zoom { get; private set; }
        public RectTransform StatusBox => _status;

        public void Init()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var root = (RectTransform)transform;
            Root = root;

            _status = Box(root, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -32f), 960f);
            _statusLabel = Ui.Text(_status, "", TextStyle.Label, TextAlignmentOptions.Center);
            _statusText = Ui.Text(_status, "", TextStyle.Heading, TextAlignmentOptions.Center);
            _statusText.fontSize = 44f;

            _toast = Box(root, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, -220f), 900f);
            _toastText = Ui.Text(_toast, "", TextStyle.Body, TextAlignmentOptions.Center);

            _info = Box(root, "Info", new Vector2(0.5f, 0f), new Vector2(0f, 32f), 1100f);
            _infoText = Ui.Text(_info, "", TextStyle.Body, TextAlignmentOptions.Center);

            _actions = Ui.Rect("Actions", root);
            _actions.anchorMin = _actions.anchorMax = _actions.pivot = new Vector2(1f, 1f);
            _actions.anchoredPosition = new Vector2(-32f, -32f);
            _actions.sizeDelta = new Vector2(360f, 0f);
            Ui.VStack(_actions, 16f, alignment: TextAnchor.UpperRight);
            var fitter = _actions.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            Zoom = gameObject.AddComponent<CardZoom>();
            Zoom.Init(root);

            Clear();
        }

        static RectTransform Box(RectTransform root, string name, Vector2 anchor, Vector2 position, float width)
        {
            var rt = Ui.Rect(name, root);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(width, 0f);
            Ui.Backdrop(rt, Palette.PanelFill, Palette.AccentDim, shadow: false);
            Ui.VStack(rt, 6f, padX: 40, padY: 22);
            var fitter = rt.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        public void Clear()
        {
            ClearStatus();
            ClearInfo();
            ClearActions();
            _toast.gameObject.SetActive(false);
            Zoom?.Hide();
        }

        public void SetStatus(string label, string text)
        {
            _statusLabel.text = label;
            _statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(label));
            _statusText.text = text;
            _status.gameObject.SetActive(true);
        }

        public void ClearStatus() => _status.gameObject.SetActive(false);

        public void Toast(string message, bool error = false)
        {
            _toastText.text = message;
            _toastText.color = error ? Palette.Danger : Palette.Ink;
            _toast.gameObject.SetActive(true);
            _toast.SetAsLastSibling();
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        public void SetInfo(string richText)
        {
            _infoText.text = richText;
            _info.gameObject.SetActive(true);
        }

        public void ClearInfo() => _info.gameObject.SetActive(false);

        public ButtonSkin AddAction(string label, ButtonStyle style, UnityAction onClick)
        {
            var button = Ui.Button(_actions, label, style, onClick);
            _actionButtons.Add(button.gameObject);
            return button;
        }

        public void ClearActions()
        {
            foreach (var go in _actionButtons)
                Destroy(go);
            _actionButtons.Clear();
        }

        void Update()
        {
            if (_toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toast.gameObject.SetActive(false);
        }
    }
}
