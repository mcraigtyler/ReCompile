using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // One building block of card text (draw, flip, shift, ...), made by an Effect and run when the text
    // yields it. Once it has finished, it records what happened so later sentences can check "if you do"
    // or refer to "that card".
    public sealed class Op
    {
        readonly Action<Op> _run;
        readonly List<CardInstance> _cards = new List<CardInstance>();

        internal Op(Action<Op> run) => _run = run;

        // The cards it affected: drawn, discarded, flipped, shifted, deleted, returned, revealed, played, given, or taken.
        public IReadOnlyList<CardInstance> Cards => _cards;
        public int Count => _cards.Count;

        // Choose/May: the option picked, or -1 if declined. ChooseLine: the field line picked, or -1.
        public int Option { get; internal set; } = -1;
        public int Line { get; internal set; } = -1;

        // It did something ("If you do ...").
        public bool Done { get; private set; }

        internal void Add(CardInstance card)
        {
            _cards.Add(card);
            Done = true;
        }

        internal void MarkDone() => Done = true;

        internal void Run() => _run(this);
    }

    public enum Whose
    {
        Any,
        Own,            // the effect owner's cards
        Opponent,       // the owner's opponent's cards
    }

    public enum Cover
    {
        Uncovered,      // the default: "1 card" means 1 uncovered card
        Covered,
        Any,            // covered or not ("all" always means this)
    }

    public enum Facing
    {
        Any,
        FaceUp,
        FaceDown,
    }

    // Which cards in the field an effect may pick, e.g. "1 of your opponent's covered cards" is
    // Target.Any.Opponents().Covered(). Immutable: each method returns a changed copy.
    public sealed class Target
    {
        public Whose Whose { get; private set; }
        public Cover Cover { get; private set; }
        public Facing Facing { get; private set; }
        public bool ExcludesSource { get; private set; }
        public int InFieldLine { get; private set; } = -1;
        public int NotInFieldLine { get; private set; } = -1;
        Func<Game, CardInstance, bool> _where;
        string _whereText;

        public static Target Any => new Target();

        Target With(Action<Target> change)
        {
            var copy = (Target)MemberwiseClone();
            change(copy);
            return copy;
        }

        public Target Own() => With(t => t.Whose = Whose.Own);
        public Target Opponents() => With(t => t.Whose = Whose.Opponent);
        public Target Covered() => With(t => t.Cover = Cover.Covered);
        public Target CoveredOrNot() => With(t => t.Cover = Cover.Any);
        public Target FaceUp() => With(t => t.Facing = Facing.FaceUp);
        public Target FaceDown() => With(t => t.Facing = Facing.FaceDown);
        public Target Other() => With(t => t.ExcludesSource = true);        // "other": not the card whose text this is
        public Target InLine(int fieldLine) => With(t => t.InFieldLine = fieldLine);
        public Target NotInLine(int fieldLine) => With(t => t.NotInFieldLine = fieldLine);

        // Any other condition, with how to say it ("with a value of 0 or 1").
        public Target Where(Func<Game, CardInstance, bool> test, string text) => With(t =>
        {
            var previous = t._where;
            t._where = previous == null ? test : (g, c) => previous(g, c) && test(g, c);
            t._whereText = t._whereText == null ? text : $"{t._whereText} {text}";
        });

        internal bool Matches(Effect e, CardInstance card, int seat, int fieldLine, bool covered, bool all)
        {
            var cover = all ? Cover.Any : Cover;
            if (cover == Cover.Uncovered && covered || cover == Cover.Covered && !covered)
                return false;
            if (Whose == Whose.Own && seat != e.Owner || Whose == Whose.Opponent && seat == e.Owner)
                return false;
            if (Facing == Facing.FaceUp && !card.FaceUp || Facing == Facing.FaceDown && card.FaceUp)
                return false;
            if (ExcludesSource && card == e.Source)
                return false;
            if (InFieldLine >= 0 && fieldLine != InFieldLine || NotInFieldLine >= 0 && fieldLine == NotInFieldLine)
                return false;
            return _where == null || _where(e.Game, card);
        }

        // "1 of your opponent's covered cards", from the chooser's point of view.
        internal string Describe(Effect e, int count, int chooser, bool all)
        {
            var words = new List<string>();
            if (ExcludesSource)
                words.Add("other");
            if (Facing != Facing.Any)
                words.Add(Facing == Facing.FaceUp ? "face-up" : "face-down");
            if (Cover == Cover.Covered && !all)
                words.Add("covered");
            var adjectives = string.Join(" ", words);

            string text;
            if (Whose == Whose.Any)
            {
                var noun = all || count != 1 ? "cards" : "card";
                text = $"{(all ? "all" : count.ToString())} {adjectives} {noun}";
            }
            else
            {
                var mine = (Whose == Whose.Own) == (chooser == e.Owner);
                text = $"{(all ? "all" : count.ToString())} of {(mine ? "your" : "your opponent's")} {adjectives} cards";
            }
            text = string.Join(" ", text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            if (InFieldLine >= 0)
                text += InFieldLine == e.SourceLine ? " in this line" : $" in the {LineName(InFieldLine, chooser)}";
            if (NotInFieldLine >= 0)
                text += NotInFieldLine == e.SourceLine ? " in another line" : $" outside the {LineName(NotInFieldLine, chooser)}";
            if (_whereText != null)
                text += " " + _whereText;
            return text;
        }

        internal static string LineName(int fieldLine, int seat) =>
            new[] { "left", "middle", "right" }[GameState.ToLocal(seat, fieldLine)] + " line";
    }

    // One use of a card's text: whose it is, which card, and the building blocks the text is made of. A
    // debug effect has no source card. Building blocks are mandatory and resolve as fully as they can
    // unless "optional" (the card says "may"); with nothing to choose from they do nothing.
    public sealed class Effect
    {
        readonly int _changes;
        readonly int _covers;

        public Game Game { get; }
        public int Owner { get; }          // the player who makes the choices the text leaves to "you"
        public int Opponent => GameState.Other(Owner);
        public CardInstance Source { get; }
        public Slot Slot { get; internal set; }

        internal readonly List<CardInstance> Revealed = new List<CardInstance>();

        internal Effect(Game game, int owner, CardInstance source, Slot slot)
        {
            Game = game;
            Owner = owner;
            Source = source;
            Slot = slot;
            if (source != null)
            {
                _changes = source.Changes;
                _covers = source.Covers;
            }
        }

        // The field line the source card is in, or -1.
        public int SourceLine => Source == null ? -1 : Game.FieldLineOf(Source);

        internal string Name => Source == null ? "Debug" : Source.Spec.ToString();

        // The text keeps resolving while its card stays in play the way it was: still in the field, face-up,
        // and (except for top text) not covered since. Text without a card always resolves.
        internal bool StillActive
        {
            get
            {
                if (Source == null)
                    return true;
                if (!Source.FaceUp || Source.Changes != _changes || !Game.IsInField(Source))
                    return false;
                if (Slot == Slot.Top)
                    return true;
                return Source.Covers == _covers && Game.IsUncovered(Source) && !(Slot == Slot.Middle && Game.MiddleIgnored(Source));
            }
        }

        int Seat(int? seat) => seat ?? Owner;

        // ---- Draw and discard ----

        public Op Draw(int count, int? seat = null) => new Op(op => Game.DrawOp(this, op, Seat(seat), count));

        // "Draw the top card of your opponent's deck": it becomes yours.
        public Op DrawFromOpponentDeck(int? seat = null) => new Op(op => Game.DrawFromOpponentOp(op, Seat(seat)));

        public Op Refresh(int? seat = null) => new Op(op => Game.RefreshOp(op, Seat(seat)));

        // The player discards this many cards of their choice (all of them if they have fewer).
        public Op Discard(int count, int? seat = null) => new Op(op => Game.DiscardOp(this, op, Seat(seat), count, count));

        // "Discard 1 or more cards."
        public Op DiscardAny(int min = 1, int? seat = null) => new Op(op => Game.DiscardOp(this, op, Seat(seat), min, int.MaxValue));

        // ---- Field cards ----

        public Op Flip(Target target, int count = 1, bool optional = false, int? chooser = null) =>
            new Op(op => Game.FlipOp(this, op, target, count, optional, Seat(chooser)));

        public Op Flip(CardInstance card) => new Op(op => Game.FlipCards(this, op, Fixed(card)));

        // Every matching card at once, covered or not.
        public Op FlipAll(Target target) => new Op(op => Game.FlipAllOp(this, op, target));

        // toLine: a fixed destination (field line), or -1 to let the chooser pick another line.
        public Op Shift(Target target, int toLine = -1, bool optional = false, int? chooser = null) =>
            new Op(op => Game.ShiftOp(this, op, target, toLine, optional, Seat(chooser)));

        public Op Shift(CardInstance card, int toLine = -1, int? chooser = null) =>
            new Op(op => Game.ShiftCards(this, op, Fixed(card), toLine, Seat(chooser)));

        // Every matching card at once, to one destination.
        public Op ShiftAll(Target target, int toLine = -1, int? chooser = null) =>
            new Op(op => Game.ShiftCards(this, op, Game.Candidates(this, target, all: true), toLine, Seat(chooser)));

        public Op Delete(Target target, int count = 1, bool optional = false, int? chooser = null) =>
            new Op(op => Game.DeleteOp(this, op, target, count, optional, Seat(chooser)));

        public Op Delete(CardInstance card) => new Op(op => Game.DeleteCards(this, op, Fixed(card)));

        public Op DeleteAll(Target target) => new Op(op => Game.DeleteCards(this, op, Game.Candidates(this, target, all: true)));

        public Op Return(Target target, int count = 1, bool optional = false, int? chooser = null) =>
            new Op(op => Game.ReturnOp(this, op, target, count, optional, Seat(chooser)));

        public Op Return(CardInstance card) => new Op(op => Game.ReturnCards(this, op, Fixed(card)));

        public Op ReturnAll(Target target) => new Op(op => Game.ReturnCards(this, op, Game.Candidates(this, target, all: true)));

        // ---- Reveal ----
        // Revealed cards are shown to both players until this text finishes, then hidden again.

        public Op RevealField(Target target, int? chooser = null) => new Op(op => Game.RevealFieldOp(this, op, target.FaceDown(), Seat(chooser)));

        public Op RevealHand(int seat) => new Op(op => Game.RevealCards(this, op, seat, Game.State.Players[seat].HandCards.ToList()));

        public Op RevealFromHand(int count = 1, int? seat = null) => new Op(op => Game.RevealFromHandOp(this, op, Seat(seat), count));

        // ---- Playing cards ----

        // The player plays a card from their hand by the normal rules, limited to face-down and/or some lines.
        public Op PlayFromHand(bool faceDownOnly = false, Func<int, bool> fieldLineAllowed = null, int? seat = null) =>
            new Op(op => Game.PlayFromHandOp(this, op, Seat(seat), faceDownOnly, fieldLineAllowed ?? (_ => true)));

        // The top card of the player's deck, face-down, into their side of a field line. Nothing if the deck is empty.
        public Op PlayTopOfDeck(int fieldLine, int? seat = null) => new Op(op => Game.PlayTopOfDeckOp(op, Seat(seat), fieldLine));

        // ---- Hands ----

        public Op Give(int count = 1, int? seat = null) => new Op(op => Game.GiveOp(this, op, Seat(seat), count));

        public Op TakeRandom(int count = 1, int? seat = null) => new Op(op => Game.TakeRandomOp(op, Seat(seat), count));

        // ---- Protocols ----

        public Op Rearrange(int targetSeat, int? chooser = null) => new Op(op => Game.RearrangeOp(this, op, ChoiceKind.Rearrange, targetSeat, Seat(chooser)));

        public Op Swap(int targetSeat, int? chooser = null) => new Op(op => Game.RearrangeOp(this, op, ChoiceKind.Swap, targetSeat, Seat(chooser)));

        // ---- Questions ----

        // "Either ... or ...": op.Option is the one picked.
        public Op Choose(string prompt, IReadOnlyList<string> options, int? chooser = null) =>
            new Op(op => Game.OptionOp(this, op, prompt, options, optional: false, Seat(chooser)));

        // "You may ...": op.Option is 0 if accepted, -1 if declined.
        public Op May(string option, int? chooser = null) =>
            new Op(op => Game.OptionOp(this, op, option, new[] { option }, optional: true, Seat(chooser)));

        // op.Line is the field line picked. If optional, the chooser may decline (op isn't Done).
        public Op ChooseLine(string prompt, IEnumerable<int> fieldLines, int? chooser = null, bool optional = false) =>
            new Op(op => Game.ChooseLineOp(this, op, prompt, fieldLines.ToList(), Seat(chooser), optional));

        // "Each": one effect per item, one at a time, in the order the active player picks. The items are
        // fixed when it starts; each per-item effect checks its card is still there.
        public Op Each<T>(IEnumerable<T> items, Func<T, string> label, Func<T, Op> effect) =>
            new Op(op => Game.EachOp(op, items.ToList(), label, effect, item => (object)item as CardInstance));

        public Op EachCard(Target target, Func<CardInstance, Op> effect) =>
            new Op(op => Game.EachOp(op, Game.Candidates(this, target), c => Game.CardName(c, Game.State.ActiveSeat), effect, c => c));

        // Card-specific text that isn't a building block. Runs when reached; it may push or ask, like any block.
        public Op Do(Action<Op> action) => new Op(action);

        List<CardInstance> Fixed(CardInstance card) => Game.IsInField(card) ? new List<CardInstance> { card } : new List<CardInstance>();
    }
}
