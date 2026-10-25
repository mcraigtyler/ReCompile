using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Views;

namespace Compile.Engine
{
    // A card as it was when an event happened.
    public sealed class CardRef
    {
        public int InstanceId { get; }
        public string CardId { get; }
        public int Owner { get; }
        public bool FaceUp { get; }

        internal CardRef(CardInstance card)
        {
            InstanceId = card.Id;
            CardId = card.Spec.Id;
            Owner = card.Owner;
            FaceUp = card.FaceUp;
        }

        internal string Name => CardId.Replace('_', ' ');

        // The card's name if this viewer could see it at the time, otherwise a stand-in.
        internal string NameFor(int viewer, string hidden = "a face-down card") => FaceUp || Owner == viewer ? Name : hidden;
    }

    // Something that happened, in order, for animation and the event log. Events hold full information;
    // Describe() only tells a viewer what they're allowed to know. (Phase 6 adds per-viewer redaction of
    // the events themselves before they're sent over the network.)
    public abstract class GameEvent
    {
        // A log line for this viewer, or null if the event isn't logged.
        public virtual string Describe(GameState state, int viewer) => null;

        // The seat that acted, or -1 when nobody did (e.g. Control returning to neutral).
        public virtual int Actor => -1;

        protected static string Who(GameState state, int seat, int viewer) => seat == viewer ? "You" : state.Players[seat].Name;

        protected static string LineName(int fieldLine, int viewer) =>
            new[] { "left", "middle", "right" }[GameState.ToLocal(viewer, fieldLine)] + " line";

        protected static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

        protected static string Names(IEnumerable<CardRef> cards) => string.Join(", ", cards.Select(c => c.Name));
    }

    public sealed class DraftPicked : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public string Protocol { get; }
        public int Slot { get; }

        internal DraftPicked(int seat, string protocol, int slot)
        {
            Seat = seat;
            Protocol = protocol;
            Slot = slot;
        }

