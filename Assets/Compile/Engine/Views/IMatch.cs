using System;

namespace Compile.Engine.Views
{
    public enum SeatKind
    {
        Human,      // a person at this screen
        Bot,        // RandomBot, the offline stand-in opponent
        Remote,     // a person in another browser
    }

    // A match as the screens see it: one player's view, updates after each change, and a way to send that
    // player's moves. A local match (MatchController: hot-seat or against the stand-in) runs the engine
    // itself; an online guest's match only has the views the host sends.
    public interface IMatch
    {
        // The seat whose view is shown. In hot-seat it changes when the screen is handed over.
        int Viewer { get; }
        PlayerView View { get; }

        bool Hotseat { get; }
        SeatKind KindOf(int seat);
        string NameOf(int seat);

        // The seat the game is waiting on, or -1 when it's over (or not known here).
        int SeatToMove { get; }

        // In hot-seat, the screen has to be handed to SeatToMove before they can move.
        bool NeedsHandoff { get; }
        void HandTo(int seat);

        // Sends a move. Returns why it isn't allowed if that's known at once, or null. A remote match may
        // refuse it later instead (Rejected).
        string Submit(Intent intent);

        // After every change, with the new view and what happened, for the viewer.
        event Action<MatchUpdate> Changed;
        event Action<string> Rejected;

        // The match can't go on (the engine failed, or the connection did). A lost connection can come
        // back (an online guest rejoining): then HasFailed is false again and an update follows.
        event Action<string> Failed;
        bool HasFailed { get; }

        // Why the game is on hold, to show over the board ("Bob lost connection..."), or null.
        string Waiting { get; }

        // Called every frame (bots take their turns, messages arrive).
        void Tick(float deltaTime);

        // The engine, when it runs here (null for an online guest): for game logs and debug tools.
        Game LocalGame { get; }
    }
}
