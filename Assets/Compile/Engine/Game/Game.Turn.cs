using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Views;

namespace Compile.Engine
{
    // The turn loop: Start → Control → Compile → Action → Cache → End, as steps on the resolution stack.
    public sealed partial class Game
    {
        // Runs steps until the game needs a player's input or ends.
        void Run()
        {
            while (State.Stage == GameStage.Playing && Pending == null && !_awaitingAction && _steps.Count > 0)
                _steps.Pop()();
        }

        void Push(Action step) => _steps.Push(step);

        // Asks a player something; the engine stops until they answer.
        void Ask(PendingChoice choice) => Pending = choice;

        Action StepFor(TurnPhase phase)
        {
            switch (phase)
            {
                case TurnPhase.Start: return StartStep;
                case TurnPhase.Control: return ControlStep;
                case TurnPhase.Compile: return CompileStep;
                case TurnPhase.Action: return ActionStep;
                case TurnPhase.Cache: return CacheStep;
                default: return EndStep;
            }
        }

        void SetPhase(TurnPhase phase)
        {
            State.Phase = phase;
            Emit(new PhaseChanged(State.ActiveSeat, phase));
        }

        // ---------- Setup ----------

        void StartGame(IReadOnlyList<string> protocols0, IReadOnlyList<string> protocols1)
        {
            var picks = new[] { protocols0, protocols1 };
            for (var seat = 0; seat < GameState.SeatCount; seat++)
            {
                if (picks[seat].Count != PlayerState.ProtocolCount)
                    throw new ArgumentException("Each player needs exactly 3 Protocols.");

                var player = State.Players[seat];
                for (var i = 0; i < PlayerState.ProtocolCount; i++)
                    player.ProtocolSlots[i] = new ProtocolSlot(picks[seat][i]);

                // 6 cards for each of the player's 3 Protocols: an 18-card deck.
                foreach (var protocol in picks[seat])
                    foreach (var spec in _catalog.CardsFor(protocol))
                        player.DeckCards.Add(new CardInstance(NewId(), spec, seat));
                State.Rng.Shuffle(player.DeckCards);
            }

            State.Stage = GameStage.Playing;
            State.Draft = null;
            State.Control = GameState.Neutral;
            State.ActiveSeat = State.FirstSeat;
            State.Turn = 1;
            Emit(new GameStarted(State.FirstSeat));
            for (var seat = 0; seat < GameState.SeatCount; seat++)
                Draw(seat, HandSize);
            Push(StartStep);
        }

        // ---------- Phases ----------

        void StartStep()
        {
            Emit(new TurnStarted(State.ActiveSeat, State.Turn));
            SetPhase(TurnPhase.Start);
            // Phase 5: queue the Start effects visible now (the owner picks their order) and resolve them.
            Push(ControlStep);
        }

        // Leading in at least 2 lines takes Control, from neutral or from the opponent.
        void ControlStep()
        {
            SetPhase(TurnPhase.Control);
            var seat = State.ActiveSeat;
            if (State.Control != seat && LinesLeading(seat) >= 2)
            {
                State.Control = seat;
                Emit(new ControlChanged(seat));
            }
            Push(CompileStep);
        }

        // A line with 10+ value and more than the opponent must be compiled; that replaces the Action.
        void CompileStep()
        {
            SetPhase(TurnPhase.Compile);
            var seat = State.ActiveSeat;
            var lines = Enumerable.Range(0, GameState.LineCount).Where(l => CanCompile(seat, l)).ToList();
            if (lines.Count == 0)
            {
                Push(ActionStep);
                return;
            }

            _actedThisTurn = true;
            Push(CacheStep);
            if (lines.Count == 1)
            {
                Push(() => CompileLine(seat, lines[0]));
                return;
            }

            Ask(new PendingChoice(seat, ChoiceKind.Line, "Choose a line to compile",
                null, lines.Select(l => GameState.ToLocal(seat, l)).ToList(), 1, 1, optional: false,
                (answer, _) => Push(() => CompileLine(seat, GameState.ToField(seat, answer.Line)))));
        }

