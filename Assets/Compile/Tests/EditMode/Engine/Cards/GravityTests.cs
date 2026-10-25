using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class GravityTests
    {
        [Test]
        public void Gravity0_PlaysATopCardUnderItselfForEvery2CardsInTheLine()
        {
            var game = MainSet.LoadB(@"
                line 0: Spirit_5 | Plague_2 Death_2
                hand 0: Gravity_0
                deck 0: Water_1 Water_2 Water_3
                hand 1: Death_0");
            game.Play("Gravity_0", 0);
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Spirit_5", "Water_1", "Water_2", "Gravity_0" }), "4 cards: 2 plays, under it");
            Assert.That(game.Find("Water_1").FaceUp, Is.False);
            Assert.That(game.Find("Water_2").FaceUp, Is.False);
            Assert.That(game.State.Players[0].Deck.Names(), Is.EqualTo(new[] { "Water_3" }));
        }

        [Test]
        public void Gravity0_AloneInItsLine_PlaysNothing()
        {
            var game = MainSet.LoadB(@"
                hand 0: Gravity_0
                deck 0: Water_1
                hand 1: Death_0");
            game.Play("Gravity_0", 0);
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Gravity_0" }));
        }

        [Test]
        public void Gravity1_DrawsThenShiftsACardFromAnotherLineToThisOne()
        {
            var game = MainSet.LoadB(@"
                line 1: | Death_5
                hand 0: Gravity_1
                deck 0: Water_1 Water_2
                hand 1: Death_0");
            game.Play("Gravity_1", 0);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Water_1", "Water_2" }));
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Gravity_1"), game.IdOf("Death_5") }));
            game.Pick("Death_5");
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Death_5" }), "to this line, on Bob's side");
        }

        [Test]
        public void Gravity1_ACardInThisLineGoesToALineOfTheChoosersPick()
        {
            var game = MainSet.LoadB(@"
                line 1: | Death_5
                hand 0: Gravity_1
                deck 0: Water_1 Water_2
                hand 1: Death_0");
            game.Play("Gravity_1", 0);
            game.Pick("Gravity_1");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 1, 2 }));
            game.Do(AnswerChoice.WithLine(0, 2));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Gravity_1" }));
        }

        [Test]
        public void Gravity2_FlipsACardThenShiftsItToThisLine()
        {
            var game = MainSet.LoadB(@"
                line 2: | ~Speed_2
                hand 0: Gravity_2
                hand 1: Death_0");
            game.Play("Gravity_2", 0);
            game.Pick("Speed_2");
            Assert.That(game.Find("Speed_2").FaceUp, Is.True);
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Speed_2" }));
            Assert.That(game.StackOf(2, 1), Is.Empty);
        }

        [Test]
        public void Gravity4_ShiftsAFaceDownCardToThisLine()
        {
            var game = MainSet.LoadB(@"
                line 1: | ~Death_3
                hand 0: Gravity_4
                hand 1: Death_0");
            game.Play("Gravity_4", 0);
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Death_3" }), "the only face-down card");
            Assert.That(game.Find("Death_3").FaceUp, Is.False);
        }

        [Test]
        public void Gravity6_TheOpponentPlaysTheirTopCardFaceDownInThisLine()
        {
            var game = MainSet.LoadB(@"
                hand 0: Gravity_6
                hand 1: Death_0
                deck 1: Plague_4 Plague_3");
            game.Play("Gravity_6", 0);
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Plague_4" }));
            Assert.That(game.Find("Plague_4").FaceUp, Is.False);
        }
    }
}
