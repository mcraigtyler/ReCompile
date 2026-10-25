using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Views;

namespace Compile.Engine
{
    // The rules engine. Players submit intents; the engine checks them, applies them, and runs the game
    // forward until it needs input again: a player's action, or an answer to a PendingChoice. It never
    // blocks or waits, so the same engine serves hot-seat play, the network host, tests, and bots.
    //
    // Work is a stack of steps (last in, first out), which is also how card text interrupts other text.
    // Card text comes from a CardEffects registry (see Game.Effects.cs); with none, cards are text-less.
    public sealed partial class Game
    {
        public const int HandSize = 5;
        public const int CompileThreshold = 10;
        public const int FaceDownValue = 2;
        const int QuietTurnsForStalemate = 2;

        readonly CardCatalog _catalog;
        readonly Func<Game, GameState> _setup;      // builds the starting state (and queues the first steps)
        readonly Stack<Action> _steps = new Stack<Action>();
        readonly List<GameEvent> _history = new List<GameEvent>();
        readonly List<Intent> _log = new List<Intent>();     // every accepted intent, for undo by replay
        List<GameEvent> _current = new List<GameEvent>();
        bool _awaitingAction;
        bool _awaitingEndTurn;
        int _draftConfirmSeat = -1;     // a player who has made their picks for this round and must confirm them
        bool _actedThisTurn;
        int _quietTurns;
        int _undoFloor;             // intents before this index in _log can't be undone
        string _undoBlockedBy;      // why, when the floor was raised by something the player saw

        public GameState State { get; private set; }
        public PendingChoice Pending { get; private set; }
        public IReadOnlyList<GameEvent> History => _history;

        // The game log for bug reports, if this game keeps one (see GameRecord).
        public GameRecord Record { get; }

        // With this on (the default), a turn doesn't pass until the player sends EndTurn, so the whole
        // turn can be undone until then. Rule tests turn it off so turns flow straight on.
        public bool ConfirmEndTurn { get; }

        // The active player is free to Play or Refresh.
        public bool AwaitingAction => State.Stage == GameStage.Playing && _awaitingAction && Pending == null;

        // The active player's turn is over and waits for EndTurn.
        public bool AwaitingEndTurn => State.Stage == GameStage.Playing && _awaitingEndTurn && Pending == null;

        // In the draft: the seat whose picks for this round are made and wait to be confirmed (EndTurn), or -1.
        public int DraftConfirmSeat => State.Stage == GameStage.Draft ? _draftConfirmSeat : -1;

        // In the draft: whose move it is (picking, or confirming their picks).
        public int DraftSeat => DraftConfirmSeat >= 0 ? DraftConfirmSeat : State.Draft?.CurrentSeat ?? -1;

        Game(CardCatalog catalog, Func<Game, GameState> setup, bool confirmEndTurn, CardEffects effects, GameRecord record = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _effects = effects ?? CardEffects.None;
            _setup = setup;
            ConfirmEndTurn = confirmEndTurn;
            Record = record;
            State = setup(this);
            Run();
            Record?.Started(this);
        }

        // ---------- Creating games ----------

        // A new game that starts with the Protocol draft (1-2-2-1) from the given pool. With record on, it
        // keeps a game log (Record).
        public static Game Create(CardCatalog catalog, IEnumerable<string> pool, int firstSeat, long seed,
            string name0 = "Player 1", string name1 = "Player 2", bool confirmEndTurn = true, CardEffects effects = null,
            bool record = false)
        {
            var protocols = pool.ToList();
            return new Game(catalog, _ => new GameState(seed, name0, name1)
            {
                Stage = GameStage.Draft,
                Draft = new Draft(protocols, firstSeat),
                FirstSeat = firstSeat,
            }, confirmEndTurn, effects,
                record ? GameRecord.ForDraft(protocols, firstSeat, seed, name0, name1, confirmEndTurn, effects) : null);
        }

        // A new game with the Protocols already chosen (each list left to right from that player's seat).
        public static Game CreateDrafted(CardCatalog catalog, IReadOnlyList<string> protocols0, IReadOnlyList<string> protocols1,
            int firstSeat, long seed, string name0 = "Player 1", string name1 = "Player 2", bool confirmEndTurn = true,
            CardEffects effects = null, bool record = false)
        {
            return new Game(catalog, game =>
            {
                game.State = new GameState(seed, name0, name1) { FirstSeat = firstSeat };
                game.StartGame(protocols0, protocols1);
                return game.State;
            }, confirmEndTurn, effects,
                record ? GameRecord.ForDrafted(protocols0, protocols1, firstSeat, seed, name0, name1, confirmEndTurn, effects) : null);
        }

