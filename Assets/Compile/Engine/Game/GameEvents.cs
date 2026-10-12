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

        internal CardPlayed(int seat, CardRef card, int fieldLine, bool faceUp)
        {
            Seat = seat;
            Card = card;
            FieldLine = fieldLine;
            FaceUp = faceUp;
        }

        public override string Describe(GameState state, int viewer)
        {
            var what = FaceUp ? $"{Card.Name} face-up" : Seat == viewer ? $"{Card.Name} face-down" : "a card face-down";
            return $"{Who(state, Seat, viewer)} played {what} into the {LineName(FieldLine, viewer)}.";
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

    // Cards removed from the field to their owners' trashes (by compiling, for now).
    public sealed class CardsDeleted : GameEvent
    {
        public IReadOnlyList<CardRef> Cards { get; }

        internal CardsDeleted(IReadOnlyList<CardRef> cards) => Cards = cards;

        public override string Describe(GameState state, int viewer) =>
            Cards.Count == 0 ? null : $"Deleted {Plural(Cards.Count, "card")}: {Names(Cards)}.";
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
