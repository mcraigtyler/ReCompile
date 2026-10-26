using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Client.App;
using Compile.Client.Board;
using Compile.Client.Cards;
using Compile.Client.Platform;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine;
using Compile.Engine.Views;
using Compile.Net;
using UnityEngine;

namespace Compile.Client.Screens
{
    // The game board. Renders the PlayerView of whoever is at the screen and sends their moves through the
    // match (IMatch: a local engine, or the host of an online game). In hot-seat, a pass screen hides everything between
    // players; against the stand-in opponent, the bot moves on its own. Every question card text can ask
    // is answered here: cards on the field or in the hand, lines, options, orders, Protocols, and plays.
    public sealed class BoardScreen : TableScreen
    {
        const float Pitch = 40f;
        const float HandSpace = 0.5f;
        const float OpponentStackReach = 9.6f;   // far edge of the opponent's first card in each lane (table units)
        const float ResultDelay = 1.6f;
        const string Hint = "\n<size=70%><color=#A397BA>{0}</color></size>";

        IMatch _match;
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
        ButtonSkin _confirm;        // the prompt's Confirm button, enabled once the answer is complete

        // Picks for a card choice (e.g. Clear Cache), before they're confirmed.
        readonly List<int> _picked = new List<int>();

        // An Order choice: option indexes in the order they'll resolve, so far.
        readonly List<int> _ordered = new List<int>();

        // Rearranging (Control, or card text): Protocol rows can be reordered, one side at a time.
        // In swap mode, each swap starts again from the original order, so the answer is always one swap.
        bool _rearranging;
        bool _swapOnly;
        string _rearrangeQuestion;
        readonly bool[] _rearrangeAllowed = new bool[2];
        CardView[][] _rearrangeSlots;
        int[][] _rearrangeOrder;
        int _pickSide = -1, _pickSlot = -1;

        ChoiceInfo Choice => _view?.Choice;
        bool Covered => _hud.HandoffShown;

        // The game is stopped (an error, a lost connection) or on hold (the opponent reconnecting).
        bool Halted => _match.HasFailed || _match.Waiting != null;
        bool CanAct => _view != null && _view.CanAct && !Covered && !Halted && _replay == null;

        // Watching a loaded game: the board shows it, cards can be read, but moves come from the replay.
        ReplayController _replay;
        bool ChoosingHandCards => Choice != null && Choice.Kind == ChoiceKind.HandCards;
        bool ChoosingFieldCards => Choice != null && Choice.Kind == ChoiceKind.FieldCards;
        bool ChoosingPlay => Choice != null && Choice.Kind == ChoiceKind.Play;

        // A hand card can be played now: in the Action step, or because card text says "play 1 card".
        bool CanPlay => CanAct || ChoosingPlay && !Covered && !Halted && _replay == null;

        protected override void OnEnter()
        {
            _match = App.Match.Controller;
            _replay = App.Match.Replay;
            _match.Changed += OnChanged;
            _match.Failed += OnFailed;
            _match.Rejected += OnRejected;

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
            if (_replay == null)
                AddDebugActions();
            _hud.SetReplay(_replay != null);

            App.CameraRig.Show(Frame);
            Render();
        }

