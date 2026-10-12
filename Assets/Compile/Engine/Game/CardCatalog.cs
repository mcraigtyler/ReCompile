using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // What the rules need to know about a printed card. Card text and its effects are added on top of this
    // in Phase 5; until then every card is "text-less".
    public sealed class CardSpec
    {
        public string Id { get; }           // e.g. "Fire_4"
        public string Protocol { get; }
        public int Value { get; }

        public CardSpec(string protocol, int value)
        {
            Protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            Value = value;
            Id = $"{protocol}_{value}";
        }

        public override string ToString() => $"{Protocol} {Value}";
    }

    // Every card the engine can build decks from, grouped by Protocol.
    public sealed class CardCatalog
    {
        public const int CardsPerProtocol = 6;

        readonly Dictionary<string, CardSpec> _cards;
        readonly Dictionary<string, IReadOnlyList<CardSpec>> _byProtocol;

        public CardCatalog(IEnumerable<CardSpec> cards)
        {
            _cards = cards.ToDictionary(c => c.Id);
            _byProtocol = _cards.Values.GroupBy(c => c.Protocol)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<CardSpec>)g.OrderBy(c => c.Value).ToList());
        }

        public IEnumerable<string> Protocols => _byProtocol.Keys.OrderBy(p => p, StringComparer.Ordinal);

        public CardSpec Get(string id) =>
            _cards.TryGetValue(id, out var card) ? card : throw new KeyNotFoundException($"Unknown card '{id}'.");

        public bool Contains(string id) => _cards.ContainsKey(id);

        public IReadOnlyList<CardSpec> CardsFor(string protocol) =>
            _byProtocol.TryGetValue(protocol, out var cards) ? cards : throw new KeyNotFoundException($"Unknown Protocol '{protocol}'.");
    }
}
