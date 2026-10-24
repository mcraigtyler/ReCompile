using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Compile.Client.Cards;
using Compile.Client.UI;
using Compile.Engine.Views;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Compile.Client.Board
{
    // Screen-space parts of the board: phase bar, player summaries, hand, actions, event log, prompts,
    // debug menu, and trash browser. Lays itself out for wide or tall screens and re-lays on rotation.
    public sealed class BoardHud : MonoBehaviour
    {
        static readonly string[] PhaseNames = { "START", "CONTROL", "COMPILE", "ACTION", "CACHE", "END" };

        RectTransform _root;
        RectTransform _phaseBar;
        TextMeshProUGUI _turnText;
        TextMeshProUGUI _phaseText;
        RectTransform _opponentBox;
        TextMeshProUGUI _opponentText;
        RectTransform _localBox;
        CanvasGroup _localBoxGroup;
        TextMeshProUGUI _localText;
        RectTransform _actions;
        ButtonSkin _faceUp, _faceDown, _refresh, _zoom, _undo, _endTurn;
        TextMeshProUGUI _undoHint;
        RectTransform _handoff;
        TextMeshProUGUI _handoffTitle;
        ButtonSkin _handoffButton;
        UnityAction _handoffReady;
        RectTransform _log;
        TextMeshProUGUI _logText;
        ButtonSkin _logToggle;
        RectTransform _prompt;
        TextMeshProUGUI _promptText;
        RectTransform _promptButtons;       // the row (tall screens) or column (the narrow wide-screen panel) in use
        RectTransform _promptRow, _promptColumn;
        RectTransform _topRight;
        RectTransform _debugMenu;
        RectTransform _trashBrowser;
        RectTransform _trashGrid;
        TextMeshProUGUI _trashTitle;
        CardArtLibrary _art;
        Theme _theme;
        bool _wide;
        bool _logOpen = true;
        bool _layoutDone;

        // Tall screens use two thin strips instead of corner boxes, so the board stays visible.
        RectTransform _tallTop, _tallBottom;
        TextMeshProUGUI _tallOpponentText, _tallLocalText;
        ButtonSkin _tallFaceUp, _tallFaceDown, _tallRefresh, _tallLogToggle, _tallUndo, _tallEndTurn;

        public bool Wide => _wide;

        public HandView Hand { get; private set; }
        public CardReader Reader { get; private set; }

        public event Action<bool> OrientationChosen;   // true = face-up
        public event Action RefreshPressed;
        public event Action ZoomPressed;
        public event Action UndoPressed;
        public event Action EndTurnPressed;
        public event Action<bool> TrashPressed;        // true = local

        public static BoardHud Create(RectTransform hudRoot, CardArtLibrary art, Theme theme)
        {
            var rt = Ui.Rect("BoardHud", hudRoot);
            Ui.Stretch(rt);
            rt.SetAsFirstSibling();     // under toasts and the card zoom
            var hud = rt.gameObject.AddComponent<BoardHud>();
            hud._root = rt;
            hud._art = art;
            hud._theme = theme;
            hud.Build();
            return hud;
        }

        void Build()
        {
            _phaseBar = Box("PhaseBar", 1000f);
            var phaseLayout = _phaseBar.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            phaseLayout.padding = new RectOffset(24, 24, 10, 10);
            _turnText = Ui.Text(_phaseBar, "", TextStyle.Label, TextAlignmentOptions.Center);
            _turnText.fontSize = 24f;
            _phaseText = Ui.Text(_phaseBar, "", TextStyle.Heading, TextAlignmentOptions.Center);
            _phaseText.fontSize = 28f;
            _phaseText.characterSpacing = 3f;
            _phaseText.textWrappingMode = TextWrappingModes.NoWrap;

            _opponentBox = Box("Opponent", 420f);
            _opponentText = Ui.Text(_opponentBox, "", TextStyle.Body);
            _opponentText.fontSize = 30f;
            Ui.Button(_opponentBox, "Opponent's trash", ButtonStyle.Quiet, () => TrashPressed?.Invoke(false));

            _localBox = Box("Local", 420f);
            _localBoxGroup = _localBox.gameObject.AddComponent<CanvasGroup>();
            _localText = Ui.Text(_localBox, "", TextStyle.Body);
            _localText.fontSize = 30f;
            Ui.Button(_localBox, "Your trash", ButtonStyle.Quiet, () => TrashPressed?.Invoke(true));

            _actions = Ui.Rect("Actions", _root);
            _actions.sizeDelta = new Vector2(340f, 0f);
            Ui.VStack(_actions, 14f);
            Fit(_actions);
            var orientation = Ui.Row(_actions, 12f, "Orientation");
            _faceUp = Ui.Button(orientation, "Face-up", ButtonStyle.Secondary, () => OrientationChosen?.Invoke(true));
            _faceDown = Ui.Button(orientation, "Face-down", ButtonStyle.Secondary, () => OrientationChosen?.Invoke(false));
            foreach (var b in new[] { _faceUp, _faceDown })
                b.Label.fontSize = 28f;
            _refresh = Ui.Button(_actions, "Refresh", ButtonStyle.Secondary, () => RefreshPressed?.Invoke());
            var turn = Ui.Row(_actions, 12f, "Turn");
            _undo = Ui.Button(turn, "Undo", ButtonStyle.Secondary, () => UndoPressed?.Invoke());
            _endTurn = Ui.Button(turn, "End turn", ButtonStyle.Primary, () => EndTurnPressed?.Invoke());
            foreach (var b in new[] { _undo, _endTurn })
                b.Label.fontSize = 28f;
            _undoHint = Ui.Text(_actions, "", TextStyle.Small, TextAlignmentOptions.Center);
            _undoHint.fontSize = 22f;
            _zoom = Ui.Button(_actions, "Zoom", ButtonStyle.Quiet, () => ZoomPressed?.Invoke());

            _topRight = Ui.Rect("TopRight", _root);
            _topRight.sizeDelta = new Vector2(300f, 0f);
            Ui.VStack(_topRight, 12f);
            Fit(_topRight);
            Ui.Button(_topRight, "Debug", ButtonStyle.Secondary, ToggleDebugMenu);
            _logToggle = Ui.Button(_topRight, "Hide log", ButtonStyle.Quiet, ToggleLog);

            _log = Box("Log", 440f);
            _logText = Ui.Text(_log, "", TextStyle.Small);
            _logText.fontSize = 26f;
            _logText.richText = true;

            _debugMenu = Box("DebugMenu", 360f);
            _debugMenu.gameObject.SetActive(false);

            // The current question and its buttons, placed where it doesn't hide the cards being chosen:
            // at the left between the player summaries on wide screens, under the top strip on tall ones.
            _prompt = Ui.Rect("Prompt", _root);
            _prompt.sizeDelta = new Vector2(440f, 0f);
            Ui.Backdrop(_prompt, Palette.PanelFill, Palette.Cyan, shadow: false);
            Ui.VStack(_prompt, 14f, padX: 28, padY: 22);
            Fit(_prompt);
            _promptText = Ui.Text(_prompt, "", TextStyle.Heading);
            _promptText.fontSize = 36f;
            _promptRow = Ui.Row(_prompt, 12f, "PromptButtons");
            _promptColumn = Ui.Rect("PromptButtonColumn", _prompt);
            Ui.VStack(_promptColumn, 10f);
            _promptButtons = _promptRow;
            _prompt.gameObject.SetActive(false);

            // Card text for the hovered or selected card, docked under the prompt.
            Reader = CardReader.Create(_root);

            BuildTallStrips();

            Hand = HandView.Create(_root, _art, _theme);

            BuildTrashBrowser();
            BuildHandoff();
            Layout(Screen.width >= Screen.height);
        }

        void BuildTallStrips()
        {
            _tallTop = Strip("TallTop");
            _tallOpponentText = TappableText(_tallTop, () => TrashPressed?.Invoke(false));
            Ui.Size(Ui.Button(_tallTop, "Debug", ButtonStyle.Secondary, ToggleDebugMenu), width: 170f);
            _tallLogToggle = Ui.Button(_tallTop, "Log", ButtonStyle.Quiet, ToggleLog);
            Ui.Size(_tallLogToggle, width: 130f);

            _tallBottom = Strip("TallBottom");
            _tallLocalText = TappableText(_tallBottom, () => TrashPressed?.Invoke(true));
            _tallFaceUp = Ui.Button(_tallBottom, "Face-up", ButtonStyle.Secondary, () => OrientationChosen?.Invoke(true));
            _tallFaceDown = Ui.Button(_tallBottom, "Face-down", ButtonStyle.Secondary, () => OrientationChosen?.Invoke(false));
            _tallRefresh = Ui.Button(_tallBottom, "Refresh", ButtonStyle.Secondary, () => RefreshPressed?.Invoke());
            _tallUndo = Ui.Button(_tallBottom, "Undo", ButtonStyle.Secondary, () => UndoPressed?.Invoke());
            _tallEndTurn = Ui.Button(_tallBottom, "End turn", ButtonStyle.Primary, () => EndTurnPressed?.Invoke());
            foreach (var b in new[] { _tallFaceUp, _tallFaceDown, _tallRefresh, _tallUndo, _tallEndTurn })
            {
                b.Label.fontSize = 26f;
                Ui.Size(b, width: 175f, height: 84f);
            }
        }

        RectTransform Strip(string name)
        {
            var rt = Ui.Rect(name, _root);
            Ui.Backdrop(rt, Palette.PanelFill, Palette.AccentDim, shadow: false);
            var layout = rt.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 12, 8, 8);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Fit(rt);
            return rt;
        }

        // A summary line that opens that player's trash when tapped.
        static TextMeshProUGUI TappableText(RectTransform parent, UnityAction onTap)
        {
            var rt = Ui.Rect("Summary", parent);
            Ui.Size(rt).flexibleWidth = 1f;
            var hit = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = Color.clear;
            rt.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(onTap);
            var text = Ui.Text(rt, "", TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            text.fontSize = 28f;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Stretch(text.rectTransform);
            Ui.Size(rt, height: 70f);
            return text;
        }

        RectTransform Box(string name, float width)
        {
            var rt = Ui.Rect(name, _root);
            rt.sizeDelta = new Vector2(width, 0f);
            Ui.Backdrop(rt, Palette.PanelFill, Palette.AccentDim, shadow: false);
            Ui.VStack(rt, 6f, padX: 28, padY: 18);
            Fit(rt);
            return rt;
        }

        static void Fit(RectTransform rt) =>
            rt.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        // ---------- State ----------

        public void Render(PlayerView view, string localName)
        {
            _turnText.text = view.LocalTurn ? $":\\> your turn · turn {view.Turn}" : $":\\> {view.Opponent.Name}'s turn · turn {view.Turn}";
            var phases = new StringBuilder();
            for (var i = 0; i < PhaseNames.Length; i++)
            {
                if (i > 0)
                    phases.Append("<color=#5E5470>  ›  </color>");
                phases.Append((int)view.Phase == i ? $"<color=#E58BFF>{PhaseNames[i]}</color>" : $"<color=#7D7290>{PhaseNames[i]}</color>");
            }
            _phaseText.text = phases.ToString();

            _opponentText.text = $"<b>{view.Opponent.Name}</b>{ControlTag(view.Control == ControlHolder.Opponent)}\n" +
                                 $"<color=#A397BA>Hand</color> {view.Opponent.HandCount}   <color=#A397BA>Deck</color> {view.Opponent.DeckCount}   <color=#A397BA>Trash</color> {view.Opponent.Trash.Count}";
            _localText.text = $"<b>{localName} (you)</b>{ControlTag(view.Control == ControlHolder.Local)}\n" +
                              $"<color=#A397BA>Deck</color> {view.Local.DeckCount}   <color=#A397BA>Trash</color> {view.Local.Trash.Count}" +
                              (view.Control == ControlHolder.Neutral ? "\n<color=#A397BA>Control is neutral</color>" : "");

            _tallOpponentText.text = $"<b>{view.Opponent.Name}</b>{ControlTag(view.Control == ControlHolder.Opponent)}  " +
                                     $"<color=#A397BA>Hand</color> {view.Opponent.HandCount}  <color=#A397BA>Deck</color> {view.Opponent.DeckCount}  <color=#A397BA>Trash</color> {view.Opponent.Trash.Count}";
            _tallLocalText.text = $"<b>You</b>{ControlTag(view.Control == ControlHolder.Local)}  " +
                                  $"<color=#A397BA>Deck</color> {view.Local.DeckCount}  <color=#A397BA>Trash</color> {view.Local.Trash.Count}";

            var log = new StringBuilder();
            var shown = _wide ? 4 : 8;      // on wide screens the log shares the right side with the actions
            foreach (var entry in view.Log.Skip(Math.Max(0, view.Log.Count - shown)))
                log.Append(entry.Local ? "<color=#ECE6F7>" : "<color=#BFF4F4>").Append("• ").Append(entry.Text).Append("</color>\n");
            _logText.text = log.ToString().TrimEnd();

            Hand.Render(view.Hand);
        }

        static string ControlTag(bool holds) => holds ? "  <color=#7CF2C4>· CONTROL</color>" : "";

        // Undo and End turn. On tall screens they take the place of the play buttons once the turn is done.
        public void SetTurnState(bool canUndo, bool canEndTurn, string undoBlockedReason)
        {
            _undo.Interactable = canUndo;
            _endTurn.Interactable = canEndTurn;
            _undoHint.text = canUndo || string.IsNullOrEmpty(undoBlockedReason) ? "" : $"<color=#A397BA>Undo: {undoBlockedReason}</color>";
            _undoHint.gameObject.SetActive(_undoHint.text.Length > 0);

            _tallUndo.Interactable = canUndo;
            _tallEndTurn.Interactable = canEndTurn;
            foreach (var b in new[] { _tallFaceUp, _tallFaceDown, _tallRefresh })
                b.gameObject.SetActive(!canEndTurn);
            _tallUndo.gameObject.SetActive(canEndTurn);
            _tallEndTurn.gameObject.SetActive(canEndTurn);
        }

        public void SetActionState(bool hasSelection, bool faceUp, bool canFaceUp, bool canRefresh)
        {
            _faceUp.Interactable = hasSelection && canFaceUp;
            _faceDown.Interactable = hasSelection;
            _faceUp.Selected = hasSelection && faceUp;
            _faceDown.Selected = hasSelection && !faceUp;
            _refresh.Interactable = canRefresh;
            _zoom.Interactable = hasSelection;
            _tallFaceUp.Interactable = _faceUp.Interactable;
            _tallFaceDown.Interactable = _faceDown.Interactable;
            _tallFaceUp.Selected = _faceUp.Selected;
            _tallFaceDown.Selected = _faceDown.Selected;
            _tallRefresh.Interactable = canRefresh;
        }

        // ---------- Prompt (choices, rearranging) ----------

        public void ShowPrompt(string text, params (string label, ButtonStyle style, UnityAction action)[] buttons)
        {
            _promptText.text = text;
            foreach (var container in new[] { _promptRow, _promptColumn })
            {
                // Detach before destroying: Destroy waits for the end of the frame, and PromptButton counts children.
                for (var i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i);
                    child.SetParent(null, false);
                    Destroy(child.gameObject);
                }
                container.gameObject.SetActive(false);
            }
            _promptButtons = _wide && buttons.Length > 1 ? _promptColumn : _promptRow;
            _promptButtons.gameObject.SetActive(buttons.Length > 0);
            foreach (var (label, style, action) in buttons)
                Ui.Button(_promptButtons, label, style, action).Label.fontSize = 30f;
            _prompt.gameObject.SetActive(true);
            _prompt.SetAsLastSibling();     // above the hand
        }

        public ButtonSkin PromptButton(int index) =>
            index < _promptButtons.childCount ? _promptButtons.GetChild(index).GetComponent<ButtonSkin>() : null;

        public void HidePrompt() => _prompt.gameObject.SetActive(false);

        // ---------- Pass the screen (hot-seat) ----------

        // Covers everything (hand included) until the named player is at the screen.
        void BuildHandoff()
        {
            _handoff = Ui.Rect("Handoff", _root);
            Ui.Stretch(_handoff);
            _handoff.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.05f, 0.03f, 0.09f, 1f);   // opaque: nothing of the last hand shows

            var panel = Ui.Rect("Panel", _handoff);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(820f, 0f);
            Ui.Backdrop(panel, Palette.PanelFill, Palette.Accent);
            Ui.VStack(panel, 24f, padX: 56, padY: 48);
            Fit(panel);
            Ui.Text(panel, ":\\> handoff", TextStyle.Label, TextAlignmentOptions.Center);
            _handoffTitle = Ui.Text(panel, "", TextStyle.Title, TextAlignmentOptions.Center);
            _handoffTitle.fontSize = 72f;
            Ui.Text(panel, "Your hand stays hidden until you're ready.", TextStyle.Body, TextAlignmentOptions.Center);
            _handoffButton = Ui.Button(panel, "", ButtonStyle.Primary, () => _handoffReady?.Invoke());
            _handoff.gameObject.SetActive(false);
        }

        public bool HandoffShown => _handoff.gameObject.activeSelf;

        public void ShowHandoff(string name, UnityAction onReady)
        {
            _handoffTitle.text = $"Pass to {name}";
            _handoffButton.SetLabel($"I'm {name}: show my hand");
            _handoffReady = onReady;
            _handoff.gameObject.SetActive(true);
            _handoff.SetAsLastSibling();
        }

        public void HideHandoff() => _handoff.gameObject.SetActive(false);

        // ---------- Debug menu ----------

        public void AddDebugAction(string label, UnityAction action) =>
            Ui.Button(_debugMenu, label, ButtonStyle.Quiet, () =>
            {
                _debugMenu.gameObject.SetActive(false);
                action();
            }).Label.fontSize = 28f;

        void ToggleDebugMenu()
        {
            _debugMenu.gameObject.SetActive(!_debugMenu.gameObject.activeSelf);
            _debugMenu.SetAsLastSibling();
        }

        // ---------- Log ----------

        void ToggleLog()
        {
            _logOpen = !_logOpen;
            ApplyLogVisibility();
        }

        void ApplyLogVisibility()
        {
            _log.gameObject.SetActive(_logOpen);
            _logToggle.SetLabel(_logOpen ? "Hide log" : "Show log");
            _tallLogToggle.Selected = _logOpen;
            if (_logOpen)
                _log.SetAsLastSibling();
        }

        // ---------- Trash browser ----------

        void BuildTrashBrowser()
        {
            _trashBrowser = Ui.Rect("TrashBrowser", _root);
            Ui.Stretch(_trashBrowser);
            var dim = _trashBrowser.gameObject.AddComponent<UnityEngine.UI.Image>();
            dim.color = new Color(0f, 0f, 0f, 0.7f);

            var panel = Ui.Rect("Panel", _trashBrowser);
            panel.anchorMin = new Vector2(0.08f, 0.1f);
            panel.anchorMax = new Vector2(0.92f, 0.9f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            Ui.Backdrop(panel, Palette.PanelFill, Palette.AccentDim, shadow: false);
            Ui.VStack(panel, 16f, padX: 40, padY: 32);
            var header = Ui.Row(panel, 20f, "Header");
            _trashTitle = Ui.Text(header, "", TextStyle.Heading, TextAlignmentOptions.MidlineLeft);
            Ui.Size(Ui.Button(header, "Close", ButtonStyle.Secondary, () => _trashBrowser.gameObject.SetActive(false)), width: 220f);
            header.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.Size(_trashTitle).flexibleWidth = 1f;

            // ScrollRect > Viewport (Mask + Image) > Content (GridLayoutGroup + ContentSizeFitter).
            var scroll = Ui.Rect("Scroll", panel);
            Ui.Size(scroll).flexibleHeight = 1f;
            var viewport = Ui.Rect("Viewport", scroll);
            Ui.Stretch(viewport);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            _trashGrid = Ui.Rect("Content", viewport);
            _trashGrid.anchorMin = new Vector2(0f, 1f);
            _trashGrid.anchorMax = new Vector2(1f, 1f);
            _trashGrid.pivot = new Vector2(0.5f, 1f);
            _trashGrid.sizeDelta = Vector2.zero;
            var grid = _trashGrid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = CardView.SizePx * 0.5f;
            grid.spacing = new Vector2(16f, 16f);
            _trashGrid.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var rect = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            rect.viewport = viewport;
            rect.content = _trashGrid;
            rect.horizontal = false;
            rect.scrollSensitivity = 40f;

            _trashBrowser.gameObject.SetActive(false);
        }

        public void ShowTrash(string title, IReadOnlyList<CardInfo> cards, Action<CardInfo> onCardPressed)
        {
            for (var i = _trashGrid.childCount - 1; i >= 0; i--)
                Destroy(_trashGrid.GetChild(i).gameObject);

            _trashTitle.text = cards.Count == 0 ? $"{title}: empty" : $"{title} · {cards.Count} card{(cards.Count == 1 ? "" : "s")}";
            foreach (var card in cards.Reverse())
            {
                var image = Ui.Rect(card.CardId, _trashGrid).gameObject.AddComponent<UnityEngine.UI.Image>();
                image.sprite = _art.Card(card.Protocol, card.PrintedValue);
                image.preserveAspect = true;
                var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
                var info = card;
                button.onClick.AddListener(() => onCardPressed(info));
            }
            _trashBrowser.gameObject.SetActive(true);
            _trashBrowser.SetAsLastSibling();
        }

        // ---------- Layout ----------

        void Update()
        {
            var wide = Screen.width >= Screen.height;
            if (!_layoutDone || wide != _wide)
                Layout(wide);
        }

        void Layout(bool wide)
        {
            _wide = wide;
            _layoutDone = true;
            var width = ((RectTransform)_root.parent).rect.width;
            foreach (var wideOnly in new[] { _opponentBox, _topRight, _localBox, _actions })
                wideOnly.gameObject.SetActive(wide);
            _tallTop.gameObject.SetActive(!wide);
            _tallBottom.gameObject.SetActive(!wide);

            if (wide)
            {
                Place(_phaseBar, new Vector2(0.5f, 1f), new Vector2(0f, -16f), 860f);
                Place(_opponentBox, new Vector2(0f, 1f), new Vector2(28f, -24f), 420f);
                Place(_topRight, new Vector2(1f, 1f), new Vector2(-28f, -24f), 300f);
                Place(_log, new Vector2(1f, 1f), new Vector2(-28f, -260f), 440f);
                Place(_debugMenu, new Vector2(1f, 1f), new Vector2(-340f, -24f), 360f);
                Place(_localBox, new Vector2(0f, 0f), new Vector2(28f, 28f), 420f);
                Place(_actions, new Vector2(1f, 0f), new Vector2(-28f, 28f), 340f);
                Hand.SetWidth(1040f);
                _logOpen = true;
            }
            else
            {
                Place(_phaseBar, new Vector2(0.5f, 1f), new Vector2(0f, -16f), Mathf.Min(860f, width - 48f));
                Place(_tallTop, new Vector2(0.5f, 1f), new Vector2(0f, -104f), width - 48f);
                Place(_tallBottom, new Vector2(0.5f, 0f), new Vector2(0f, 168f), width - 48f);
                Place(_log, new Vector2(1f, 1f), new Vector2(-24f, -200f), Mathf.Min(620f, width - 48f));
                Place(_debugMenu, new Vector2(1f, 1f), new Vector2(-24f, -200f), 360f);
                Hand.SetWidth(width - 48f);
                _logOpen = false;
            }
            ApplyLogVisibility();
        }

        // The prompt and the card reader share a column that keeps clear of the lanes: at the left between
        // the player summaries on wide screens, and under the top strip (over the far end of the
        // opponent's lanes) on tall ones. Placed every frame because the boxes around them change height.
        void LateUpdate()
        {
            if (_handoff.gameObject.activeSelf && _handoff.GetSiblingIndex() != _root.childCount - 1)
                _handoff.SetAsLastSibling();    // nothing may show over the pass screen

            var rootHeight = _root.rect.height;
            Vector2 anchor;
            float x, top, bottom, stretchBottom, width;
            if (_wide)
            {
                anchor = new Vector2(0f, 1f);
                x = 28f;
                top = 24f + _opponentBox.rect.height + 16f;
                bottom = 28f + _localBox.rect.height + 16f;
                stretchBottom = 28f;    // a long card may cover your summary while it's shown
                width = 440f;
            }
            else
            {
                anchor = new Vector2(0.5f, 1f);
                x = 0f;
                top = 104f + _tallTop.rect.height + 12f;
                bottom = rootHeight * 0.5f;
                stretchBottom = rootHeight * 0.4f;
                width = _tallTop.rect.width;
            }

            Place(_prompt, anchor, new Vector2(x, -top), width);
            if (_prompt.gameObject.activeSelf)
                top += _prompt.rect.height + 12f;
            Reader.SetLayout(_wide, width, Mathf.Max(240f, rootHeight - top - bottom), rootHeight - top - stretchBottom);
            Reader.Dock(anchor, new Vector2(x, -top));

            // Hide your summary while a long card's text covers it.
            if (_wide)
            {
                var covered = top + Reader.Height > rootHeight - bottom + 1f;
                _localBoxGroup.alpha = covered ? 0f : 1f;
                _localBoxGroup.blocksRaycasts = !covered;
            }
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 position, float width)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        }
    }
}
