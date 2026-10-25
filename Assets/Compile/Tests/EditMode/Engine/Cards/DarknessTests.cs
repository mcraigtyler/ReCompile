using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class DarknessTests
    {
        [Test]
        public void Darkness0_DrawsThreeThenShiftsAnOpponentsCoveredCard()
        {
            var game = MainSet.LoadC(@"
                line 1: | ~Death_5 Death_3
                hand 0: Darkness_0
                deck 0: Life_1 Life_2 Life_3
                hand 1: Fire_0");
            game.Play("Darkness_0", 0);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Life_1", "Life_2", "Life_3" }));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line), "Death 5 is the only covered card of Bob's");
            game.Do(AnswerChoice.WithLine(0, 2));
            Assert.That(game.StackOf(2, 1), Is.EqualTo(new[] { "Death_5" }));
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Death_3" }));
        }

        [Test]
        public void Darkness1_FlipsAnOpponentsCard_ThenMayShiftIt()
        {
            var game = MainSet.LoadC(@"
                line 1: | ~Death_1
                hand 0: Darkness_1
                hand 1: Fire_0");
            game.Play("Darkness_1", 0);
            Assert.That(game.Find("Death_1").FaceUp, Is.True);
            game.Do(AnswerChoice.WithOption(0, 0));
            game.Do(AnswerChoice.WithLine(0, 0));
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Death_1" }));
        }

        [Test]
        public void Darkness1_DecliningTheShiftLeavesIt()
        {
            var game = MainSet.LoadC(@"
                line 1: | ~Death_1
                hand 0: Darkness_1
                hand 1: Fire_0");
            game.Play("Darkness_1", 0);
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Death_1" }));
        }

        [Test]
        public void Darkness2_FaceDownCardsInItsStackAreWorth4_EvenWhileCovered()
        {
            var game = MainSet.LoadC(@"
                line 0: ~Life_2 Darkness_2 ~Light_3 | ~Fire_1
                hand 0: Life_5
                hand 1: Fire_0");
            Assert.That(game.LineValue(0, 0), Is.EqualTo(10), "4 + 2 + 4");
            Assert.That(game.LineValue(1, 0), Is.EqualTo(2), "not the other stack in the line");
        }

        [Test]
        public void Darkness2_MayFlipACoveredCardInThisLine()
        {
            var game = MainSet.LoadC(@"
                line 0: ~Life_2 | Fire_1 ~Fire_2
                hand 0: Darkness_2
                hand 1: Fire_0");
            game.Play("Darkness_2", 0);
            Assert.That(game.Pending.Optional, Is.True);
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Life_2"), game.IdOf("Fire_1") }));
            game.Pick("Life_2");
            Assert.That(game.Find("Life_2").FaceUp, Is.True);
        }

        [Test]
        public void Darkness3_PlaysACardFaceDownInAnotherLine()
        {
            var game = MainSet.LoadC(@"
                hand 0: Darkness_3 Light_5
                hand 1: Fire_0");
            game.Play("Darkness_3", 0);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Play));
            Assert.That(game.Pending.FaceUpAllowed, Is.False);
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 1, 2 }));
            game.Do(new PlayCard(0, game.IdOf("Light_5"), 2, faceUp: false));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Light_5" }));
        }

        [Test]
        public void Darkness4_ShiftsAFaceDownCard()
        {
            var game = MainSet.LoadC(@"
                line 1: | ~Death_2
                hand 0: Darkness_4
                hand 1: Fire_0");
            game.Play("Darkness_4", 0);
            game.Do(AnswerChoice.WithLine(0, 2));
            Assert.That(game.StackOf(2, 1), Is.EqualTo(new[] { "Death_2" }));
        }
    }
}
