using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class LoveTests
    {
        // It draws Bob's top card, which is Ann's from then on; at the End she may give 1 and draw 2.
        [Test]
        public void Love1_DrawsFromTheOpponentsDeck_ThenAtTheEndMayGiveOneToDrawTwo()
        {
            var game = MainSet.LoadD(@"
                hand 0: Love_1
                deck 0: Hate_1 Hate_2
                deck 1: Water_1 Water_2
                hand 1: Fire_0");
            game.Play("Love_1", 2);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Water_1" }));
            Assert.That(game.Find("Water_1").Owner, Is.EqualTo(0));
            Assert.That(game.Pending.Optional, Is.True, "End: you may give 1 card");
            game.Pick("Water_1");
            Assert.That(game.State.Players[1].Hand.Names(), Is.EquivalentTo(new[] { "Fire_0", "Water_1" }));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Hate_1", "Hate_2" }));
        }

        [Test]
        public void Love1_DecliningToGiveDrawsNothing()
        {
            var game = MainSet.LoadD(@"
                hand 0: Love_1
                deck 0: Hate_1 Hate_2
                deck 1: Water_1 Water_2
                hand 1: Fire_0");
            game.Play("Love_1", 2);
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Water_1" }));
        }

        [Test]
        public void Love2_TheOpponentDrawsOne_ThenYouRefresh()
        {
            var game = MainSet.LoadD(@"
                hand 0: Love_2 Hate_5
                deck 0: Apathy_1 Apathy_2 Apathy_3 Apathy_4 Apathy_5
                deck 1: Water_1
                hand 1: Fire_0");
            game.Play("Love_2", 2);
            Assert.That(game.State.Players[1].Hand.Names(), Is.EquivalentTo(new[] { "Fire_0", "Water_1" }));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Hate_5", "Apathy_1", "Apathy_2", "Apathy_3", "Apathy_4" }));
        }

        [Test]
        public void Love3_TakesARandomCard_ThenGivesOne()
        {
            var game = MainSet.LoadD(@"
                hand 0: Love_3 Hate_5
                hand 1: Fire_0");
            game.Play("Love_3", 2);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Hate_5", "Fire_0" }));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            game.Pick("Hate_5");
            Assert.That(game.State.Players[1].Hand.Names(), Is.EqualTo(new[] { "Hate_5" }));
            Assert.That(game.Find("Hate_5").Owner, Is.EqualTo(1));
            Assert.That(game.Find("Fire_0").Owner, Is.EqualTo(0));
        }

        // The sentences stand alone: nothing to take, and Ann still gives.
        [Test]
        public void Love3_WithNothingToTake_StillGives()
        {
            var game = MainSet.LoadD(@"
                hand 0: Love_3 Hate_5
                deck 1: Water_1");
            game.Play("Love_3", 2);
            Assert.That(game.State.Players[1].Hand.Names(), Is.EqualTo(new[] { "Hate_5" }));
        }

        [Test]
        public void Love4_RevealsACardFromYourHand_ThenFlipsACard()
        {
            var game = MainSet.LoadD(@"
                line 1: | Death_5
                hand 0: Love_4 Hate_5 Hate_4
                hand 1: Fire_0");
            game.Play("Love_4", 2);
            var result = game.Pick("Hate_5");
            var reveal = result.Events.OfType<CardsRevealed>().Single();
            Assert.That(reveal.InHand, Is.True);
            Assert.That(reveal.Cards.Single().CardId, Is.EqualTo("Hate_5"));
            game.Pick("Death_5");
            Assert.That(game.Find("Death_5").FaceUp, Is.False);
        }

        [Test]
        public void Love6_TheOpponentDrawsTwo()
        {
            var game = MainSet.LoadD(@"
                hand 0: Love_6
                deck 1: Water_1 Water_2
                hand 1: Fire_0");
            game.Play("Love_6", 2);
            Assert.That(game.State.Players[1].Hand.Names(), Is.EquivalentTo(new[] { "Fire_0", "Water_1", "Water_2" }));
        }
    }
}
