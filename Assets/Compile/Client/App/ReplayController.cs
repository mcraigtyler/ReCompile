using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Engine;

namespace Compile.Client.App
{
    // Steps through a finished or saved game, one decision at a time, on a MatchController the board shows.
    // Forward plays the next decision on the game shown, so it animates; going back, or jumping, rebuilds
    // the game from the start (the engine is deterministic, so that's the same game). The draft is skipped:
    // a replay starts once the game begins.
    public sealed class ReplayController
    {
        readonly GameSetup _setup;
        readonly IReadOnlyList<Intent> _moves;
        readonly IReadOnlyList<string> _steps;          // each move's notation
        readonly int[] _turnAfter;                      // the turn number after each number of moves
        readonly bool[] _overAfter;

        public MatchController Match { get; }
        public int Position { get; private set; }       // moves made so far
        public int First { get; }                        // the first position after the draft
        public int Count => _moves.Count;
        public IReadOnlyList<string> Names => _setup.Names;

        // full: the game played out to the end of what was loaded.
        public ReplayController(Game full)
        {
            _setup = full.Setup ?? throw new ArgumentException("The game has no setup to replay from.");
            _moves = full.AcceptedIntents.ToList();
            _steps = GameNotation.Steps(full);

            var game = _setup.NewGame();
            _turnAfter = new int[_moves.Count + 1];
            _overAfter = new bool[_moves.Count + 1];
            First = -1;
            for (var i = 0; ; i++)
            {
                _turnAfter[i] = game.State.Turn;
                _overAfter[i] = game.State.Stage == GameStage.Over;
                if (First < 0 && game.State.Stage != GameStage.Draft)
                    First = i;
                if (i == _moves.Count)
                    break;
                game.Submit(_moves[i]);
            }
            if (First < 0)
                throw new ArgumentException("The game didn't get past the draft.");

            Position = First;
            Match = new MatchController(Build(First), SeatKind.Human, SeatKind.Human, viewer: 0, botSeed: (int)_setup.Seed);
        }

        // A new game with the first count decisions made.
        public Game Build(int count, bool record = false)
        {
            var game = _setup.NewGame(record: record);
            for (var i = 0; i < count; i++)
                game.Submit(_moves[i]);
            return game;
        }

        public bool AtStart => Position <= First;
        public bool AtEnd => Position >= _moves.Count;
        public bool Over => _overAfter[Position];
        public int Turn => _turnAfter[Position];

        // The last move's notation, or "" at the start.
        public string LastStep => Position > First ? _steps[Position - 1] : "";

        // Who made the last move.
        public int LastSeat => Position > First ? _moves[Position - 1].Seat : -1;

        public void Forward()
        {
            if (AtEnd)
                return;
            Position++;
            Match.Submit(_moves[Position - 1]);
        }

        public void Back() => Seek(Position - 1);

        // To the start of the next turn, or of this one (or the one before, if this one just started).
        public void NextTurn()
        {
            var p = Position;
            while (p < _moves.Count && _turnAfter[p] == Turn)
                p++;
            Seek(p);
        }

        public void PreviousTurn()
        {
            var start = TurnStart(Position);
            Seek(start == Position && start > First ? TurnStart(start - 1) : start);
        }

        // The first position with the same turn as this one.
        int TurnStart(int position)
        {
            while (position > First && _turnAfter[position - 1] == _turnAfter[position])
                position--;
            return position;
        }

        public void Seek(int position)
        {
            position = Math.Max(First, Math.Min(_moves.Count, position));
            if (position == Position)
                return;
            if (position == Position + 1)
            {
                Forward();
                return;
            }
            Position = position;
            Match.Reset(Build(position));
        }

        public void ToStart() => Seek(First);
        public void ToEnd() => Seek(_moves.Count);

        // Whose screen a game taken over from here starts on.
        public int SeatToMove(Game game) => Math.Max(0, RandomBot.SeatToMove(game));
    }
}
