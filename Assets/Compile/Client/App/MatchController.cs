using System;
using System.Collections.Generic;
using Compile.Engine;
using Compile.Engine.Views;

namespace Compile.Client.App
{
    public enum SeatKind
    {
        Human,
        Bot,
    }

    // Runs a match on a local engine: the draft, the game, and the result. Each seat is played by a person
    // at this screen (hot-seat) or by RandomBot (the offline stand-in opponent). Screens show the view of
    // the human seat currently at the screen, the Viewer, and send that player's moves through Submit.
    public sealed class MatchController
    {
        const float BotThinkSeconds = 0.9f;
        const float BotQuickSeconds = 0.35f;      // ending a turn or answering a quick question

        readonly SeatKind[] _seats;
        readonly Random _botRandom;
        float _botWaited;

        public Game Game { get; private set; }
        public int Viewer { get; private set; }

        public bool Hotseat => _seats[0] == SeatKind.Human && _seats[1] == SeatKind.Human;

        // Called after every accepted move, with the events it caused.
        public event Action<IReadOnlyList<GameEvent>> Changed;

        // Called once if the engine throws. The game can't go on after that, but its log can still be saved.
        public event Action<Exception> Failed;

        public Exception Failure { get; private set; }

        public MatchController(Game game, SeatKind seat0, SeatKind seat1, int viewer, int botSeed)
        {
            Game = game;
            _seats = new[] { seat0, seat1 };
            Viewer = viewer;
            _botRandom = new Random(botSeed);
        }

        public SeatKind KindOf(int seat) => _seats[seat];

        // Shows another game in its place (a replay going back a move), seen from the same seat.
        public void Reset(Game game)
        {
            Game = game;
            Failure = null;
            _botWaited = 0f;
            Changed?.Invoke(Array.Empty<GameEvent>());
        }

        public string NameOf(int seat) => Game.State.Players[seat].Name;

        public PlayerView View => Game.ViewFor(Viewer);

        // The seat the engine is waiting on, or -1 once the game is over.
        public int SeatToMove => RandomBot.SeatToMove(Game);

        // In hot-seat, the screen has to be handed to the other player before they can move.
        public bool NeedsHandoff => Hotseat && Game.State.Stage == GameStage.Playing && SeatToMove >= 0 && SeatToMove != Viewer;

        // The viewer may move now (it's their pick, action, choice, or end of turn).
        public bool ViewerToMove => SeatToMove == Viewer && _seats[Viewer] == SeatKind.Human;

        // The next player takes the screen (after the pass screen).
        public void HandTo(int seat)
        {
            if (_seats[seat] != SeatKind.Human || seat == Viewer)
                return;
            Viewer = seat;
            Changed?.Invoke(Array.Empty<GameEvent>());
        }

        // Submits a move for the viewer (or for the seat given in the intent). Returns the engine's reason
        // if the move isn't allowed, or null.
        public string Submit(Intent intent)
        {
            if (Failure != null)
                return "The game stopped after an error. Save the game log from the Debug menu.";
            IntentResult result;
            try
            {
                result = Game.Submit(intent);
            }
            catch (Exception e)
            {
                Failure = e;
                UnityEngine.Debug.LogException(e);
                Failed?.Invoke(e);
                return "Something went wrong.";
            }
            if (!result.Accepted)
                return result.Error;
            _botWaited = 0f;
            Changed?.Invoke(result.Events);
            return null;
        }

        // Lets bots take their turns, a move at a time, with a pause so people can follow along.
        public void Tick(float deltaTime)
        {
            var seat = Failure == null ? SeatToMove : -1;
            if (seat < 0 || _seats[seat] != SeatKind.Bot)
                return;

            _botWaited += deltaTime;
            var quick = Game.AwaitingEndTurn || Game.Pending != null || Game.State.Stage == GameStage.Draft;
            if (_botWaited < (quick ? BotQuickSeconds : BotThinkSeconds))
                return;

            var intent = RandomBot.Choose(Game, _botRandom);
            if (intent != null)
                Submit(intent);
        }

        // A random legal move for the viewer (Debug menu: "Auto-play").
        public void AutoPlayViewer()
        {
            if (SeatToMove != Viewer)
                return;
            var intent = RandomBot.Choose(Game, _botRandom);
            if (intent != null)
                Submit(intent);
        }
    }
}
