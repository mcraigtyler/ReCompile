using Compile.Client.App;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine;
using Compile.Net;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    // Shown when a match ends. The panel rests on the local player's lanes; the winner's Protocols flip
    // to their Compiled side in the slots above it.
    public sealed class ResultScreen : MenuScreen
    {
        const float Pitch = 24f;

        protected override float Width => 900f;
        protected override Vector2 Spot => MatLayout.ToTable(699f, 3000f);

        protected override void Build(RectTransform panel)
        {
            var match = App.Match;
            var draw = match.WinnerSeat < 0;
            var won = !draw && match.LocalWon;
            var winner = draw ? null : match.SeatName(match.WinnerSeat);
            var loser = draw ? null : match.SeatName(1 - match.WinnerSeat);

            Ui.Text(panel, ":\\> session complete", TextStyle.Label);
            string heading;
            if (draw)
                heading = "DRAW";
            else if (match.Hotseat)
                heading = $"{winner.ToUpperInvariant()} WINS";
            else
                heading = won ? "VICTORY" : "DEFEAT";
            var title = Ui.Text(panel, heading, TextStyle.Title);
            title.color = draw || (!match.Hotseat && !won) ? Palette.Muted : Palette.Accent;

            string reason;
            switch (match.EndReason)
            {
                case EndReason.Conceded:
                    reason = $"{loser} conceded.";
                    break;
                case EndReason.Stalemate:
                    reason = draw ? "Neither player could act, with the same number of Protocols compiled."
                        : $"Neither player could act. {winner} had more Protocols compiled.";
                    break;
                default:
                    reason = $"{winner} compiled all three Protocols.";
                    break;
            }
            Ui.Text(panel, reason, TextStyle.Body);

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Leave", ButtonStyle.Quiet, () => InMatch.Leave(App));
            Ui.Button(row, "Rematch", ButtonStyle.Primary, Rematch);

            if (match.EndReason == EndReason.Compiled && match.Controller != null)
            {
                // Flip the winner's Protocols, on whichever side of the mat they ended up.
                var winnerAtBottom = match.WinnerSeat == match.Controller.Viewer;
                for (var i = 0; i < 3; i++)
                    App.Mat.SetCompiled(winnerAtBottom, i, compiled: true);
            }
        }

        // Frame both Protocol rows and the panel.
        protected override CameraShot Frame()
        {
            var panelSize = Panel != null ? Panel.sizeDelta / Ui.PixelsPerUnit : new Vector2(9f, 5f);
            var bottom = Spot.y - panelSize.y / 2f;
            var area = Rect.MinMaxRect(-7f, bottom, 7f, MatLayout.ProtocolRows.yMax);
            return new CameraShot(area, Pitch, margin: 0.06f);
        }

        // Offline rematch: same players, a new draft. The loser of the last game goes first.
        void Rematch()
        {
            var last = App.Match.Start;
            var first = App.Match.WinnerSeat < 0 ? 1 - last.FirstSeat : 1 - App.Match.WinnerSeat;
            App.Match = new MatchInfo(new MatchStart(last.LocalSeat, first, last.Seed + 1,
                last.LocalName, last.OpponentName), App.Match.Hotseat);
            App.Nav.Go(new DraftScreen());
        }
    }
}
