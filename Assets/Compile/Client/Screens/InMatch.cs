using Compile.Client.App;
using Compile.Client.UI;
using Compile.Net;

namespace Compile.Client.Screens
{
    // Actions and wording shared by screens shown during a match.
    static class InMatch
    {
        // The host's browser holds the whole game (design: Hidden information and trust), so players are told.
        public const string HostingNotice = "Matches are hosted by the player who creates the room.";

        public static void AddLeaveAction(AppContext app) =>
            app.Hud.AddAction("Leave game", ButtonStyle.Quiet, () => Leave(app));

        // Online, leaving a game that's still going concedes it.
        public static void Leave(AppContext app)
        {
            app.Session.Leave();
            app.Match = null;
            app.Mat.ClearProtocols();
            app.Nav.Go(new TitleScreen());
        }

        public static string SessionErrorText(SessionError error) => error switch
        {
            SessionError.RoomNotFound => "Room not found. Check the code with the host and try again.",
            SessionError.RoomFull => "That room already has two players.",
            SessionError.InvalidCode => "Room codes are 6 letters and numbers.",
            SessionError.ServicesUnavailable => "Online play isn't available right now. Try again later, or play locally.",
            _ => "Couldn't connect. Check your connection and try again.",
        };
    }
}
