using System;
using System.Linq;
using Compile.Client.App;
using Compile.Engine.Cards;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Watching a loaded game: stepping forward and back, by move and by turn, always shows the game as it
    // was at that move, and a game taken over from any move carries on from there.
    public class ReplayControllerTests
    {
        static Game Played(int seed)
        {
            var random = new Random(seed);
            var game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols.ToList(), 0, seed, "Ann", "Bob", effects: MainSet1.Effects);
            for (var i = 0; i < 3000 && game.State.Stage != GameStage.Over; i++)
                game.Submit(RandomBot.Choose(game, random));
            return game;
        }

        static string Snapshot(Game game) => GameNotation.Write(game);

        [Test]
        public void StartsAfterTheDraft()
        {
            var replay = new ReplayController(Played(1));
            Assert.That(replay.Match.Game.State.Stage, Is.EqualTo(GameStage.Playing));
            Assert.That(replay.Position, Is.EqualTo(replay.First));
            Assert.That(replay.AtStart, Is.True);
            Assert.That(replay.LastStep, Is.Empty);
            replay.Back();
            Assert.That(replay.Position, Is.EqualTo(replay.First), "can't go back into the draft");
        }

        [Test]
        public void ForwardAndBackShowTheGameAtThatMove()
        {
            var full = Played(2);
            var replay = new ReplayController(full);
            for (var i = 0; i < 30; i++)
                replay.Forward();
            var after30 = Snapshot(replay.Match.Game);
            replay.Forward();
            replay.Forward();
            replay.Back();
            replay.Back();
            Assert.That(Snapshot(replay.Match.Game), Is.EqualTo(after30));
            Assert.That(replay.Match.Game.AcceptedIntents.Count, Is.EqualTo(replay.Position));

            replay.ToEnd();
            Assert.That(replay.AtEnd, Is.True);
            Assert.That(replay.Over, Is.True);
            Assert.That(Snapshot(replay.Match.Game), Is.EqualTo(Snapshot(full)));
        }

        [Test]
        public void TurnStepsLandOnTheStartOfATurn()
        {
            var replay = new ReplayController(Played(3));
            var turn = replay.Turn;
            replay.NextTurn();
            Assert.That(replay.Turn, Is.EqualTo(turn + 1));
            replay.NextTurn();
            replay.Forward();
            var mid = replay.Turn;
            replay.PreviousTurn();
            Assert.That(replay.Turn, Is.EqualTo(mid), "back to the start of this turn first");
            replay.PreviousTurn();
            Assert.That(replay.Turn, Is.EqualTo(mid - 1), "then to the turn before");
        }

        [Test]
        public void EachMoveHasItsNotation()
        {
            var replay = new ReplayController(Played(4));
            replay.Forward();
            Assert.That(replay.LastStep, Is.Not.Empty);
            Assert.That(replay.LastSeat, Is.EqualTo(replay.Match.Game.AcceptedIntents.Last().Seat));
        }

        [Test]
        public void AGameTakenOverCarriesOnFromThere_WithItsLog()
        {
            var replay = new ReplayController(Played(5));
            for (var i = 0; i < 20; i++)
                replay.Forward();
            var game = replay.Build(replay.Position, record: true);
            Assert.That(Snapshot(game), Is.EqualTo(Snapshot(replay.Match.Game)));
            Assert.That(game.Record.Moves, Has.Count.EqualTo(replay.Position));

            var random = new Random(9);
            for (var i = 0; i < 50 && game.State.Stage != GameStage.Over; i++)
                Assert.That(game.Submit(RandomBot.Choose(game, random)).Accepted, Is.True);
            var replayed = GameRecord.Load(game.Record.ToText()).Replay(Scenario.Catalog, MainSet1.Effects);
            Assert.That(Snapshot(replayed), Is.EqualTo(Snapshot(game)));
        }

        [Test]
        public void LoadsTheNotationSectionOfAGameLog()
        {
            var game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols.ToList(), 0, 6, "Ann", "Bob", effects: MainSet1.Effects, record: true);
            var random = new Random(6);
            for (var i = 0; i < 60; i++)
                game.Submit(RandomBot.Choose(game, random));
            var log = game.Record.ToText(GameNotation.Write(game));
            var loaded = GameNotation.Read(GameNotation.FromLog(log)).Replay(Scenario.Catalog, MainSet1.Effects);
            Assert.That(Snapshot(loaded), Is.EqualTo(Snapshot(game)));
        }
    }
}
