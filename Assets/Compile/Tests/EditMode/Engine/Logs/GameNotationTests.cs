using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Compile.Engine.Cards;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // The short notation (docs/notation.md): what's written replays to the same game.
    public class GameNotationTests
    {
        static Game Play(int seed, int maxMoves = 3000, double debugChance = 0.02, double undoChance = 0.1)
        {
            var random = new Random(seed);
            var game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols.ToList(), firstSeat: random.Next(2), seed: seed,
                name0: "Ann", name1: "Bob", effects: MainSet1.Effects);
            for (var i = 0; i < maxMoves && game.State.Stage != GameStage.Over; i++)
            {
                var intent = RandomBot.Choose(game, random, debugChance);
                Assert.That(game.Submit(intent).Accepted, Is.True);
                if (random.NextDouble() < undoChance && game.State.Stage != GameStage.Over && game.CheckUndo(intent.Seat) == null)
                    Assert.That(game.Submit(new Undo(intent.Seat)).Accepted, Is.True);
            }
            return game;
        }

        // Where every card is, by name: the same game, whatever the ids.
        static string Board(Game game)
        {
            var s = game.State;
            string Cards(System.Collections.Generic.IEnumerable<CardInstance> cards) => string.Join(" ", cards.Select(c => (c.FaceUp ? "" : "~") + c.Spec));
            return string.Join("\n", s.Players.Select(p => $"{p.Name} hand {Cards(p.Hand)} deck {Cards(p.Deck)} trash {Cards(p.Trash)} protocols {string.Join(" ", p.Protocols.Select(x => x.Name + (x.Compiled ? "*" : "")))}")
                .Concat(s.Lines.Select(l => $"line {l.Index}: {Cards(l.Stack(0))} | {Cards(l.Stack(1))}"))
                .Concat(new[] { $"turn {s.Turn} {s.Phase} active {s.ActiveSeat} control {s.Control} stage {s.Stage} winner {s.Winner}" }));
        }

        [Test]
        public void WrittenGamesReplayToTheSameGame()
        {
            for (var seed = 1; seed <= 200; seed++)
            {
                var game = Play(seed);
                var text = GameNotation.Write(game);
                Game replayed;
                try
                {
                    replayed = GameNotation.Read(text).Replay(Scenario.Catalog, MainSet1.Effects);
                }
                catch (FormatException e)
                {
                    Assert.Fail($"seed {seed}: {e.Message}\n{text}");
                    throw;
                }
                Assert.That(Board(replayed), Is.EqualTo(Board(game)), $"seed {seed}");
                Assert.That(GameNotation.Write(replayed), Is.EqualTo(text), $"seed {seed}");
            }
        }

        [Test]
        public void AGameStoppedPartwayReplaysToThatPoint()
        {
            var game = Play(12, maxMoves: 60);
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Playing));
            var replayed = GameNotation.Read(GameNotation.Write(game)).Replay(Scenario.Catalog, MainSet1.Effects);
            Assert.That(Board(replayed), Is.EqualTo(Board(game)));
        }

        [Test]
        public void UndoneMovesAreLeftOut()
        {
            var game = Game.CreateDrafted(Scenario.Catalog, new[] { "Death", "Fire", "Metal" }, new[] { "Plague", "Speed", "Water" },
                firstSeat: 0, seed: 5, name0: "Ann", name1: "Bob", effects: MainSet1.Effects);
            var hand = game.State.Players[0].Hand.ToList();
            game.Submit(new PlayCard(0, hand[0].Id, 0, faceUp: false));
            game.Submit(new Undo(0));
            game.Submit(new PlayCard(0, hand[1].Id, 1, faceUp: false));

            var text = GameNotation.Write(game);
            var codes = new System.Collections.Generic.Dictionary<string, string> { ["Death"] = "De", ["Fire"] = "Fi", ["Metal"] = "Me" };
            string Code(CardInstance c) => codes[c.Spec.Protocol] + c.Spec.Value;
            Assert.That(text, Does.Contain($"[Protocols \"Death, Fire, Metal | Plague, Speed, Water\"]"));
            Assert.That(text, Does.Contain($"1. ~{Code(hand[1])}b"));
            Assert.That(text, Does.Not.Contain($"~{Code(hand[0])}a"));
        }

        [Test]
        public void TheWrittenFormLooksLikeTheSpec()
        {
            var text = GameNotation.Write(Play(3, undoChance: 0));
            Assert.That(text, Does.StartWith("[P1 \"Ann\"] [P2 \"Bob\"] [First \""));
            Assert.That(text, Does.Contain("[Cards \"MainSet1\"]"));
            Assert.That(text, Does.Contain("[Pool \""));
            Assert.That(text, Does.Contain("\n1. "));
            Assert.That(text, Does.Contain(" | "));
            Assert.That(text, Does.Match(@"[A-Z][a-z]\d[abc]\("), "a face-up play with its text in parentheses");
            Assert.That(text, Does.Not.Contain("( "));
            Assert.That(text, Does.Not.Contain(" )"));
        }

        // The worked example in docs/notation.md is real: it replays.
        [Test]
        public void TheSpecsExampleReplays()
        {
            var doc = File.ReadAllText("docs/notation.md");
            var example = Regex.Match(doc, "<!-- example -->\\s*```\\s*\\n(.*?)```", RegexOptions.Singleline);
            Assert.That(example.Success, Is.True, "docs/notation.md has an example block");
            var game = GameNotation.Read(example.Groups[1].Value).Replay(Scenario.Catalog, MainSet1.Effects);
            Assert.That(game.State.Stage, Is.Not.EqualTo(GameStage.Draft));
        }

        [TestCase("[P1 \"A\"] [P2 \"B\"] [First \"P1\"] [Seed \"1\"]", "no [Pool] or [Protocols] tag")]
        [TestCase("[P2 \"B\"] [First \"P1\"] [Seed \"1\"] [Pool \"Death\"]", "no [P1] tag")]
        [TestCase("[P1 \"A\"] [P2 \"B\"] [First \"P3\"] [Seed \"1\"] [Pool \"Death, Fire, Metal, Plague, Speed, Water\"]", "[First] must be P1 or P2")]
        [TestCase("[P1 \"A\"] [P2 \"B\"] [First \"P1\"] [Seed \"1\"] [Pool \"Death, Fire, Metal, Plague, Speed, Water\"]\nZz", "there's no Protocol 'Zz'")]
        [TestCase("[P1 \"A\"] [P2 \"B\"] [First \"P1\"] [Seed \"1\"] [Protocols \"Death, Fire, Metal | Plague, Speed, Water\"]\nR", "token 1: the game did")]
        [TestCase("[P1 \"A\"] [P2 \"B\"] [First \"P1\"] [Seed \"1\"] [Protocols \"Death, Fire, Metal | Plague, Speed, Water\"]\n+De0", "token 1: the game did")]
        public void BadNotationSaysWhereItsWrong(string text, string message)
        {
            var error = Assert.Throws<FormatException>(() => GameNotation.Read(text).Replay(Scenario.Catalog, MainSet1.Effects));
            Assert.That(error.Message, Does.Contain(message));
        }

        [Test]
        public void AFullLogHasTheNotationFirst_AndStillLoads()
        {
            var game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols.ToList(), 0, 4, "Ann", "Bob", effects: MainSet1.Effects, record: true);
            var random = new Random(4);
            for (var i = 0; i < 80; i++)
                game.Submit(RandomBot.Choose(game, random));
            var notation = GameNotation.Write(game);
            var text = game.Record.ToText(notation);
            Assert.That(text.IndexOf("== Notation ==", StringComparison.Ordinal), Is.LessThan(text.IndexOf("== Moves ==", StringComparison.Ordinal)));
            var replayed = GameRecord.Load(text).Replay(Scenario.Catalog, MainSet1.Effects);
            Assert.That(Board(replayed), Is.EqualTo(Board(game)));
        }
    }
}
