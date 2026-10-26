using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class LightTests
    {
        // Codex: Light 0 flips Fire 5 face-down, then draws its value now, 2.
        [Test]
        public void Light0_FlipsACardThenDrawsItsValueAfterTheFlip()
        {
            var game = MainSet.LoadC(@"
                line 0: | Fire_5
                hand 0: Light_0
                deck 0: Life_1 Life_2 Life_3
                hand 1: Fire_0");
            game.Play("Light_0", 2);
            game.Pick("Fire_5");
            Assert.That(game.Find("Fire_5").FaceUp, Is.False);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Life_1", "Life_2" }));
        }

        // Codex: Metal 6 deletes itself instead of flipping, and "that card" still means it: 6 in the trash.
        [Test]
        public void Light0_OnMetal6_StillDrawsItsValue()
        {
            var game = MainSet.LoadC(@"
                line 0: | Metal_6
                hand 0: Light_0
                deck 0: Life_0 Life_1 Life_2 Life_3 Life_4 Life_5 Light_1
                hand 1: Fire_0");
            game.Play("Light_0", 2);
            game.Pick("Metal_6");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Metal_6" }));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(6));
        }

        [Test]
        public void Light1_DrawsAtTheEnd()
        {
            var game = MainSet.LoadC(@"
                line 2: Light_1 |
                hand 0: Life_5
                deck 0: Life_1
                hand 1: Fire_0
                turn 0 End");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Life_5", "Life_1" }));
        }

        // Two End texts at once: the question says why, and names both cards.
        [Test]
        public void Light1_AndAnotherEndText_TheOrderQuestionSaysWhy()
        {
            var game = MainSet.LoadC(@"
                line 2: Light_1 |
                line 1: Life_0 Life_5 |
                hand 0: Life_4
                deck 0: Life_1
                hand 1: Fire_0
                turn 0 End");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order));
            Assert.That(game.Pending.Prompt, Does.Contain("Light 1").And.Contain("Life 0").And.Contain("End text"));
        }

        [Test]
        public void Light2_DrawsRevealsThenMayFlipTheCard()
        {
            var game = MainSet.LoadC(@"
                line 0: | ~Fire_3
                hand 0: Light_2
                deck 0: Life_1 Life_2
                hand 1: Fire_0");
            var result = game.Play("Light_2", 2);
            Assert.That(result.Events.OfType<CardsRevealed>().Single().Cards.Single().CardId, Is.EqualTo("Fire_3"));
            Assert.That(game.Find("Fire_3").FaceUp, Is.False, "revealed, not flipped");
            game.Do(AnswerChoice.WithOption(0, 1));
            Assert.That(game.Find("Fire_3").FaceUp, Is.True);
        }

        [Test]
        public void Light2_OrShiftsIt_OrLeavesIt()
        {
            var game = MainSet.LoadC(@"
                line 0: | ~Fire_3
                hand 0: Light_2
                deck 0: Life_1 Life_2
                hand 1: Fire_0");
            game.Play("Light_2", 2);
            game.Do(AnswerChoice.WithOption(0, 0));
            game.Do(AnswerChoice.WithLine(0, 1));
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Fire_3" }));
            Assert.That(game.Find("Fire_3").FaceUp, Is.False);
        }

        // Codex: the face-down cards keep their order and all go to the same line.
        [Test]
        public void Light3_ShiftsEveryFaceDownCardInThisLine_KeepingTheirOrder()
        {
            var game = MainSet.LoadC(@"
                line 2: ~Darkness_1 ~Darkness_2 | ~Psychic_2 Psychic_5
                hand 0: Light_3
                hand 1: Fire_0");
            game.Play("Light_3", 2);
            game.Do(AnswerChoice.WithLine(0, 0));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Darkness_1", "Darkness_2" }));
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Psychic_2" }));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Light_3" }));
            Assert.That(game.StackOf(2, 1), Is.EqualTo(new[] { "Psychic_5" }));
        }

        [Test]
        public void Light4_TheOpponentRevealsTheirHand()
        {
            var game = MainSet.LoadC(@"
                hand 0: Light_4
                hand 1: Fire_0 Fire_1");
            var result = game.Play("Light_4", 2);
            var reveal = result.Events.OfType<CardsRevealed>().Single();
            Assert.That(reveal.InHand, Is.True);
            Assert.That(reveal.Cards.Select(c => c.CardId), Is.EquivalentTo(new[] { "Fire_0", "Fire_1" }));
        }
    }
}