        // Resumes a game from a hand-built state (used by tests). The state's phase says where to pick up:
        // Action waits for the active player; any other phase runs from the start of that phase. The
        // factory is called again whenever an undo replays the game, so it must build the same state each time.
        internal static Game Resume(CardCatalog catalog, Func<GameState> buildState, bool confirmEndTurn = false, CardEffects effects = null)
        {
            return new Game(catalog, game =>
            {
                var state = buildState();
                state.Stage = GameStage.Playing;
                game.State = state;
                game._steps.Push(game.StepFor(state.Phase));
                return state;
            }, confirmEndTurn, effects);
        }

        // Events since the game was created or since the last Submit, whichever is later.
        public IReadOnlyList<GameEvent> TakeEvents()
        {
            var events = _current;
            _current = new List<GameEvent>();
            return events;
        }

        // ---------- Intents ----------

        public IntentResult Submit(Intent intent)
        {
            if (intent == null)
                throw new ArgumentNullException(nameof(intent));
            if (Record == null)
                return SubmitUnrecorded(intent);

            var note = Record.Before(this, intent);
            IntentResult result;
            try
            {
                result = SubmitUnrecorded(intent);
            }
            catch (Exception e)
            {
                Record.Crashed(this, note, e);
                throw;
            }
            Record.After(this, note, result);
            return result;
        }

        IntentResult SubmitUnrecorded(Intent intent)
        {
            if (intent is Undo undo)
            {
                var undoError = CheckUndo(undo.Seat);
                if (undoError != null)
                    return IntentResult.Rejected(undoError);
                UndoLast();
                return IntentResult.Ok(TakeEvents());
            }

            var error = Accept(intent);
            return error != null ? IntentResult.Rejected(error) : IntentResult.Ok(TakeEvents());
        }

        // Applies an intent, runs the game on, and records it for undo. Also used when replaying.
        string Accept(Intent intent)
        {
            var firstEvent = _history.Count;
            var error = Apply(intent);
            if (error != null)
                return error;
            Run();
            _log.Add(intent);

            // Nothing before an undo barrier can be undone: ending the turn, or anything that showed a
            // player cards they hadn't seen (taking it back would let them peek and then choose again).
            // The reason is only shown for the player's own moves this turn; a barrier from the turn's start
            // (the opening deal, or a forced Refresh after the last turn ended) just means "nothing to undo".
            var ownMove = !(intent is EndTurn) && !(intent is DraftPick) && intent.Seat == State.ActiveSeat;
            if (intent is EndTurn || intent is Concede || State.Stage == GameStage.Over)
                RaiseUndoFloor(null);
            for (var i = firstEvent; i < _history.Count; i++)
            {
                var blockedBy = UndoBarrier(_history[i]);
                if (blockedBy != null)
                    RaiseUndoFloor(ownMove ? blockedBy : null);
            }
            // The opponent's answers to questions during this turn are theirs, not the active player's to take back.
            if (State.Stage == GameStage.Playing && intent.Seat != State.ActiveSeat && !(intent is Concede))
                RaiseUndoFloor("Your opponent made a choice this turn.");
            return null;
        }

        void RaiseUndoFloor(string reason)
        {
            _undoFloor = _log.Count;
            _undoBlockedBy = reason;
        }

        // Events that showed someone cards they hadn't seen, or used randomness whose result they saw.
        string UndoBarrier(GameEvent e)
        {
            var active = State.ActiveSeat;
            switch (e)
            {
                case CardsDrawn drawn when drawn.Cards.Count > 0:
                    return drawn.Seat == active ? "You drew cards this turn." : "Your opponent drew cards this turn.";
                case DeckReshuffled _:
                    return "A deck was shuffled this turn.";
                case CardTaken taken when taken.Card != null:
                    return taken.Seat == active ? "You drew from your opponent's deck this turn." : "Your opponent drew from your deck this turn.";
                case CardPlayed played when played.FromDeck:
                    return "A card was played from the top of a deck this turn.";
                case CardsRevealed _:
                    return "Cards were revealed this turn.";
                case HandCardsTaken _:
                    return "A card was taken at random this turn.";
                default:
                    return null;
            }
        }