        protected override void OnExit()
        {
            _match.Changed -= OnChanged;
            _match.Failed -= OnFailed;
            _match.Rejected -= OnRejected;
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

            // Waiting for the opponent to reconnect counts down without any update from the game.
            if (_view != null && (_match.Waiting != _shownWaiting || _match.HasFailed != _shownFailed))
                Render();

            // The prompt's buttons are laid out for the screen shape; rebuild them after a rotation.
            if (_hud.Wide != _promptWide)
            {
                _promptWide = _hud.Wide;
                _promptKey = null;
                if (_view != null)
                    UpdatePrompt();
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (_replay != null)
            {
                if (keyboard != null && keyboard.rightArrowKey.wasPressedThisFrame)
                    _replay.Forward();
                else if (keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame)
                    _replay.Back();
                return;
            }
            if (keyboard != null && !Covered && keyboard.zKey.wasPressedThisFrame &&
                (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed))
                Undo();
        }

        // ---------- Rendering ----------

        void OnChanged(MatchUpdate update)
        {
            var events = update.Events;
            // Cards changing places in ways the view alone can't tell apart: back to a hand rather than to
            // the trash, and between the two hands.
            var returned = new HashSet<int>();
            var leaving = new HashSet<int>();
            var arriving = false;
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case EventKind.Returned:
                        returned.UnionWith(e.Cards.Select(c => c.InstanceId));
                        break;
                    case EventKind.Given:
                        if (e.Seat == Viewer)
                            leaving.UnionWith(e.Cards.Select(c => c.InstanceId));
                        arriving |= e.ToSeat == Viewer;
                        break;
                    case EventKind.Taken:
                        if (e.FromSeat == Viewer)
                            leaving.UnionWith(e.Cards.Select(c => c.InstanceId));
                        arriving |= e.Seat == Viewer;
                        break;
                }
            }
            _hud.Hand.ExpectTransfers(leaving, arriving);
            Render(returned);
            ShowReveals(events);
            ShowNotes(events);
        }

        void Render() => Render(null);

        void Render(ICollection<int> returned)
        {
            _view = _match.View;
            _table.Render(_view, returned);
            _hud.Render(_view, _view.Local.Name);

            if (_selected >= 0 && _view.Hand.All(c => c.InstanceId != _selected))
                _selected = -1;
            if (!ChoosingHandCards && !ChoosingFieldCards)
                _picked.Clear();
            _picked.RemoveAll(id => Choice == null || !Choice.ValidCards.Contains(id));
            if (Choice == null || Choice.Kind != ChoiceKind.Order)
                _ordered.Clear();
            _hud.Hand.Select(ChoosingHandCards ? -1 : _selected, _faceUp);
            _hud.Hand.SetMarked(ChoosingHandCards ? _picked : null, (Choice?.Verb ?? "Chosen").ToUpperInvariant());

            UpdateTargets();
            UpdateReader();
            UpdatePrompt();
            UpdateHandoff();

            if (_view.Stage == GameStage.Over && _resultAt < 0f && _replay == null)
            {
                App.Match.WinnerSeat = _view.Outcome == Outcome.Draw ? -1 : _view.Outcome == Outcome.LocalWon ? _view.Seat : 1 - _view.Seat;
                App.Match.EndReason = _view.EndReason;
                _resultAt = _time + ResultDelay;
            }
        }

        void UpdateTargets()
        {
            var selected = !ChoosingHandCards && _selected >= 0;
            _hud.SetActionState(selected, _faceUp, selected && FaceUpAllowed(_selected), CanAct && _view.CanRefresh);
            _hud.SetTurnState(_view.CanUndo && !Covered, _view.CanEndTurn && !Covered,
                _view.LocalTurn && _view.Stage == GameStage.Playing ? _view.UndoBlockedReason : null);

            if (Choice != null && Choice.Kind == ChoiceKind.Line)
            {
                var (local, opponent) = LineSides();
                _table.ShowLanes(Enumerable.Range(0, 3).Select(line => Choice.ValidLines.Contains(line)).ToArray(), local, opponent);
            }
            else if (selected && CanPlay)
                _table.ShowLanes(Enumerable.Range(0, 3).Select(line => CanPlayInto(_selected, line, _faceUp)).ToArray());
            else
                _table.ShowLanes(null);

            if (_rearranging)
                return;
            if (ChoosingFieldCards)
                _table.ShowChoice(Choice.ValidCards, _picked);
            else if (Choice != null && Choice.Kind == ChoiceKind.Order && Choice.ValidCards.Count > 0)
                _table.ShowChoice(Choice.ValidCards, _ordered.Select(i => Choice.ValidCards.ElementAt(i)).ToList());
            else
                _table.ShowChoice(null, null, Choice?.SubjectInstanceId ?? 0);
        }

