using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // Card text: running it on the step stack, triggers, cards entering the field, the building blocks, and
    // the rule queries persistent text can change. See CardEffect for how a card's text is written.
    //
    // Rulings the framework follows (rules.md "Resolving Card Text", "Committed Cards", "Shifting"):
    // - Text resolves last in, first out: anything it sets off is pushed above it and finishes first.
    // - If the card is covered, flipped, or leaves the field while its text resolves, the rest is lost.
    // - Middle text runs when a card lands face-up, is flipped face-up while uncovered, or becomes
    //   uncovered face-up. A covered card flipped face-up doesn't run it (covered cards have no active text).
    // - A committed card (being played or shifted) is in no stack, so nothing can pick it, and it always
    //   lands. "When this card would be covered" text on the card it lands on resolves first.
    // - Several texts set off at once resolve in the order the active player picks (Start and End: the owner).
    // - "When this card would be ..." text doesn't set itself off again while it's resolving.
    public sealed partial class Game
    {
        const int MaxStepsPerRun = 100_000;

        readonly CardEffects _effects;

        CardEffect EffectOf(CardInstance card) => _effects.IsEmpty ? null : _effects.For(card.Spec.Id);

        // ---------- Where cards are ----------

        bool Locate(CardInstance card, out int seat, out int fieldLine, out int index)
        {
            for (fieldLine = 0; fieldLine < GameState.LineCount; fieldLine++)
                for (seat = 0; seat < GameState.SeatCount; seat++)
                {
                    index = State.Lines[fieldLine].StackCards[seat].IndexOf(card);
                    if (index >= 0)
                        return true;
                }
            seat = fieldLine = index = -1;
            return false;
        }

        public bool IsInField(CardInstance card) => Locate(card, out _, out _, out _);

        public int FieldLineOf(CardInstance card) => Locate(card, out _, out var line, out _) ? line : -1;

        public bool IsUncovered(CardInstance card) =>
            Locate(card, out var seat, out var line, out var index) && index == State.Lines[line].StackCards[seat].Count - 1;

        IEnumerable<CardInstance> FieldCards() =>
            State.Lines.SelectMany(l => l.StackCards[0].Concat(l.StackCards[1]));

        // Field cards an effect may pick. "all" includes covered cards whatever the target says.
        internal List<CardInstance> Candidates(Effect e, Target target, bool all = false)
        {
            var cards = new List<CardInstance>();
            for (var line = 0; line < GameState.LineCount; line++)
                for (var seat = 0; seat < GameState.SeatCount; seat++)
                {
                    var stack = State.Lines[line].StackCards[seat];
                    for (var i = 0; i < stack.Count; i++)
                        if (target.Matches(e, stack[i], seat, line, covered: i < stack.Count - 1, all))
                            cards.Add(stack[i]);
                }
            return cards;
        }

        // A card's name as this seat may know it.
        internal string CardName(CardInstance card, int seat) =>
            card.FaceUp || card.Owner == seat || State.Revealed.Contains(card) ? card.Spec.ToString() : "a face-down card";

        // ---------- Running text ----------

        // Pushes text to resolve: one building block at a time, each finishing (with everything it sets off)
        // before the next. Stops early if the card's text is lost.
        void RunText(Effect e, IEnumerable<Op> text)
        {
            if (text == null)
                return;
            IEnumerator<Op> sentences = null;
            void Advance()
            {
                if (!e.StillActive)
                {
                    FinishText(e);
                    return;
                }
                // Text says what set it off (the log and the board explain all but plain plays), and later
                // that it's done (TextEnded), so what it did can be told apart from what it set off.
                if (sentences == null)
                {
                    e.Started = true;
                    if (e.Source != null)
                        Emit(new CardTextRuns(e.Owner, new CardRef(e.Source), e.Reason ?? TextReason.Played));
                }
                sentences = sentences ?? text.GetEnumerator();
                if (!sentences.MoveNext())
                {
                    FinishText(e);
                    return;
                }
                Push(Advance);
                sentences.Current?.Run();
            }
            Push(Advance);
        }

        // Text is done (or lost): anything it revealed is hidden again. A revealed hand card gets a new id,
        // since its owner could have shuffled their hand.
        void FinishText(Effect e)
        {
            if (e.Revealed.Count > 0)
            {
                foreach (var card in e.Revealed)
                {
                    State.Revealed.Remove(card);
                    if (State.Players.Any(p => p.HandCards.Contains(card)))
                        Rekey(card);
                }
                Emit(new RevealEnded(e.Owner));
                e.Revealed.Clear();
            }
            if (e.Started)
                Emit(new TextEnded(e.Owner, e.Source == null ? null : new CardRef(e.Source)));
        }

        sealed class PendingText
        {
            public Effect Effect;
            public IEnumerable<Op> Text;
        }

        // Several texts set off at once: the chooser picks their order (if it matters), and then they resolve
        // one after another. Pushed as a step, so the question is asked only when the stack gets there.
        void PushTogether(List<PendingText> texts, int chooser)
        {
            if (texts.Count == 0)
                return;
            Push(() =>
            {
                var live = texts.Where(t => t.Effect.StillActive).ToList();
                if (live.Count <= 1)
                {
                    foreach (var t in live)
                        RunText(t.Effect, t.Text);
                    return;
                }
                Ask(new PendingChoice(chooser, ChoiceKind.Order, "Choose the order these effects resolve in", answer =>
                {
                    var order = ((AnswerChoice)answer).Options;
                    for (var i = order.Count - 1; i >= 0; i--)
                        RunText(live[order[i]].Effect, live[order[i]].Text);
                })
                {
                    Options = live.Select(t => t.Effect.Name).ToList(),
                    ValidCards = live.Select(t => t.Effect.Source.Id).ToList(),
                    Min = live.Count,
                    Max = live.Count,
                });
            });
        }

        // Middle text of cards that just landed face-up (no reason), were flipped face-up, or were uncovered.
        void TriggerMiddle(IEnumerable<CardInstance> cards, TextReason? reason)
        {
            if (_effects.IsEmpty)
                return;
            var texts = new List<PendingText>();
            foreach (var card in cards)
            {
                var fx = EffectOf(card);
                if (fx == null)
                    continue;
                var e = new Effect(this, card.Owner, card, Slot.Middle) { Reason = reason };
                var text = fx.Middle(e);
                if (text != null && e.StillActive)
                    texts.Add(new PendingText { Effect = e, Text = text });
            }
            PushTogether(texts, State.ActiveSeat);
        }

        void TriggerMiddle(CardInstance card, TextReason? reason) => TriggerMiddle(new[] { card }, reason);

        // Text on these cards that reacts to the trigger, if the card is in play for its slot right now.
        List<PendingText> Collect(Trigger trigger, int seat, IEnumerable<CardInstance> cards)
        {
            var texts = new List<PendingText>();
            if (_effects.IsEmpty)
                return texts;
            foreach (var card in cards)
            {
                var fx = EffectOf(card);
                if (fx == null || !card.FaceUp)
                    continue;
                var e = new Effect(this, card.Owner, card, Slot.Top);
                var triggered = fx.OnTrigger(trigger, e, seat);
                if (triggered == null)
                    continue;
                e.Slot = triggered.Slot;
                e.Reason = trigger == Trigger.Start ? TextReason.Start : trigger == Trigger.End ? TextReason.End : TextReason.Response;
                if (e.StillActive)
                    texts.Add(new PendingText { Effect = e, Text = triggered.Text });
            }
            return texts;
        }

        // Something happened to a seat (they drew, discarded, ...): every card in play that reacts.
        void FireTrigger(Trigger trigger, int seat) => PushTogether(Collect(trigger, seat, FieldCards().ToList()), State.ActiveSeat);

        // Something is about to happen to this card ("When this card would be ..."). That text can't set
        // itself off again while it's resolving (say, by flipping its own card), or it would never end.
        void FireCardTrigger(Trigger trigger, CardInstance card)
        {
            var key = (card, trigger);
            if (State.Forewarned.Contains(key))
                return;
            var texts = Collect(trigger, card.Owner, new[] { card });
            if (texts.Count == 0)
                return;
            State.Forewarned.Add(key);
            Push(() => State.Forewarned.Remove(key));
            PushTogether(texts, State.ActiveSeat);
        }

        // Start and End text on the active player's cards, as the field is when the phase begins. Text that
        // becomes visible during the phase doesn't run; text lost before its turn comes doesn't either.
        void PushPhaseTriggers(Trigger trigger)
        {
            var seat = State.ActiveSeat;
            PushTogether(Collect(trigger, seat, FieldCards().Where(c => c.Owner == seat).ToList()), seat);
        }

        // ---------- Rule queries ----------

        // Persistent text in play: face-up cards' rules (bottom rules only while uncovered).
        IEnumerable<(CardEffect fx, Effect e)> Rules()
        {
            if (_effects.IsEmpty)
                yield break;
            foreach (var line in State.Lines)
                for (var seat = 0; seat < GameState.SeatCount; seat++)
                {
                    var stack = line.StackCards[seat];
                    for (var i = 0; i < stack.Count; i++)
                    {
                        var card = stack[i];
                        var fx = card.FaceUp ? EffectOf(card) : null;
                        if (fx == null || fx.RuleSlot == Slot.Bottom && i < stack.Count - 1)
                            continue;
                        yield return (fx, new Effect(this, card.Owner, card, fx.RuleSlot));
                    }
                }
        }

        public int CardValue(CardInstance card)
        {
            var value = card.FaceUp ? card.Spec.Value : FaceDownValue;
            foreach (var (fx, e) in Rules())
                value = fx.CardValue(e, card, value);
            return value;
        }

        public int LineValue(int seat, int fieldLine)
        {
            var total = State.Lines[fieldLine].StackCards[seat].Sum(CardValue);
            foreach (var (fx, e) in Rules())
                total = fx.LineValue(e, seat, fieldLine, total);
            return total;
        }

        // Face-up cards go into a line where either player's Protocol matches theirs, unless text allows otherwise.
        public bool CanPlayFaceUp(int seat, CardInstance card, int fieldLine) =>
            State.ProtocolIn(seat, fieldLine).Name == card.Spec.Protocol ||
            State.ProtocolIn(GameState.Other(seat), fieldLine).Name == card.Spec.Protocol ||
            Rules().Any(r => r.fx.AllowsFaceUp(r.e, seat, card, fieldLine));

        // Why this card can't go into this line this way (by any play, from an action or an effect), or null.
        public string CheckPlacement(int seat, CardInstance card, int fieldLine, bool faceUp)
        {
            if (faceUp && !CanPlayFaceUp(seat, card, fieldLine))
                return $"{card.Spec.Protocol} cards can only be played face-up into a line with a {card.Spec.Protocol} Protocol.";
            foreach (var (fx, e) in Rules())
            {
                var reason = fx.ForbidsPlay(e, seat, card, fieldLine, faceUp);
                if (reason != null)
                    return reason;
            }
            return null;
        }

        bool HasLegalPlay(int seat) =>
            State.Players[seat].HandCards.Any(card => Enumerable.Range(0, GameState.LineCount)
                .Any(line => CheckPlacement(seat, card, line, false) == null || CheckPlacement(seat, card, line, true) == null));

        public bool CanCompile(int seat, int fieldLine)
        {
            var value = LineValue(seat, fieldLine);
            return value >= CompileThreshold && value > LineValue(GameState.Other(seat), fieldLine) &&
                   !Rules().Any(r => r.fx.ForbidsCompile(r.e, seat, fieldLine));
        }

        public bool CanFlip(CardInstance card) => !Rules().Any(r => r.fx.ForbidsFlip(r.e, card));

        bool SkipsCheckCache(int seat) => Rules().Any(r => r.fx.SkipsCheckCache(r.e, seat));

        internal bool MiddleIgnored(CardInstance card) => Rules().Any(r => r.fx.IgnoresMiddle(r.e, card));

        // ---------- Questions ----------

        string Prompt(Effect e, string verb, Target target, int count, bool optional, int chooser, bool all = false) =>
            $"{(optional ? "You may " + verb.ToLowerInvariant() : verb)} {target.Describe(e, count, chooser, all)}";

        // Asks for min..max of these cards and passes the picks on. With no real choice (everything must be
        // picked) it doesn't ask; with nothing to pick, nothing happens.
        void PickCards(Effect e, int chooser, ChoiceKind kind, string verb, string prompt, List<CardInstance> valid, int min, int max,
            bool optional, Action<List<CardInstance>> then)
        {
            if (valid.Count == 0)
                return;
            max = Math.Min(max, valid.Count);
            min = Math.Min(min, max);
            if (!optional && min == valid.Count)
            {
                Emit(new ChoiceMadeFor(chooser, e.Source == null ? null : new CardRef(e.Source), verb, valid.Select(c => new CardRef(c)).ToList(),
                    inHand: kind == ChoiceKind.HandCards));
                then(valid);
                return;
            }
            Ask(new PendingChoice(chooser, kind, prompt, answer =>
            {
                var a = (AnswerChoice)answer;
                if (!a.Decline)
                    then(a.Cards.Select(id => valid.First(c => c.Id == id)).ToList());
            })
            {
                ValidCards = valid.Select(c => c.Id).ToList(),
                Min = min,
                Max = max,
                Optional = optional,
                Verb = verb,
                Source = e.Source,
            });
        }

        // ---------- Building blocks: draw, discard, refresh ----------

        internal void DrawOp(Effect e, Op op, int seat, int count)
        {
            foreach (var card in Draw(seat, count))
                op.Add(card);
        }

        internal void DrawFromOpponentOp(Op op, int seat)
        {
            var card = TakeTopCard(seat, GameState.Other(seat));
            if (card != null)
                op.Add(card);
        }

        internal void RefreshOp(Op op, int seat)
        {
            op.MarkDone();
            RefreshHand(seat, forced: false);
        }

        internal void DiscardOp(Effect e, Op op, int seat, int min, int max, bool optional)
        {
            var hand = State.Players[seat].HandCards.ToList();
            var prompt = max == int.MaxValue ? $"Discard {min} or more cards"
                : optional ? $"You may discard {Plural(min, "card")}" : $"Discard {Plural(min, "card")}";
            PickCards(e, seat, ChoiceKind.HandCards, "Discard", prompt, hand, min, max, optional, cards =>
            {
                Discard(seat, cards.Select(c => c.Id).ToList());
                foreach (var card in cards)
                    op.Add(card);
            });
        }

        // ---------- Building blocks: flip ----------

        internal void FlipOp(Effect e, Op op, Target target, int count, bool optional, int chooser)
        {
            var valid = Candidates(e, target).Where(CanFlip).ToList();
            PickCards(e, chooser, ChoiceKind.FieldCards, "Flip", Prompt(e, "Flip", target, count, optional, chooser), valid, count, count, optional,
                cards => FlipCards(e, op, cards));
        }

        // Flips cards one at a time. "When this card would be flipped" text resolves before each flip.
        internal void FlipCards(Effect e, Op op, List<CardInstance> cards)
        {
            for (var i = cards.Count - 1; i >= 0; i--)
            {
                var card = cards[i];
                var changes = card.Changes;
                Push(() =>
                {
                    // Gone, or already flipped, by the time it would be flipped.
                    if (card.Changes != changes || !IsInField(card) || !CanFlip(card))
                        return;
                    Turn(card);
                    op.Add(card);
                    Emit(new CardsFlipped(e.Owner, new[] { new CardRef(card) }));
                    if (card.FaceUp && IsUncovered(card))
                        TriggerMiddle(card, TextReason.FlippedUp);
                });
                FireCardTrigger(Trigger.WouldBeFlipped, card);
            }
        }

        // "Flip all": at once. Middle text of the cards this turns face-up (and uncovered) resolves after.
        internal void FlipAllOp(Effect e, Op op, Target target)
        {
            var cards = Candidates(e, target, all: true).Where(CanFlip).ToList();
            if (cards.Count == 0)
                return;
            var changes = cards.Select(c => c.Changes).ToList();
            Push(() =>
            {
                var flipped = cards.Where((c, i) => c.Changes == changes[i] && IsInField(c) && CanFlip(c)).ToList();
                if (flipped.Count == 0)
                    return;
                foreach (var card in flipped)
                {
                    Turn(card);
                    op.Add(card);
                }
                Emit(new CardsFlipped(e.Owner, flipped.Select(c => new CardRef(c)).ToList()));
                TriggerMiddle(flipped.Where(c => c.FaceUp && IsUncovered(c)), TextReason.FlippedUp);
            });
            foreach (var card in cards)
                FireCardTrigger(Trigger.WouldBeFlipped, card);
        }

        static void Turn(CardInstance card)
        {
            card.FaceUp = !card.FaceUp;
            card.Changes++;
        }

        // ---------- Building blocks: shift ----------

        internal void ShiftOp(Effect e, Op op, Target target, int toLine, bool optional, int chooser)
        {
            var valid = Candidates(e, target).Where(c => toLine < 0 || FieldLineOf(c) != toLine).ToList();
            PickCards(e, chooser, ChoiceKind.FieldCards, "Shift", Prompt(e, "Shift", target, 1, optional, chooser), valid, 1, 1, optional,
                cards => ShiftCards(e, op, cards, toLine, chooser));
        }

        // "Shift 1 card either to or from this line": a card in the line goes to another line (the chooser
        // picks which); a card elsewhere comes to this line.
        internal void ShiftToOrFromOp(Effect e, Op op, int fieldLine, int chooser)
        {
            PickCards(e, chooser, ChoiceKind.FieldCards, "Shift", "Shift 1 card to or from this line", Candidates(e, Target.Any), 1, 1, optional: false,
                cards => ShiftCards(e, op, cards, FieldLineOf(cards[0]) == fieldLine ? -1 : fieldLine, chooser));
        }

        // Shifts cards to another line on their own side. Without a fixed destination the chooser picks one of
        // the lines none of them is in (all of them move to the same line).
        internal void ShiftCards(Effect e, Op op, List<CardInstance> cards, int toLine, int chooser)
        {
            if (cards.Count == 0)
                return;
            if (toLine >= 0)
            {
                MoveShifted(e, op, cards.Where(c => FieldLineOf(c) != toLine).ToList(), toLine);
                return;
            }

            var lines = Enumerable.Range(0, GameState.LineCount).Where(l => cards.All(c => FieldLineOf(c) != l)).ToList();
            if (lines.Count == 0)
                return;
            if (lines.Count == 1)
            {
                MoveShifted(e, op, cards, lines[0]);
                return;
            }
            var what = cards.Count == 1 ? CardName(cards[0], chooser) : Plural(cards.Count, "card");
            Ask(new PendingChoice(chooser, ChoiceKind.Line, $"Shift {what} to which line?", answer =>
                MoveShifted(e, op, cards, GameState.ToField(chooser, ((AnswerChoice)answer).Line)))
            {
                ValidLines = lines.Select(l => GameState.ToLocal(chooser, l)).ToList(),
                Purpose = LinePurpose.ShiftTo,
                Source = e.Source,
                Subject = cards.Count == 1 ? cards[0] : null,
            });
        }

        // The shifting cards leave their stacks together and are committed; anything this uncovers resolves
        // right away; then they land on top of the destination stack one at a time.
        void MoveShifted(Effect e, Op op, List<CardInstance> cards, int toLine)
        {
            if (cards.Count == 0)
                return;
            var moves = cards.Select(c =>
            {
                Locate(c, out var seat, out var line, out var index);
                return (card: c, seat, from: line, wasCovered: index < State.Lines[line].StackCards[seat].Count - 1);
            }).ToList();

            var uncovered = TakeFromField(cards, leaving: false);
            State.Committed.AddRange(cards);
            for (var i = moves.Count - 1; i >= 0; i--)
            {
                var move = moves[i];
                PushLanding(move.card, move.seat, toLine, move.card.FaceUp, () =>
                {
                    op.Add(move.card);
                    Emit(new CardShifted(e.Owner, new CardRef(move.card), move.from, toLine));
                    // A covered card that lands on top is now uncovered.
                    if (move.card.FaceUp && move.wasCovered)
                        TriggerMiddle(move.card, TextReason.Uncovered);
                });
            }
            TriggerMiddle(uncovered, TextReason.Uncovered);
        }

        // ---------- Building blocks: delete and return ----------

        internal void DeleteOp(Effect e, Op op, Target target, int count, bool optional, int chooser)
        {
            PickCards(e, chooser, ChoiceKind.FieldCards, "Delete", Prompt(e, "Delete", target, count, optional, chooser), Candidates(e, target), count, count, optional,
                cards => DeleteCards(e, op, cards));
        }

        // To their owners' trashes, face-up, all at once.
        internal void DeleteCards(Effect e, Op op, List<CardInstance> cards)
        {
            if (cards.Count == 0)
                return;
            var uncovered = TakeFromField(cards, leaving: true);
            foreach (var card in cards)
            {
                card.FaceUp = true;
                State.Players[card.Owner].TrashCards.Add(card);
                op.Add(card);
            }
            Emit(new CardsDeleted(cards.Select(c => new CardRef(c)).ToList(), e.Owner));
            FireTrigger(Trigger.AfterDelete, e.Owner);
            TriggerMiddle(uncovered, TextReason.Uncovered);
        }

        internal void ReturnOp(Effect e, Op op, Target target, int count, bool optional, int chooser)
        {
            PickCards(e, chooser, ChoiceKind.FieldCards, "Return", Prompt(e, "Return", target, count, optional, chooser), Candidates(e, target), count, count, optional,
                cards => ReturnCards(e, op, cards));
        }

        // To their owners' hands, all at once.
        internal void ReturnCards(Effect e, Op op, List<CardInstance> cards)
        {
            if (cards.Count == 0)
                return;
            var refs = cards.Select(c => new CardRef(c)).ToList();
            var uncovered = TakeFromField(cards, leaving: true);
            foreach (var card in cards)
            {
                ToHand(State.Players[card.Owner], card);
                op.Add(card);
            }
            Emit(new CardsReturned(e.Owner, refs));
            TriggerMiddle(uncovered, TextReason.Uncovered);
        }

        // Takes cards out of their stacks. Returns the face-up cards this leaves uncovered (their middle text
        // should run). Cards leaving the field lose any text they had resolving; shifted cards don't.
        List<CardInstance> TakeFromField(List<CardInstance> cards, bool leaving)
        {
            var before = new CardInstance[GameState.LineCount, GameState.SeatCount];
            for (var line = 0; line < GameState.LineCount; line++)
                for (var seat = 0; seat < GameState.SeatCount; seat++)
                    before[line, seat] = State.Lines[line].Uncovered(seat);

            foreach (var card in cards)
            {
                Locate(card, out var seat, out var line, out _);
                State.Lines[line].StackCards[seat].Remove(card);
                if (leaving)
                    card.Changes++;
            }

            var uncovered = new List<CardInstance>();
            for (var line = 0; line < GameState.LineCount; line++)
                for (var seat = 0; seat < GameState.SeatCount; seat++)
                {
                    var top = State.Lines[line].Uncovered(seat);
                    if (top != null && top != before[line, seat] && top.FaceUp)
                        uncovered.Add(top);
                }
            return uncovered;
        }

        // ---------- Cards entering the field ----------

        // A committed card lands in its line and orientation once "when this card would be covered" text on
        // the card it will cover has resolved. landed runs as it arrives.
        void PushLanding(CardInstance card, int seat, int fieldLine, bool faceUp, Action landed)
        {
            Push(() =>
            {
                Push(() =>
                {
                    State.Committed.Remove(card);
                    var stack = State.Lines[fieldLine].StackCards[seat];
                    if (stack.Count > 0)
                        stack[stack.Count - 1].Covers++;
                    card.FaceUp = faceUp;
                    stack.Add(card);
                    landed();
                });
                var top = State.Lines[fieldLine].Uncovered(seat);
                if (top != null)
                    FireCardTrigger(Trigger.WouldBeCovered, top);
            });
        }

        // Plays a card (already out of its hand or deck) into the field: committed, then lands, then a
        // face-up card's middle text resolves.
        void PlayToField(Op op, CardInstance card, int seat, int fieldLine, bool faceUp, bool fromDeck)
        {
            State.Committed.Add(card);
            PushLanding(card, seat, fieldLine, faceUp, () =>
            {
                op?.Add(card);
                Emit(new CardPlayed(seat, new CardRef(card), fieldLine, faceUp, fromDeck));
                if (faceUp)
                    TriggerMiddle(card, null);
            });
        }

        internal void PlayFromHandOp(Effect e, Op op, int seat, bool faceDownOnly, Func<int, bool> fieldLineAllowed)
        {
            string Check(CardInstance card, int fieldLine, bool faceUp) =>
                faceUp && faceDownOnly ? "This card must be played face-down."
                : !fieldLineAllowed(fieldLine) ? "You can't play into that line."
                : CheckPlacement(seat, card, fieldLine, faceUp);

            var hand = State.Players[seat].HandCards;
            var lines = Enumerable.Range(0, GameState.LineCount).Where(fieldLineAllowed).ToList();
            var playable = hand.Where(c => lines.Any(l => Check(c, l, false) == null || Check(c, l, true) == null)).ToList();
            if (playable.Count == 0)
                return;

            Ask(new PendingChoice(seat, ChoiceKind.Play, faceDownOnly ? "Play 1 card face-down" : "Play 1 card", answer =>
            {
                var play = (PlayCard)answer;
                var card = hand.First(c => c.Id == play.CardId);
                hand.Remove(card);
                PlayToField(op, card, seat, GameState.ToField(seat, play.Line), play.FaceUp, fromDeck: false);
            })
            {
                ValidCards = playable.Select(c => c.Id).ToList(),
                ValidLines = lines.Select(l => GameState.ToLocal(seat, l)).ToList(),
                FaceUpAllowed = !faceDownOnly,
                FaceDownAllowed = true,
                Source = e.Source,
                CheckPlay = play =>
                {
                    var card = playable.FirstOrDefault(c => c.Id == play.CardId);
                    if (card == null)
                        return "That card can't be played now.";
                    if (play.Line < 0 || play.Line >= GameState.LineCount)
                        return "There's no such line.";
                    return Check(card, GameState.ToField(seat, play.Line), play.FaceUp);
                },
            });
        }

        // Does nothing if the deck is empty (no reshuffle).
        internal void PlayTopOfDeckOp(Op op, int seat, int fieldLine)
        {
            var deck = State.Players[seat].DeckCards;
            if (deck.Count == 0)
                return;
            var card = deck[0];
            deck.RemoveAt(0);
            PlayToField(op, card, seat, fieldLine, faceUp: false, fromDeck: true);
        }

        // "Play the top card of your deck face-down under this card": it goes straight into the stack just
        // below that card, which stays on top. Nothing is uncovered or covered, so no text is set off.
        internal void PlayTopOfDeckUnderOp(Op op, int seat, CardInstance above)
        {
            var deck = State.Players[seat].DeckCards;
            if (deck.Count == 0 || !Locate(above, out var stackSeat, out var fieldLine, out var index) || stackSeat != seat)
                return;
            var card = deck[0];
            deck.RemoveAt(0);
            card.FaceUp = false;
            var stack = State.Lines[fieldLine].StackCards[seat];
            if (index > 0)
                stack[index - 1].Covers++;
            stack.Insert(index, card);
            op.Add(card);
            Emit(new CardPlayed(seat, new CardRef(card), fieldLine, faceUp: false, fromDeck: true));
        }

        // ---------- Building blocks: reveal ----------

        internal void RevealFieldOp(Effect e, Op op, Target target, int chooser)
        {
            PickCards(e, chooser, ChoiceKind.FieldCards, "Reveal", $"Reveal {target.Describe(e, 1, chooser, false)}", Candidates(e, target), 1, 1, optional: false,
                cards => RevealCards(e, op, cards[0].Owner, cards));
        }

        internal void RevealFromHandOp(Effect e, Op op, int seat, int count)
        {
            PickCards(e, seat, ChoiceKind.HandCards, "Reveal", $"Reveal {Plural(count, "card")} from your hand", State.Players[seat].HandCards.ToList(), count, count,
                optional: false, cards => RevealCards(e, op, seat, cards));
        }

        // Shown to both players until the text finishes.
        internal void RevealCards(Effect e, Op op, int owner, List<CardInstance> cards)
        {
            if (cards.Count == 0)
                return;
            foreach (var card in cards)
            {
                State.Revealed.Add(card);
                e.Revealed.Add(card);
                op.Add(card);
            }
            Emit(new CardsRevealed(e.Owner, owner, cards.Select(c => new CardRef(c)).ToList(), inHand: !IsInField(cards[0])));
        }

        // ---------- Building blocks: hands ----------

        // Chosen cards from the player's hand to their opponent's; the opponent owns them from now on.
        internal void GiveOp(Effect e, Op op, int seat, int count)
        {
            var hand = State.Players[seat].HandCards;
            PickCards(e, seat, ChoiceKind.HandCards, "Give", $"Give {Plural(count, "card")} to your opponent", hand.ToList(), count, count, optional: false, cards =>
            {
                var to = GameState.Other(seat);
                var refs = cards.Select(c => new CardRef(c)).ToList();
                foreach (var card in cards)
                {
                    hand.Remove(card);
                    card.Owner = to;
                    ToHand(State.Players[to], card);
                    op.Add(card);
                }
                Emit(new CardsGiven(seat, to, refs));
            });
        }

        // Random cards from the opponent's hand; the player owns them from now on.
        internal void TakeRandomOp(Op op, int seat, int count)
        {
            var from = GameState.Other(seat);
            var hand = State.Players[from].HandCards;
            var refs = new List<CardRef>();
            for (var i = 0; i < count && hand.Count > 0; i++)
            {
                var card = hand[State.Rng.Next(hand.Count)];
                refs.Add(new CardRef(card));
                hand.Remove(card);
                card.Owner = seat;
                ToHand(State.Players[seat], card);
                op.Add(card);
            }
            if (refs.Count > 0)
                Emit(new HandCardsTaken(seat, from, refs));
        }

        // ---------- Building blocks: Protocols ----------

        internal void RearrangeOp(Effect e, Op op, ChoiceKind kind, int targetSeat, int chooser)
        {
            var whose = targetSeat == chooser ? "your" : "your opponent's";
            var prompt = kind == ChoiceKind.Swap ? $"Swap 2 of {whose} Protocols" : $"Rearrange {whose} Protocols";
            Ask(new PendingChoice(chooser, kind, prompt, answer =>
            {
                var r = (Rearrange)answer;
                RearrangeProtocols(chooser, r.TargetSeat, r.Order);
                op.MarkDone();
            })
            {
                TargetSeat = targetSeat,
                Source = e.Source,
            });
        }

        // ---------- Building blocks: questions ----------

        internal void OptionOp(Effect e, Op op, string prompt, IReadOnlyList<string> options, bool optional, int chooser)
        {
            Ask(new PendingChoice(chooser, ChoiceKind.Option, prompt, answer =>
            {
                var a = (AnswerChoice)answer;
                if (a.Decline)
                    return;
                op.Option = a.Options[0];
                op.MarkDone();
            })
            {
                Options = options.ToList(),
                Optional = optional,
                Source = e.Source,
            });
        }

        internal void ChooseLineOp(Effect e, Op op, string prompt, List<int> fieldLines, int chooser, bool optional)
        {
            void Picked(int line)
            {
                op.Line = line;
                op.MarkDone();
            }

            if (fieldLines.Count == 0)
                return;
            if (fieldLines.Count == 1 && !optional)
            {
                Picked(fieldLines[0]);
                return;
            }
            Ask(new PendingChoice(chooser, ChoiceKind.Line, prompt, answer =>
            {
                var a = (AnswerChoice)answer;
                if (!a.Decline)
                    Picked(GameState.ToField(chooser, a.Line));
            })
            {
                ValidLines = fieldLines.Select(l => GameState.ToLocal(chooser, l)).ToList(),
                Purpose = LinePurpose.Choose,
                Optional = optional,
                Source = e.Source,
            });
        }

        internal void EachOp<T>(Op op, List<T> items, Func<T, string> label, Func<T, Op> effect, Func<T, CardInstance> cardOf)
        {
            void RunInOrder(IReadOnlyList<int> order)
            {
                for (var i = order.Count - 1; i >= 0; i--)
                {
                    var item = items[order[i]];
                    Push(() =>
                    {
                        var each = effect(item);
                        if (each == null)
                            return;
                        Push(() =>
                        {
                            foreach (var card in each.Cards)
                                op.Add(card);
                            if (each.Done)
                                op.MarkDone();
                        });
                        each.Run();
                    });
                }
            }

            if (items.Count <= 1)
            {
                RunInOrder(Enumerable.Range(0, items.Count).ToList());
                return;
            }
            var cards = items.Select(cardOf).ToList();
            Ask(new PendingChoice(State.ActiveSeat, ChoiceKind.Order, "Choose the order to resolve these in", answer => RunInOrder(((AnswerChoice)answer).Options))
            {
                Options = items.Select(label).ToList(),
                ValidCards = cards.All(c => c != null) ? cards.Select(c => c.Id).ToList() : new List<int>(),
                Min = items.Count,
                Max = items.Count,
            });
        }

        static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
    }
}
