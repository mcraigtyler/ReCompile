using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // One command box on a card. Emphasis holds trigger words such as "Start:" or
    // "When this card would be covered:"; Text holds the rest.
    public sealed class CardText
    {
        public static readonly CardText Empty = new CardText("", "");

        public string Emphasis { get; }
        public string Text { get; }
        public bool IsEmpty => Emphasis.Length == 0 && Text.Length == 0;

        public CardText(string emphasis, string text)
        {
            Emphasis = emphasis ?? "";
            Text = text ?? "";
        }

        public override string ToString() => Emphasis.Length == 0 ? Text : $"{Emphasis} {Text}";
    }

    // Printed data for a Command card, as listed in cards.json.
    public sealed class CardDefinition
    {
        public string Id { get; }
        public string Protocol { get; }
        public int Value { get; }
        public string Set { get; }
        public CardText Top { get; }
        public CardText Middle { get; }
        public CardText Bottom { get; }
        public IReadOnlyCollection<string> Keywords { get; }

        public CardDefinition(string protocol, int value, string set, CardText top, CardText middle, CardText bottom,
            IReadOnlyCollection<string> keywords)
        {
            Id = MakeId(protocol, value);
            Protocol = protocol;
            Value = value;
            Set = set;
            Top = top;
            Middle = middle;
            Bottom = bottom;
            Keywords = keywords;
        }

        public static string MakeId(string protocol, int value) => $"{protocol}_{value}";

        public override string ToString() => Id;
    }

    // A Protocol card, as listed in protocols.json.
    public sealed class ProtocolDefinition
    {
        public string Name { get; }
        public string Tagline { get; }
        public string Focus { get; }
        public string Set { get; }

        public ProtocolDefinition(string name, string tagline, string focus, string set)
        {
            Name = name;
            Tagline = tagline;
            Focus = focus;
            Set = set;
        }

        public override string ToString() => Name;
    }
}
