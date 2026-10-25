using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class FireTests
    {
        [Test]
        public void Fire0_FlipsAnotherCardThenDraws2()
        {
            var game = MainSet.Load(@"
                line 1: | ~Fire_3
                hand 0: Fire_0
                deck 0: Death_1 Death_2
                hand 1: Water_0");
            game.Play("Fire_0", 1);
            Assert.That(game.Find("Fire_3").FaceUp, Is.True, "the only other card, so no question");
            Assert.That(game.Find("Fire_0").FaceUp, Is.True);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Death_1", "Death_2" }));
        }

        // Errata (12/2024): "When this card would be covered: First, draw 1 card. Then, flip 1 other card."
        // The card about to cover it is committed, so it can't be flipped.
        [Test]
        public void Fire0_WhenAboutToBeCovered_DrawsThenFlipsAnotherCard_NotTheCoveringOne()
        {
            var game = MainSet.Load(@"
                line 1: Fire_0 | ~Fire_3
                hand 0: Death_2
                deck 0: Metal_5
                hand 1: Water_0");
            game.Play("Death_2", 1, faceUp: false);

            Assert.That(game.Find("Fire_3").FaceUp, Is.True);
            Assert.That(game.Find("Death_2").FaceUp, Is.False);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Metal_5" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_0", "Death_2" }));
        }

        [Test]
        public void Fire1_DiscardsThenDeletes()
        {
            var game = MainSet.Load(@"
                line 0: | Plague_5
                hand 0: Fire_1 Death_3
                hand 1: Water_0");
            game.Play("Fire_1", 1);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Death_3" }), "the only card in hand");
            game.Pick("Plague_5");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Plague_5" }));
        }

        // Codex: an "if you do" clause doesn't happen if the first sentence couldn't be done.
        [Test]
        public void Fire1_WithNothingToDiscard_DeletesNothing()
        {
            var game = MainSet.Load(@"
                line 0: | Plague_5
                hand 0: Fire_1
                hand 1: Water_0");
            game.Play("Fire_1", 1);
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Plague_5" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_1" }));
        }

        [Test]
        public void Fire2_DiscardsThenReturns()
        {
            var game = MainSet.Load(@"
                line 0: | Plague_5
                hand 0: Fire_2 Death_3
                hand 1: Water_0");
            game.Play("Fire_2", 1);
            game.Pick("Plague_5");
            Assert.That(game.State.Players[1].Hand.Names(), Is.EquivalentTo(new[] { "Water_0", "Plague_5" }));
        }

        [Test]
        public void Fire3_AtEnd_MayDiscardToFlipACard()
        {
            var game = MainSet.Load(@"
                line 1: Fire_3 |
                line 2: | ~Death_1
                hand 0: Death_4 Death_2
                hand 1: Water_0");
            game.Play("Death_4", 0, faceUp: false);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            Assert.That(game.Pending.Optional, Is.True);
            game.Pick("Death_2");
            game.Pick("Death_1");
            Assert.That(game.Find("Death_1").FaceUp, Is.True);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Death_2" }));
        }

        [Test]
        public void Fire3_DecliningToDiscardFlipsNothing()
        {
            var game = MainSet.Load(@"
                line 1: Fire_3 |
                line 2: | ~Death_1
                hand 0: Death_4 Death_2
                hand 1: Water_0");
            game.Play("Death_4", 0, faceUp: false);
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.Pending, Is.Null);
            Assert.That(game.Find("Death_1").FaceUp, Is.False);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Death_2" }));
        }

        [Test]
        public void Fire4_DrawsTheAmountDiscardedPlus1()
        {
            var game = MainSet.Load(@"
                hand 0: Fire_4 Death_1 Death_2
                deck 0: Metal_0 Metal_1 Metal_2 Metal_3
                hand 1: Water_0");
            game.Play("Fire_4", 1);
            game.Pick("Death_1", "Death_2");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Metal_0", "Metal_1", "Metal_2" }));
        }

        // Codex: with nothing to discard, the second sentence still draws 1.
        [Test]
        public void Fire4_WithAnEmptyHand_StillDraws1()
        {
            var game = MainSet.Load(@"
                hand 0: Fire_4
                deck 0: Metal_0 Metal_1
                hand 1: Water_0");
            game.Play("Fire_4", 1);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Metal_0" }));
        }
    }
}
