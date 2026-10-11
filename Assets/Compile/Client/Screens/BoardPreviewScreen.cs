using Compile.Client.App;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Net;
using UnityEngine;

namespace Compile.Client.Screens
{
    // Stand-in for the game board until Phase 2: shows the drafted Protocols from the player's seat and
    // offers debug actions to finish the match.
    public sealed class BoardPreviewScreen : TableScreen
    {
        const float Pitch = 42f;

        // The local lanes plus both Protocol rows, seen from the near edge of the mat, with room above for the HUD.
        static readonly Rect ViewArea = Rect.MinMaxRect(-7f, MatLayout.ToTable(0f, 3788f).y, 7f, MatLayout.ToTable(0f, 1150f).y);

        protected override void OnEnter()
        {
            App.CameraRig.Show(new CameraShot(ViewArea, Pitch, margin: 0.02f));
            App.Hud.SetStatus(":\\> phase 2 preview", "The game board arrives in Phase 2");
            App.Hud.AddAction("Simulate win", ButtonStyle.Secondary, () => Finish(App.Match.Start.LocalSeat));
            App.Hud.AddAction("Simulate loss", ButtonStyle.Secondary, () => Finish(1 - App.Match.Start.LocalSeat));
            if (App.Session is FakeSessionService fake)
                App.Hud.AddAction("Opponent leaves", ButtonStyle.Quiet, fake.SimulateOpponentLeft);
            InMatch.AddLeaveAction(App);
        }

        void Finish(int winnerSeat)
        {
            App.Match.WinnerSeat = winnerSeat;
            App.Nav.Go(new ResultScreen());
        }
    }
}
