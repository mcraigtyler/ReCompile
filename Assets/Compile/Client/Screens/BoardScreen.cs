using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Client.App;
using Compile.Client.Board;
using Compile.Client.Cards;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine;
using Compile.Engine.Views;
using Compile.Net;
using UnityEngine;

namespace Compile.Client.Screens
{
    // The game board. Renders the PlayerView of whoever is at the screen and sends their moves to the
    // rules engine through the MatchController. In hot-seat, a pass screen hides everything between
    // players; against the stand-in opponent, the bot moves on its own.
    public sealed class BoardScreen : TableScreen
    {
        const float Pitch = 40f;
        const float HandSpace = 0.5f;
        const float OpponentStackReach = 9.6f;   // far edge of the opponent's first card in each lane (table units)
        const float ResultDelay = 1.6f;

        MatchController _match;
        BoardTable _table;
        BoardHud _hud;
        PlayerView _view;
        int Viewer => _match.Viewer;

        int _selected = -1;
        bool _faceUp = true;
        float _time;
        float _resultAt = -1f;
        string _promptKey;
        bool _promptWide;

        // Picks for a card choice (e.g. Clear Cache), before they're confirmed.
        readonly List<int> _picked = new List<int>();

        // Rearranging (Control): both Protocol rows can be reordered, one side at a time.
        bool _rearranging;
        CardView[][] _rearrangeSlots;
        int[][] _rearrangeOrder;
        int _pickSide = -1, _pickSlot = -1;

        ChoiceInfo Choice => _view?.Choice;
        bool Covered => _hud.HandoffShown;
        bool CanAct => _view != null && _view.CanAct && !Covered;
        bool ChoosingHandCards => Choice != null && Choice.Kind == ChoiceKind.HandCards;
        bool ChoosingFieldCards => Choice != null && Choice.Kind == ChoiceKind.FieldCards;

        protected override void OnEnter()
        {
            _match = App.Match.Controller;
            _match.Changed += OnChanged;

            _table = new GameObject("BoardTable").AddComponent<BoardTable>();
            _table.transform.SetParent(App.Table, false);
            _table.Init(App.Mat, App.Art, App.Theme);
            _table.CardClicked += OnTableCardClicked;
            _table.CardHovered += OnTableCardHovered;
            _table.CardLongPressed += ZoomTableCard;
            _table.ProtocolHovered += OnProtocolHovered;
            _table.ProtocolLongPressed += ZoomProtocol;
            _table.ProtocolClicked += OnProtocolClicked;
            _table.LaneClicked += OnLaneChosen;
            _table.LaneDropped += OnLaneChosen;
            _table.TrashClicked += ShowTrash;

            _hud = BoardHud.Create(App.Hud.Root, App.Art, App.Theme);
            _hud.Hand.CardTapped += OnHandCardTapped;
            _hud.Hand.CardRightClicked += OnHandCardRightClicked;
            _hud.Hand.CardHovered += OnHandCardHovered;
            _hud.Hand.CardLongPressed += ZoomHandCard;
            _hud.Hand.DragStarted += id =>
            {
                _hud.Reader.Dragging = true;
                // Dragging the selected card keeps the side chosen for it (face-down stays face-down).
                if (!ChoosingHandCards && id != _selected)
                    Select(id, DefaultOrientation(id));
            };
            _hud.Hand.DragEnded += id => _hud.Reader.Dragging = false;
            _hud.OrientationChosen += SetOrientation;
            _hud.RefreshPressed += Refresh;
            _hud.UndoPressed += Undo;
            _hud.EndTurnPressed += EndTurn;
            _hud.ZoomPressed += () => ZoomHandCard(_selected);
            _hud.TrashPressed += ShowTrash;
            AddDebugActions();

            App.CameraRig.Show(Frame);
            Render();
        }

        protected override void OnExit()
        {
            _match.Changed -= OnChanged;
            _table.Dispose();
            UnityEngine.Object.Destroy(_hud.gameObject);
            App.Hud.Zoom.Hide();
        }

