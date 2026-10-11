using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // The Protocol draft: picks go 1-2-2-1, starting with the first player. Each player's picks fill
    // their left, middle, then right Protocol space, in the order they were drafted.
    public sealed class Draft
    {
        public const int SeatCount = 2;
        public const int ProtocolsPerPlayer = 3;
        static readonly int[] PicksPerTurn = { 1, 2, 2, 1 };

        readonly List<string> _available;
        readonly List<string>[] _picks = { new List<string>(), new List<string>() };
        int _turn;
        int _picksThisTurn;

        public int FirstSeat { get; }
        public IReadOnlyList<string> Available => _available;
        public bool IsComplete => _turn >= PicksPerTurn.Length;

        // Seat whose pick it is, or -1 once the draft is complete.
        public int CurrentSeat => IsComplete ? -1 : (_turn % 2 == 0 ? FirstSeat : 1 - FirstSeat);

        public int PicksRemainingThisTurn => IsComplete ? 0 : PicksPerTurn[_turn] - _picksThisTurn;

        public Draft(IEnumerable<string> pool, int firstSeat)
        {
            if (firstSeat != 0 && firstSeat != 1)
                throw new ArgumentOutOfRangeException(nameof(firstSeat), "Seat must be 0 or 1.");

            _available = pool.Distinct().ToList();
            if (_available.Count < SeatCount * ProtocolsPerPlayer)
                throw new ArgumentException($"A draft needs at least {SeatCount * ProtocolsPerPlayer} Protocols.", nameof(pool));

            FirstSeat = firstSeat;
        }

        // Protocols drafted by a seat, in space order: left, middle, right.
        public IReadOnlyList<string> PicksFor(int seat) => _picks[seat];

        // Records a pick for the current seat and returns the space index (0 = left, 1 = middle, 2 = right) it fills.
        public int Pick(string protocol)
        {
            if (IsComplete)
                throw new InvalidOperationException("The draft is already complete.");
            if (!_available.Remove(protocol))
                throw new ArgumentException($"'{protocol}' is not available to draft.", nameof(protocol));

            var picks = _picks[CurrentSeat];
            picks.Add(protocol);

            if (++_picksThisTurn == PicksPerTurn[_turn])
            {
                _turn++;
                _picksThisTurn = 0;
            }

            return picks.Count - 1;
        }
    }
}
