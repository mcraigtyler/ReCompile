using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class SpiritTests
    {
        [Test]
        public void Spirit0_RefreshesThenDraws1_AndSkipsCheckCache()
        {
            var game = MainSet.LoadB(@"
                hand 0: Spirit_0 Gravity_5 Water_5
                deck 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4
                hand 1: Death_0");
            game.Play("Spirit_0", 1);
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(6), "refreshed to 5, then 1 more");
            Assert.That(game.Pending, Is.Null, "no clear cache with Spirit 0 uncovered");
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
        }

        // Codex: the refresh is a normal one, Control included.
        [Test]
        public void Spirit0_TheRefreshUsesControl()
        {
            var game = MainSet.LoadB(@"
                hand 0: Spirit_0 Gravity_5
                deck 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4
                hand 1: Death_0
                control 0");
            game.Play("Spirit_0", 1);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange), "Control returns, with a rearrange first");
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
        }

        // Errata (10/2024): "When you play cards face-up, they may be played without matching protocols."
        [TestCase("Spirit_1")]
        [TestCase("Spirit_1 Spirit_5")]
        public void Spirit1_ItsOwnerMayPlayFaceUpAnywhere_EvenWhileCovered(string stack)
        {
            var game = MainSet.LoadB($@"
                line 1: {stack} |
                hand 0: Death_0
                hand 1: Fire_0");
            Assert.That(game.CheckPlay(0, game.IdOf("Death_0"), 0, faceUp: true), Is.Null);

            var without = MainSet.LoadB(@"
                hand 0: Death_0
                hand 1: Fire_0");
            Assert.That(without.CheckPlay(0, without.IdOf("Death_0"), 0, faceUp: true), Is.Not.Null);
        }

        [Test]
        public void Spirit1_NotForTheOpponent()
        {
            var game = MainSet.LoadB(@"
                line 1: Spirit_1 |
                hand 0: Gravity_5
                hand 1: Fire_0
                turn 1 Action");
            Assert.That(game.CheckPlay(1, game.IdOf("Fire_0"), 0, faceUp: true), Is.Not.Null);
        }

        [Test]
        public void Spirit1_AtTheStart_EitherDiscardOrFlip()
        {
            var game = MainSet.LoadB(@"
                line 1: Spirit_1 |
                hand 0: Gravity_5 Water_5
                hand 1: Fire_0
                turn 0 Start");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Option));
            game.Do(AnswerChoice.WithOption(0, 1));
            Assert.That(game.Find("Spirit_1").FaceUp, Is.False);
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(2));
        }

        [Test]
        public void Spirit1_AtTheStart_DiscardingKeepsItFaceUp()
        {
            var game = MainSet.LoadB(@"
                line 1: Spirit_1 |
                hand 0: Gravity_5 Water_5
                hand 1: Fire_0
                turn 0 Start");
            game.Do(AnswerChoice.WithOption(0, 0));
            game.Pick("Water_5");
            Assert.That(game.Find("Spirit_1").FaceUp, Is.True);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Water_5" }));
        }

        // Ruling: with no cards, discarding changes nothing, and text that can change the game must, so it flips.
        [Test]
        public void Spirit1_AtTheStart_WithNoCardsInHand_Flips()
        {
            var game = MainSet.LoadB(@"
                line 1: Spirit_1 |
                deck 0: Gravity_5 Water_5 Water_4 Water_3 Water_2
                hand 1: Fire_0
                turn 0 Start");
            Assert.That(game.Find("Spirit_1").FaceUp, Is.False);
        }

        [Test]
        public void Spirit2_MayFlipACard()
        {
            var game = MainSet.LoadB(@"
                line 2: | ~Speed_2
                hand 0: Spirit_2
                hand 1: Fire_0");
            game.Play("Spirit_2", 1);
            Assert.That(game.Pending.Optional, Is.True);
            game.Pick("Speed_2");
            Assert.That(game.Find("Speed_2").FaceUp, Is.True);
        }

        [Test]
        public void Spirit3_AfterYouDraw_YouMayShiftIt_EvenCovered()
        {
            var game = MainSet.LoadB(@"
                line 1: Spirit_3 Spirit_5 |
                hand 0: Gravity_5
                deck 0: Water_5
                hand 1: Fire_0");
            game.Debug("draw-1");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Option));
            game.Do(AnswerChoice.WithOption(0, 0));
            game.Do(AnswerChoice.WithLine(0, 0));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Spirit_3" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Spirit_5" }));
        }

        [Test]
        public void Spirit3_NotWhenTheOpponentDraws()
        {
            var game = MainSet.LoadB(@"
                line 1: Spirit_3 |
                hand 0: Gravity_5
                hand 1: Fire_0
                deck 1: Death_1
                turn 1 Action");
            game.Debug("draw-1");
            Assert.That(game.Pending, Is.Null);
        }

        [Test]
        public void Spirit4_SwapsTwoOfYourProtocols()
        {
            var game = MainSet.LoadB(@"
                hand 0: Spirit_4
                hand 1: Fire_0");
            game.Play("Spirit_4", 1);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Swap));
            Assert.That(game.Pending.TargetSeat, Is.EqualTo(0));
            game.Do(new Rearrange(0, 0, 2, 1, 0));
            Assert.That(game.State.ProtocolIn(0, 0).Name, Is.EqualTo("Water"));
            Assert.That(game.State.ProtocolIn(0, 2).Name, Is.EqualTo("Gravity"));
        }
    }
}