        // Your lanes and both Protocol rows from a seat at the near edge, leaving room for the hand
        // below and the phase bar above. Wide screens also show the decks, trashes, and Control beside the mat.
        CameraShot Frame()
        {
            var wide = Screen.width >= Screen.height;
            var halfWidth = wide ? BoardTable.DeskReach : MatLayout.SizeUnits.x / 2f + 0.2f;
            var bottom = -MatLayout.SizeUnits.y / 2f - HandSpace;
            var top = OpponentStackReach + (wide ? 1.0f : 2.4f);
            return new CameraShot(Rect.MinMaxRect(-halfWidth, bottom, halfWidth, top), Pitch, margin: 0.01f);
        }

        public override void Tick(float deltaTime)
        {
            _time += deltaTime;
            _table.SetDeskVisible(Screen.width >= Screen.height);

            if (_resultAt >= 0f)
            {
                if (_time >= _resultAt)
                {
                    _resultAt = -1f;
                    App.Nav.Go(new ResultScreen());
                }
                return;
            }

            _match.Tick(deltaTime);

            // The prompt's buttons are laid out for the screen shape; rebuild them after a rotation.
            if (_hud.Wide != _promptWide)
            {
                _promptWide = _hud.Wide;
                _promptKey = null;
                if (_view != null)
                    UpdatePrompt();
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && !Covered && keyboard.zKey.wasPressedThisFrame &&
                (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed))
                Undo();
        }

        // ---------- Rendering ----------

        void OnChanged(IReadOnlyList<GameEvent> events) => Render();

        void Render()
        {
            _view = _match.View;
            _table.Render(_view);
            _hud.Render(_view, _view.Local.Name);

            if (_selected >= 0 && _view.Hand.All(c => c.InstanceId != _selected))
                _selected = -1;
            if (!ChoosingHandCards && !ChoosingFieldCards)
                _picked.Clear();
            _picked.RemoveAll(id => Choice == null || !Choice.ValidCards.Contains(id));
            _hud.Hand.Select(ChoosingHandCards ? -1 : _selected, _faceUp);
            _hud.Hand.SetMarked(ChoosingHandCards ? _picked : null, "DISCARD");

            UpdateTargets();
            UpdateReader();
            UpdatePrompt();
            UpdateHandoff();

            if (_view.Stage == GameStage.Over && _resultAt < 0f)
            {
                var state = _match.Game.State;
                App.Match.WinnerSeat = state.Winner;
                App.Match.EndReason = state.EndReason;
                _resultAt = _time + ResultDelay;
            }
        }

        void UpdateTargets()
        {
            var selected = !ChoosingHandCards && _selected >= 0;
            _hud.SetActionState(selected, _faceUp, selected && CanPlayFaceUpAnywhere(_selected), CanAct && _view.CanRefresh);
            _hud.SetTurnState(_view.CanUndo && !Covered, _view.CanEndTurn && !Covered,
                _view.LocalTurn && _view.Stage == GameStage.Playing ? _view.UndoBlockedReason : null);

            if (Choice != null && Choice.Kind == ChoiceKind.Line)
                _table.ShowLanes(Enumerable.Range(0, 3).Select(line => Choice.ValidLines.Contains(line)).ToArray());
            else if (selected && CanAct)
                _table.ShowLanes(Enumerable.Range(0, 3).Select(line => !_faceUp || CanPlayFaceUp(_selected, line)).ToArray());
            else
                _table.ShowLanes(null);

            if (!_rearranging)
                _table.ShowChoice(ChoosingFieldCards ? Choice.ValidCards : null);
        }

        // ---------- Prompt ----------

