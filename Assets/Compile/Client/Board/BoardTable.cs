using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Client.Cards;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine.Views;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Compile.Client.Board
{
    // Everything on the table during a game: the cards in each stack, line totals, the lanes as drop
    // targets, and (beside the mat) the decks, trashes, and Control component. Renders a PlayerView.
    public sealed class BoardTable : MonoBehaviour
    {
        public const float CardScale = 0.95f;
        static readonly float CardHeightPx = CardView.SizePx.y * CardScale;
        const float StackMarginPx = 26f;
        const float StepPx = 228f;        // leaves a covered card's value and top command visible
        const float MinStepPx = 110f;     // long stacks squeeze down to this, then run off the mat onto the desk
        const float RevealSeconds = 2.2f; // a revealed card stays face-up at least this long, even if its text finished at once
        static readonly Color Dimmed = new Color(0.42f, 0.4f, 0.48f, 1f);

        // Beside the mat, in table units (local side; the opponent's are mirrored).
        static readonly Vector2 DeckSpot = new Vector2(9.1f, -9.6f);
        static readonly Vector2 TrashSpot = new Vector2(9.1f, -15.2f);
        static readonly Vector2 ControlSpot = new Vector2(9.1f, 0f);
        public const float DeskReach = 10.9f;    // how far from the mat's center line the desk items extend

        readonly Dictionary<int, CardView> _cards = new Dictionary<int, CardView>();
        readonly Dictionary<int, CardInfo> _info = new Dictionary<int, CardInfo>();
        readonly LaneTarget[] _lanes = new LaneTarget[3];
        readonly LaneTarget[] _opponentLanes = new LaneTarget[3];
        readonly Dictionary<int, (Sprite face, float until)> _reveals = new Dictionary<int, (Sprite, float)>();
        readonly HashSet<int> _trashIds = new HashSet<int>();
        readonly LineChip[] _chips = new LineChip[3];
        readonly DeskPile[] _decks = new DeskPile[2];
        readonly DeskPile[] _trashes = new DeskPile[2];
        CardView _control;

        MatView _mat;
        CardArtLibrary _art;
        Theme _theme;
        RectTransform _stackLayer;
        RectTransform _overlayLayer;
        PlayerView _view;

        public event Action<CardView> CardClicked;
        public event Action<CardView, bool> CardHovered;
        public event Action<CardView> CardLongPressed;
        public event Action<int> LaneClicked;
        public event Action<int> LaneDropped;
        public event Action<bool> TrashClicked;
        public event Action<CardView, bool> ProtocolHovered;
        public event Action<CardView> ProtocolLongPressed;
        public event Action<bool, int> ProtocolClicked;     // local, slot index

        readonly HashSet<CardView> _hookedProtocols = new HashSet<CardView>();

        public CardInfo InfoFor(CardView card) => card != null ? InfoFor(card.InstanceId) : null;

        public CardInfo InfoFor(int instanceId) => _info.TryGetValue(instanceId, out var info) ? info : null;

        public void Init(MatView mat, CardArtLibrary art, Theme theme)
        {
            _mat = mat;
            _art = art;
            _theme = theme;

            _stackLayer = Ui.Rect("Stacks", mat.Cards);
            _overlayLayer = Ui.Rect("BoardOverlay", mat.Cards);

            for (var line = 0; line < 3; line++)
            {
                // Lines are shared columns: the opponent's half of line L is their own lane 2 - L, turned around.
                _lanes[line] = LaneTarget.Create(_overlayLayer, $"Lane{line}", MatLayout.LocalLaneCanvasRect(line), theme);
                var opponentRect = MatLayout.LocalLaneCanvasRect(2 - line);
                _opponentLanes[line] = LaneTarget.Create(_overlayLayer, $"OpponentLane{line}",
                    new Rect(-opponentRect.max, opponentRect.size), theme);
                var index = line;
                foreach (var lane in new[] { _lanes[line], _opponentLanes[line] })
                {
                    lane.Clicked += () => LaneClicked?.Invoke(index);
                    lane.Dropped += () => LaneDropped?.Invoke(index);
                }
                _chips[line] = LineChip.Create(_overlayLayer, line, theme);
            }

            for (var seat = 0; seat < 2; seat++)
            {
                var local = seat == 0;
                _decks[seat] = DeskPile.Create(_overlayLayer, local ? "Deck" : "OpponentDeck", theme, Spot(DeckSpot, local), local);
                _decks[seat].SetFace(theme.cardBack);
                _trashes[seat] = DeskPile.Create(_overlayLayer, local ? "Trash" : "OpponentTrash", theme, Spot(TrashSpot, local), local);
                _trashes[seat].Clicked += () => TrashClicked?.Invoke(local);
            }

            _control = CardView.Create(_overlayLayer, theme.controlNeutral, "Control");
            _control.Place(Spot(ControlSpot, true), -90f, 0.5f);
            _control.HoverLifts = false;
        }

        static Vector2 Spot(Vector2 tableUnits, bool local) => (local ? tableUnits : -tableUnits) * MatLayout.PixelsPerUnit;

        public void Clear()
        {
            foreach (var card in _cards.Values)
                Destroy(card.gameObject);
            _cards.Clear();
            _info.Clear();
            ShowLanes(null);
        }

        // returned: ids of field cards that went back to a hand (they glide off toward it instead of to the trash).
        public void Render(PlayerView view, ICollection<int> returned = null)
        {
            _view = view;
            _mat.ShowProtocols(true, view.Lines.Select(l => l.LocalProtocol.Name).ToList(), view.Lines.Select(l => l.LocalProtocol.Compiled).ToList());
            _mat.ShowProtocols(false, Enumerable.Range(0, 3).Select(i => view.Lines[2 - i].OpponentProtocol.Name).ToList(),
                Enumerable.Range(0, 3).Select(i => view.Lines[2 - i].OpponentProtocol.Compiled).ToList());

            HookProtocols();
            // Protocol cards made just now (e.g. when the other hot-seat player takes the screen) were added
            // after the stacks and line totals; keep those on top.
            _stackLayer.SetAsLastSibling();
            _overlayLayer.SetAsLastSibling();

            var seen = new HashSet<int>();
            foreach (var line in view.Lines)
            {
                LayoutStack(line.Local, line.Index, local: true, seen);
                LayoutStack(line.Opponent, line.Index, local: false, seen);
                _chips[line.Index].Show(line.Local.Total, line.Opponent.Total, line.LocalCanCompile, line.OpponentCanCompile);
            }

            foreach (var id in _cards.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                // Cards that left the field fly to their owner's trash (deleted, compiled) or hand (returned).
                var card = _cards[id];
                var owner = _info[id].LocalOwner;
                card.Interactive = false;
                card.SetHighlight(false);
                card.SetBadge(null, Color.clear);
                if (returned != null && returned.Contains(id))
                {
                    var edge = MatLayout.SizePx.y / 2f + 700f;
                    card.MoveTo(new Vector2(card.Rect.anchoredPosition.x, owner ? -edge : edge), owner ? 0f : 180f, 0.6f, 0.5f);
                }
                else
                    card.MoveTo(Spot(TrashSpot, owner), owner ? 0f : 180f, 0.5f, 0.4f);
                card.gameObject.AddComponent<DestroyAfter>().Seconds = 0.55f;
                _cards.Remove(id);
                _info.Remove(id);
            }

            for (var seat = 0; seat < 2; seat++)
            {
                var side = seat == 0 ? view.Local : view.Opponent;
                _decks[seat].SetCount("Deck", side.DeckCount);
                _decks[seat].SetVisible(side.DeckCount > 0);
                _trashes[seat].SetCount("Trash", side.Trash.Count);
                var top = side.Trash.Count > 0 ? side.Trash[side.Trash.Count - 1] : null;
                _trashes[seat].SetFace(top != null ? _art.Card(top.Protocol, top.PrintedValue) : null);
            }
            // Remembered so a card coming back out of a trash (an undone delete) starts from there.
            _trashIds.Clear();
            _trashIds.UnionWith(view.Local.Trash.Concat(view.Opponent.Trash).Select(c => c.InstanceId));

            var controlY = view.Control == ControlHolder.Local ? -3.4f : view.Control == ControlHolder.Opponent ? 3.4f : 0f;
            var controlTarget = new Vector2(ControlSpot.x, controlY) * MatLayout.PixelsPerUnit;
            if (Vector2.Distance(_control.Rect.anchoredPosition, controlTarget) > 1f)
                _control.MoveTo(controlTarget, view.Control == ControlHolder.Opponent ? 90f : -90f, 0.5f, 0.45f);
        }

        void LayoutStack(StackInfo stack, int line, bool local, HashSet<int> seen)
        {
            var count = stack.Cards.Count;
            var available = MatLayout.SizePx.y - StackMarginPx - MatLayout.LaneTopPx - CardHeightPx;
            var step = count > 1 ? Mathf.Clamp(available / (count - 1), MinStepPx, StepPx) : 0f;
            // Lines are shared columns: the opponent's stack in line L sits in their own lane 2 - L, which
            // is the same column seen from the other side (mirrored top to bottom only).
            var x = local ? MatLayout.LaneCenterPx(line) : MatLayout.SizePx.x - MatLayout.LaneCenterPx(2 - line);

            for (var i = 0; i < count; i++)
            {
                var info = stack.Cards[i];
                seen.Add(info.InstanceId);
                var y = MatLayout.LaneTopPx + StackMarginPx + CardHeightPx / 2f + i * step;
                var px = local ? new Vector2(x, y) : new Vector2(x, MatLayout.SizePx.y - y);
                var position = MatLayout.ToCanvas(px.x, px.y);
                var rotation = local ? 0f : 180f;
                // A revealed face-down card shows its face to both players while it's revealed.
                var held = _reveals.TryGetValue(info.InstanceId, out var reveal);
                var revealed = info.Revealed || held;
                var face = (info.FaceUp || info.Revealed) && !info.IsHidden ? _art.Card(info.Protocol, info.PrintedValue)
                    : held ? reveal.face : _theme.cardBack;

                if (!_cards.TryGetValue(info.InstanceId, out var card))
                {
                    card = CardView.Create(_stackLayer, face, info.CardId ?? "Hidden");
                    card.InstanceId = info.InstanceId;
                    card.HoverLifts = false;
                    card.Clicked += c => CardClicked?.Invoke(c);
                    card.Hovered += (c, on) => CardHovered?.Invoke(c, on);
                    card.LongPressed += c => CardLongPressed?.Invoke(c);
                    // New cards arrive from their owner's side of the table, or out of the trash (an undone delete).
                    if (_trashIds.Contains(info.InstanceId))
                        card.Place(Spot(TrashSpot, info.LocalOwner), rotation, 0.5f);
                    else
                        card.Place(position + new Vector2(0f, local ? -900f : 900f), rotation, CardScale);
                    card.MoveTo(position, rotation, CardScale, 0.35f);
                    _cards[info.InstanceId] = card;
                }
                else
                {
                    if (card.Face != face)
                        card.Flip(face);
                    if (Vector2.Distance(card.Rect.anchoredPosition, position) > 1f && !card.IsMoving)
                        card.MoveTo(position, rotation, CardScale, 0.3f);
                }

                card.SetBadge(revealed && !info.FaceUp ? "REVEALED" : null, Palette.Cyan);
                _info[info.InstanceId] = info;
                card.name = info.CardId ?? "Hidden";
                card.transform.SetSiblingIndex(_stackLayer.childCount - 1);
            }
        }

        void HookProtocols()
        {
            for (var seat = 0; seat < 2; seat++)
            for (var i = 0; i < 3; i++)
            {
                var card = _mat.ProtocolCard(seat == 0, i);
                if (card == null || !_hookedProtocols.Add(card))
                    continue;
                card.HoverLifts = false;
                card.Hovered += (c, on) => ProtocolHovered?.Invoke(c, on);
                card.LongPressed += c => ProtocolLongPressed?.Invoke(c);
                card.Clicked += c =>
                {
                    for (var s = 0; s < 2; s++)
                    for (var j = 0; j < 3; j++)
                        if (_mat.ProtocolCard(s == 0, j) == c)
                            ProtocolClicked?.Invoke(s == 0, j);
                };
            }
        }

        public bool IsLocalProtocol(CardView card) =>
            Enumerable.Range(0, 3).Any(i => _mat.ProtocolCard(true, i) == card);

        // Removes everything this table created (the Protocol cards belong to the mat and stay).
        public void Dispose()
        {
            Destroy(_stackLayer.gameObject);
            Destroy(_overlayLayer.gameObject);
            Destroy(gameObject);
        }

        // Shows which lines can be chosen, on the viewer's side, the opponent's, or both (a whole line).
        // Pass null to hide the lanes.
        public void ShowLanes(bool[] legal, bool local = true, bool opponent = false)
        {
            for (var line = 0; line < 3; line++)
            {
                var state = legal == null ? LaneTarget.State.Hidden : legal[line] ? LaneTarget.State.Legal : LaneTarget.State.Illegal;
                _lanes[line].SetState(local ? state : LaneTarget.State.Hidden);
                _opponentLanes[line].SetState(opponent ? state : LaneTarget.State.Hidden);
            }
        }

        // Highlights the cards that can be chosen (picked ones in the accent color) and dims the rest, which
        // stop responding to clicks. subject is a card the question is about (e.g. the one being shifted).
        // Pass null and 0 to show the field normally.
        public void ShowChoice(IReadOnlyCollection<int> valid, ICollection<int> picked = null, int subject = 0)
        {
            var choosing = valid != null && valid.Count > 0;
            foreach (var (id, card) in _cards)
            {
                var on = choosing && valid.Contains(id);
                card.Interactive = on;
                if (id == subject)
                    card.SetHighlight(true, Palette.Accent);
                else
                    card.SetHighlight(on, picked != null && picked.Contains(id) ? Palette.Accent : Palette.Cyan);
                card.SetTint(choosing && !on && id != subject ? Dimmed : Color.white);
            }
        }

        // Keeps a revealed card face-up for a moment, so a reveal that ends straight away can still be seen.
        public void HoldReveal(int instanceId, Sprite face)
        {
            _reveals[instanceId] = (face, Time.unscaledTime + RevealSeconds);
            if (_view != null)
                Render(_view);
        }

        void Update()
        {
            if (_reveals.Count == 0 || _reveals.Values.All(r => r.until > Time.unscaledTime))
                return;
            foreach (var id in _reveals.Where(r => r.Value.until <= Time.unscaledTime).Select(r => r.Key).ToList())
                _reveals.Remove(id);
            if (_view != null)
                Render(_view);
        }

        public IEnumerable<CardView> Cards => _cards.Values;

        // The decks, trashes, and Control beside the mat; hidden on tall screens, where the HUD shows them instead.
        public void SetDeskVisible(bool visible)
        {
            foreach (var pile in _decks.Concat(_trashes))
                pile.gameObject.SetActive(visible);
            _control.gameObject.SetActive(visible);
        }
    }

    // A local lane as a drop target. Shown only while a card is being played.
    public sealed class LaneTarget : MonoBehaviour, IPointerClickHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public enum State { Hidden, Legal, Illegal }

        UnityEngine.UI.Image _fill;
        UnityEngine.UI.Image _frame;
        State _state;
        bool _hovered;

        public event Action Clicked;
        public event Action Dropped;

        public static LaneTarget Create(RectTransform parent, string name, Rect rect, Theme theme)
        {
            var rt = Ui.Rect(name, parent);
            rt.anchoredPosition = rect.center;
            rt.sizeDelta = rect.size;

            var target = rt.gameObject.AddComponent<LaneTarget>();
            target._fill = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            target._fill.sprite = theme.chamferFill;
            target._fill.type = UnityEngine.UI.Image.Type.Sliced;
            target._frame = Ui.Layer(rt, "Frame", theme.chamferFrame, Palette.Accent);
            target._frame.pixelsPerUnitMultiplier = 0.22f;     // a thicker outline, readable over the mat art
            target._fill.pixelsPerUnitMultiplier = 0.22f;
            target.SetState(State.Hidden);
            return target;
        }

        public void SetState(State state)
        {
            _state = state;
            Refresh();
        }

        void Refresh()
        {
            _fill.enabled = _frame.enabled = _state != State.Hidden;
            _fill.raycastTarget = _state == State.Legal;
            if (_state == State.Illegal)
            {
                _fill.color = new Color(0.02f, 0.01f, 0.04f, 0.68f);
                _frame.color = Color.clear;
            }
        }

        void Update()
        {
            if (_state != State.Legal)
                return;
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            _fill.color = new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, _hovered ? 0.38f : 0.14f + 0.12f * pulse);
            _frame.color = new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.7f + 0.3f * pulse);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_state == State.Legal)
                Clicked?.Invoke();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (_state == State.Legal)
                Dropped?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Refresh();
        }
    }

    // Both totals for a line, on the center line between the Protocol rows: opponent above, local below.
    public sealed class LineChip : MonoBehaviour
    {
        TextMeshProUGUI _local;
        TextMeshProUGUI _opponent;
        UnityEngine.UI.Image _frame;

        public static LineChip Create(RectTransform parent, int line, Theme theme)
        {
            var rt = Ui.Rect($"LineTotal{line}", parent);
            rt.anchoredPosition = MatLayout.ToCanvas(MatLayout.LaneCenterPx(line), MatLayout.CenterLinePx);
            rt.sizeDelta = new Vector2(200f, 150f);
            Ui.Backdrop(rt, Palette.PanelFill, Palette.AccentDim, shadow: false);

            var chip = rt.gameObject.AddComponent<LineChip>();
            chip._frame = rt.Find("Frame").GetComponent<UnityEngine.UI.Image>();
            chip._opponent = Number(rt, 36f, Palette.Cyan);
            chip._local = Number(rt, -36f, Palette.Accent);
            return chip;
        }

        static TextMeshProUGUI Number(RectTransform parent, float y, Color color)
        {
            var text = Ui.Text(parent, "0", TextStyle.Heading, TextAlignmentOptions.Center);
            text.fontSize = 68f;
            text.color = color;
            text.rectTransform.sizeDelta = new Vector2(200f, 74f);
            text.rectTransform.anchoredPosition = new Vector2(0f, y);
            return text;
        }

        public void Show(int local, int opponent, bool localCanCompile, bool opponentCanCompile)
        {
            _local.text = local.ToString();
            _opponent.text = opponent.ToString();
            _local.color = localCanCompile ? Palette.Success : Palette.Accent;
            _opponent.color = opponentCanCompile ? Palette.Danger : Palette.Cyan;
            _frame.color = localCanCompile ? Palette.Success : opponentCanCompile ? Palette.Danger : Palette.AccentDim;
        }
    }

    // A deck or trash lying on the desk beside the mat, with its card count.
    public sealed class DeskPile : MonoBehaviour, IPointerClickHandler
    {
        UnityEngine.UI.Image _face;
        UnityEngine.UI.Image _empty;
        TextMeshProUGUI _label;

        public event Action Clicked;

        public static DeskPile Create(RectTransform parent, string name, Theme theme, Vector2 position, bool local)
        {
            var rt = Ui.Rect(name, parent);
            rt.anchoredPosition = position;
            rt.sizeDelta = CardView.SizePx * 0.7f;
            rt.localEulerAngles = new Vector3(0f, 0f, local ? 0f : 180f);
            var pile = rt.gameObject.AddComponent<DeskPile>();

            pile._empty = Ui.Layer(rt, "Empty", theme.chamferFrame, new Color(1f, 1f, 1f, 0.25f));
            pile._empty.raycastTarget = true;

            pile._face = Ui.Layer(rt, "Face", null, Color.white);
            pile._face.type = UnityEngine.UI.Image.Type.Simple;
            pile._face.raycastTarget = true;
            pile._face.preserveAspect = true;

            pile._label = Ui.Text(rt, "", TextStyle.Label, TextAlignmentOptions.Center);
            pile._label.rectTransform.anchorMin = new Vector2(0f, 0f);
            pile._label.rectTransform.anchorMax = new Vector2(1f, 0f);
            pile._label.rectTransform.pivot = new Vector2(0.5f, 1f);
            pile._label.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            pile._label.rectTransform.sizeDelta = new Vector2(120f, 90f);
            pile._label.fontSize = 64f;
            pile._label.textWrappingMode = TextWrappingModes.NoWrap;
            return pile;
        }

        public void SetFace(Sprite sprite)
        {
            _face.sprite = sprite;
            _face.enabled = sprite != null;
        }

        public void SetVisible(bool visible) => _face.enabled = visible && _face.sprite != null;

        public void SetCount(string label, int count) => _label.text = $"{label} {count}";

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }

    sealed class DestroyAfter : MonoBehaviour
    {
        public float Seconds;

        void Update()
        {
            Seconds -= Time.unscaledDeltaTime;
            if (Seconds <= 0f)
                Destroy(gameObject);
        }
    }
}
