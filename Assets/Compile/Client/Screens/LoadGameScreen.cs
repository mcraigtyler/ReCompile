using System;
using Compile.Client.App;
using Compile.Client.Platform;
using Compile.Client.UI;
using Compile.Engine;
using Compile.Engine.Cards;
using Compile.Net;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    // Loads a game to watch (and take over from any move): its notation (docs/notation.md), or a whole
    // saved game log. Only reached from the title screen.
    public sealed class LoadGameScreen : MenuScreen
    {
        TextMeshProUGUI _status;
        ButtonSkin _watch;
        Game _loaded;

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> compile.exe --load", TextStyle.Label);
            Ui.Text(panel, "Load game", TextStyle.Heading);
            Ui.Text(panel, "Watch a game move by move from its notation (Debug → Copy game notation) or a saved game log, " +
                           "then carry on playing it from any move.", TextStyle.Small);
#if UNITY_WEBGL && !UNITY_EDITOR
            const string paste = "Paste or open a game…";
#else
            const string paste = "Paste a game from the clipboard";
#endif
            Ui.Button(panel, paste, ButtonStyle.Secondary, Ask);
            _status = Ui.Text(panel, "Nothing loaded yet.", TextStyle.Small);

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Back", ButtonStyle.Quiet, () => App.Nav.Go(new TitleScreen()));
            _watch = Ui.Button(row, "Watch", ButtonStyle.Primary, Watch);
            _watch.Interactable = false;
        }

        public override void Tick(float deltaTime) => TextDialog.Poll();

        void Ask()
        {
            if (TextDialog.Open)
                return;
            TextDialog.Ask("Load game", "Paste a game's notation or a whole game log, or choose a saved .txt file.", Load);
        }

        void Load(string text)
        {
            if (text == null)
                return;
            _loaded = null;
            _watch.Interactable = false;
            if (string.IsNullOrWhiteSpace(text))
            {
                Show("There's nothing to load: the clipboard is empty.", error: true);
                return;
            }
            try
            {
                _loaded = Parse(text);
                var s = _loaded.State;
                if (s.Stage == GameStage.Draft)
                {
                    _loaded = null;
                    Show("That game stops during the draft, so there's nothing to watch yet.", error: true);
                    return;
                }
                var result = s.Stage != GameStage.Over ? $"stopped at turn {s.Turn}"
                    : s.Winner < 0 ? "a draw" : $"{s.Players[s.Winner].Name} won";
                Show($"{s.Players[0].Name} vs {s.Players[1].Name}: {_loaded.AcceptedIntents.Count} moves, {result}.", error: false);
                _watch.Interactable = true;
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is ArgumentException || e is System.Collections.Generic.KeyNotFoundException)
            {
                Show($"Couldn't load that game. {e.Message}", error: true);
            }
        }

        // Notation (on its own, or the first section of a game log), or an older game log's moves.
        Game Parse(string text)
        {
            var notation = GameNotation.FromLog(text);
            if (notation.Contains("[P1 "))
                return GameNotation.Read(notation).Replay(App.Catalog, MainSet1.Effects);
            if (text.Contains("== Moves =="))
                return GameRecord.Load(text).Replay(App.Catalog, MainSet1.Effects);
            throw new FormatException("It isn't game notation or a game log.");
        }

        void Show(string message, bool error)
        {
            _status.text = message;
            _status.color = error ? Palette.Danger : Palette.Ink;
        }

        void Watch()
        {
            if (_loaded == null)
                return;
            var replay = new ReplayController(_loaded);
            var setup = _loaded.Setup;
            App.Match = new MatchInfo(new MatchStart(localSeat: 0, setup.FirstSeat, (int)setup.Seed, setup.Names[0], setup.Names[1]), hotseat: true)
            {
                Controller = replay.Match,
                Replay = replay,
            };
            App.Nav.Go(new BoardScreen());
        }
    }
}
