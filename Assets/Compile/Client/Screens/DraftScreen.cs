using System.Collections.Generic;
using System.Linq;
using Compile.Client.App;
using Compile.Client.Cards;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    // The Protocol draft, played on the mat: the pool is spread over the local player's lanes and each
    // pick moves into its owner's Protocol slot. The rules engine runs the draft (the match starts here);
    // in hot-seat both players pick at this screen, otherwise the stand-in opponent picks at random.
    // After each round of picks the player confirms them, and can undo them until then.
    public sealed class DraftScreen : TableScreen
    {
        const int Columns = 4;
        const float PoolScale = 0.6f;
        const float Pitch = 18f;
        const float PauseAfterDraft = 1.4f;

        // Pool area over the local lanes (mat pixels).
        static readonly Rect PoolPx = Rect.MinMaxRect(30f, 2420f, 1368f, 3720f);

        // From the far Protocol row to the bottom of the pool, in table units, plus room above for the HUD status bar.
        static readonly Rect ViewArea = Rect.MinMaxRect(-7f, MatLayout.ToTable(0f, 3740f).y, 7f, MatLayout.ToTable(0f, 1230f).y);

        readonly Dictionary<string, CardView> _cards = new Dictionary<string, CardView>();
        readonly Dictionary<string, Vector2> _home = new Dictionary<string, Vector2>();
        readonly Dictionary<string, (int seat, int slot)> _placed = new Dictionary<string, (int, int)>();
        MatchController _match;
        float _time;
        float _finishesAt = -1f;
        CardReader _reader;
        RectTransform _actions;
        TextMeshProUGUI _actionsText;
        ButtonSkin _undo, _confirm;

        Game Game => _match.Game;
        Draft Draft => Game.State.Draft;
        bool DraftDone => Game.State.Stage != GameStage.Draft;
        int Seat => Game.DraftSeat;                         // picking or confirming
        bool HumanSeat => !DraftDone && _match.KindOf(Seat) == SeatKind.Human;
        bool CanPick => HumanSeat && Game.DraftConfirmSeat < 0;
        bool CanConfirm => HumanSeat && Game.DraftConfirmSeat >= 0;
        bool CanUndo => HumanSeat && Game.CheckUndo(Seat) == null;

        protected override void OnEnter()
        {
            App.Mat.ClearProtocols();

            // A new match: names by seat, a human at the local seat, and a person or the bot opposite.
            var match = App.Match;
            var start = match.Start;
            var names = App.Catalog.Protocols.ToList();
            var game = Game.Create(App.Catalog, names, start.FirstSeat, start.Seed, match.SeatName(0), match.SeatName(1));
            var opponent = match.Hotseat ? SeatKind.Human : SeatKind.Bot;
            _match = start.LocalSeat == 0
                ? new MatchController(game, SeatKind.Human, opponent, viewer: 0, botSeed: start.Seed)
                : new MatchController(game, opponent, SeatKind.Human, viewer: 1, botSeed: start.Seed);
            match.Controller = _match;
            _match.Changed += OnChanged;

            // Hovered Protocols show in a docked panel beside the pool, not over it.
            _reader = CardReader.Create(App.Hud.Root);
            _reader.Rect.SetAsFirstSibling();
            BuildActions();
            Layout();

            DealPool(names);
            App.CameraRig.Show(new CameraShot(ViewArea, Pitch, margin: 0.04f));
            InMatch.AddLeaveAction(App);
            Refresh();
        }

        protected override void OnExit()
        {
            _match.Changed -= OnChanged;
            foreach (var (name, card) in _cards)
                if (!_placed.ContainsKey(name))
                    Object.Destroy(card.gameObject);      // the drafted ones stay on the mat for the board
            _cards.Clear();
            Object.Destroy(_reader.gameObject);
            Object.Destroy(_actions.gameObject);
            App.Hud.Zoom.Hide();
            App.Mat.HighlightSlots(true, -1);
        }

        // Undo and Confirm for the player who's picking.
        void BuildActions()
        {
            _actions = Ui.Rect("DraftActions", App.Hud.Root);
            _actions.SetAsFirstSibling();
            Ui.Backdrop(_actions, Palette.PanelFill, Palette.AccentDim, shadow: false);
            Ui.VStack(_actions, 14f, padX: 28, padY: 22);
            _actions.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            _actionsText = Ui.Text(_actions, "", TextStyle.Body);
            _actionsText.fontSize = 28f;
            var row = Ui.Row(_actions, 12f, "Buttons");
            _undo = Ui.Button(row, "Undo", ButtonStyle.Secondary, Undo);
            _confirm = Ui.Button(row, "Confirm", ButtonStyle.Primary, Confirm);
            foreach (var b in new[] { _undo, _confirm })
                b.Label.fontSize = 28f;
        }

        void DealPool(IReadOnlyList<string> names)
        {
            var rows = Mathf.CeilToInt(names.Count / (float)Columns);
            var cell = new Vector2(PoolPx.width / Columns, PoolPx.height / rows);
            var deckPosition = MatLayout.ToCanvas(PoolPx.center.x, PoolPx.yMax + 400f);

            for (var i = 0; i < names.Count; i++)
            {
                var row = i / Columns;
                var inRow = Mathf.Min(Columns, names.Count - row * Columns);
                var column = i % Columns + (Columns - inRow) / 2f;
                var target = MatLayout.ToCanvas(PoolPx.xMin + cell.x * (column + 0.5f), PoolPx.yMin + cell.y * (row + 0.5f));

                var card = CardView.Create(App.Mat.Cards, App.Art.ProtocolSide(names[i], compiled: false), names[i]);
                card.Protocol = names[i];
                card.Clicked += OnCardClicked;
                card.Hovered += OnCardHovered;
                card.LongPressed += OnCardLongPressed;
                card.Place(deckPosition, -90f, PoolScale);
                card.MoveTo(target, -90f, PoolScale, 0.45f + i * 0.03f);
                _cards[names[i]] = card;
                _home[names[i]] = target;
            }
        }

        public override void Tick(float deltaTime)
        {
            _time += deltaTime;
            Layout();

            if (!DraftDone)
            {
                _match.Tick(deltaTime);
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.zKey.wasPressedThisFrame &&
                    (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed))
                    Undo();
            }

            if (_finishesAt >= 0f && _time >= _finishesAt)
            {
                _finishesAt = -1f;
                App.Nav.Go(new BoardScreen());
            }
        }

        void Submit(Intent intent)
        {
            var error = _match.Submit(intent);
            if (error != null)
                App.Hud.Toast(error, error: true);
        }

        void OnCardClicked(CardView card)
        {
            if (CanPick && !_placed.ContainsKey(card.Protocol))
                Submit(new DraftPick(Seat, card.Protocol));
        }

        void Undo()
        {
            if (CanUndo)
                Submit(new Compile.Engine.Undo(Seat));
        }

        void Confirm()
        {
            if (CanConfirm)
                Submit(new EndTurn(Seat));
        }

        void OnChanged(IReadOnlyList<GameEvent> events) => Refresh();

        // Puts every Protocol where the engine says it is: in a player's slot, or back in the pool (after an undo).
        void Sync()
        {
            var state = Game.State;
            var wanted = new Dictionary<string, (int seat, int slot)>();
            for (var seat = 0; seat < 2; seat++)
            {
                var picks = state.Stage == GameStage.Draft
                    ? state.Draft.PicksFor(seat)
                    : state.Players[seat].Protocols.Select(p => p.Name).ToList();
                for (var slot = 0; slot < picks.Count; slot++)
                    wanted[picks[slot]] = (seat, slot);
            }

            // Return undone picks first, so their slots are free before anything else lands there.
            foreach (var name in _placed.Keys.ToList())
            {
                if (wanted.TryGetValue(name, out var spot) && spot == _placed[name])
                    continue;
                var card = _cards[name];
                App.Mat.ReleaseProtocol(card);
                card.MoveTo(_home[name], -90f, PoolScale, 0.35f);
                _placed.Remove(name);
            }
            foreach (var (name, spot) in wanted)
            {
                if (_placed.ContainsKey(name) || !_cards.TryGetValue(name, out var card))
                    continue;
                App.Mat.PlaceProtocol(card, App.Match.IsLocal(spot.seat), spot.slot);
                _placed[name] = spot;
                _reader.Unhover(card);
                App.Hud.Zoom.Release(card);
            }
        }

        // Resting the pointer on a Protocol (desktop) shows it in the docked reader; long-press (touch)
        // opens the large zoom until it's closed.
        void OnCardHovered(CardView card, bool hovered)
        {
            if (hovered)
                _reader.Hover(card, App.Art.ProtocolSide(card.Protocol, compiled: false),
                    CardDescription.ForProtocol(App.Cards.GetProtocol(card.Protocol), compiled: false));
            else
                _reader.Unhover(card);
        }

        void OnCardLongPressed(CardView card) =>
            App.Hud.Zoom.ShowProtocol(card, App.Art.ProtocolSide(card.Protocol, compiled: false), App.Cards.GetProtocol(card.Protocol),
                compiled: false, pinned: true);

        // Wide screens: the reader on the desk at the left of the mat, Undo/Confirm at the right. Tall
        // screens: Undo/Confirm under the status bar and the reader under them, over the Protocol rows.
        void Layout()
        {
            var root = App.Hud.Root.rect;
            if (Screen.width >= Screen.height)
            {
                _reader.SetLayout(true, 460f, root.height - 120f, root.height - 120f);
                _reader.Dock(new Vector2(0f, 0.5f), new Vector2(28f, 0f));
                Dock(_actions, new Vector2(1f, 0.5f), new Vector2(-28f, 0f), 460f);
            }
            else
            {
                var top = 32f + App.Hud.StatusBox.rect.height + 12f;
                Dock(_actions, new Vector2(0.5f, 1f), new Vector2(0f, -top), root.width - 48f);
                if (_actions.gameObject.activeSelf)
                    top += _actions.rect.height + 12f;
                _reader.SetLayout(false, root.width - 48f, 420f, 420f);
                _reader.Dock(new Vector2(0.5f, 1f), new Vector2(0f, -top));
            }
        }

        static void Dock(RectTransform rt, Vector2 anchor, Vector2 position, float width)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        }

        void Refresh()
        {
            Sync();

            foreach (var (name, card) in _cards)
            {
                card.Interactive = CanPick && !_placed.ContainsKey(name);
                card.SetHighlight(false);
            }

            _actions.gameObject.SetActive(CanUndo || CanConfirm);
            _undo.Interactable = CanUndo;
            _confirm.Interactable = CanConfirm;

            if (DraftDone)
            {
                if (_finishesAt >= 0f)
                    return;
                App.Mat.HighlightSlots(true, -1);
                App.Hud.SetStatus(":\\> draft complete", "Protocols loaded. Preparing the field...");
                _finishesAt = _time + PauseAfterDraft;
                return;
            }

            var draft = Draft;
            var seat = Seat;
            var made = App.Catalog.Protocols.Count() - draft.Available.Count;
            var label = $":\\> pick {Mathf.Min(6, CanConfirm ? made : made + 1)} of 6";
            var who = _match.Hotseat ? $"{_match.NameOf(seat)}: " : "";

            if (_match.KindOf(seat) == SeatKind.Bot)
            {
                App.Mat.HighlightSlots(App.Match.IsLocal(seat), draft.PicksFor(seat).Count);
                App.Hud.SetStatus(label, $"{_match.NameOf(seat)} is choosing...");
            }
            else if (CanConfirm)
            {
                App.Mat.HighlightSlots(true, -1);
                App.Hud.SetStatus(label, $"{who}confirm your picks");
                _actionsText.text = "Happy with your picks? Confirm them, or undo to change one.";
                _confirm.SetLabel(draft.IsComplete ? "Start game" : "Confirm");
            }
            else
            {
                App.Mat.HighlightSlots(App.Match.IsLocal(seat), draft.PicksFor(seat).Count);
                var remaining = draft.PicksRemainingThisTurn;
                App.Hud.SetStatus(label, $"{(_match.Hotseat ? who : "Your pick: ")}choose {remaining} Protocol{(remaining > 1 ? "s" : "")}");
                _actionsText.text = "You can undo a pick until you confirm.";
                _confirm.SetLabel("Confirm");
            }
        }
    }
}