        // Waits for Play or Refresh, except: with no cards in hand the player must Refresh, and if they
        // can't draw either, the Action step is skipped.
        void ActionStep()
        {
            SetPhase(TurnPhase.Action);
            var seat = State.ActiveSeat;
            if (State.Players[seat].HandCards.Count > 0)
            {
                _awaitingAction = true;
                return;
            }

            Push(CacheStep);
            if (CanDraw(seat))
            {
                _actedThisTurn = true;
                Push(() => RefreshHand(seat, forced: true));
            }
            else
            {
                Emit(new ActionSkipped(seat));
            }
        }

        // More than 5 cards in hand: discard down to 5.
        void CacheStep()
        {
            SetPhase(TurnPhase.Cache);
            var seat = State.ActiveSeat;
            Push(EndStep);

            var hand = State.Players[seat].HandCards;
            var excess = hand.Count - HandSize;
            if (excess <= 0)
                return;

            Ask(new PendingChoice(seat, ChoiceKind.Cards, $"Clear cache: discard {excess} card{(excess == 1 ? "" : "s")}",
                hand.Select(c => c.Id).ToList(), null, excess, excess, optional: false,
                (answer, _) => Discard(seat, answer.Cards)));
        }

        void EndStep()
        {
            SetPhase(TurnPhase.End);
            // Phase 5: queue the End effects visible now and resolve them.
            Push(NextTurnStep);
        }

        void NextTurnStep()
        {
            // House ruling: if neither player could act for two turns in a row, nothing can change, so the
            // game ends. More compiled Protocols wins; otherwise it's a draw.
            _quietTurns = _actedThisTurn ? 0 : _quietTurns + 1;
            if (_quietTurns >= QuietTurnsForStalemate)
            {
                var c0 = State.Players[0].CompiledCount;
                var c1 = State.Players[1].CompiledCount;
                End(c0 == c1 ? -1 : c0 > c1 ? 0 : 1, EndReason.Stalemate);
                return;
            }

            _actedThisTurn = false;
            State.ActiveSeat = GameState.Other(State.ActiveSeat);
            State.Turn++;
            Push(StartStep);
        }

        // ---------- Actions ----------

        // Draw until 5 cards in hand. Holding Control, it first returns to neutral (with an optional rearrange).
        void RefreshHand(int seat, bool forced)
        {
            Emit(new Refreshed(seat, forced));
            UseControl(seat, () => Draw(seat, HandSize - State.Players[seat].HandCards.Count));
        }

        // Compile: Control first, then delete every card in the line at once, then flip the Protocol to
        // its Compiled side, or on a Recompile draw the opponent's top card.
        void CompileLine(int seat, int fieldLine)
        {
            UseControl(seat, () =>
            {
                var line = State.Lines[fieldLine];
                var deleted = new List<CardRef>();
                for (var s = 0; s < GameState.SeatCount; s++)
                {
                    foreach (var card in line.StackCards[s])
                    {
                        card.FaceUp = true;
                        State.Players[card.Owner].TrashCards.Add(card);
                        deleted.Add(new CardRef(card));
                    }
                    line.StackCards[s].Clear();
                }

                // After a rearrange, "your Protocol in that line" may be a different one.
                var protocol = State.ProtocolIn(seat, fieldLine);
                var recompile = protocol.Compiled;
                Emit(new LineCompiled(seat, fieldLine, protocol.Name, recompile));
                Emit(new CardsDeleted(deleted));

                if (recompile)
                {
                    TakeTopCard(seat, GameState.Other(seat));
                    return;
                }

                protocol.Compiled = true;
                Emit(new ProtocolCompiled(seat, protocol.Name));
                if (State.Players[seat].CompiledCount == PlayerState.ProtocolCount)
                    End(seat, EndReason.Compiled);
            });
        }

