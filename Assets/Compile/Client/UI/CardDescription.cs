using System.Text;
using Compile.Engine.Cards;

namespace Compile.Client.UI
{
    // Title and rules text for a card or Protocol, shared by the card zoom and the board's reading panel.
    public readonly struct CardDescription
    {
        public readonly string Title;
        public readonly string Body;

        public CardDescription(string title, string body)
        {
            Title = title;
            Body = body;
        }

        // Title in bold with the body under it, for a single text box.
        public string Combined => string.IsNullOrEmpty(Body) ? $"<b>{Title}</b>" : $"<b>{Title}</b>\n\n{Body}";

        public static CardDescription ForCard(CardDefinition card, bool faceUp, bool hiddenFromViewer)
        {
            if (hiddenFromViewer)
                return new CardDescription("FACE-DOWN CARD", "<color=#A397BA>Worth 2. Only its owner can see what it is.</color>");

            var body = new StringBuilder();
            if (!faceUp)
                body.Append("<color=#BFF4F4>Face-down: worth 2, and none of its text is active.</color>");
            Section(body, "Top", card.Top);
            Section(body, "Middle", card.Middle);
            Section(body, "Bottom", card.Bottom);
            return new CardDescription($"{card.Protocol.ToUpperInvariant()} {card.Value}", body.ToString());
        }

        public static CardDescription ForProtocol(ProtocolDefinition protocol, bool compiled) =>
            new CardDescription($"{protocol.Name.ToUpperInvariant()} PROTOCOL",
                $"<color=#A397BA>{protocol.Tagline}</color>\n\n<color=#E58BFF>FOCUS</color>  {protocol.Focus}\n\n" +
                (compiled ? "<color=#7CF2C4>Compiled.</color>" : "<color=#A397BA>Loading... (not yet compiled)</color>"));

        static void Section(StringBuilder text, string label, CardText command)
        {
            if (command.IsEmpty)
                return;
            if (text.Length > 0)
                text.Append("\n\n");
            text.Append($"<size=75%><color=#A397BA>{label.ToUpperInvariant()}</color></size>\n");
            if (command.Emphasis.Length > 0)
                text.Append($"<b><color=#E58BFF>{command.Emphasis}</color></b> ");
            text.Append(command.Text);
        }
    }
}
