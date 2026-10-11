using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Views
{
    // What one player can see of a game. The board and HUD render only from these types; for now a
    // client-side stand-in produces them, and later the rules engine (Phase 3) and the network
    // (Phase 6) will. Everything is from the viewer's seat: "local" is the viewer, and lines run from
    // the viewer's left to right.

    public enum TurnPhase
    {
        Start,
        Control,
        Compile,
        Action,
        Cache,
        End,
    }

    public enum ControlHolder
    {
        Neutral,
        Local,
        Opponent,
    }

    // A card as the viewer sees it. Cards the viewer may not see (the opponent's face-down cards and
    // hand) have no CardId.
    public sealed class CardInfo
    {
        public const int FaceDownValue = 2;

        public int InstanceId { get; set; }
        public string CardId { get; set; }          // e.g. "Fire_4"; null when hidden from the viewer
        public string Protocol { get; set; }        // null when hidden
        public int PrintedValue { get; set; }       // 0 when hidden
        public bool FaceUp { get; set; }
        public bool LocalOwner { get; set; }

        public bool IsHidden => CardId == null;
        public int Value => FaceUp ? PrintedValue : FaceDownValue;
    }

    // One player's cards in a line, bottom (nearest the Protocol) to top. The last card is uncovered.
    public sealed class StackInfo
    {
        public IReadOnlyList<CardInfo> Cards { get; set; } = new List<CardInfo>();

        public int Total => Cards.Sum(c => c.Value);
        public CardInfo Top => Cards.Count > 0 ? Cards[Cards.Count - 1] : null;
    }

    public sealed class ProtocolInfo
    {
        public string Name { get; set; }
        public bool Compiled { get; set; }
    }

    public sealed class LineInfo
    {
        public int Index { get; set; }              // 0 = viewer's left
        public ProtocolInfo LocalProtocol { get; set; }
        public ProtocolInfo OpponentProtocol { get; set; }
        public StackInfo Local { get; set; } = new StackInfo();
        public StackInfo Opponent { get; set; } = new StackInfo();

        public bool LocalCanCompile => Local.Total >= 10 && Local.Total > Opponent.Total;
        public bool OpponentCanCompile => Opponent.Total >= 10 && Opponent.Total > Local.Total;

        // Face-up cards may be played into a line whose Protocol (on either side) matches theirs.
        public bool Accepts(string protocol) => LocalProtocol?.Name == protocol || OpponentProtocol?.Name == protocol;
    }

    public sealed class SideInfo
    {
        public string Name { get; set; }
        public int HandCount { get; set; }
        public int DeckCount { get; set; }
        public IReadOnlyList<CardInfo> Trash { get; set; } = new List<CardInfo>();   // public, always face-up
    }

    // A decision the viewer has to make, such as "Choose 1 card to flip".
    public sealed class ChoiceInfo
    {
        public string Prompt { get; set; }
        public IReadOnlyCollection<int> ValidCards { get; set; } = new List<int>();
        public bool Optional { get; set; }
    }

    public sealed class LogEntry
    {
        public string Text { get; set; }
        public bool Local { get; set; }
    }

    public sealed class PlayerView
    {
        public SideInfo Local { get; set; } = new SideInfo();
        public SideInfo Opponent { get; set; } = new SideInfo();
        public IReadOnlyList<CardInfo> Hand { get; set; } = new List<CardInfo>();
        public IReadOnlyList<LineInfo> Lines { get; set; } = new List<LineInfo>();
        public int Turn { get; set; }
        public bool LocalTurn { get; set; }
        public TurnPhase Phase { get; set; }
        public ControlHolder Control { get; set; }
        public ChoiceInfo Choice { get; set; }
        public IReadOnlyList<LogEntry> Log { get; set; } = new List<LogEntry>();
    }
}