        void UpdatePrompt()
        {
            string key = null;
            if (Choice != null)
                key = $"choice:{Choice.Kind}:{Choice.Prompt}:{_picked.Count}:{_view.CanUndo}";
            if (key == _promptKey)
                return;
            _promptKey = key;

            if (Choice == null)
            {
                EndRearrange();
                _hud.HidePrompt();
                return;
            }

            var buttons = new List<(string, ButtonStyle, UnityEngine.Events.UnityAction)>();
            if (_view.CanUndo)
                buttons.Add(("Undo", ButtonStyle.Quiet, Undo));

            switch (Choice.Kind)
            {
                case ChoiceKind.Rearrange:
                    StartRearrange();
                    buttons.Add(("Don't rearrange", ButtonStyle.Secondary, () => Submit(AnswerChoice.Declined(Viewer))));
                    buttons.Add(("Confirm", ButtonStyle.Primary, ConfirmRearrange));
                    _hud.ShowPrompt(Choice.Prompt + "\n<size=70%><color=#A397BA>Tap two Protocols on the same side to swap them. Cards in the lines stay put.</color></size>",
                        buttons.ToArray());
                    _hud.PromptButton(buttons.Count - 1).Interactable = RearrangedSide() >= 0;
                    return;

                case ChoiceKind.Line:
                    _hud.ShowPrompt(Choice.Prompt + "\n<size=70%><color=#A397BA>Tap a highlighted line.</color></size>", buttons.ToArray());
                    return;

                default:
                    if (Choice.Optional)
                        buttons.Add(("Skip", ButtonStyle.Secondary, () => Submit(AnswerChoice.Declined(Viewer))));
                    var needsConfirm = ChoosingHandCards || Choice.Max > 1;
                    if (needsConfirm)
                        buttons.Add(($"Confirm ({_picked.Count} of {Choice.Max})", ButtonStyle.Primary, ConfirmPicks));
                    var hint = ChoosingHandCards ? "Tap cards in your hand to choose them." : "Tap a highlighted card.";
                    _hud.ShowPrompt(Choice.Prompt + $"\n<size=70%><color=#A397BA>{hint}</color></size>", buttons.ToArray());
                    if (needsConfirm)
                        _hud.PromptButton(buttons.Count - 1).Interactable = _picked.Count >= Choice.Min && _picked.Count <= Choice.Max;
                    return;
            }
        }

        void ConfirmPicks()
        {
            if (Choice != null)
                Submit(AnswerChoice.WithCards(Viewer, _picked.ToArray()));
        }

        // ---------- Pass the screen ----------

        void UpdateHandoff()
        {
            if (!_match.NeedsHandoff || Covered)
                return;
            var seat = _match.SeatToMove;
            Select(-1, true);
            App.Hud.Zoom.Hide();
            _hud.ShowHandoff(_match.NameOf(seat), () =>
            {
                _hud.HideHandoff();
                _match.HandTo(seat);
            });
        }

        // ---------- Moves ----------

        void Submit(Intent intent)
        {
            var error = _match.Submit(intent);
            if (error != null)
                App.Hud.Toast(error, error: true);
        }

        void Undo()
        {
            if (Covered || _view == null || !_view.LocalTurn)
                return;
            if (!_view.CanUndo)
            {
                if (!string.IsNullOrEmpty(_view.UndoBlockedReason))
                    App.Hud.Toast(_view.UndoBlockedReason);
                return;
            }
            Select(-1, true);
            Submit(new Compile.Engine.Undo(Viewer));
        }

        void EndTurn()
        {
            if (!Covered && _view.CanEndTurn)
                Submit(new Compile.Engine.EndTurn(Viewer));
        }

        void Refresh()
        {
            if (CanAct && _view.CanRefresh)
                Submit(new Compile.Engine.Refresh(Viewer));
        }

        void OnLaneChosen(int line)
        {
            if (Covered)
                return;
            if (Choice != null && Choice.Kind == ChoiceKind.Line)
            {
                if (Choice.ValidLines.Contains(line))
                    Submit(AnswerChoice.WithLine(Viewer, line));
                return;
            }
            PlaySelected(line);
        }

