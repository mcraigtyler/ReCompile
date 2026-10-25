using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class PlagueTests
    {
        [Test]
        public void Plague0_TheOpponentDiscards1()
        {
            var game = MainSet.Load(@"
                hand 0: Plague_0 Death_1
                hand 1: Water_0 Water_1");
            game.Play("Plague_0", 2);
            Assert.That(game.Pending.Seat, Is.EqualTo(1), "Bob chooses what he discards");
            game.Pick("Water_1");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Water_1" }));
        }

        [Test]
        public void Plague0_TheOpponentCantPlayInItsLine()
        {
            var game = MainSet.Load(@"
                line 2: Plague_0 |
                hand 0: Death_1
                hand 1: Plague_2 Water_0
                turn 1 Action");
            Assert.That(game.CheckPlay(1, game.IdOf("Plague_2"), 0, faceUp: false), Does.Contain("Plague 0"));
            Assert.That(game.CheckPlay(1, game.IdOf("Plague_2"), 0, faceUp: true), Does.Contain("Plague 0"));
            Assert.That(game.CheckPlay(1, game.IdOf("Plague_2"), 1, faceUp: false), Is.Null);
        }

        [Test]
        public void Plague0_IsBottomText_SoNotWhileCovered()
        {
            var game = MainSet.Load(@"
                line 2: Plague_0 Death_3 |
                hand 0: Death_1
                hand 1: Plague_2 Water_0
                turn 1 Action");
            Assert.That(game.CheckPlay(1, game.IdOf("Plague_2"), 0, faceUp: false), Is.Null);
        }

        [Test]
        public void Plague1_DrawsWhenTheOpponentDiscards_NotWhenYouDo()
        {
            var game = MainSet.Load(@"
                line 2: Plague_1 |
                hand 0: Death_0 Death_3
                deck 0: Death_1 Death_2
                hand 1: Water_0 Water_1");
            game.Debug("opponent-discards-1");
            game.Pick("Water_0");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Death_0", "Death_3", "Death_1" }));

            game.Debug("discard-1");
            game.Pick("Death_0");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Death_3", "Death_1" }));
        }

        [Test]
        public void Plague1_ItsOwnDiscardSetsOffItsTopText()
        {
            var game = MainSet.Load(@"
                hand 0: Plague_1 Death_0
                deck 0: Death_1
                hand 1: Water_0
                deck 1: Water_1 Water_2 Water_3 Water_4 Water_5");
            game.Play("Plague_1", 2);
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Water_0" }));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Death_0", "Death_1" }));
        }

        [Test]
        public void Plague2_TheOpponentDiscardsTheAmountDiscardedPlus1()
        {
            var game = MainSet.Load(@"
                hand 0: Plague_2 Fire_1 Fire_2
                hand 1: Water_0 Water_1 Water_2 Water_3");
            game.Play("Plague_2", 2);
            game.Pick("Fire_1", "Fire_2");
            Assert.That(game.Pending.Seat, Is.EqualTo(1));
            Assert.That(game.Pending.Min, Is.EqualTo(3));
            game.Pick("Water_0", "Water_1", "Water_2");
            Assert.That(game.State.Players[1].Hand.Names(), Is.EqualTo(new[] { "Water_3" }));
        }

        // Codex: only uncovered cards, since it doesn't say "covered" or "all".
        [Test]
        public void Plague3_FlipsEachOtherUncoveredFaceUpCard()
        {
            var game = MainSet.Load(@"
                line 0: Death_3 Death_4 | Plague_5
                line 1: Fire_1 | ~Fire_3
                hand 0: Plague_3
                hand 1: Water_0");
            game.Play("Plague_3", 2);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order));
            Assert.That(game.Pending.Options, Has.Count.EqualTo(3));
            game.Do(AnswerChoice.WithOrder(0, 0, 1, 2));

            Assert.That(game.Find("Death_4").FaceUp, Is.False);
            Assert.That(game.Find("Plague_5").FaceUp, Is.False);
            Assert.That(game.Find("Fire_1").FaceUp, Is.False);
            Assert.That(game.Find("Death_3").FaceUp, Is.True, "covered");
            Assert.That(game.Find("Plague_3").FaceUp, Is.True, "other cards");
            Assert.That(game.Find("Fire_3").FaceUp, Is.False, "face-up cards only");
        }

        [Test]
        public void Plague4_AtEnd_TheOpponentDeletesOneOfTheirFaceDownCards_ThenYouMayFlipIt()
        {
            var game = MainSet.Load(@"
                line 2: Plague_4 |
                line 0: | ~Water_1 ~Water_2
                line 1: | ~Fire_3
                hand 0: Fire_1 Death_2
                hand 1: Water_0");
            game.Play("Fire_1", 1, faceUp: false);

            Assert.That(game.Pending.Seat, Is.EqualTo(1));
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Water_2"), game.IdOf("Fire_3") }), "his uncovered face-down cards");
            game.Pick("Fire_3");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Fire_3" }));

            Assert.That(game.Pending.Seat, Is.EqualTo(0));
            game.Do(AnswerChoice.WithOption(0, 0));
            Assert.That(game.Find("Plague_4").FaceUp, Is.False);
        }
    }
}
