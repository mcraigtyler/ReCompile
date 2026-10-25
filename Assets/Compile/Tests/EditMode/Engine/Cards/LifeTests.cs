using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class LifeTests
    {
        // Life 0 plays into each line with a card of Ann's (its own included), which covers it; then, with
        // the errata'd top text, it deletes itself at the End because it's covered.
        [Test]
        public void Life0_PlaysInEachLineWithYourCard_ThenIfCoveredDeletesItselfAtTheEnd()
        {
            var game = MainSet.LoadC(@"
                line 0: Darkness_5 |
                hand 0: Life_0
                deck 0: Light_1 Light_2 Light_3
                hand 1: Fire_0");
            game.Play("Life_0", 1);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order));
            game.Do(AnswerChoice.WithOrder(0, 0, 1));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Darkness_5", "Light_1" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Light_2" }), "Life 0 was covered, so it deleted itself at the End");
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Life_0" }));
            Assert.That(game.StackOf(2, 0), Is.Empty, "no card of Ann's there");
        }

        [Test]
        public void Life0_UncoveredAtTheEnd_Stays()
        {
            var game = MainSet.LoadC(@"
                line 1: Life_0 |
                hand 0: Life_5
                hand 1: Fire_0
                turn 0 End");
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Life_0" }));
        }

        [Test]
        public void Life1_FlipsTwice_TheSameCardIfYouLike()
        {
            var game = MainSet.LoadC(@"
                line 1: | ~Death_1
                hand 0: Life_1
                hand 1: Fire_0");
            game.Play("Life_1", 1);
            game.Pick("Death_1");
            Assert.That(game.Find("Death_1").FaceUp, Is.True);
            game.Pick("Death_1");
            Assert.That(game.Find("Death_1").FaceUp, Is.False);
        }

        [Test]
        public void Life2_DrawsThenMayFlipAFaceDownCard()
        {
            var game = MainSet.LoadC(@"
                line 1: | ~Death_1
                hand 0: Life_2
                deck 0: Light_4
                hand 1: Fire_0");
            game.Play("Life_2", 1);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Light_4" }));
            Assert.That(game.Pending.Optional, Is.True);
            game.Pick("Death_1");
            Assert.That(game.Find("Death_1").FaceUp, Is.True);
        }

        [Test]
        public void Life3_WhenAboutToBeCovered_FirstPlaysTheTopCardInAnotherLine()
        {
            var game = MainSet.LoadC(@"
                line 1: Life_3 |
                hand 0: Darkness_5
                deck 0: Light_4
                hand 1: Fire_0");
            game.Play("Darkness_5", 1, faceUp: false);
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 0, 2 }));
            game.Do(AnswerChoice.WithLine(0, 0));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Light_4" }));
            Assert.That(game.Find("Light_4").FaceUp, Is.False);
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Life_3", "Darkness_5" }));
        }

        [Test]
        public void Life3_WithAnEmptyDeck_DoesNothing()
        {
            var game = MainSet.LoadC(@"
                line 1: Life_3 |
                hand 0: Darkness_5
                trash 0: Light_4
                hand 1: Fire_0");
            game.Play("Darkness_5", 1, faceUp: false);
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Life_3", "Darkness_5" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Light_4" }));
        }

        [TestCase("Darkness_5", 1)]
        [TestCase("", 0)]
        public void Life4_DrawsOnlyIfItsCoveringACard(string under, int drawn)
        {
            var game = MainSet.LoadC($@"
                line 1: {under} |
                hand 0: Life_4
                deck 0: Light_4
                hand 1: Fire_0");
            game.Play("Life_4", 1);
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(drawn));
        }
    }
}
