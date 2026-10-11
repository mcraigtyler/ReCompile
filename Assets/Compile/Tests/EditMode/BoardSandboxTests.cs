using System.IO;
using System.Linq;
using Compile.Client.Board;
using Compile.Engine.Cards;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class BoardSandboxTests
    {
        static readonly string[] Mine = { "Fire", "Speed", "Light" };
        static readonly string[] Theirs = { "Metal", "Death", "Water" };   // their left → right; Water faces my Fire

        static BoardSandbox Create()
        {
            var db = CardDatabase.Parse(File.ReadAllText("Assets/Compile/Data/cards.json"), File.ReadAllText("Assets/Compile/Data/protocols.json"));
            return new BoardSandbox(db, Mine, Theirs, "Ada", "Daemon_7", seed: 7);
        }

        [Test]
        public void DealsFiveCardsEachFromEighteenCardDecks()
        {
            var view = Create().View();

            Assert.That(view.Hand, Has.Count.EqualTo(5));
            Assert.That(view.Local.DeckCount, Is.EqualTo(13));
            Assert.That(view.Opponent.HandCount, Is.EqualTo(5));
            Assert.That(view.Opponent.DeckCount, Is.EqualTo(13));
            Assert.That(view.Hand.All(c => Mine.Contains(c.Protocol)), Is.True);
        }

        [Test]
        public void LinesPairEachProtocolWithTheOpposingOne()
        {
            var view = Create().View();

            Assert.That(view.Lines.Select(l => l.LocalProtocol.Name), Is.EqualTo(Mine));
            Assert.That(view.Lines.Select(l => l.OpponentProtocol.Name), Is.EqualTo(new[] { "Water", "Death", "Metal" }));
        }

        [Test]
        public void FaceUpOnlyIntoAMatchingLine()
        {
            var sandbox = Create();
            var card = sandbox.View().Hand.First(c => c.Protocol == "Fire" || c.Protocol == "Speed" || c.Protocol == "Light");
            var matching = Mine.ToList().IndexOf(card.Protocol);
            var other = (matching + 1) % 3;

            Assert.That(sandbox.Play(card.InstanceId, other, faceUp: true, out var error), Is.False);
            Assert.That(error, Does.Contain("face-down"));
            Assert.That(sandbox.Play(card.InstanceId, matching, faceUp: true, out _), Is.True);
            Assert.That(sandbox.View().Lines[matching].Local.Top.InstanceId, Is.EqualTo(card.InstanceId));
        }

        [Test]
        public void FaceDownAnywhereForTwo()
        {
            var sandbox = Create();
            var card = sandbox.View().Hand[0];
            var line = (Mine.ToList().IndexOf(card.Protocol) + 1) % 3;

            Assert.That(sandbox.Play(card.InstanceId, line, faceUp: false, out _), Is.True);
            var top = sandbox.View().Lines[line].Local.Top;
            Assert.That(top.FaceUp, Is.False);
            Assert.That(top.Value, Is.EqualTo(2));
            Assert.That(sandbox.View().Lines[line].Local.Total, Is.EqualTo(2));
        }

        [Test]
        public void OpponentFaceDownCardsAreHidden()
        {
            var sandbox = Create();
            sandbox.FillStack(0, local: false, count: 6);

            foreach (var card in sandbox.View().Lines[0].Opponent.Cards)
                Assert.That(card.IsHidden, Is.EqualTo(!card.FaceUp));
        }

        [Test]
        public void RefreshDrawsBackToFive()
        {
            var sandbox = Create();
            var view = sandbox.View();
            sandbox.Play(view.Hand[0].InstanceId, 0, faceUp: false, out _);
            sandbox.Play(view.Hand[1].InstanceId, 1, faceUp: false, out _);
            Assert.That(sandbox.CanRefresh, Is.True);

            sandbox.Refresh();

            Assert.That(sandbox.View().Hand, Has.Count.EqualTo(5));
            Assert.That(sandbox.CanRefresh, Is.False);
        }

        [Test]
        public void CompileDeletesTheLineAndFlipsTheProtocol()
        {
            var sandbox = Create();
            sandbox.FillStack(1, local: true, count: 3);
            sandbox.FillStack(1, local: false, count: 2);

            sandbox.Compile(1);
            var view = sandbox.View();

            Assert.That(view.Lines[1].Local.Cards, Is.Empty);
            Assert.That(view.Lines[1].Opponent.Cards, Is.Empty);
            Assert.That(view.Lines[1].LocalProtocol.Compiled, Is.True);
            Assert.That(view.Local.Trash, Has.Count.EqualTo(3));
            Assert.That(view.Opponent.Trash, Has.Count.EqualTo(2));
        }

        [Test]
        public void RearrangeMovesProtocolsButNotCards()
        {
            var sandbox = Create();
            sandbox.FillStack(0, local: true, count: 2);
            var before = sandbox.View().Lines[0].Local.Cards.Select(c => c.InstanceId).ToList();

            Assert.That(sandbox.Rearrange(new[] { 0, 1, 2 }), Is.False, "The new order must differ.");
            Assert.That(sandbox.Rearrange(new[] { 2, 0, 1 }), Is.True);

            var view = sandbox.View();
            Assert.That(view.Lines.Select(l => l.LocalProtocol.Name), Is.EqualTo(new[] { "Light", "Fire", "Speed" }));
            Assert.That(view.Lines[0].Local.Cards.Select(c => c.InstanceId), Is.EqualTo(before));
        }

        [Test]
        public void ChoiceAcceptsOnlyUncoveredCards()
        {
            var sandbox = Create();
            sandbox.FillStack(2, local: true, count: 3);
            var stack = sandbox.View().Lines[2].Local.Cards;
            var chosen = -2;
            sandbox.RequestChoice("Choose 1 card to flip", optional: false, id => chosen = id);

            sandbox.AnswerChoice(stack[0].InstanceId);      // covered: ignored
            Assert.That(chosen, Is.EqualTo(-2));
            sandbox.AnswerChoice(stack[2].InstanceId);
            Assert.That(chosen, Is.EqualTo(stack[2].InstanceId));
            Assert.That(sandbox.Choice, Is.Null);
        }
    }
}
