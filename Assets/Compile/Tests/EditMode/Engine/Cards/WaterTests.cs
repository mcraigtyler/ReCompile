using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class WaterTests
    {
        [Test]
        public void Water0_FlipsAnotherCardThenItself()
        {
            var game = MainSet.LoadB(@"
                line 1: | ~Speed_2
                hand 0: Water_0
                hand 1: Fire_0");
            game.Play("Water_0", 2);
            Assert.That(game.Find("Speed_2").FaceUp, Is.True, "the only other card");
            Assert.That(game.Find("Water_0").FaceUp, Is.False);
        }

        [Test]
        public void Water1_PlaysTheTopCardFaceDownInEachOtherLine()
        {
            var game = MainSet.LoadB(@"
                hand 0: Water_1
                deck 0: Fire_1 Fire_2 Fire_3
                hand 1: Death_0");
            game.Play("Water_1", 2);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order), "which line gets a card first");
            game.Do(AnswerChoice.WithOrder(0, 0, 1));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_1" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_2" }));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Water_1" }));
            Assert.That(game.Find("Fire_1").FaceUp, Is.False);
        }

        // Codex: playing from the top of an empty deck doesn't shuffle the trash into a new one.
        [Test]
        public void Water1_WithAnEmptyDeck_PlaysNothing_AndDoesntShuffle()
        {
            var game = MainSet.LoadB(@"
                hand 0: Water_1
                trash 0: Fire_1 Fire_2
                hand 1: Death_0");
            game.Play("Water_1", 2);
            if (game.Pending != null)
                game.Do(AnswerChoice.WithOrder(0, 0, 1));
            Assert.That(game.StackOf(0, 0), Is.Empty);
            Assert.That(game.StackOf(1, 0), Is.Empty);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Fire_1", "Fire_2" }));
        }

        [Test]
        public void Water2_DrawsThenRearrangesYourProtocols()
        {
            var game = MainSet.LoadB(@"
                hand 0: Water_2
                deck 0: Fire_1 Fire_2
                hand 1: Death_0");
            game.Play("Water_2", 2);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_1", "Fire_2" }));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));
            Assert.That(game.Pending.Optional, Is.False);
            game.Do(new Rearrange(0, 0, 1, 2, 0));
            Assert.That(game.State.ProtocolIn(0, 0).Name, Is.EqualTo("Spirit"));
        }

        [Test]
        public void Water3_ReturnsEveryValue2InTheLine_FaceDownAndCoveredToo()
        {
            var game = MainSet.LoadB(@"
                line 0: ~Fire_1 Spirit_2 | Plague_2 ~Plague_0
                line 1: Spirit_5 | Death_3
                hand 0: Water_3
                hand 1: Death_0");
            game.Play("Water_3", 2);
            Assert.That(game.StackOf(0, 0), Is.Empty, "the only line with a value of 2, so no question");
            Assert.That(game.StackOf(0, 1), Is.Empty);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_1", "Spirit_2" }));
            Assert.That(game.State.Players[1].Hand.Names(), Is.EquivalentTo(new[] { "Death_0", "Plague_2", "Plague_0" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Spirit_5" }));
        }

        [Test]
        public void Water3_AsksWhichLine_WhenMoreThanOneHasA2()
        {
            var game = MainSet.LoadB(@"
                line 0: Spirit_2 |
                line 1: | Death_2
                hand 0: Water_3
                hand 1: Death_0");
            game.Play("Water_3", 2);
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 0, 1 }), "not the line with no 2s");
            game.Do(AnswerChoice.WithLine(0, 1));
            Assert.That(game.StackOf(1, 1), Is.Empty);
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Spirit_2" }));
        }

        // Codex: Water 4 returns itself if it's the only one of your cards.
        [Test]
        public void Water4_ReturnsOneOfYourCards_ItselfIfThatsAll()
        {
            var game = MainSet.LoadB(@"
                line 0: | Plague_5
                hand 0: Water_4
                hand 1: Death_0");
            game.Play("Water_4", 2);
            Assert.That(game.StackOf(2, 0), Is.Empty);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Water_4" }));
        }
    }
}