        // Which halves of the lines a Line choice is about: your own to compile; the moving card's side to
        // shift; both for a whole line.
        (bool local, bool opponent) LineSides()
        {
            switch (Choice.Purpose)
            {
                case LinePurpose.Compile:
                    return (true, false);
                case LinePurpose.ShiftTo:
                    var subject = _table.InfoFor(Choice.SubjectInstanceId);
                    return subject == null ? (true, true) : (subject.LocalOwner, !subject.LocalOwner);
                default:
                    return (true, true);
            }
        }

        // ---------- Prompt ----------

        void UpdatePrompt()
        {
            if (_replay != null)
            {
                UpdateReplayPrompt();
                return;
            }
            _shownWaiting = _match.Waiting;
            _shownFailed = _match.HasFailed;
            if (Halted)
            {
                ShowHalt();
                return;
            }

            string key = null;
            if (Choice != null)
                key = $"choice:{Choice.Kind}:{Choice.Prompt}:{_picked.Count}:{_ordered.Count}:{_view.CanUndo}:{Covered}";
            if (key == _promptKey)
                return;
            _promptKey = key;
            _confirm = null;

            // Unconfirmed Protocol moves are put back when the question goes away or changes.
            if (Choice == null || Choice.Kind != ChoiceKind.Rearrange && Choice.Kind != ChoiceKind.Swap)
                EndRearrange();
            if (Choice == null)
            {
                _hud.HidePrompt();
                return;
            }

            var buttons = new List<(string, ButtonStyle, UnityEngine.Events.UnityAction)>();
            if (_view.CanUndo)
                buttons.Add(("Undo", ButtonStyle.Quiet, Undo));
            var text = Choice.Prompt;
            if (!string.IsNullOrEmpty(Choice.Resolving))
                text += string.Format(Hint, "From " + Choice.Resolving);
            if (!_view.CanUndo && _view.LocalTurn && !string.IsNullOrEmpty(_view.UndoBlockedReason))
                text += string.Format(Hint, "Can't undo: " + _view.UndoBlockedReason);

            switch (Choice.Kind)
            {
                case ChoiceKind.Rearrange:
                case ChoiceKind.Swap:
                    StartRearrange();
                    if (Choice.Optional)
                        buttons.Add((Choice.Kind == ChoiceKind.Swap ? "Don't swap" : "Don't rearrange", ButtonStyle.Secondary, Decline));
                    buttons.Add(("Confirm", ButtonStyle.Primary, ConfirmRearrange));
                    var whose = Choice.Side == ChoiceSide.Either ? "on the same side " : "";
                    ShowPrompt(text + string.Format(Hint, $"Tap two Protocols {whose}to swap them. Cards in the lines stay put."), buttons,
                        RearrangedSide() >= 0);
                    return;

                case ChoiceKind.Line:
                    var lineHint = Choice.Purpose == LinePurpose.ShiftTo ? "Tap a highlighted line to move the card there."
                        : Choice.Purpose == LinePurpose.Compile ? "Tap a highlighted line to compile it."
                        : "Tap a highlighted line.";
                    // An optional line comes after one already picked ("another line?"): declining finishes.
                    if (Choice.Optional)
                        buttons.Add(("Done", ButtonStyle.Secondary, Decline));
                    _hud.ShowPrompt(text + string.Format(Hint, lineHint), buttons.ToArray());
                    return;

                case ChoiceKind.Option:
                    for (var i = 0; i < Choice.Options.Count; i++)
                    {
                        var option = i;
                        buttons.Add((Choice.Options[i], i == 0 ? ButtonStyle.Primary : ButtonStyle.Secondary,
                            () => Answer(AnswerChoice.WithOption(Viewer, option))));
                    }
                    if (Choice.Optional)
                        buttons.Add((Choice.Options.Count == 1 ? "Don't" : "Skip", ButtonStyle.Quiet, Decline));
                    _hud.ShowPrompt(text, buttons.ToArray());
                    return;

                case ChoiceKind.Order:
                    var order = string.Join("   ", _ordered.Select((option, i) => $"<color=#E58BFF>{i + 1}.</color> {Choice.Options[option]}"));
                    var orderHint = _ordered.Count == Choice.Options.Count ? "Confirm, or start over."
                        : "Tap them in the order they'll resolve, first to last." + (Choice.ValidCards.Count > 0 ? " You can tap the cards too." : "");
                    for (var i = 0; i < Choice.Options.Count; i++)
                    {
                        if (_ordered.Contains(i))
                            continue;
                        var option = i;
                        buttons.Add(($"{_ordered.Count + 1}: {Choice.Options[i]}", ButtonStyle.Secondary, () => AddToOrder(option)));
                    }
                    if (_ordered.Count > 0)
                        buttons.Add(("Start over", ButtonStyle.Quiet, () =>
                        {
                            _ordered.Clear();
                            UpdateTargets();
                            UpdatePrompt();
                        }));
                    buttons.Add(("Confirm", ButtonStyle.Primary, () => Answer(AnswerChoice.WithOrder(Viewer, _ordered))));
                    ShowPrompt(text + (order.Length > 0 ? "\n<size=80%>" + order + "</size>" : "") + string.Format(Hint, orderHint), buttons,
                        _ordered.Count == Choice.Options.Count);
                    return;

                case ChoiceKind.Play:
                    var playHint = "Choose a card in your hand, then a highlighted line" + (Choice.FaceUpAllowed ? "." : ". It goes face-down.");
                    if (Choice.Optional)
                        buttons.Add(("Skip", ButtonStyle.Secondary, Decline));
                    _hud.ShowPrompt(text + string.Format(Hint, playHint), buttons.ToArray());
                    return;

                default:
                    if (Choice.Optional)
                        buttons.Add(("Skip", ButtonStyle.Secondary, Decline));
                    var verb = (Choice.Verb ?? "choose").ToLowerInvariant();
                    var hint = ChoosingHandCards ? $"Tap cards in your hand to {verb} them."
                        : Choice.Max > 1 ? $"Tap highlighted cards to {verb} them." : $"Tap a highlighted card to {verb} it.";
                    text += string.Format(Hint, hint);
                    // One card on the field is answered by tapping it; anything else is confirmed.
                    if (!ChoosingHandCards && Choice.Max == 1)
                    {
                        _hud.ShowPrompt(text, buttons.ToArray());
                        return;
                    }
                    var count = Choice.Min == Choice.Max ? $"{_picked.Count} of {Choice.Max}" : $"{_picked.Count}";
                    buttons.Add(($"Confirm ({count})", ButtonStyle.Primary, ConfirmPicks));
                    ShowPrompt(text, buttons, _picked.Count >= Choice.Min && _picked.Count <= Choice.Max);
                    return;
            }
        }