        // ---------- Undo ----------

        // Why this player can't undo now, or null if they can.
        public string CheckUndo(int seat)
        {
            if (State.Stage == GameStage.Over)
                return "There's nothing to undo.";
            if (seat != (State.Stage == GameStage.Draft ? DraftSeat : State.ActiveSeat))
                return "Only the player whose turn it is can undo.";
            if (_log.Count > _undoFloor)
                return null;
            return _undoBlockedBy ?? "There's nothing to undo this turn.";
        }

        // Takes back the last decision by rebuilding the game from the start and replaying every accepted
        // intent but the last. The engine is deterministic (seeded shuffles, ids, and bots' choices come in
        // as intents), so the replay ends in exactly the state before that decision.
        void UndoLast()
        {
            var undone = _log[_log.Count - 1];
            var replay = _log.Take(_log.Count - 1).ToList();

            _steps.Clear();
            _history.Clear();
            _log.Clear();
            Pending = null;
            _awaitingAction = _awaitingEndTurn = _actedThisTurn = false;
            _draftConfirmSeat = -1;
            _quietTurns = 0;
            _undoFloor = 0;
            _undoBlockedBy = null;
            State = _setup(this);
            Run();
            foreach (var intent in replay)
            {
                var error = Accept(intent);
                if (error != null)
                    throw new InvalidOperationException($"Undo replay diverged at {intent.GetType().Name}: {error}");
            }

            _current = new List<GameEvent>();
            Emit(new MoveUndone(undone.Seat, Describe(undone)));
        }

        static string Describe(Intent intent)
        {
            switch (intent)
            {
                case DraftPick pick: return $"drafting {pick.Protocol}";
                case PlayCard _: return "a play";
                case DebugEffect d: return $"a debug effect ({DebugEffects.Label(d.Key)})";
                case Refresh _: return "a refresh";
                case Rearrange _: return "a rearrange";
                case AnswerChoice a when a.Decline: return "a skipped choice";
                case AnswerChoice _: return "a choice";
                default: return "a move";
            }
        }

        string Apply(Intent intent)
        {
            if (intent.Seat != 0 && intent.Seat != 1)
                return "Unknown seat.";
            if (State.Stage == GameStage.Over)
                return "The game is over.";

            if (intent is Concede)
            {
                End(GameState.Other(intent.Seat), EndReason.Conceded);
                return null;
            }

            if (State.Stage == GameStage.Draft)
            {
                switch (intent)
                {
                    case DraftPick pick:
                        return ApplyDraftPick(pick);
                    case EndTurn _:
                        return ConfirmDraftPicks(intent.Seat);
                    default:
                        return "The draft isn't finished.";
                }
            }

            if (Pending != null)
                return ApplyAnswer(intent);

            if (intent is DebugEffect debug)
                return ApplyDebugEffect(debug);

            if (intent is EndTurn)
            {
                if (!_awaitingEndTurn)
                    return "Your turn isn't over yet.";
                if (intent.Seat != State.ActiveSeat)
                    return "It's not your turn.";
                _awaitingEndTurn = false;
                return null;
            }
            if (_awaitingEndTurn)
                return intent.Seat == State.ActiveSeat ? "Your turn is over: end it, or undo." : "It's not your turn.";

            if (!_awaitingAction)
                return "The game isn't waiting for an action.";
            if (intent.Seat != State.ActiveSeat)
                return "It's not your turn.";

            switch (intent)
            {
                case PlayCard play:
                    return ApplyPlay(play);
                case Refresh _:
                    return ApplyRefresh(intent.Seat);
                default:
                    return "Play a card or refresh.";
            }
        }