        void PlaySelected(int line)
        {
            if (_selected < 0 || !CanAct)
                return;
            // Read the choice before clearing the selection (clearing resets it to face-up).
            var id = _selected;
            var faceUp = _faceUp;
            Select(-1, true);
            Submit(new PlayCard(Viewer, id, line, faceUp));
        }

        // ---------- Selecting hand cards ----------

        CardInfo HandInfo(int id) => _view?.Hand.FirstOrDefault(c => c.InstanceId == id);

        bool CanPlayFaceUp(int id, int line)
        {
            var info = HandInfo(id);
            return info != null && _view.Lines[line].Accepts(info.Protocol);
        }

        bool CanPlayFaceUpAnywhere(int id) => Enumerable.Range(0, 3).Any(line => CanPlayFaceUp(id, line));

        bool DefaultOrientation(int id) => CanPlayFaceUpAnywhere(id);

        void OnHandCardTapped(int id)
        {
            if (Covered)
                return;
            if (ChoosingHandCards)
            {
                TogglePick(id);
                return;
            }

            if (_selected == id)
                Select(-1, true);
            else
                Select(id, DefaultOrientation(id));

            if (_selected >= 0 && !CanAct && Choice == null)
            {
                var message = !_view.LocalTurn ? "Wait for your turn to play a card."
                    : _view.CanEndTurn ? "Your turn is done: End turn, or Undo to change it."
                    : "You can play a card during your Action step.";
                App.Hud.Toast(message);
            }
        }

        void TogglePick(int id)
        {
            if (!Choice.ValidCards.Contains(id))
                return;
            if (_picked.Contains(id))
                _picked.Remove(id);
            else if (_picked.Count < Choice.Max)
                _picked.Add(id);
            else if (Choice.Max == 1)
            {
                _picked.Clear();
                _picked.Add(id);
            }
            else
                App.Hud.Toast($"You've already chosen {Choice.Max}. Tap a chosen card to put it back.");
            _hud.Hand.SetMarked(_picked, "DISCARD");
            UpdatePrompt();
        }

        void OnHandCardRightClicked(int id)
        {
            if (Covered || ChoosingHandCards)
                return;
            if (_selected != id)
                Select(id, DefaultOrientation(id));
            SetOrientation(!_faceUp);
        }

        void Select(int id, bool faceUp)
        {
            _selected = id;
            _faceUp = faceUp;
            _hud.Hand.Select(id, faceUp);
            UpdateTargets();
            UpdateReader();
        }

        void SetOrientation(bool faceUp)
        {
            if (_selected < 0)
                return;
            if (faceUp && !CanPlayFaceUpAnywhere(_selected))
            {
                var protocol = HandInfo(_selected)?.Protocol;
                App.Hud.Toast($"{protocol} cards can only be played face-up into a line with a {protocol} Protocol.", error: true);
                return;
            }
            _faceUp = faceUp;
            _hud.Hand.SetSelectedFaceUp(faceUp);
            UpdateTargets();
            UpdateReader();
        }

        // ---------- Card choices on the field ----------

        void OnTableCardClicked(CardView card)
        {
            if (Covered || !ChoosingFieldCards || !Choice.ValidCards.Contains(card.InstanceId))
                return;
            if (Choice.Max == 1)
            {
                Submit(AnswerChoice.WithCards(Viewer, card.InstanceId));
                return;
            }
            if (_picked.Contains(card.InstanceId))
                _picked.Remove(card.InstanceId);
            else if (_picked.Count < Choice.Max)
                _picked.Add(card.InstanceId);
            card.SetHighlight(true, _picked.Contains(card.InstanceId) ? Palette.Accent : Palette.Cyan);
            UpdatePrompt();
        }

        // ---------- Rearranging ----------
        // Side 0 is the viewer's Protocol row, side 1 the opponent's. Slots are in that player's own order.