        // Shows the prompt; its last button is Confirm, enabled when the answer is complete.
        void ShowPrompt(string text, List<(string, ButtonStyle, UnityEngine.Events.UnityAction)> buttons, bool complete)
        {
            _hud.ShowPrompt(text, buttons.ToArray());
            _confirm = _hud.PromptButton(buttons.Count - 1);
            if (_confirm != null)
                _confirm.Interactable = complete;
        }

        void ConfirmPicks()
        {
            if (Choice != null)
                Answer(AnswerChoice.WithCards(Viewer, _picked.ToArray()));
        }

        void Decline() => Answer(AnswerChoice.Declined(Viewer));

        // Sends the answer to the current question; whatever was picked for it is done with.
        void Answer(Intent answer)
        {
            if (Covered || Choice == null)
                return;
            _picked.Clear();
            _ordered.Clear();
            Submit(answer);
        }

        void AddToOrder(int option)
        {
            if (Choice == null || _ordered.Contains(option))
                return;
            _ordered.Add(option);
            // The last one has nowhere else to go.
            if (_ordered.Count == Choice.Options.Count - 1)
                _ordered.Add(Enumerable.Range(0, Choice.Options.Count).First(i => !_ordered.Contains(i)));
            UpdateTargets();
            UpdatePrompt();
        }

        // ---------- Pass the screen ----------

