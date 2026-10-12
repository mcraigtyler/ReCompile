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
    // Work is a stack of steps (last in, first out), which is also how card effects will interrupt each
    // other in Phase 5. Cards are text-less for now: they have Protocols and values but no effects.
    public sealed partial class Game
    {
        public const int HandSize = 5;
        public const int CompileThreshold = 10;
        public const int FaceDownValue = 2;
        const int QuietTurnsForStalemate = 2;

        readonly CardCatalog _catalog;
        readonly Stack<Action> _steps = new Stack<Action>();
        readonly List<GameEvent> _history = new List<GameEvent>();
        List<GameEvent> _current = new List<GameEvent>();
        bool _awaitingAction;
        bool _actedThisTurn;
        int _quietTurns;

        public GameState State { get; }
        public PendingChoice Pending { get; private set; }
        public IReadOnlyList<GameEvent> History => _history;

        // The active player is free to Play or Refresh.
        public bool AwaitingAction => State.Stage == GameStage.Playing && _awaitingAction && Pending == null;

        Game(CardCatalog catalog, GameState state)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            State = state;
        }

        // ---------- Creating games ----------

        // A new game that starts with the Protocol draft (1-2-2-1) from the given pool.
        public static Game Create(CardCatalog catalog, IEnumerable<string> pool, int firstSeat, long seed,
            string name0 = "Player 1", string name1 = "Player 2")
        {
            var state = new GameState(seed, name0, name1)
            {
                Stage = GameStage.Draft,
                Draft = new Draft(pool, firstSeat),
                FirstSeat = firstSeat,
            };
            return new Game(catalog, state);
        }

        // A new game with the Protocols already chosen (each list left to right from that player's seat).
        public static Game CreateDrafted(CardCatalog catalog, IReadOnlyList<string> protocols0, IReadOnlyList<string> protocols1,
            int firstSeat, long seed, string name0 = "Player 1", string name1 = "Player 2")
        {
            var state = new GameState(seed, name0, name1) { FirstSeat = firstSeat };
            var game = new Game(catalog, state);
            game.StartGame(protocols0, protocols1);
            game.Run();
            return game;
        }

        // Resumes a game from a hand-built state (used by tests). The state's phase says where to pick up:
        // Action waits for the active player; any other phase runs from the start of that phase.
        internal static Game Resume(CardCatalog catalog, GameState state)
        {
            var game = new Game(catalog, state);
            state.Stage = GameStage.Playing;
            game._steps.Push(game.StepFor(state.Phase));
            game.Run();
            return game;
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

            var error = Apply(intent);
            if (error != null)
                return IntentResult.Rejected(error);
            Run();
            return IntentResult.Ok(TakeEvents());
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
                return intent is DraftPick pick ? ApplyDraftPick(pick) : "The draft isn't finished.";

            if (Pending != null)
                return ApplyAnswer(intent);

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
            if (pick.Seat != draft.CurrentSeat)
                return "It's not your pick.";
            if (!draft.Available.Contains(pick.Protocol))
                return $"{pick.Protocol} isn't available.";

            var slot = draft.Pick(pick.Protocol);
            Emit(new DraftPicked(pick.Seat, pick.Protocol, slot));
            if (draft.IsComplete)
                StartGame(draft.PicksFor(0), draft.PicksFor(1));
            return null;
        }

        string ApplyAnswer(Intent intent)
        {
            var choice = Pending;
            if (intent.Seat != choice.Seat)
                return $"Waiting for {State.Players[choice.Seat].Name} to choose.";

            AnswerChoice answer = null;
            Rearrange rearrange = null;
            switch (intent)
            {
                case AnswerChoice a when a.Decline:
                    if (!choice.Optional)
                        return "This choice can't be skipped.";
                    answer = a;
                    break;

                case AnswerChoice a when choice.Kind == ChoiceKind.Cards:
                    var cards = a.Cards;
                    if (cards.Count < choice.Min || cards.Count > choice.Max)
                        return choice.Min == choice.Max ? $"Choose {choice.Min} card{(choice.Min == 1 ? "" : "s")}." : $"Choose {choice.Min} to {choice.Max} cards.";
                    if (cards.Distinct().Count() != cards.Count || cards.Any(c => !choice.ValidCards.Contains(c)))
                        return "That card can't be chosen.";
                    answer = a;
                    break;

                case AnswerChoice a when choice.Kind == ChoiceKind.Line:
                    if (!choice.ValidLines.Contains(a.Line))
                        return "That line can't be chosen.";
                    answer = a;
                    break;

                case Rearrange r when choice.Kind == ChoiceKind.Rearrange:
                    var rearrangeError = CheckRearrange(r);
                    if (rearrangeError != null)
                        return rearrangeError;
                    rearrange = r;
                    break;

                default:
                    return $"Answer the current choice first: {choice.Prompt}";
            }

            Pending = null;
            choice.Resume(answer, rearrange);
            return null;
        }

        string ApplyPlay(PlayCard play)
        {
            var error = CheckPlay(play.Seat, play.CardId, play.Line, play.FaceUp);
            if (error != null)
                return error;

            var player = State.Players[play.Seat];
            var card = player.HandCards.First(c => c.Id == play.CardId);
            var fieldLine = GameState.ToField(play.Seat, play.Line);
            player.HandCards.Remove(card);
            card.FaceUp = play.FaceUp;
            State.Lines[fieldLine].StackCards[play.Seat].Add(card);
            Emit(new CardPlayed(play.Seat, new CardRef(card), fieldLine, play.FaceUp));

            _awaitingAction = false;
            _actedThisTurn = true;
            Push(CacheStep);
            // Phase 5: a face-up card's middle text resolves here, before Check Cache.
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

        // ---------- Rule queries (Phase 5 persistent effects will hook in here) ----------

        public CardSpec Spec(string cardId) => _catalog.Get(cardId);

        public int CardValue(CardInstance card) => card.FaceUp ? card.Spec.Value : FaceDownValue;

        public int LineValue(int seat, int fieldLine) => State.Lines[fieldLine].StackCards[seat].Sum(CardValue);

        // Face-up cards go into a line where either player's Protocol matches theirs.
        public bool CanPlayFaceUp(int seat, CardInstance card, int fieldLine) =>
            State.ProtocolIn(seat, fieldLine).Name == card.Spec.Protocol ||
            State.ProtocolIn(GameState.Other(seat), fieldLine).Name == card.Spec.Protocol;

        public bool CanCompile(int seat, int fieldLine)
        {
            var value = LineValue(seat, fieldLine);
            return value >= CompileThreshold && value > LineValue(GameState.Other(seat), fieldLine);
        }

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
            if (faceUp && !CanPlayFaceUp(seat, card, GameState.ToField(seat, line)))
                return $"{card.Spec.Protocol} cards can only be played face-up into a line with a {card.Spec.Protocol} Protocol.";
            return null;
        }

        // Why this player can't refresh now, or null if they can.
        public string CheckRefresh(int seat)
        {
            if (!AwaitingAction || seat != State.ActiveSeat)
                return "You can only refresh in your Action step.";
            if (State.Players[seat].HandCards.Count >= HandSize)
                return $"You can only refresh with fewer than {HandSize} cards in hand.";
            // House ruling: a Refresh that couldn't draw anything isn't allowed (see rules.md).
            if (!CanDraw(seat))
                return "You have no cards left to draw.";
            return null;
        }

        string CheckRearrange(Rearrange r)
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
