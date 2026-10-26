using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class HateTests
    {
        [Test]
        public void Hate0_DeletesACard()
        {
            var game = MainSet.LoadD(@"
                line 1: | Death_5
                hand 0: Hate_0
                hand 1: Water_0");
            game.Play("Hate_0", 1);
            Assert.That(game.Pending.ValidCards, Has.Count.EqualTo(2), "Bob's Death 5, or Hate 0 itself");
            game.Pick("Death_5");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Death_5" }));
        }

        // With 1 card in hand it discards that one, and both deletes still happen.
        [Test]
        public void Hate1_DiscardsWhatItCan_ThenDeletesTwice()
        {
            var game = MainSet.LoadD(@"
                line 1: | Death_5
                line 2: | Water_5
                hand 0: Hate_1 Love_5
                hand 1: Water_0");
            game.Play("Hate_1", 1);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Love_5" }));
            game.Pick("Death_5");
            game.Pick("Water_5");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EquivalentTo(new[] { "Death_5", "Water_5" }));
        }

        [Test]
        public void Hate2_DeletesYourHighestUncoveredCard_ThenYourOpponents()
        {
            var game = MainSet.LoadD(@"
                line 0: Apathy_5 |
                line 1: | Death_5
                line 2: | Water_3
                hand 0: Hate_2
                hand 1: Water_0");
            game.Play("Hate_2", 1);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Apathy_5" }));
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Death_5" }));
        }

        // Codex: Hate 2 as your highest card deletes itself, and the second sentence doesn't happen.
        [Test]
        public void Hate2_AsYourHighest_DeletesItself_AndStops()
        {
            var game = MainSet.LoadD(@"
                line 0: Apathy_1 |
                line 1: | Death_5
                hand 0: Hate_2
                hand 1: Water_0");
            game.Play("Hate_2", 1);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Hate_2" }));
            Assert.That(game.State.Players[1].Trash, Is.Empty);
        }

        [Test]
        public void Hate2_ATieIsTheOwnersPick()
        {
            var game = MainSet.LoadD(@"
                line 0: Apathy_5 |
                line 2: Love_5 |
                line 1: | Death_5
                hand 0: Hate_2
                hand 1: Water_0");
            game.Play("Hate_2", 1);
            Assert.That(game.Pending.Seat, Is.EqualTo(0));
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Apathy_5"), game.IdOf("Love_5") }));
            game.Pick("Love_5");
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Love_5" }));
        }

        // Top text, so it works while covered.
        [Test]
        public void Hate3_DrawsAfterYouDelete_EvenWhileCovered()
        {
            var game = MainSet.LoadD(@"
                line 1: Hate_3 Hate_5 |
                line 2: | Water_5
                hand 0: Hate_0
                deck 0: Love_1
                hand 1: Water_0");
            game.Play("Hate_0", 1);
            game.Pick("Water_5");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Love_1" }));
        }

        [Test]
        public void Hate3_NotWhenYourOpponentDeletes()
        {
            var game = MainSet.LoadD(@"
                line 1: Hate_3 Hate_5 |
                line 0: ~Apathy_1 |
                deck 0: Love_1
                hand 0: Love_5
                hand 1: Death_3
                turn 1 Action");
            game.Play("Death_3", 1, faceUp: true);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Love_5" }));
        }

        // Bob's face-down Death 3 (2) is lower than Ann's Love 5, so it goes before Hate 5 lands.
        [Test]
        public void Hate4_WhenItWouldBeCovered_DeletesTheLowestCoveredCardInTheLine()
        {
            var game = MainSet.LoadD(@"
                line 1: Love_5 Hate_4 | ~Death_3 Death_5
                hand 0: Hate_5
                hand 1: Water_0");
            game.Play("Hate_5", 1);
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Death_3" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Love_5", "Hate_4", "Hate_5" }));
        }

        // Ann's covered Love 5 ties with Bob's covered Death 5: Hate 4's owner (Ann) picks which goes.
        [Test]
        public void Hate4_ATieBetweenBothPlayersCardsIsPickedByHate4sOwner()
        {
            var game = MainSet.LoadD(@"
                line 1: Love_5 Hate_4 | Death_5 Death_3
                hand 0: Hate_5
                hand 1: Fire_0");
            game.Play("Hate_5", 1);
            Assert.That(game.Pending.Seat, Is.EqualTo(0));
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Love_5"), game.IdOf("Death_5") }));
            game.Pick("Death_5");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Death_5" }));
        }
    }
}