        void UpdateHandoff()
        {
            if (!_match.NeedsHandoff || Covered || _replay != null)
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

        // ---------- Reveals ----------
        // Revealed field cards turn face-up for a moment with a banner; a revealed hand opens in a panel.

        void ShowReveals(IReadOnlyList<EventInfo> events)
        {
            if (Covered)
                return;
            foreach (var reveal in events.Where(e => e.Kind == EventKind.Revealed))
            {
                if (!reveal.InHand)
                {
                    foreach (var card in reveal.Cards)
                    {
                        var spec = App.Cards.GetCard(card.CardId);
                        _table.HoldReveal(card.InstanceId, App.Art.Card(spec.Protocol, spec.Value));
                    }
                    continue;
                }

                if (reveal.Owner == Viewer)
                    continue;
                var cards = reveal.Cards.Select(c =>
                {
                    var spec = App.Cards.GetCard(c.CardId);
                    return new CardInfo { InstanceId = c.InstanceId, CardId = c.CardId, Protocol = spec.Protocol, PrintedValue = spec.Value, FaceUp = true };
                }).ToList();
                var name = _match.NameOf(reveal.Owner);
                var title = cards.Count == 1 ? $"{name} revealed a card" : $"{name} revealed their hand · {cards.Count} cards";
                _hud.ShowCards(title, cards, ZoomCard);
            }
        }

        // ---------- Notes ----------
        // Things the board alone doesn't explain (text set off by uncovering or flipping a card, a pick made
        // for a player, a reveal) also show for a moment, one line each, besides going in the log.

        // Notes that came while the screen was being handed over are sent again, for the next player, when
        // they take it (MatchController.HandTo).
        void ShowNotes(IReadOnlyList<EventInfo> events)
        {
            if (Covered)
                return;
            var notes = events
                .Where(e => e.Kind == EventKind.Note || e.Kind == EventKind.Revealed)
                .Select(e => e.Text)
                .Where(text => !string.IsNullOrEmpty(text))
                .ToList();
            if (notes.Count > 0)
                App.Hud.Toast(string.Join("\n", notes), seconds: Mathf.Clamp(2.5f * notes.Count, 3.5f, 8f));
        }

        // ---------- Moves ----------

        void Submit(Intent intent)
        {
            if (_replay != null)
            {
                App.Hud.Toast("This is a replay. Press Play from here to carry on playing.");
                return;
            }
            var error = _match.Submit(intent);
            if (error != null)
                OnRejected(error);
        }

        // The host refused a move (online, the reason comes back after the move was sent).
        void OnRejected(string error) => App.Hud.Toast(error, error: true);

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
            _picked.Clear();
            _ordered.Clear();
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
                    Answer(AnswerChoice.WithLine(Viewer, line));
                return;
            }
            PlaySelected(line);
        }

        void PlaySelected(int line)
        {
            if (_selected < 0 || !CanPlay)
                return;
            // Read the choice before clearing the selection (clearing resets it to face-up).
            var id = _selected;
            var faceUp = _faceUp;
            Select(-1, true);
            Submit(new PlayCard(Viewer, id, line, faceUp));
        }

        // ---------- Selecting hand cards ----------

        CardInfo HandInfo(int id) => _view?.Hand.FirstOrDefault(c => c.InstanceId == id);

        // Whether this card could go into this line on this side. A "play 1 card" choice limits the cards,
        // lines, and sides; card text can forbid more, which the engine explains if it's tried.
        bool CanPlayInto(int id, int line, bool faceUp)
        {
            var info = HandInfo(id);
            if (info == null)
                return false;
            if (ChoosingPlay && (!Choice.ValidCards.Contains(id) || !Choice.ValidLines.Contains(line) ||
                                 (faceUp ? !Choice.FaceUpAllowed : !Choice.FaceDownAllowed)))
                return false;
            var lines = faceUp ? info.FaceUpLines : info.FaceDownLines;
            if (lines != null)
                return lines[line];
            return !faceUp || _view.Lines[line].Accepts(info.Protocol);
        }

        bool FaceUpAllowed(int id) => Enumerable.Range(0, 3).Any(line => CanPlayInto(id, line, true));

        bool DefaultOrientation(int id) => FaceUpAllowed(id);

        void OnHandCardTapped(int id)
        {
            if (Covered)
                return;
            if (ChoosingHandCards)
            {
                TogglePick(id);
                return;
            }
            if (ChoosingPlay && !Choice.ValidCards.Contains(id))
            {
                App.Hud.Toast("That card can't be played now.");
                return;
            }

            if (_selected == id)
                Select(-1, true);
            else
                Select(id, DefaultOrientation(id));

            if (_selected >= 0 && !CanPlay && Choice == null)
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
            _hud.Hand.SetMarked(_picked, (Choice.Verb ?? "Chosen").ToUpperInvariant());
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
            if (ChoosingPlay && (faceUp ? !Choice.FaceUpAllowed : !Choice.FaceDownAllowed))
            {
                App.Hud.Toast(faceUp ? "This card must be played face-down." : "This card must be played face-up.", error: true);
                return;
            }
            if (faceUp && !FaceUpAllowed(_selected))
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
            if (Covered || Choice == null || !Choice.ValidCards.Contains(card.InstanceId))
                return;

            if (Choice.Kind == ChoiceKind.Order)
            {
                AddToOrder(Choice.ValidCards.ToList().IndexOf(card.InstanceId));
                return;
            }
            if (!ChoosingFieldCards)
                return;
            if (Choice.Max == 1)
            {
                Answer(AnswerChoice.WithCards(Viewer, card.InstanceId));
                return;
            }
            if (_picked.Contains(card.InstanceId))
                _picked.Remove(card.InstanceId);
            else if (_picked.Count < Choice.Max)
                _picked.Add(card.InstanceId);
            card.SetHighlight(true, _picked.Contains(card.InstanceId) ? Palette.Accent : Palette.Cyan);
            UpdatePrompt();
        }

        // ---------- Rearranging and swapping ----------
        // Side 0 is the viewer's Protocol row, side 1 the opponent's. Slots are in that player's own order.

        void StartRearrange()
        {
            var question = $"{Choice.Kind}:{Choice.Side}:{Choice.Prompt}";
            if (_rearranging && question == _rearrangeQuestion)
                return;
            EndRearrange();
            Select(-1, true);
            _rearranging = true;
            _rearrangeQuestion = question;
            _swapOnly = Choice.Kind == ChoiceKind.Swap;
            _rearrangeAllowed[0] = Choice.Side != ChoiceSide.Opponent;
            _rearrangeAllowed[1] = Choice.Side != ChoiceSide.Local;
            _rearrangeSlots = new CardView[2][];
            _rearrangeOrder = new int[2][];
            for (var side = 0; side < 2; side++)
            {
                _rearrangeSlots[side] = Enumerable.Range(0, 3).Select(i => App.Mat.ProtocolCard(side == 0, i)).ToArray();
                _rearrangeOrder[side] = new[] { 0, 1, 2 };
                foreach (var card in _rearrangeSlots[side])
                {
                    card.Interactive = _rearrangeAllowed[side];
                    card.SetHighlight(_rearrangeAllowed[side], Palette.CyanDim);
                }
            }
            _pickSide = _pickSlot = -1;
            _table.ShowChoice(null);
        }

        void EndRearrange()
        {
            if (!_rearranging)
                return;
            for (var side = 0; side < 2; side++)
                ResetSide(side);
            EndRearrangeWithoutReset();
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
            if (!_rearrangeAllowed[side])
            {
                App.Hud.Toast(local ? "Rearrange your opponent's Protocols." : "Rearrange your own Protocols.");
                return;
            }
            if (_pickSide != side)
            {
                if (_pickSide >= 0)
                    UnpickProtocol();
                _pickSide = side;
                _pickSlot = slot;
                _rearrangeSlots[side][slot].SetHighlight(true, Palette.Accent);
                return;
            }

            var other = _pickSlot;
            UnpickProtocol();
            if (other == slot)
                return;

            // Only one player's Protocols can be rearranged: changing this side puts the other one back.
            // A swap is always one swap from where they started.
            ResetSide(1 - side);
            if (_swapOnly)
                ResetSide(side);
            var slots = _rearrangeSlots[side];
            var order = _rearrangeOrder[side];
            (slots[other], slots[slot]) = (slots[slot], slots[other]);
            (order[other], order[slot]) = (order[slot], order[other]);
            App.Mat.PreviewProtocolSlot(slots[other], side == 0, other);
            App.Mat.PreviewProtocolSlot(slots[slot], side == 0, slot);

            if (_confirm != null)
                _confirm.Interactable = RearrangedSide() >= 0;
        }

        void UnpickProtocol()
        {
            _rearrangeSlots[_pickSide][_pickSlot].SetHighlight(true, Palette.CyanDim);
            _pickSide = _pickSlot = -1;
        }

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
            Answer(new Compile.Engine.Rearrange(Viewer, target, order));
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

        // A face-up card from a panel (a trash, or revealed cards).
        void ZoomCard(CardInfo info) =>
            App.Hud.Zoom.ShowCard(info, App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                faceUp: true, hiddenFromViewer: false, pinned: true);

        void ShowTrash(bool local)
        {
            if (Covered)
                return;
            var side = local ? _view.Local : _view.Opponent;
            _hud.ShowTrash(local ? "Your trash" : $"{side.Name}'s trash", side.Trash, ZoomCard);
        }

        // ---------- Replay ----------

        void UpdateReplayPrompt()
        {
            var r = _replay;
            var key = $"replay:{r.Position}:{Viewer}:{_promptWide}";
            if (key == _promptKey)
                return;
            _promptKey = key;
            var moves = r.Count - r.First;
            var round = (r.Turn + 1) / 2;
            var text = $"REPLAY - turn {round}, move {r.Position - r.First} of {moves}";
            if (r.LastSeat >= 0)
                text += string.Format(Hint, $"<noparse>{_match.NameOf(r.LastSeat)}: {r.LastStep}</noparse>");
            else
                text += string.Format(Hint, "The game begins. Next > plays the first move (or use the arrow keys).");
            if (r.Over && r.AtEnd)
                text += string.Format(Hint, "The game is over.");
            text += string.Format(Hint, $"Seen from {_match.NameOf(Viewer)}'s seat.");

            // Tall screens put the buttons in one row, so their labels are short.
            var wide = _hud.Wide;
            _hud.ShowPrompt(text,
                (wide ? "< Back" : "<", ButtonStyle.Secondary, () => _replay.Back()),
                (wide ? "Next >" : ">", ButtonStyle.Primary, () => _replay.Forward()),
                (wide ? "<< Turn" : "<<", ButtonStyle.Quiet, () => _replay.PreviousTurn()),
                (wide ? "Turn >>" : ">>", ButtonStyle.Quiet, () => _replay.NextTurn()),
                (wide ? "Switch side" : "Side", ButtonStyle.Quiet, () => _match.HandTo(1 - Viewer)),
                (wide ? "Play from here" : "Play on", ButtonStyle.Secondary, PlayFromHere),
                ("Leave", ButtonStyle.Quiet, () => InMatch.Leave(App)));
            SetEnabled(0, !r.AtStart);
            SetEnabled(1, !r.AtEnd);
            SetEnabled(2, !r.AtStart);
            SetEnabled(3, !r.AtEnd);
            SetEnabled(5, !r.Over);
        }

        void SetEnabled(int button, bool on)
        {
            var skin = _hud.PromptButton(button);
            if (skin != null)
                skin.Interactable = on;
        }

        // A hot-seat game from this move on, with its own game log.
        void PlayFromHere()
        {
            var r = _replay;
            if (r.Over)
                return;
            var game = r.Build(r.Position, record: true);
            GameLog.Describe(game.Record, $"hot-seat, carried on from a loaded game after move {r.Position - r.First}");
            var controller = new MatchController(game, SeatKind.Human, SeatKind.Human, viewer: r.SeatToMove(game), botSeed: App.Match.Start.Seed);
            App.Match = new MatchInfo(App.Match.Start, hotseat: true) { Controller = controller };
            App.Nav.Go(new BoardScreen());
        }

        // ---------- Game log ----------

        void SaveLog()
        {
            try
            {
                App.Hud.Toast(GameLog.Save(_match.LocalGame), seconds: 6f);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                App.Hud.Toast($"Couldn't save the game log: {e.Message}", error: true);
            }
        }

        string _shownWaiting;
        bool _shownFailed;

        void OnFailed(string error)
        {
            App.Hud.Zoom.Hide();
            Render();
        }

        // Over the board while the game can't go on: the engine threw (offer the log for a bug report), the
        // connection was lost (offer to rejoin), or the opponent is reconnecting (a countdown).
        void ShowHalt()
        {
            var local = _match as MatchController;
            string text;
            var buttons = new List<(string, ButtonStyle, UnityEngine.Events.UnityAction)>();
            if (!_match.HasFailed)
                text = _match.Waiting;
            else if (local != null)
            {
                text = "Something went wrong, and the game can't continue. Save the game log to report it?";
                buttons.Add(("Save game log", ButtonStyle.Primary, SaveLog));
                buttons.Add(("Copy game log", ButtonStyle.Secondary, () => App.Hud.Toast(GameLog.Copy(local.Game), seconds: 5f)));
            }
            else
            {
                text = (_match as RemoteMatch)?.Failure ?? "The game can't continue.";
                if (App.Session.CanRejoin)
                    buttons.Add(("Rejoin", ButtonStyle.Primary, () =>
                    {
                        App.Session.Rejoin();
                        App.Hud.Toast("Reconnecting...");
                    }));
                if (_match.LocalGame != null)
                    buttons.Add(("Save game log", ButtonStyle.Secondary, SaveLog));
            }
            buttons.Add(("Leave game", ButtonStyle.Quiet, () => InMatch.Leave(App)));

            var key = $"halt:{text}:{buttons.Count}";
            if (key == _promptKey)
                return;
            _promptKey = key;
            _confirm = null;
            EndRearrange();
            _hud.ShowPrompt(text, buttons.ToArray());
        }

        // ---------- Debug ----------

        void AddDebugActions()
        {
            _hud.AddDebugAction("Card effects…", _hud.ShowEffectsMenu);
            foreach (var effect in DebugEffects.All)
            {
                var key = effect.Key;
                _hud.AddDebugEffect(effect.Group, effect.Label, () =>
                {
                    if (Covered)
                        return;
                    Select(-1, true);
                    Submit(new DebugEffect(Viewer, key));
                });
            }
            // The full log needs the engine: here, or on the host of an online game. An online guest gets it
            // when the game ends (it would show the host's hidden cards before that).
            var game = _match.LocalGame;
            if (game != null)
            {
                _hud.AddDebugAction("Save game log", SaveLog);
                _hud.AddDebugAction("Copy game log", () => App.Hud.Toast(GameLog.Copy(game), seconds: 5f));
                _hud.AddDebugAction("Copy game notation", () => App.Hud.Toast(GameLog.CopyNotation(game)));
            }
            if (_match is MatchController local)
            {
                _hud.AddDebugAction("Auto-play a move", local.AutoPlayViewer);
                _hud.AddDebugAction("Auto-play your turn", () =>
                {
                    for (var i = 0; i < 50 && local.SeatToMove == Viewer && _view.Stage == GameStage.Playing; i++)
                        local.AutoPlayViewer();
                });
            }
            _hud.AddDebugAction("Concede", () => Submit(new Concede(Viewer)));
            if (App.Session is FakeSessionService fake && !App.Match.Hotseat)
                _hud.AddDebugAction("Opponent leaves", fake.SimulateOpponentLeft);
            _hud.AddDebugAction("Leave game", () => InMatch.Leave(App));
        }
    }
}
