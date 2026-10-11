using Compile.Client.App;
using Compile.Client.Table;
using Compile.Client.UI;
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
            var won = match.LocalWon;

            Ui.Text(panel, ":\\> session complete", TextStyle.Label);
            var title = Ui.Text(panel, won ? "VICTORY" : "DEFEAT", TextStyle.Title);
            title.color = won ? Palette.Accent : Palette.Muted;
            Ui.Text(panel, $"{match.NameOf(match.WinnerSeat)} compiled all three Protocols.", TextStyle.Body);

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Leave", ButtonStyle.Quiet, () => InMatch.Leave(App));
            Ui.Button(row, "Rematch", ButtonStyle.Primary, Rematch);

            for (var i = 0; i < 3; i++)
                App.Mat.SetCompiled(won, i, compiled: true);
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
            App.Match = new MatchInfo(new MatchStart(last.LocalSeat, 1 - App.Match.WinnerSeat, last.Seed + 1,
                last.LocalName, last.OpponentName));
            App.Nav.Go(new DraftScreen());
        }
    }
}
