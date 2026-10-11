using Compile.Client.App;
using Compile.Client.UI;

namespace Compile.Client.Screens
{
    // Actions shared by screens shown during a match.
    static class InMatch
    {
        public static void AddLeaveAction(AppContext app) =>
            app.Hud.AddAction("Leave game", ButtonStyle.Quiet, () => Leave(app));

        public static void Leave(AppContext app)
        {
            app.Session.Leave();
            app.Match = null;
            app.Mat.ClearProtocols();
            app.Nav.Go(new TitleScreen());
        }
    }
}
