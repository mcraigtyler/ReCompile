using System.Collections.Generic;

namespace Compile.Engine.Views
{
    // What the board needs to know about a change besides the new view: cards that moved in ways the view
    // alone can't tell apart (back to a hand rather than to the trash, between the two hands), reveals, and
    // notes to show for a moment. The engine makes these for one player (Game.UpdateFor), so like the view
    // they hold nothing that player may not see.
    public enum EventKind
    {
        Returned,       // field cards went back to their owners' hands (Cards)
        Given,          // Seat gave cards to ToSeat (Cards: only when the viewer gave them)
        Taken,          // Seat took cards from FromSeat's hand (Cards: only when they were the viewer's)
        Revealed,       // Owner's cards shown to both players (Cards, InHand)
        Note,           // a line of text to show (Text)
    }

    public sealed class EventInfo
    {
        public EventKind Kind { get; set; }
        public int Seat { get; set; } = -1;
        public int ToSeat { get; set; } = -1;
        public int FromSeat { get; set; } = -1;
        public int Owner { get; set; } = -1;
        public bool InHand { get; set; }
        public IReadOnlyList<CardInfo> Cards { get; set; } = new List<CardInfo>();
        public string Text { get; set; }        // Note and Revealed: what to show, or null
    }

    // One player's picture of the game after a change: the new view and what happened. Version counts the
    // changes, so a player who missed one can tell and ask for the whole view again.
    public sealed class MatchUpdate
    {
        public int Version { get; set; }
        public PlayerView View { get; set; }
        public IReadOnlyList<EventInfo> Events { get; set; } = new List<EventInfo>();
    }
}
