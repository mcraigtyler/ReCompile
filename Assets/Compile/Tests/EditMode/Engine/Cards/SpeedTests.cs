using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class SpeedTests
    {
        [Test]
        public void Speed0_PlaysAnotherCard()
        {
            var game = MainSet.LoadB(@"
                hand 0: Gravity_5
                hand 1: Speed_0 Speed_2 Death_0
                turn 1 Action");
            game.Play("Speed_0", 0);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Play));
            game.Do(new PlayCard(1, game.IdOf("Speed_2"), 0, faceUp: true));
            Assert.That(game.StackOf(2, 1), Is.EqualTo(new[] { "Speed_0", "Speed_2" }));
        }

        [Test]
        public void Speed1_DrawsAfterItsOwnerClearsCache()
        {
            var game = MainSet.LoadB(@"
                line 2: | Speed_1
                hand 0: Gravity_5
                hand 1: Death_0 Death_1 Death_2 Death_3 Death_4 Death_5 Plague_0
                deck 1: Plague_1 Plague_2
                turn 1 Action");
            game.Play("Death_0", 1, faceUp: false);
            Assert.That(game.Pending.Prompt, Does.StartWith("Clear cache"));
            game.Pick("Death_1");
            Assert.That(game.State.Players[1].Hand.Names(),
                Is.EquivalentTo(new[] { "Death_2", "Death_3", "Death_4", "Death_5", "Plague_0", "Plague_1" }), "discarded down to 5, then drew 1");
        }

        // Codex: the line's cards are deleted together; Speed 2 is shifted out first, and the compile goes on.
        [Test]
        public void Speed2_IsShiftedOutOfALineBeingCompiled_EvenCovered()
        {
            var game = MainSet.LoadB(@"
                line 2: | Speed_2 Death_5 Plague_5
                hand 0: Gravity_5
                hand 1: Death_0
                turn 1 Start");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line), "Bob picks where Speed 2 goes");
            Assert.That(game.Pending.Seat, Is.EqualTo(1));
            game.Do(AnswerChoice.WithLine(1, 1));
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Speed_2" }));
            Assert.That(game.State.Players[1].Trash.Names(), Is.EquivalentTo(new[] { "Death_5", "Plague_5" }));
            Assert.That(game.State.ProtocolIn(1, 2).Compiled, Is.True);
        }

        [Test]
        public void Speed3_ShiftsAnotherOfYourCards()
        {
            var game = MainSet.LoadB(@"
                line 1: | Death_5
                hand 0: Gravity_5
                hand 1: Speed_3 Death_0
                turn 1 Action");
            game.Play("Speed_3", 0);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line), "Death 5 is the only other card of Bob's");
            game.Do(AnswerChoice.WithLine(1, 2));
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Death_5" }));
        }

        [Test]
        public void Speed3_AtTheEnd_YouMayShiftACard_AndIfYouDoItFlips()
        {
            var game = MainSet.LoadB(@"
                line 2: | Speed_3
                line 1: | Death_5
                hand 0: Gravity_5
                hand 1: Death_0
                turn 1 End");
            Assert.That(game.Pending.Optional, Is.True);
            game.Pick("Death_5");
            game.Do(AnswerChoice.WithLine(1, 2));
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Death_5" }));
            Assert.That(game.Find("Speed_3").FaceUp, Is.False);
        }

        [Test]
        public void Speed3_AtTheEnd_DecliningLeavesItFaceUp()
        {
            var game = MainSet.LoadB(@"
                line 2: | Speed_3
                hand 0: Gravity_5
                hand 1: Death_0
                turn 1 End");
            game.Do(AnswerChoice.Declined(1));
            Assert.That(game.Find("Speed_3").FaceUp, Is.True);
        }

        [Test]
        public void Speed4_ShiftsOneOfTheOpponentsFaceDownCards()
        {
            var game = MainSet.LoadB(@"
                line 1: ~Water_3 Spirit_5 |
                line 0: ~Gravity_4 |
                hand 0: Gravity_5
                hand 1: Speed_4 Death_0
                turn 1 Action");
            game.Play("Speed_4", 0);
            Assert.That(game.Pending.Seat, Is.EqualTo(1), "Bob chooses where it goes");
            game.Do(AnswerChoice.WithLine(1, 0));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Gravity_4" }), "the only uncovered face-down card of Ann's");
        }
    }
}