        void StartRearrange()
        {
            if (_rearranging)
                return;
            Select(-1, true);
            _rearranging = true;
            _rearrangeSlots = new CardView[2][];
            _rearrangeOrder = new int[2][];
            for (var side = 0; side < 2; side++)
            {
                _rearrangeSlots[side] = Enumerable.Range(0, 3).Select(i => App.Mat.ProtocolCard(side == 0, i)).ToArray();
                _rearrangeOrder[side] = new[] { 0, 1, 2 };
                foreach (var card in _rearrangeSlots[side])
                    card.Interactive = true;
            }
            _pickSide = _pickSlot = -1;
            _table.ShowChoice(null);
        }

        void EndRearrange()
        {
            if (!_rearranging)
                return;
            _rearranging = false;
            for (var side = 0; side < 2; side++)
            {
                ResetSide(side);
                foreach (var card in _rearrangeSlots[side])
                {
                    card.Interactive = false;
                    card.SetHighlight(false);
                }
            }
        }

        // The side whose order has changed, or -1.
        int RearrangedSide()
        {
            if (!_rearranging)
                return -1;
            for (var side = 0; side < 2; side++)
                if (!_rearrangeOrder[side].SequenceEqual(new[] { 0, 1, 2 }))
                    return side;
            return -1;
        }

        void OnProtocolClicked(bool local, int slot)
        {
            if (!_rearranging || Covered)
                return;
            var side = local ? 0 : 1;
            if (_pickSide != side)
            {
                if (_pickSide >= 0)
                    _rearrangeSlots[_pickSide][_pickSlot].SetHighlight(false);
                _pickSide = side;
                _pickSlot = slot;
                _rearrangeSlots[side][slot].SetHighlight(true, Palette.Accent);
                return;
            }

            var other = _pickSlot;
            _rearrangeSlots[side][other].SetHighlight(false);
            _pickSide = _pickSlot = -1;
            if (other == slot)
                return;

            // Only one player's Protocols can be rearranged: changing this side puts the other one back.
            ResetSide(1 - side);
            var slots = _rearrangeSlots[side];
            var order = _rearrangeOrder[side];
            (slots[other], slots[slot]) = (slots[slot], slots[other]);
            (order[other], order[slot]) = (order[slot], order[other]);
            App.Mat.PreviewProtocolSlot(slots[other], side == 0, other);
            App.Mat.PreviewProtocolSlot(slots[slot], side == 0, slot);

            var confirm = _hud.PromptButton(PromptButtonCount() - 1);
            if (confirm != null)
                confirm.Interactable = RearrangedSide() >= 0;
        }

        int PromptButtonCount() => (_view.CanUndo ? 1 : 0) + 2;

        void ResetSide(int side)
        {
            var order = _rearrangeOrder[side];
            if (order.SequenceEqual(new[] { 0, 1, 2 }))
                return;
            var original = new CardView[3];
            for (var i = 0; i < 3; i++)
                original[order[i]] = _rearrangeSlots[side][i];
            _rearrangeSlots[side] = original;
            _rearrangeOrder[side] = new[] { 0, 1, 2 };
            for (var i = 0; i < 3; i++)
                App.Mat.PreviewProtocolSlot(original[i], side == 0, i);
        }

        void ConfirmRearrange()
        {
            var side = RearrangedSide();
            if (side < 0)
                return;
            var target = side == 0 ? Viewer : GameState.Other(Viewer);
            var order = _rearrangeOrder[side].ToArray();
            EndRearrangeWithoutReset();
            Submit(new Compile.Engine.Rearrange(Viewer, target, order));
        }

        // The engine now holds the new order; the next render moves the cards for real.
        void EndRearrangeWithoutReset()
        {
            _rearranging = false;
            for (var side = 0; side < 2; side++)
                foreach (var card in _rearrangeSlots[side])
                {
                    card.Interactive = false;
                    card.SetHighlight(false);
                }
        }

        // ---------- Card text ----------
        // Hovering (after a short rest) or selecting a card shows its text in the docked reader; a long
        // press or the Zoom button opens the large zoom, which stays until it's closed.

        static object HandSubject(int id) => "hand:" + id;

