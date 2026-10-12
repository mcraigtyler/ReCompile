using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Compile.Engine.Cards
{
    // Read-only lookup of every Protocol and card in play, loaded from cards.json and protocols.json.
    // Parses through JToken rather than reflection so IL2CPP stripping can't break it.
    public sealed class CardDatabase
    {
        readonly Dictionary<string, CardDefinition> _cards;
        readonly Dictionary<string, ProtocolDefinition> _protocols;

        public IReadOnlyCollection<CardDefinition> Cards => _cards.Values;
        public IReadOnlyCollection<ProtocolDefinition> Protocols => _protocols.Values;

        CardDatabase(IEnumerable<CardDefinition> cards, IEnumerable<ProtocolDefinition> protocols)
        {
            _cards = cards.ToDictionary(c => c.Id);
            _protocols = protocols.ToDictionary(p => p.Name);
        }

        public CardDefinition GetCard(string id) =>
            _cards.TryGetValue(id, out var card) ? card : throw new KeyNotFoundException($"Unknown card '{id}'.");

        public ProtocolDefinition GetProtocol(string name) =>
            _protocols.TryGetValue(name, out var protocol) ? protocol : throw new KeyNotFoundException($"Unknown Protocol '{name}'.");

        public IEnumerable<CardDefinition> CardsFor(string protocol) =>
            _cards.Values.Where(c => c.Protocol == protocol).OrderBy(c => c.Value);

        // The cards as the rules engine sees them (Protocol and value).
        public CardCatalog ToCatalog() => new CardCatalog(_cards.Values.Select(c => new CardSpec(c.Protocol, c.Value)));

        public static CardDatabase Parse(string cardsJson, string protocolsJson)
        {
            var cards = JArray.Parse(cardsJson).Select(ParseCard).ToList();
            var protocols = JArray.Parse(protocolsJson).Select(ParseProtocol).ToList();

            var orphan = cards.FirstOrDefault(c => protocols.All(p => p.Name != c.Protocol));
            if (orphan != null)
                throw new FormatException($"Card '{orphan.Id}' belongs to a Protocol missing from protocols.json.");

            return new CardDatabase(cards, protocols);
        }

        static CardDefinition ParseCard(JToken token)
        {
            var keywords = token["keywords"] is JObject k
                ? k.Properties().Where(p => p.Value.Type == JTokenType.Boolean && (bool)p.Value).Select(p => p.Name).ToArray()
                : Array.Empty<string>();

            return new CardDefinition(
                Required(token, "protocol"),
                (int)token["value"],
                (string)token["set"] ?? "",
                ParseText(token["top"]),
                ParseText(token["middle"]),
                ParseText(token["bottom"]),
                keywords);
        }

        static CardText ParseText(JToken token) =>
            token is JObject o ? new CardText(((string)o["emphasis"])?.Trim(), ((string)o["text"])?.Trim()) : CardText.Empty;

        static ProtocolDefinition ParseProtocol(JToken token) =>
            new ProtocolDefinition(
                Required(token, "protocol"),
                ((string)token["top"])?.Trim() ?? "",
                ((string)token["bottom"])?.Trim() ?? "",
                (string)token["set"] ?? "");

        static string Required(JToken token, string field) =>
            (string)token[field] ?? throw new FormatException($"Missing '{field}' in {token.ToString(Newtonsoft.Json.Formatting.None)}");
    }
}