        string ApplyDraftPick(DraftPick pick)
        {
            var draft = State.Draft;
            if (_draftConfirmSeat >= 0)
                return pick.Seat == _draftConfirmSeat ? "Confirm your picks, or undo one." : $"Waiting for {State.Players[_draftConfirmSeat].Name} to confirm.";
            if (pick.Seat != draft.CurrentSeat)
                return "It's not your pick.";
            if (!draft.Available.Contains(pick.Protocol))
                return $"{pick.Protocol} isn't available.";

            var slot = draft.Pick(pick.Protocol);
            Emit(new DraftPicked(pick.Seat, pick.Protocol, slot));

            // A round of picks is done when the turn passes (or the draft ends). With turn confirmation on,
            // the player confirms their picks first, and can undo them until then.
            if (draft.IsComplete || draft.CurrentSeat != pick.Seat)
            {
                if (ConfirmEndTurn)
                    _draftConfirmSeat = pick.Seat;
                else if (draft.IsComplete)
                    StartGame(draft.PicksFor(0), draft.PicksFor(1));
            }
            return null;
        }

        string ConfirmDraftPicks(int seat)
        {
            if (_draftConfirmSeat < 0)
                return "Finish your picks first.";
            if (seat != _draftConfirmSeat)
                return "It's not your pick.";
            _draftConfirmSeat = -1;
            if (State.Draft.IsComplete)
                StartGame(State.Draft.PicksFor(0), State.Draft.PicksFor(1));
            return null;
        }

        string ApplyAnswer(Intent intent)
        {
            var choice = Pending;
            if (intent.Seat != choice.Seat)
                return $"Waiting for {State.Players[choice.Seat].Name} to choose.";

            var error = CheckAnswer(choice, intent);
            if (error != null)
                return error;
            Pending = null;
            choice.Resume(intent);
            return null;
        }

        static string CheckAnswer(PendingChoice choice, Intent intent)
        {
            switch (intent)
            {
                case AnswerChoice a when a.Decline:
                    return choice.Optional ? null : "This choice can't be skipped.";

                case AnswerChoice a when choice.Kind == ChoiceKind.FieldCards || choice.Kind == ChoiceKind.HandCards:
                    var cards = a.Cards;
                    if (cards.Count < choice.Min || cards.Count > choice.Max)
                        return choice.Min == choice.Max ? $"Choose {choice.Min} card{(choice.Min == 1 ? "" : "s")}." : $"Choose {choice.Min} to {choice.Max} cards.";
                    if (cards.Distinct().Count() != cards.Count || cards.Any(c => !choice.ValidCards.Contains(c)))
                        return "That card can't be chosen.";
                    return null;

                case AnswerChoice a when choice.Kind == ChoiceKind.Line:
                    return choice.ValidLines.Contains(a.Line) ? null : "That line can't be chosen.";

                case AnswerChoice a when choice.Kind == ChoiceKind.Option:
                    return a.Options.Count == 1 && a.Options[0] >= 0 && a.Options[0] < choice.Options.Count ? null : "Choose one of the options.";

                case AnswerChoice a when choice.Kind == ChoiceKind.Order:
                    var order = a.Options;
                    return order.Count == choice.Options.Count && order.Distinct().Count() == order.Count && order.All(i => i >= 0 && i < order.Count)
                        ? null : "Put every effect in order.";

                case Rearrange r when choice.Kind == ChoiceKind.Rearrange || choice.Kind == ChoiceKind.Swap:
                    if (choice.TargetSeat >= 0 && r.TargetSeat != choice.TargetSeat)
                        return choice.TargetSeat == choice.Seat ? "Rearrange your own Protocols." : "Rearrange your opponent's Protocols.";
                    var rearrangeError = CheckRearrange(r);
                    if (rearrangeError != null)
                        return rearrangeError;
                    if (choice.Kind == ChoiceKind.Swap && r.Order.Where((from, to) => from != to).Count() != 2)
                        return "Swap exactly 2 Protocols.";
                    return null;

                case PlayCard p when choice.Kind == ChoiceKind.Play:
                    if (p.FaceUp ? !choice.FaceUpAllowed : !choice.FaceDownAllowed)
                        return p.FaceUp ? "This card must be played face-down." : "This card must be played face-up.";
                    return choice.CheckPlay?.Invoke(p);

                default:
                    return $"Answer the current choice first: {choice.Prompt}";
            }
        }

        // Why a PlayCard wouldn't answer the current Play choice, or null if it would (for bots and the board).
        public string CheckChoicePlay(PlayCard play) =>
            Pending == null || Pending.Kind != ChoiceKind.Play ? "There's no card to play now." : CheckAnswer(Pending, play);