        public override string Describe(GameState state, int viewer) => $"{Who(state, Seat, viewer)} drafted {Protocol}.";
    }

    public sealed class GameStarted : GameEvent
    {
        public int FirstSeat { get; }

        internal GameStarted(int firstSeat) => FirstSeat = firstSeat;

        public override string Describe(GameState state, int viewer) =>
            $"The game begins. Both players draw 5 cards. {Who(state, FirstSeat, viewer)} {(FirstSeat == viewer ? "go" : "goes")} first.";
    }

    public sealed class TurnStarted : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int Turn { get; }

        internal TurnStarted(int seat, int turn)
        {
            Seat = seat;
            Turn = turn;
        }
    }

    public sealed class PhaseChanged : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public TurnPhase Phase { get; }

        internal PhaseChanged(int seat, TurnPhase phase)
        {
            Seat = seat;
            Phase = phase;
        }
    }

    public sealed class ControlChanged : GameEvent
    {
        public int Holder { get; }      // GameState.Neutral or a seat

        internal ControlChanged(int holder) => Holder = holder;

        public override string Describe(GameState state, int viewer) =>
            Holder == GameState.Neutral
                ? "Control returns to neutral."
                : $"{Who(state, Holder, viewer)} {(Holder == viewer ? "take" : "takes")} Control.";
    }

    public sealed class DeckReshuffled : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int Count { get; }

        internal DeckReshuffled(int seat, int count)
        {
            Seat = seat;
            Count = count;
        }

        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} shuffled {(Seat == viewer ? "your" : "their")} trash ({Plural(Count, "card")}) into a new deck.";
    }

    public sealed class CardsDrawn : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsDrawn(int seat, IReadOnlyList<CardRef> cards)
        {
            Seat = seat;
            Cards = cards;
        }

        public override string Describe(GameState state, int viewer) =>
            Cards.Count == 0 ? null
            : Seat == viewer ? $"You drew {Names(Cards)}."
            : $"{Who(state, Seat, viewer)} drew {Plural(Cards.Count, "card")}.";
    }

    public sealed class CardPlayed : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public CardRef Card { get; }
        public int FieldLine { get; }
        public bool FaceUp { get; }
        public bool FromDeck { get; }       // the top card of the player's deck, rather than from their hand

        internal CardPlayed(int seat, CardRef card, int fieldLine, bool faceUp, bool fromDeck = false)
        {
            Seat = seat;
            Card = card;
            FieldLine = fieldLine;
            FaceUp = faceUp;
            FromDeck = fromDeck;
        }

        public override string Describe(GameState state, int viewer)
        {
            var what = FaceUp ? $"{Card.Name} face-up" : Seat == viewer ? $"{Card.Name} face-down" : "a card face-down";
            var from = FromDeck ? $" from the top of {(Seat == viewer ? "your" : "their")} deck" : "";
            return $"{Who(state, Seat, viewer)} played {what}{from} into the {LineName(FieldLine, viewer)}.";
        }
    }

    public sealed class Refreshed : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public bool Forced { get; }

        internal Refreshed(int seat, bool forced)
        {
            Seat = seat;
            Forced = forced;
        }

        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} {(Forced ? "had no cards in hand and refreshed" : "refreshed")}.";
    }

    public sealed class ActionSkipped : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;

        internal ActionSkipped(int seat) => Seat = seat;

        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} couldn't play or draw, so {(Seat == viewer ? "your" : "their")} Action step was skipped.";
    }

    public sealed class CardsDiscarded : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsDiscarded(int seat, IReadOnlyList<CardRef> cards)
        {
            Seat = seat;
            Cards = cards;
        }

        public override string Describe(GameState state, int viewer) =>
            Cards.Count == 0 ? null : $"{Who(state, Seat, viewer)} discarded {Names(Cards)}.";
    }

    public sealed class LineCompiled : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int FieldLine { get; }
        public string Protocol { get; }
        public bool Recompile { get; }

        internal LineCompiled(int seat, int fieldLine, string protocol, bool recompile)
        {
            Seat = seat;
            FieldLine = fieldLine;
            Protocol = protocol;
            Recompile = recompile;
        }

        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} {(Recompile ? "recompiled" : "compiled")} {Protocol} in the {LineName(FieldLine, viewer)}.";
    }

    // Cards removed from the field to their owners' trashes, by compiling or by card text.
    public sealed class CardsDeleted : GameEvent
    {
        public int Seat { get; }            // whose effect deleted them, or -1 for compiling
        public override int Actor => Seat;
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsDeleted(IReadOnlyList<CardRef> cards, int seat = -1)
        {
            Cards = cards;
            Seat = seat;
        }

        public override string Describe(GameState state, int viewer) =>
            Cards.Count == 0 ? null
            : Seat < 0 ? $"Deleted {Plural(Cards.Count, "card")}: {Names(Cards)}."
            : $"{Who(state, Seat, viewer)} deleted {Names(Cards)}.";
    }

    // Field cards turned over, by card text. The refs show each card as it is after the flip.
    public sealed class CardsFlipped : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsFlipped(int seat, IReadOnlyList<CardRef> cards)
        {
            Seat = seat;
            Cards = cards;
        }

        // A flipped card was face-up before or after, so everyone has seen it.
        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} flipped {string.Join(", ", Cards.Select(c => $"{c.Name} {(c.FaceUp ? "face-up" : "face-down")}"))}.";
    }

    public sealed class CardShifted : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public CardRef Card { get; }
        public int FromLine { get; }        // field lines
        public int ToLine { get; }

        internal CardShifted(int seat, CardRef card, int fromLine, int toLine)
        {
            Seat = seat;
            Card = card;
            FromLine = fromLine;
            ToLine = toLine;
        }

        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} shifted {Card.NameFor(viewer)} from the {LineName(FromLine, viewer)} to the {LineName(ToLine, viewer)}.";
    }

    // Field cards back to their owners' hands. The refs show each card as it was in the field.
    public sealed class CardsReturned : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsReturned(int seat, IReadOnlyList<CardRef> cards)
        {
            Seat = seat;
            Cards = cards;
        }

        public override string Describe(GameState state, int viewer) =>
            $"{Who(state, Seat, viewer)} returned {string.Join(", ", Cards.Select(c => c.NameFor(viewer)))} to {(Cards.All(c => c.Owner == viewer) ? "your" : Cards.All(c => c.Owner != viewer) ? $"{state.Players[Cards[0].Owner].Name}'s" : "its owner's")} hand.";
    }

    // Cards shown to both players (from a hand, or face-down in the field) until the text that revealed
    // them finishes; RevealEnded follows.
    public sealed class CardsRevealed : GameEvent
    {
        public int Seat { get; }            // whose effect revealed them
        public override int Actor => Seat;
        public int Owner { get; }
        public IReadOnlyList<CardRef> Cards { get; }
        public bool InHand { get; }

        internal CardsRevealed(int seat, int owner, IReadOnlyList<CardRef> cards, bool inHand)
        {
            Seat = seat;
            Owner = owner;
            Cards = cards;
            InHand = inHand;
        }

        public override string Describe(GameState state, int viewer)
        {
            var whose = Owner == viewer ? "your" : Owner == Seat ? "their" : $"{state.Players[Owner].Name}'s";
            return InHand
                ? $"{Who(state, Seat, viewer)} revealed {Names(Cards)} from {whose} hand."
                : $"{Who(state, Seat, viewer)} revealed {whose} face-down {Names(Cards)}.";
        }
    }

    public sealed class RevealEnded : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;

        internal RevealEnded(int seat) => Seat = seat;
    }

    // Cards from one hand to the other; the receiver owns them from now on. Both players see which.
    public sealed class CardsGiven : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int ToSeat { get; }
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsGiven(int seat, int toSeat, IReadOnlyList<CardRef> cards)
        {
            Seat = seat;
            ToSeat = toSeat;
            Cards = cards;
        }

        public override string Describe(GameState state, int viewer) =>
            Seat == viewer ? $"You gave {Names(Cards)} to {state.Players[ToSeat].Name}." : $"{Who(state, Seat, viewer)} gave you {Names(Cards)}.";
    }

    // Random cards taken from the opponent's hand; the taker owns them from now on. Both players see which.
    public sealed class HandCardsTaken : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int FromSeat { get; }
        public IReadOnlyList<CardRef> Cards { get; }

        internal HandCardsTaken(int seat, int fromSeat, IReadOnlyList<CardRef> cards)
        {
            Seat = seat;
            FromSeat = fromSeat;
            Cards = cards;
        }

        public override string Describe(GameState state, int viewer) =>
            Seat == viewer ? $"You took {Names(Cards)} from {state.Players[FromSeat].Name}'s hand." : $"{Who(state, Seat, viewer)} took {Names(Cards)} from your hand.";
    }

    public enum TextReason { Uncovered, FlippedUp, Start, End, Response }

    // A card's text starts to resolve for some reason other than the card being played.
    public sealed class CardTextRuns : GameEvent
    {
        public int Seat { get; }            // the card's owner
        public override int Actor => Seat;
        public CardRef Card { get; }
        public TextReason Reason { get; }

        internal CardTextRuns(int seat, CardRef card, TextReason reason)
        {
            Seat = seat;
            Card = card;
            Reason = reason;
        }

        public override string Describe(GameState state, int viewer)
        {
            switch (Reason)
            {
                case TextReason.Uncovered: return $"{Card.Name} was uncovered, so its text resolves.";
                case TextReason.FlippedUp: return $"{Card.Name} was flipped face-up, so its text resolves.";
                case TextReason.Start: return $"{Card.Name}: its Start text resolves.";
                case TextReason.End: return $"{Card.Name}: its End text resolves.";
                default: return $"{Card.Name} responds.";
            }
        }
    }

    // Card text asked for exactly as many cards as could be picked, so they were picked without asking.
    public sealed class ChoiceMadeFor : GameEvent
    {
        public int Seat { get; }            // who would have chosen
        public override int Actor => Seat;
        public CardRef Source { get; }      // null for debug effects
        public string Verb { get; }         // "Delete", "Discard", ...
        public IReadOnlyList<CardRef> Cards { get; }
        public bool InHand { get; }

        internal ChoiceMadeFor(int seat, CardRef source, string verb, IReadOnlyList<CardRef> cards, bool inHand)
        {
            Seat = seat;
            Source = source;
            Verb = verb;
            Cards = cards;
            InHand = inHand;
        }

        static string Done(string verb) => verb == "Give" ? "given" : verb.ToLowerInvariant().TrimEnd('e') + (verb == "Flip" ? "ped" : "ed");

        public override string Describe(GameState state, int viewer)
        {
            var source = Source?.Name ?? "Debug";
            var hidden = InHand ? "a card" : "a face-down card";
            var names = Cards.Select(c => c.NameFor(viewer, hidden)).ToList();
            var whom = Seat == viewer ? "you" : state.Players[Seat].Name;
            if (names.Count > 1 && names.Contains(hidden))
                return $"{source}: {(Seat == viewer ? "You" : whom)} had only {names.Count} cards that could be {Done(Verb)}, so {(names.Count == 2 ? "both" : "all")} were picked.";
            var list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
            return names.Count == 1
                ? $"{source}: {list} was the only card that could be {Done(Verb)}, so it was picked for {whom}."
                : $"{source}: {list} were the only cards that could be {Done(Verb)}, so {(names.Count == 2 ? "both" : "all")} were picked for {whom}.";
        }
    }

    public sealed class DebugEffectUsed : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public string Label { get; }

        internal DebugEffectUsed(int seat, string label)
        {
            Seat = seat;
            Label = label;
        }

        public override string Describe(GameState state, int viewer) => $"[Debug] {Who(state, Seat, viewer)}: {Label}";
    }

    public sealed class ProtocolCompiled : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public string Protocol { get; }

        internal ProtocolCompiled(int seat, string protocol)
        {
            Seat = seat;
            Protocol = protocol;
        }
    }

    // On a Recompile, the compiling player draws the top card of the opponent's deck and owns it from now on.
    public sealed class CardTaken : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int FromSeat { get; }
        public CardRef Card { get; }        // null if the opponent had no cards to draw

        internal CardTaken(int seat, int fromSeat, CardRef card)
        {
            Seat = seat;
            FromSeat = fromSeat;
            Card = card;
        }

        public override string Describe(GameState state, int viewer)
        {
            if (Card == null)
                return $"{Who(state, FromSeat, viewer)} had no cards left to draw from.";
            var what = Seat == viewer ? Card.Name : "a card";
            return $"{Who(state, Seat, viewer)} drew {what} from {(FromSeat == viewer ? "your" : $"{state.Players[FromSeat].Name}'s")} deck.";
        }
    }

    public sealed class ProtocolsRearranged : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public int TargetSeat { get; }
        public IReadOnlyList<string> Order { get; }     // new order, from the target's seat

        internal ProtocolsRearranged(int seat, int targetSeat, IReadOnlyList<string> order)
        {
            Seat = seat;
            TargetSeat = targetSeat;
            Order = order;
        }

        public override string Describe(GameState state, int viewer)
        {
            var whose = TargetSeat == viewer ? "your" : Seat == TargetSeat ? "their" : $"{state.Players[TargetSeat].Name}'s";
            return $"{Who(state, Seat, viewer)} rearranged {whose} Protocols: {string.Join(", ", Order)}.";
        }
    }

    public sealed class MoveUndone : GameEvent
    {
        public int Seat { get; }
        public override int Actor => Seat;
        public string What { get; }

        internal MoveUndone(int seat, string what)
        {
            Seat = seat;
            What = what;
        }

        public override string Describe(GameState state, int viewer) => $"{Who(state, Seat, viewer)} undid {What}.";
    }

    public sealed class GameEnded : GameEvent
    {
        public int Winner { get; }          // -1 for a drawn stalemate
        public EndReason Reason { get; }

        internal GameEnded(int winner, EndReason reason)
        {
            Winner = winner;
            Reason = reason;
        }

        public override string Describe(GameState state, int viewer)
        {
            switch (Reason)
            {
                case EndReason.Conceded:
                    return $"{Who(state, GameState.Other(Winner), viewer)} conceded.";
                case EndReason.Stalemate:
                    return Winner < 0 ? "Neither player can act. The game is a draw."
                        : $"Neither player can act. {Who(state, Winner, viewer)} {(Winner == viewer ? "win" : "wins")} on compiled Protocols.";
                default:
                    return $"{Who(state, Winner, viewer)} compiled all 3 Protocols and {(Winner == viewer ? "win" : "wins")}!";
            }
        }
    }
}
