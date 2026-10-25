using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Cards;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Game logs for bug reports: a saved log replays to exactly the same game, at any move.
    // Logs that people share go next to this file, with a test that replays them to the move in question.
    public class GameRecordTests
    {
        static Game PlayRecorded(int seed, CardEffects effects, int maxMoves = 3000, double debugChance = 0)
        {
            var random = new Random(seed);
            var game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols.ToList(), firstSeat: random.Next(2), seed: seed,
                name0: "Ann", name1: "Bob", effects: effects, record: true);
            for (var i = 0; i < maxMoves && game.State.Stage != GameStage.Over; i++)
            {
                var intent = RandomBot.Choose(game, random, debugChance);
                Assert.That(game.Submit(intent).Accepted, Is.True);
                if (random.NextDouble() < 0.1 && game.State.Stage != GameStage.Over && game.CheckUndo(intent.Seat) == null)
                    Assert.That(game.Submit(new Undo(intent.Seat)).Accepted, Is.True);
            }
            return game;
        }

        [Test]
        public void SavedLogsReplayToTheSameGame()
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var game = PlayRecorded(seed, MainSet1.Effects, debugChance: 0.02);
                var text = game.Record.ToText();

                var loaded = GameRecord.Load(text);
                Assert.That(loaded.Moves.Select(GameRecord.Format), Is.EqualTo(game.Record.Moves.Select(GameRecord.Format)), $"seed {seed}");
                var replayed = loaded.Replay(Scenario.Catalog, MainSet1.Effects);
                Assert.That(replayed.Record.Transcript, Is.EqualTo(game.Record.Transcript), $"seed {seed}: the replay went differently");
                Assert.That(replayed.Record.ToText(), Is.EqualTo(text), $"seed {seed}");
            }
        }

        [Test]
        public void ReplayStopsAtAnyMove()
        {
            var game = PlayRecorded(7, MainSet1.Effects);
            var loaded = GameRecord.Load(game.Record.ToText());
            foreach (var moves in new[] { 0, 1, 5, 40, loaded.Moves.Count / 2 })
            {
                var partial = loaded.Replay(Scenario.Catalog, MainSet1.Effects, moves);
                Assert.That(partial.Record.Moves, Has.Count.EqualTo(moves));
                Assert.That(game.Record.Transcript, Does.StartWith(partial.Record.Transcript), $"after {moves} moves");
            }
        }

        [Test]
        public void TheTranscriptSaysWhatWasAskedAndShowsTheBoard()
        {
            var text = PlayRecorded(3, MainSet1.Effects).Record.ToText();
            Assert.That(text, Does.Contain("format: 1"));
            Assert.That(text, Does.Contain("cards: MainSet1"));
            Assert.That(text, Does.Contain("== Moves =="));
            Assert.That(text, Does.Contain("[Setup]"));
            Assert.That(text, Does.Contain("Ann drafts "));
            Assert.That(text, Does.Contain("  Question: "));
            Assert.That(text, Does.Contain("    1st line: Ann "));
            Assert.That(text, Does.Contain("Bob: hand ["));
            Assert.That(text, Does.Contain(" undoes their last move"));
        }

        [Test]
        public void HiddenCardsAreNamedInTheLog()
        {
            var game = Game.CreateDrafted(Scenario.Catalog, new[] { "Death", "Fire", "Metal" }, new[] { "Plague", "Speed", "Water" },
                firstSeat: 0, seed: 5, name0: "Ann", name1: "Bob", record: true);
            var card = game.State.Players[0].Hand[0];
            game.Submit(new PlayCard(0, card.Id, 0, faceUp: false));
            Assert.That(game.Record.Transcript, Does.Contain($"Ann played {card.Spec} face-down into the 1st line."));
            Assert.That(game.Record.Transcript, Does.Contain($"~{card.Spec}#{card.Id}"));
        }

        [Test]
        public void MovesThatArentAllowedAreNotedButNotReplayed()
        {
            var game = Game.CreateDrafted(Scenario.Catalog, new[] { "Death", "Fire", "Metal" }, new[] { "Plague", "Speed", "Water" },
                firstSeat: 0, seed: 5, name0: "Ann", name1: "Bob", record: true);
            Assert.That(game.Submit(new Refresh(1)).Accepted, Is.False);
            Assert.That(game.Record.Moves, Is.Empty);
            Assert.That(game.Record.Transcript, Does.Contain("(not allowed) Bob refreshes: "));
        }

        [Test]
        public void WhenTheEngineFails_TheLogKeepsTheMoveAndTheError()
        {
            var broken = new CardEffects("Broken");
            foreach (var protocol in new[] { "Death", "Fire", "Metal" })
                for (var value = 0; value <= 6; value++)
                    broken.Add($"{protocol}_{value}", new TestCard { MiddleText = e => throw new InvalidOperationException("card text bug") });
            var game = Game.CreateDrafted(Scenario.Catalog, new[] { "Death", "Fire", "Metal" }, new[] { "Plague", "Speed", "Water" },
                firstSeat: 0, seed: 5, name0: "Ann", name1: "Bob", effects: broken, record: true);
            var card = game.State.Players[0].Hand.First(c => c.Spec.Protocol == "Death");

            Assert.Throws<InvalidOperationException>(() => game.Submit(new PlayCard(0, card.Id, 0, faceUp: true)));
            var text = game.Record.ToText();
            Assert.That(text, Does.Contain("error: System.InvalidOperationException: card text bug"));
            Assert.That(text, Does.Contain($"1 p0 play {card.Id} 0 up  # the engine failed on this move"));
            Assert.That(text, Does.Contain("!! The engine failed on this move:"));

            var loaded = GameRecord.Load(text);
            Assert.That(loaded.Error, Does.Contain("card text bug"));
            Assert.Throws<InvalidOperationException>(() => loaded.Replay(Scenario.Catalog, broken), "the replay fails the same way");
        }

        [Test]
        public void LoadReadsEveryKindOfMove()
        {
            var moves = new[]
            {
                "p0 pick Death", "p1 play 17 2 down", "p0 play 3 0 up", "p0 refresh", "p0 end", "p1 undo", "p1 concede",
                "p0 debug draw-1", "p1 rearrange 0 2,0,1", "p0 decline", "p0 line 1", "p0 options 1,0", "p1 cards 4,9", "p1 cards -",
            };
            var text = "format: 1\nseed: 9\nfirst: 1\nnames: Ann | Bob\npool: Death, Fire\n\n== Moves ==\n" +
                       string.Join("\n", moves.Select((m, i) => $"{i + 1} {m}   # a comment")) + "\n\n== Transcript ==\n1 p0 nonsense\n";
            var record = GameRecord.Load(text);
            Assert.That(record.Moves.Select(GameRecord.Format), Is.EqualTo(moves));
            Assert.That(record.Seed, Is.EqualTo(9));
            Assert.That(record.FirstSeat, Is.EqualTo(1));
            Assert.That(record.Names, Is.EqualTo(new[] { "Ann", "Bob" }));
            Assert.That(record.Pool, Is.EqualTo(new[] { "Death", "Fire" }));
        }

        [TestCase("format: 1\nseed: 1\n== Moves ==\n", "no 'pool' or 'drafted'")]
        [TestCase("format: 1\npool: Death\n== Moves ==\n2 p0 end\n", "expected move 1")]
        [TestCase("format: 1\npool: Death\n== Moves ==\n1 p0 jump\n", "unknown move 'jump'")]
        [TestCase("format: 99\npool: Death\n", "newer than this version")]
        public void LoadSaysWhatIsWrongWithABadLog(string text, string message)
        {
            var error = Assert.Throws<FormatException>(() => GameRecord.Load(text));
            Assert.That(error.Message, Does.Contain(message));
        }

        [Test]
        public void GamesDontKeepALogUnlessAsked()
        {
            var game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols, 0, 1);
            Assert.That(game.Record, Is.Null);
        }
    }
}
