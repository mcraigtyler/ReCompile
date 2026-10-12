using System;
using System.Collections.Generic;

namespace Compile.Engine
{
    // Seeded random numbers (SplitMix64). The engine uses its own generator rather than System.Random so a
    // seed replays the same game on every runtime (Mono, IL2CPP, .NET).
    public sealed class Rng
    {
        ulong _state;

        public Rng(long seed) => _state = (ulong)seed;

        public ulong NextULong()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // A number in [0, maxExclusive).
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        // Fisher–Yates shuffle in place.
        public void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
