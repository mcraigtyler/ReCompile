using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // Something a player asks the engine to do. The engine checks it against the rules and either applies
    // it or rejects it with a reason. Lines are always given from the acting player's own seat (0 = their left).
    public abstract class Intent
    {
        public int Seat { get; }

        protected Intent(int seat) => Seat = seat;
    }

    public sealed class DraftPick : Intent
    {
        public string Protocol { get; }

        public DraftPick(int seat, string protocol) : base(seat) => Protocol = protocol;
    }

    public sealed class PlayCard : Intent
    {
        public int CardId { get; }
        public int Line { get; }
        public bool FaceUp { get; }

        public PlayCard(int seat, int cardId, int line, bool faceUp) : base(seat)
        {
            CardId = cardId;
            Line = line;
            FaceUp = faceUp;
        }
    }

    public sealed class Refresh : Intent
    {
        public Refresh(int seat) : base(seat) { }
    }

    // The answer to a PendingChoice: cards, a line, an option, an order, or (for an optional choice)
    // declining it. Rearrange, Swap, and Play choices are answered with Rearrange and PlayCard intents.
    public sealed class AnswerChoice : Intent
    {
        public IReadOnlyList<int> Cards { get; }
        public int Line { get; }
        public IReadOnlyList<int> Options { get; }     // Option: the one picked; Order: every option, first to resolve first
        public bool Decline { get; }

        AnswerChoice(int seat, IReadOnlyList<int> cards, int line, IReadOnlyList<int> options, bool decline) : base(seat)
        {
            Cards = cards ?? Array.Empty<int>();
            Line = line;
            Options = options ?? Array.Empty<int>();
            Decline = decline;
        }

        public static AnswerChoice WithCards(int seat, params int[] cards) => new AnswerChoice(seat, cards, -1, null, false);
        public static AnswerChoice WithCards(int seat, IEnumerable<int> cards) => new AnswerChoice(seat, cards.ToArray(), -1, null, false);
        public static AnswerChoice WithLine(int seat, int line) => new AnswerChoice(seat, null, line, null, false);
        public static AnswerChoice WithOption(int seat, int option) => new AnswerChoice(seat, null, -1, new[] { option }, false);
        public static AnswerChoice WithOrder(int seat, params int[] order) => new AnswerChoice(seat, null, -1, order, false);
        public static AnswerChoice WithOrder(int seat, IEnumerable<int> order) => new AnswerChoice(seat, null, -1, order.ToArray(), false);
        public static AnswerChoice Declined(int seat) => new AnswerChoice(seat, null, -1, null, true);
    }

    // Answers a Rearrange or Swap choice. Order[i] is the current index of the Protocol that moves to position i,
    // both counted from the target player's own seat.
    public sealed class Rearrange : Intent
    {
        public int TargetSeat { get; }
        public IReadOnlyList<int> Order { get; }

        public Rearrange(int seat, int targetSeat, params int[] order) : base(seat)
        {
            TargetSeat = targetSeat;
            Order = order ?? Array.Empty<int>();
        }
    }

    // Ends the active player's turn once its End step is done (when the game confirms turn ends).
    public sealed class EndTurn : Intent
    {
        public EndTurn(int seat) : base(seat) { }
    }

    // Takes back the active player's last decision this turn.
    public sealed class Undo : Intent
    {
        public Undo(int seat) : base(seat) { }
    }

    // Development only: runs one of the DebugEffects building blocks for this player, as if a card's text
    // said it. Allowed in the player's Action step or once their turn is done; it doesn't use up the Action.
    public sealed class DebugEffect : Intent
    {
        public string Key { get; }

        public DebugEffect(int seat, string key) : base(seat) => Key = key;
    }

    public sealed class Concede : Intent
    {
        public Concede(int seat) : base(seat) { }
    }

    public sealed class IntentResult
    {
        public bool Accepted { get; }
        public string Error { get; }
        public IReadOnlyList<GameEvent> Events { get; }

        IntentResult(bool accepted, string error, IReadOnlyList<GameEvent> events)
        {
            Accepted = accepted;
            Error = error;
            Events = events;
        }

        internal static IntentResult Ok(IReadOnlyList<GameEvent> events) => new IntentResult(true, null, events);
        internal static IntentResult Rejected(string error) => new IntentResult(false, error, Array.Empty<GameEvent>());

        public override string ToString() => Accepted ? $"Accepted ({Events.Count} events)" : $"Rejected: {Error}";
    }

    public enum ChoiceKind
    {
        FieldCards,     // pick Min..Max of ValidCards in the field
        HandCards,      // pick Min..Max of ValidCards from the chooser's hand
        Line,           // pick one of ValidLines (Purpose says what for)
        Option,         // pick one of Options ("either ... or ...", or a single option to accept or decline for "may")
        Order,          // put all Options in order (simultaneous effects); ValidCards lines up with Options when they're cards
        Rearrange,      // answer with a Rearrange intent: any new order of TargetSeat's Protocols
        Swap,           // answer with a Rearrange intent that swaps exactly 2 of TargetSeat's Protocols
        Play,           // answer with a PlayCard intent: one of ValidCards into one of ValidLines, face-up if allowed
    }

    public enum LinePurpose
    {
        None,
        Compile,        // which line to compile
        ShiftTo,        // where a card is shifted to
        Choose,         // a line card text asks for ("delete all cards in 1 line ...")
    }

    // A question the engine is waiting on. Nothing else happens until its player answers.
    public sealed class PendingChoice
    {
        public int Seat { get; }
        public ChoiceKind Kind { get; }
        public string Prompt { get; }
        public IReadOnlyList<int> ValidCards { get; internal set; } = Array.Empty<int>();
        public IReadOnlyList<int> ValidLines { get; internal set; } = Array.Empty<int>();   // from the choosing player's seat
        public IReadOnlyList<string> Options { get; internal set; } = Array.Empty<string>();
        public int Min { get; internal set; } = 1;
        public int Max { get; internal set; } = 1;
        public bool Optional { get; internal set; }
        public int TargetSeat { get; internal set; } = -1;     // Rearrange/Swap: whose Protocols, or -1 for the chooser's pick
        public LinePurpose Purpose { get; internal set; }
        public bool FaceUpAllowed { get; internal set; }        // Play
        public bool FaceDownAllowed { get; internal set; }      // Play
        public CardInstance Source { get; internal set; }       // the card whose text asks, if any
        public CardInstance Subject { get; internal set; }      // the card the question is about (e.g. the one being shifted)

        // Play: why a PlayCard answer isn't allowed, or null.
        internal Func<PlayCard, string> CheckPlay { get; set; }

        // Called with the accepted answer (an AnswerChoice, Rearrange, or PlayCard; declining is AnswerChoice.Decline).
        internal Action<Intent> Resume { get; }

        internal PendingChoice(int seat, ChoiceKind kind, string prompt, Action<Intent> resume)
        {
            Seat = seat;
            Kind = kind;
            Prompt = prompt;
            Resume = resume;
        }

        public override string ToString() => $"{Kind} for seat {Seat}: {Prompt}";
    }
}
