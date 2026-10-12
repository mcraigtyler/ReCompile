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

    // The answer to a PendingChoice: cards, a line, or (for an optional choice) declining it.
    public sealed class AnswerChoice : Intent
    {
        public IReadOnlyList<int> Cards { get; }
        public int Line { get; }
        public bool Decline { get; }

        AnswerChoice(int seat, IReadOnlyList<int> cards, int line, bool decline) : base(seat)
        {
            Cards = cards ?? Array.Empty<int>();
            Line = line;
            Decline = decline;
        }

        public static AnswerChoice WithCards(int seat, params int[] cards) => new AnswerChoice(seat, cards, -1, false);
        public static AnswerChoice WithCards(int seat, IEnumerable<int> cards) => new AnswerChoice(seat, cards.ToArray(), -1, false);
        public static AnswerChoice WithLine(int seat, int line) => new AnswerChoice(seat, null, line, false);
        public static AnswerChoice Declined(int seat) => new AnswerChoice(seat, null, -1, true);
    }

    // Answers a rearrange choice. Order[i] is the current index of the Protocol that moves to position i,
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
        Cards,          // pick Min..Max of ValidCards
        Line,           // pick one of ValidLines
        Rearrange,      // answer with a Rearrange intent, or decline if optional
    }

    // A question the engine is waiting on. Nothing else happens until its player answers.
    public sealed class PendingChoice
    {
        public int Seat { get; }
        public ChoiceKind Kind { get; }
        public string Prompt { get; }
        public IReadOnlyList<int> ValidCards { get; }
        public IReadOnlyList<int> ValidLines { get; }   // from the choosing player's seat
        public int Min { get; }
        public int Max { get; }
        public bool Optional { get; }

        internal Action<AnswerChoice, Rearrange> Resume { get; }

        internal PendingChoice(int seat, ChoiceKind kind, string prompt, IReadOnlyList<int> validCards, IReadOnlyList<int> validLines,
            int min, int max, bool optional, Action<AnswerChoice, Rearrange> resume)
        {
            Seat = seat;
            Kind = kind;
            Prompt = prompt;
            ValidCards = validCards ?? Array.Empty<int>();
            ValidLines = validLines ?? Array.Empty<int>();
            Min = min;
            Max = max;
            Optional = optional;
            Resume = resume;
        }

        public override string ToString() => $"{Kind} for seat {Seat}: {Prompt}";
    }
}