        // When the Control holder compiles or refreshes, Control returns to neutral and they may rearrange
        // one player's Protocols before the compile or refresh happens.
        void UseControl(int seat, Action then)
        {
            if (State.Control != seat)
            {
                then();
                return;
            }

            State.Control = GameState.Neutral;
            Emit(new ControlChanged(GameState.Neutral));
            Ask(new PendingChoice(seat, ChoiceKind.Rearrange, "You may rearrange one player's Protocols",
                null, null, 0, 1, optional: true,
                (_, rearrange) =>
                {
                    if (rearrange != null)
                        RearrangeProtocols(seat, rearrange.TargetSeat, rearrange.Order);
                    Push(then);
                }));
        }

        void RearrangeProtocols(int seat, int target, IReadOnlyList<int> order)
        {
            var slots = State.Players[target].ProtocolSlots;
            var before = slots.ToArray();
            for (var i = 0; i < slots.Length; i++)
                slots[i] = before[order[i]];
            Emit(new ProtocolsRearranged(seat, target, slots.Select(p => p.Name).ToList()));
        }

        // ---------- Cards moving ----------

        // Draws up to count cards at once, shuffling the trash into a new deck whenever the deck runs out.
        void Draw(int seat, int count)
        {
            var player = State.Players[seat];
            var drawn = new List<CardRef>();
            for (var i = 0; i < count; i++)
            {
                if (player.DeckCards.Count == 0 && !Reshuffle(seat))
                    break;
                var card = player.DeckCards[0];
                player.DeckCards.RemoveAt(0);
                ToHand(player, card);
                drawn.Add(new CardRef(card));
            }
            Emit(new CardsDrawn(seat, drawn));
            // Phase 5: "after you draw" triggers fire once here, for the whole batch.
        }

        // On a Recompile: draw the top card of the opponent's deck (reshuffling their trash if needed).
        // The card belongs to the compiling player from now on.
        void TakeTopCard(int seat, int from)
        {
            var source = State.Players[from];
            if (source.DeckCards.Count == 0 && !Reshuffle(from))
            {
                Emit(new CardTaken(seat, from, null));
                return;
            }

            var card = source.DeckCards[0];
            source.DeckCards.RemoveAt(0);
            card.Owner = seat;
            ToHand(State.Players[seat], card);
            Emit(new CardTaken(seat, from, new CardRef(card)));
        }

        bool Reshuffle(int seat)
        {
            var player = State.Players[seat];
            if (player.TrashCards.Count == 0)
                return false;

            foreach (var card in player.TrashCards)
            {
                card.FaceUp = false;
                Rekey(card);
            }
            player.DeckCards.AddRange(player.TrashCards);
            player.TrashCards.Clear();
            State.Rng.Shuffle(player.DeckCards);
            Emit(new DeckReshuffled(seat, player.DeckCards.Count));
            return true;
        }

        void Discard(int seat, IReadOnlyList<int> cardIds)
        {
            var player = State.Players[seat];
            var discarded = new List<CardRef>();
            foreach (var id in cardIds)
            {
                var card = player.HandCards.First(c => c.Id == id);
                player.HandCards.Remove(card);
                card.FaceUp = true;
                State.Players[card.Owner].TrashCards.Add(card);
                discarded.Add(new CardRef(card));
            }
            Emit(new CardsDiscarded(seat, discarded));
            // Phase 5: "after you discard" / "after your opponent discards" triggers fire here.
        }

        void ToHand(PlayerState player, CardInstance card)
        {
            card.FaceUp = false;
            Rekey(card);
            player.HandCards.Add(card);
        }

        // ---------- Ids ----------

        int NewId()
        {
            int id;
            do
                id = State.Rng.Next(1_000_000_000) + 1;
            while (!State.UsedIds.Add(id));
            return id;
        }

        // A card going somewhere hidden gets a fresh, unpredictable id, so a player who saw it before can't
        // recognize it when it's later played face-down.
        void Rekey(CardInstance card) => card.Id = NewId();

        void End(int winner, EndReason reason)
        {
            State.Stage = GameStage.Over;
            State.Winner = winner;
            State.EndReason = reason;
            Pending = null;
            _awaitingAction = false;
            _steps.Clear();
            Emit(new GameEnded(winner, reason));
        }
    }
}
