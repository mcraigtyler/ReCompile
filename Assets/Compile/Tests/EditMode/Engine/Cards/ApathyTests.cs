using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class ApathyTests
    {
        // Apathy 0 (0) + face-down Hate 5 (2), plus 1 for each face-down card in the line, Bob's included: 4.
        // Covered, since it's top text.
        [Test]
        public void Apathy0_AddsOneForEachFaceDownCardInTheLine_EvenWhileCovered()
        {
            var game = MainSet.LoadD(@"
                line 0: Apathy_0 ~Hate_5 | ~Fire_1 Fire_2
                hand 0: Love_5
                hand 1: Water_0");
            Assert.That(game.LineValue(0, 0), Is.EqualTo(4));
            Assert.That(game.LineValue(1, 0), Is.EqualTo(4), "only its owner's total goes up");
        }

        [Test]
        public void Apathy1_FlipsEveryOtherFaceUpCardInTheLine_CoveredOnesToo()
        {
            var game = MainSet.LoadD(@"
                line 0: Love_3 | Fire_1 Fire_2
                line 1: | Death_5
                hand 0: Apathy_1
                hand 1: Water_0");
            game.Play("Apathy_1", 0);
            Assert.That(game.Find("Love_3").FaceUp, Is.False);
            Assert.That(game.Find("Fire_1").FaceUp, Is.False);
            Assert.That(game.Find("Fire_2").FaceUp, Is.False);
            Assert.That(game.Find("Apathy_1").FaceUp, Is.True);
            Assert.That(game.Find("Death_5").FaceUp, Is.True, "another line");
        }

        // Apathy 3 would flip Bob's Death 5, but Apathy 2's top text (working while covered) ignores it.
        [Test]
        public void Apathy2_IgnoresMiddleCommandsInItsLine_EvenWhileCovered()
        {
            var game = MainSet.LoadD(@"
                line 0: Apathy_2 Hate_5 |
                line 1: | Death_5
                hand 0: Apathy_3
                hand 1: Water_0");
            game.Play("Apathy_3", 0);
            Assert.That(game.Find("Death_5").FaceUp, Is.True);
        }

        // Covering it flips it face-down first, so the covering card's middle text runs: Apathy 5's discard.
        [Test]
        public void Apathy2_FlipsItselfWhenItWouldBeCovered()
        {
            var game = MainSet.LoadD(@"
                line 0: Apathy_2 |
                hand 0: Apathy_5 Hate_5
                hand 1: Water_0");
            game.Play("Apathy_5", 0);
            Assert.That(game.Find("Apathy_2").FaceUp, Is.False);
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Apathy_2", "Apathy_5" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Hate_5" }));
        }

        [Test]
        public void Apathy3_FlipsAnOpponentsFaceUpCard()
        {
            var game = MainSet.LoadD(@"
                line 1: | Death_5
                hand 0: Apathy_3
                hand 1: Water_0");
            game.Play("Apathy_3", 0);
            Assert.That(game.Find("Death_5").FaceUp, Is.False);
        }

        [Test]
        public void Apathy4_MayFlipOneOfYourFaceUpCoveredCards()
        {
            var game = MainSet.LoadD(@"
                line 1: Hate_5 Hate_0 |
                hand 0: Apathy_4
                hand 1: Water_0");
            game.Play("Apathy_4", 0);
            Assert.That(game.Pending.Optional, Is.True);
            game.Pick("Hate_5");
            Assert.That(game.Find("Hate_5").FaceUp, Is.False);
            Assert.That(game.Find("Hate_0").FaceUp, Is.True);
        }

        [TestCase("Apathy_5", 0)]
        [TestCase("Hate_5", 1)]
        [TestCase("Love_5", 2)]
        public void YouDiscardOneCard(string card, int line)
        {
            var game = MainSet.LoadD($@"
                hand 0: {card} Love_1 Love_2
                hand 1: Water_0");
            game.Play(card, line);
            game.Pick("Love_2");
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Love_2" }));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Love_1" }));
        }
    }
}
