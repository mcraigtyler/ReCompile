using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Psychic is Bob's: these are his turns ("turn 1 ..."), and his Psychic line is his line 0 (field line 2).
    public class PsychicTests
    {
        [Test]
        public void Psychic0_DrawsThenTheOpponentDiscards2AndRevealsTheirHand()
        {
            var game = MainSet.LoadC(@"
                hand 0: Life_1 Life_2 Life_3 Life_4
                hand 1: Psychic_0
                deck 1: Fire_1 Fire_2
                turn 1 Action");
            game.Play("Psychic_0", 0);
            Assert.That(game.State.Players[1].Hand.Names(), Is.EquivalentTo(new[] { "Fire_1", "Fire_2" }));
            Assert.That(game.Pending.Seat, Is.EqualTo(0), "Ann picks her discards");
            var result = game.Do(AnswerChoice.WithCards(0, game.IdOf("Life_1"), game.IdOf("Life_2")));
            var reveal = result.Events.OfType<CardsRevealed>().Single();
            Assert.That(reveal.Cards.Select(c => c.CardId), Is.EquivalentTo(new[] { "Life_3", "Life_4" }));
        }

        [TestCase("Psychic_1")]
        [TestCase("Psychic_1 Psychic_5")]
        public void Psychic1_TheOpponentCanOnlyPlayFaceDown_EvenWhileCovered(string stack)
        {
            var game = MainSet.LoadC($@"
                line 2: | {stack}
                hand 0: Darkness_5
                hand 1: Fire_0");
            Assert.That(game.CheckPlay(0, game.IdOf("Darkness_5"), 0, faceUp: true), Does.Contain("Psychic 1"));
            Assert.That(game.CheckPlay(0, game.IdOf("Darkness_5"), 0, faceUp: false), Is.Null);
        }

        [Test]
        public void Psychic1_FlipsItselfAtTheStart()
        {
            var game = MainSet.LoadC(@"
                line 2: | Psychic_1
                hand 0: Darkness_5
                hand 1: Fire_0
                turn 1 Start");
            Assert.That(game.Find("Psychic_1").FaceUp, Is.False);
        }

        [Test]
        public void Psychic2_TheOpponentDiscards2_ThenTheirProtocolsAreRearranged()
        {
            var game = MainSet.LoadC(@"
                hand 0: Life_1 Life_2 Life_3
                hand 1: Psychic_2
                turn 1 Action");
            game.Play("Psychic_2", 0);
            game.Do(AnswerChoice.WithCards(0, game.IdOf("Life_1"), game.IdOf("Life_2")));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));
            Assert.That(game.Pending.Seat, Is.EqualTo(1));
            Assert.That(game.Pending.TargetSeat, Is.EqualTo(0));
            game.Do(new Rearrange(1, 0, 2, 0, 1));
            Assert.That(game.State.ProtocolIn(0, 0).Name, Is.EqualTo("Light"));
        }

        [Test]
        public void Psychic3_TheOpponentDiscards1_ThenOneOfTheirCardsIsShifted()
        {
            var game = MainSet.LoadC(@"
                line 1: Life_5 |
                hand 0: Life_1
                hand 1: Psychic_3
                turn 1 Action");
            game.Play("Psychic_3", 0);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Life_1" }));
            Assert.That(game.Pending.Seat, Is.EqualTo(1), "Bob picks where Ann's card goes");
            game.Do(AnswerChoice.WithLine(1, 0));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Life_5" }));
        }

        [Test]
        public void Psychic4_AtTheEnd_MayReturnAnOpponentsCard_AndIfSoFlips()
        {
            var game = MainSet.LoadC(@"
                line 2: | Psychic_4
                line 1: Life_5 |
                hand 0: Darkness_5
                hand 1: Fire_0
                turn 1 End");
            Assert.That(game.Pending.Optional, Is.True);
            Assert.That(game.ViewFor(1).Choice.Resolving, Is.EqualTo("Psychic 4's End text"), "the question says which text asks");
            game.Pick("Life_5");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Darkness_5", "Life_5" }));
            Assert.That(game.Find("Psychic_4").FaceUp, Is.False);
        }

        [Test]
        public void Psychic4_DecliningLeavesItFaceUp()
        {
            var game = MainSet.LoadC(@"
                line 2: | Psychic_4
                line 1: Life_5 |
                hand 0: Darkness_5
                hand 1: Fire_0
                turn 1 End");
            game.Do(AnswerChoice.Declined(1));
            Assert.That(game.Find("Psychic_4").FaceUp, Is.True);
        }
    }
}
