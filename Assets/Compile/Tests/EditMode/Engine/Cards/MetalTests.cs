using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class MetalTests
    {
        [Test]
        public void Metal0_ReducesTheOpponentsTotalInItsLineBy2()
        {
            var game = MainSet.Load(@"
                line 1: | Plague_2
                line 2: Metal_0 | Plague_5
                hand 1: Water_0");
            Assert.That(game.LineValue(1, 2), Is.EqualTo(3));
            Assert.That(game.LineValue(0, 2), Is.EqualTo(0), "not its owner's total");
            Assert.That(game.LineValue(1, 1), Is.EqualTo(2), "not other lines");
        }

        [Test]
        public void Metal0_WorksWhileCovered_AndNotBelow0()
        {
            var game = MainSet.Load(@"
                line 2: Metal_0 Death_3 | Plague_1
                hand 1: Water_0");
            Assert.That(game.LineValue(1, 2), Is.EqualTo(0));
        }

        [Test]
        public void Metal0_FlipsACard()
        {
            var game = MainSet.Load(@"
                line 0: | ~Fire_3
                hand 0: Metal_0 Death_1
                hand 1: Water_0");
            game.Play("Metal_0", 2);
            Assert.That(game.Pending.ValidCards, Has.Member(game.IdOf("Metal_0")), "any card, itself included");
            game.Pick("Fire_3");
            Assert.That(game.Find("Fire_3").FaceUp, Is.True);
        }

        // Errata (12/2024) and Codex: "Your opponent cannot compile on their next turn", while the text is visible.
        [Test]
        public void Metal1_StopsTheOpponentCompilingOnTheirNextTurnOnly()
        {
            var game = MainSet.Load(@"
                line 0: Death_5 |
                line 1: | Fire_5 Plague_5
                hand 0: Metal_1 Fire_2
                deck 0: Death_0 Death_2
                hand 1: Plague_2 Plague_3");
            game.Play("Metal_1", 2);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_2", "Death_0", "Death_2" }));

            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
            Assert.That(game.AwaitingAction, Is.True, "Bob can't compile, so he plays instead");
            Assert.That(game.State.Lines[1].Stack(1), Has.Count.EqualTo(2));

            game.Do(new PlayCard(1, game.IdOf("Plague_2"), 2, faceUp: false));
            game.Play("Fire_2", 0, faceUp: false);

            // Compiling is his action, so it's Ann's turn again.
            Assert.That(game.State.Turn, Is.EqualTo(5));
            Assert.That(game.State.Lines[1].Stack(1), Is.Empty, "the turn after, he compiles");
            Assert.That(game.State.ProtocolIn(1, 1).Compiled, Is.True);
        }

        [Test]
        public void Metal1_OnceCovered_NoLongerStopsCompiling()
        {
            var game = MainSet.Load(@"
                line 0: Death_5 |
                line 1: | Fire_5 Plague_5
                hand 0: Metal_1 Fire_2
                deck 0: Death_0 Death_2
                hand 1: Plague_2 Plague_3", confirmEndTurn: true);
            game.Play("Metal_1", 2);
            game.Debug("play-face-down");
            game.Do(new PlayCard(0, game.IdOf("Fire_2"), 2, faceUp: false));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Metal_1", "Fire_2" }));
            game.Do(new EndTurn(0));

            Assert.That(game.State.Lines[1].Stack(1), Is.Empty);
            Assert.That(game.State.ProtocolIn(1, 1).Compiled, Is.True);
        }

        [TestCase("Metal_2")]
        [TestCase("Metal_2 Death_3")]
        public void Metal2_TheOpponentCantPlayFaceDownInItsLine_EvenWhileCovered(string stack)
        {
            var game = MainSet.Load($@"
                line 2: {stack} |
                hand 0: Death_0
                hand 1: Plague_2 Plague_3
                turn 1 Action");
            var faceDown = game.Submit(new PlayCard(1, game.IdOf("Plague_2"), 0, faceUp: false));
            Assert.That(faceDown.Accepted, Is.False);
            Assert.That(faceDown.Error, Does.Contain("Metal 2"));
            Assert.That(game.CheckPlay(1, game.IdOf("Plague_2"), 1, faceUp: false), Is.Null, "other lines are fine");
            Assert.That(game.CheckPlay(1, game.IdOf("Plague_2"), 0, faceUp: true), Is.Null, "face-up is fine");
        }

        [Test]
        public void Metal3_DrawsThenDeletesEveryCardInAnotherLineWith8OrMore()
        {
            var game = MainSet.Load(@"
                line 1: Fire_1 Fire_2 Fire_3 Fire_4 | ~Plague_0 ~Plague_1 ~Plague_2 ~Plague_3
                line 2: ~Death_0 ~Death_1 ~Death_2 ~Death_3 | ~Water_0 ~Water_1 ~Water_2 ~Water_3
                hand 0: Metal_3
                deck 0: Death_4
                hand 1: Water_4");
            game.Play("Metal_3", 2);

            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Death_4" }));
            Assert.That(game.State.Lines[1].Stack(0), Is.Empty);
            Assert.That(game.State.Lines[1].Stack(1), Is.Empty);
            Assert.That(game.State.Lines[2].Stack(0), Has.Count.EqualTo(5), "not its own line");
        }

        [Test]
        public void Metal6_DeletesItselfWhenAboutToBeCovered()
        {
            var game = MainSet.Load(@"
                line 2: Metal_6 |
                hand 0: Fire_1 Fire_2
                hand 1: Water_0");
            game.Play("Fire_1", 2, faceUp: false);
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Fire_1" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Metal_6" }));
        }

        // Codex: a flip aimed at Metal 6 deletes it instead, and the flip is used up.
        [Test]
        public void Metal6_AFlipAimedAtItDeletesItAndIsUsedUp()
        {
            var game = MainSet.Load(@"
                line 2: Metal_6 |
                line 1: | ~Fire_3
                hand 0: Fire_1
                hand 1: Water_0");
            game.Debug("flip-1");
            game.Pick("Metal_6");
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Metal_6" }));
            Assert.That(game.Find("Fire_3").FaceUp, Is.False);
            Assert.That(game.Pending, Is.Null);
        }
    }
}
