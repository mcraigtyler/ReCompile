using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Views;

namespace Compile.Engine
{
    public enum GameStage
    {
        Draft,
        Playing,
        Over,
    }

    public enum EndReason
    {
        None,
        Compiled,       // a player compiled all 3 Protocols
        Conceded,
        Stalemate,      // house rule: neither player could act for two turns in a row
    }

    // One physical card. Its Id changes whenever it goes somewhere hidden (a hand or a deck), so a viewer
    // can't follow a card they saw earlier into a face-down play (see Game.Rekey).
    public sealed class CardInstance
    {
        public int Id { get; internal set; }
        public CardSpec Spec { get; }
        public int Owner { get; internal set; }
        public bool FaceUp { get; internal set; }

        internal CardInstance(int id, CardSpec spec, int owner)
        {
            Id = id;
            Spec = spec;
            Owner = owner;
        }

        public override string ToString() => $"{Spec}#{Id}{(FaceUp ? "" : " (face-down)")}";
    }

    public sealed class ProtocolSlot
    {
        public string Name { get; }
        public bool Compiled { get; internal set; }

        internal ProtocolSlot(string name, bool compiled = false)
        {
            Name = name;
            Compiled = compiled;
        }
    }

    public sealed class PlayerState
    {
        public const int ProtocolCount = 3;

        internal readonly ProtocolSlot[] ProtocolSlots = new ProtocolSlot[ProtocolCount];
        internal readonly List<CardInstance> DeckCards = new List<CardInstance>();     // index 0 = top
        internal readonly List<CardInstance> HandCards = new List<CardInstance>();
        internal readonly List<CardInstance> TrashCards = new List<CardInstance>();     // last = most recent

        public int Seat { get; }
        public string Name { get; }

        // Index 0..2 = left..right from this player's own seat.
        public IReadOnlyList<ProtocolSlot> Protocols => ProtocolSlots;
        public IReadOnlyList<CardInstance> Deck => DeckCards;
        public IReadOnlyList<CardInstance> Hand => HandCards;
        public IReadOnlyList<CardInstance> Trash => TrashCards;

        public int CompiledCount => ProtocolSlots.Count(p => p != null && p.Compiled);

        internal PlayerState(int seat, string name)
        {
            Seat = seat;
            Name = name;
        }
    }

    // One third of the field. Lines are numbered from seat 0's left; seat 1 sits opposite, so its left
    // line is line 2. Each line holds one stack per seat, bottom (nearest the Protocol) to top.
    public sealed class Line
    {
        internal readonly List<CardInstance>[] StackCards = { new List<CardInstance>(), new List<CardInstance>() };

        public int Index { get; }

        public IReadOnlyList<CardInstance> Stack(int seat) => StackCards[seat];

        public CardInstance Uncovered(int seat) => StackCards[seat].Count > 0 ? StackCards[seat][StackCards[seat].Count - 1] : null;

        internal Line(int index) => Index = index;
    }

    public sealed class GameState
    {
        public const int SeatCount = 2;
        public const int LineCount = 3;
        public const int Neutral = -1;

        internal readonly HashSet<int> UsedIds = new HashSet<int>();

        public long Seed { get; }
        internal Rng Rng { get; }

        public GameStage Stage { get; internal set; }
        public Draft Draft { get; internal set; }
        public int FirstSeat { get; internal set; }
        public int Turn { get; internal set; }
        public int ActiveSeat { get; internal set; }
        public TurnPhase Phase { get; internal set; }
        public int Control { get; internal set; } = Neutral;      // Neutral, or the seat holding it
        public int Winner { get; internal set; } = -1;            // -1 = none (or a drawn stalemate)
        public EndReason EndReason { get; internal set; }

        public PlayerState[] Players { get; }
        public Line[] Lines { get; }

        internal GameState(long seed, string name0, string name1)
        {
            Seed = seed;
            Rng = new Rng(seed);
            Players = new[] { new PlayerState(0, name0), new PlayerState(1, name1) };
            Lines = Enumerable.Range(0, LineCount).Select(i => new Line(i)).ToArray();
        }

        // Every card in the game, wherever it is.
        public IEnumerable<CardInstance> AllCards() =>
            Players.SelectMany(p => p.DeckCards.Concat(p.HandCards).Concat(p.TrashCards))
                .Concat(Lines.SelectMany(l => l.StackCards[0].Concat(l.StackCards[1])));

        // ---- Seats and lines ----

        public static int Other(int seat) => 1 - seat;

        // A seat's own line index (0 = their left) to the field line, and back. The mapping is its own inverse.
        public static int ToField(int seat, int localLine) => seat == 0 ? localLine : LineCount - 1 - localLine;
        public static int ToLocal(int seat, int fieldLine) => ToField(seat, fieldLine);

        // The Protocol a seat has in a field line.
        public ProtocolSlot ProtocolIn(int seat, int fieldLine) => Players[seat].ProtocolSlots[ToLocal(seat, fieldLine)];
    }
}