        (Sprite art, CardDescription text) Describe(CardInfo info, bool faceUp)
        {
            if (info.IsHidden)
                return (App.Theme.cardBack, CardDescription.ForCard(null, faceUp: false, hiddenFromViewer: true));
            return (App.Art.Card(info.Protocol, info.PrintedValue),
                CardDescription.ForCard(App.Cards.GetCard(info.CardId), faceUp, hiddenFromViewer: false));
        }

        // The selected hand card stays in the reader, showing the side it will be played on.
        void UpdateReader()
        {
            var info = _selected >= 0 ? HandInfo(_selected) : null;
            if (info == null)
            {
                _hud.Reader.ClearResting();
                return;
            }
            var (art, text) = Describe(info, _faceUp);
            _hud.Reader.SetResting(HandSubject(_selected), art, text);
        }

        void OnHandCardHovered(int id, bool on)
        {
            var info = HandInfo(id);
            if (!on || info == null || Covered)
            {
                _hud.Reader.Unhover(HandSubject(id));
                return;
            }
            var (art, text) = Describe(info, id != _selected || _faceUp);
            _hud.Reader.Hover(HandSubject(id), art, text);
        }

        void OnTableCardHovered(CardView card, bool on)
        {
            var info = _table.InfoFor(card);
            if (!on || info == null || Covered)
            {
                _hud.Reader.Unhover(card);
                return;
            }
            var (art, text) = Describe(info, info.FaceUp);
            _hud.Reader.Hover(card, art, text);
        }

        void OnProtocolHovered(CardView card, bool on)
        {
            if (!on || Covered)
            {
                _hud.Reader.Unhover(card);
                return;
            }
            _hud.Reader.Hover(card, App.Art.ProtocolSide(card.Protocol, card.Compiled),
                CardDescription.ForProtocol(App.Cards.GetProtocol(card.Protocol), card.Compiled));
        }

        void ZoomHandCard(int id)
        {
            var info = HandInfo(id);
            if (info == null || Covered)
                return;
            App.Hud.Zoom.ShowCard(HandSubject(id), App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                faceUp: true, hiddenFromViewer: false, pinned: true);
        }

        void ZoomTableCard(CardView card)
        {
            var info = _table.InfoFor(card);
            if (info == null || Covered)
                return;
            if (info.IsHidden)
                App.Hud.Zoom.ShowCard(card, App.Theme.cardBack, null, faceUp: false, hiddenFromViewer: true, pinned: true);
            else
                App.Hud.Zoom.ShowCard(card, App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                    info.FaceUp, hiddenFromViewer: false, pinned: true);
        }

        void ZoomProtocol(CardView card)
        {
            if (!Covered)
                App.Hud.Zoom.ShowProtocol(card, App.Art.ProtocolSide(card.Protocol, card.Compiled), App.Cards.GetProtocol(card.Protocol),
                    card.Compiled, pinned: true);
        }

        void ShowTrash(bool local)
        {
            if (Covered)
                return;
            var side = local ? _view.Local : _view.Opponent;
            _hud.ShowTrash(local ? "Your trash" : $"{side.Name}'s trash", side.Trash,
                info => App.Hud.Zoom.ShowCard(info, App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                    faceUp: true, hiddenFromViewer: false, pinned: true));
        }

        // ---------- Debug ----------

        void AddDebugActions()
        {
            _hud.AddDebugAction("Auto-play a move", _match.AutoPlayViewer);
            _hud.AddDebugAction("Auto-play your turn", () =>
            {
                for (var i = 0; i < 50 && _match.SeatToMove == Viewer && _view.Stage == GameStage.Playing; i++)
                    _match.AutoPlayViewer();
            });
            _hud.AddDebugAction("Concede", () => Submit(new Concede(Viewer)));
            if (App.Session is FakeSessionService fake && !App.Match.Hotseat)
                _hud.AddDebugAction("Opponent leaves", fake.SimulateOpponentLeft);
            _hud.AddDebugAction("Leave game", () => InMatch.Leave(App));
        }
    }
}