        string ApplyPlay(PlayCard play)
        {
            var error = CheckPlay(play.Seat, play.CardId, play.Line, play.FaceUp);
            if (error != null)
                return error;

            var player = State.Players[play.Seat];
            var card = player.HandCards.First(c => c.Id == play.CardId);
            player.HandCards.Remove(card);

            _awaitingAction = false;
            _actedThisTurn = true;
            Push(CacheStep);
            PlayToField(null, card, play.Seat, GameState.ToField(play.Seat, play.Line), play.FaceUp, fromDeck: false);
            return null;
        }

        string ApplyRefresh(int seat)
        {
            var error = CheckRefresh(seat);
            if (error != null)
                return error;

            _awaitingAction = false;
            _actedThisTurn = true;
            Push(CacheStep);
            Push(() => RefreshHand(seat, forced: false));
            return null;
        }

        // Runs a debug effect as if card text said it, then goes back to waiting where the player was.
        string ApplyDebugEffect(DebugEffect debug)
        {
            if (!_awaitingAction && !_awaitingEndTurn)
                return "Debug effects can only be used in your Action step or after it.";
            if (debug.Seat != State.ActiveSeat)
                return "It's not your turn.";
            var text = DebugEffects.Find(debug.Key);
            if (text == null)
                return $"There's no debug effect \"{debug.Key}\".";

            if (_awaitingAction)
                Push(ActionStep);       // looks again: the effect may have emptied the hand
            else
                Push(() => _awaitingEndTurn = true);
            _awaitingAction = _awaitingEndTurn = false;
            Emit(new DebugEffectUsed(debug.Seat, DebugEffects.Label(debug.Key)));
            var effect = new Effect(this, debug.Seat, null, Slot.Middle);
            RunText(effect, text(effect));
            return null;
        }

        // ---------- Rule queries (the ones card text can change are in Game.Effects.cs) ----------

        public CardSpec Spec(string cardId) => _catalog.Get(cardId);

        public int LinesLeading(int seat) =>
            Enumerable.Range(0, GameState.LineCount).Count(l => LineValue(seat, l) > LineValue(GameState.Other(seat), l));

        // A player can draw if their deck or trash has cards (the trash is shuffled into a new deck).
        public bool CanDraw(int seat) => State.Players[seat].DeckCards.Count + State.Players[seat].TrashCards.Count > 0;

        // Why this play isn't allowed, or null if it is. Line is from the player's own seat.
        public string CheckPlay(int seat, int cardId, int line, bool faceUp)
        {
            if (!AwaitingAction || seat != State.ActiveSeat)
                return "You can only play a card in your Action step.";
            var card = State.Players[seat].HandCards.FirstOrDefault(c => c.Id == cardId);
            if (card == null)
                return "That card isn't in your hand.";
            if (line < 0 || line >= GameState.LineCount)
                return "There's no such line.";
            return CheckPlacement(seat, card, GameState.ToField(seat, line), faceUp);
        }

        // Why this player can't refresh now, or null if they can.
        public string CheckRefresh(int seat)
        {
            if (!AwaitingAction || seat != State.ActiveSeat)
                return "You can only refresh in your Action step.";
            return RefreshBlocked(seat);
        }

        // Why this player couldn't refresh as their action, whenever it is.
        string RefreshBlocked(int seat)
        {
            if (State.Players[seat].HandCards.Count >= HandSize)
                return $"You can only refresh with fewer than {HandSize} cards in hand.";
            // House ruling: a Refresh that couldn't draw anything isn't allowed (see rules.md).
            if (!CanDraw(seat))
                return "You have no cards left to draw.";
            return null;
        }

        static string CheckRearrange(Rearrange r)
        {
            if (r.TargetSeat != 0 && r.TargetSeat != 1)
                return "Choose whose Protocols to rearrange.";
            var order = r.Order;
            if (order.Count != PlayerState.ProtocolCount || order.Distinct().Count() != order.Count || order.Any(i => i < 0 || i >= PlayerState.ProtocolCount))
                return "That isn't an arrangement of 3 Protocols.";
            if (order.Select((from, to) => from == to).All(same => same))
                return "The new arrangement must be different.";
            return null;
        }

        // ---------- Events ----------

        void Emit(GameEvent e)
        {
            _history.Add(e);
            _current.Add(e);
        }
    }
}
