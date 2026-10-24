using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Views
{
    // What one player can see of a game. The board and HUD render only from these types. The rules
    // engine produces them (Game.ViewFor), with everything the viewer may not see left out; the Phase 2
    // board stand-in and, later, the network (Phase 6) produce the same types. Everything is from the
    // viewer's seat: "local" is the viewer, and lines run from the viewer's left to right.

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
        public bool Revealed { get; set; }          // shown to both players for now by card text

        public bool IsHidden => CardId == null;

        // In the field, the engine sets the value card text gives it; otherwise it's the printed value
        // (face-down: 2).
        public int Value
        {
            get => _value ?? (FaceUp ? PrintedValue : FaceDownValue);
            set => _value = value;
        }

        int? _value;
    }

    // One player's cards in a line, bottom (nearest the Protocol) to top. The last card is uncovered.
    public sealed class StackInfo
    {
        public IReadOnlyList<CardInfo> Cards { get; set; } = new List<CardInfo>();

        // The engine sets the total card text gives the line; otherwise it's the sum of the card values.
        public int Total
        {
            get => _total ?? Cards.Sum(c => c.Value);
            set => _total = value;
        }

        int? _total;
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
        public IReadOnlyList<CardInfo> RevealedHand { get; set; } = new List<CardInfo>();   // hand cards revealed to both players
    }

    // Whose Protocols a Rearrange or Swap choice is about.
    public enum ChoiceSide
    {
        Either,         // the chooser picks
        Local,
        Opponent,
    }

    // A decision the viewer has to make, such as "Flip 1 other card".
    public sealed class ChoiceInfo
    {
        public ChoiceKind Kind { get; set; } = ChoiceKind.FieldCards;
        public string Prompt { get; set; }
        public IReadOnlyCollection<int> ValidCards { get; set; } = new List<int>();   // Order: lines up with Options when they're cards
        public IReadOnlyCollection<int> ValidLines { get; set; } = new List<int>();   // from the viewer's seat
        public IReadOnlyList<string> Options { get; set; } = new List<string>();      // Option and Order
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 1;
        public bool Optional { get; set; }
        public ChoiceSide Side { get; set; }                // Rearrange and Swap
        public LinePurpose Purpose { get; set; }            // Line
        public bool FaceUpAllowed { get; set; }             // Play
        public bool FaceDownAllowed { get; set; }           // Play
        public string SourceCardId { get; set; }            // the card whose text asks ("Fire_0"), if any
        public int SourceInstanceId { get; set; }
        public int SubjectInstanceId { get; set; }          // the card the question is about (e.g. the one being shifted), or 0
    }

    public sealed class DraftInfo
    {
        public IReadOnlyList<string> Available { get; set; } = new List<string>();
        public IReadOnlyList<string> LocalPicks { get; set; } = new List<string>();      // left, middle, right
        public IReadOnlyList<string> OpponentPicks { get; set; } = new List<string>();
        public bool LocalPick { get; set; }         // it's the viewer's pick
        public bool LocalConfirm { get; set; }      // the viewer has made this round's picks and must confirm them
        public bool CurrentSeatIsLocal { get; set; }   // the viewer is picking or confirming
        public int PicksRemaining { get; set; }     // in the current player's turn
    }

    public enum Outcome
    {
        None,
        LocalWon,
        OpponentWon,
        Draw,
    }

    public sealed class LogEntry
    {
        public string Text { get; set; }
        public bool Local { get; set; }
    }

    public sealed class PlayerView
    {
        public GameStage Stage { get; set; } = GameStage.Playing;
        public DraftInfo Draft { get; set; }        // only during the draft
        public Outcome Outcome { get; set; }
        public EndReason EndReason { get; set; }
        public bool CanAct { get; set; }            // the viewer may Play or Refresh now
        public bool CanRefresh { get; set; }
        public bool CanEndTurn { get; set; }        // the viewer's turn is done and waits for End turn
        public bool CanUndo { get; set; }
        public string UndoBlockedReason { get; set; }   // why Undo is unavailable on the viewer's turn
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
