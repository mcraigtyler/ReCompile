using Compile.Client.App;
using Compile.Client.UI;
using Compile.Net;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    // Sets up a hot-seat game: two people sharing this screen, passing it between turns.
    public sealed class LocalGameScreen : MenuScreen
    {
        TMP_InputField _first;
        TMP_InputField _second;
        ButtonSkin _start;

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> compile.exe --local", TextStyle.Label);
            Ui.Text(panel, "Local game", TextStyle.Heading);
            Ui.Text(panel, "Two players at one screen. You'll pass it between turns; each hand stays hidden until its player is ready.",
                TextStyle.Small);

            Ui.Text(panel, "Player 1", TextStyle.Label);
            _first = Ui.Input(panel, "Name", PlayerProfile.MaxNameLength);
            _first.text = App.Profile.DisplayName;
            _first.onValueChanged.AddListener(_ => Refresh());

            Ui.Text(panel, "Player 2", TextStyle.Label);
            _second = Ui.Input(panel, "Name", PlayerProfile.MaxNameLength);
            _second.text = "Player 2";
            _second.onValueChanged.AddListener(_ => Refresh());

            Ui.Text(panel, "Who goes first is chosen at random. They draft first, too.", TextStyle.Small);

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Back", ButtonStyle.Quiet, () => App.Nav.Go(new TitleScreen()));
            _start = Ui.Button(row, "Start", ButtonStyle.Primary, Begin);
            Refresh();
        }

        string First => _first.text.Trim();
        string Second => _second.text.Trim();

        void Refresh() => _start.Interactable = First.Length > 0 && Second.Length > 0 && First != Second;

        void Begin()
        {
            var seed = Random.Range(1, int.MaxValue);
            var firstSeat = Random.Range(0, 2);
            App.Profile.DisplayName = First;
            App.Match = new MatchInfo(new MatchStart(localSeat: 0, firstSeat, seed, First, Second), hotseat: true);
            App.Nav.Go(new DraftScreen());
        }
    }
}
