using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class DeathTests
    {
        [Test]
        public void Death0_DeletesOneCardFromEachOtherLine()
        {
            var game = MainSet.Load(@"
                line 0: | Plague_5
                line 1: Fire_1 | Death_3
                line 2: | ~Plague_2
                hand 0: Death_0
                hand 1: Water_0");
            game.Play("Death_0", 0);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order), "the active player orders the lines");
            game.Do(AnswerChoice.WithOrder(0, 0, 1));
            game.Pick("Death_3");       // line 1 has two cards to choose from; line 2 only one

            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_1" }));
            Assert.That(game.State.Lines[1].Stack(1), Is.Empty);
            Assert.That(game.State.Lines[2].Stack(1), Is.Empty);
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Plague_5" }), "not its own line");
        }

        [Test]
        public void Death1_AtStart_MayDrawThenDeletesAnotherCardThenItself()
        {
            var game = MainSet.Load(@"
                line 0: Death_1 |
                line 1: Fire_1 | Death_3
                hand 0: Fire_4
                deck 0: Fire_3
                hand 1: Water_0
                turn 0 Start");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Option));
            game.Do(AnswerChoice.WithOption(0, 0));
            Assert.That(game.Pending.ValidCards, Has.No.Member(game.IdOf("Death_1")), "another card");
            game.Pick("Death_3");

            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_4", "Fire_3" }));
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Death_3" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Death_1" }));
            Assert.That(game.AwaitingAction, Is.True);
        }

        // Ruling: "Then, delete this card" is its own sentence, so it happens even if you don't draw; and top
        // text deletes its card even while covered (Codex FAQ).
        [Test]
        public void Death1_DecliningStillDeletesItself_EvenWhileCovered()
        {
            var game = MainSet.Load(@"
                line 0: Death_1 Fire_1 |
                hand 0: Fire_4
                deck 0: Fire_3
                hand 1: Water_0
                turn 0 Start");
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_1" }));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Fire_4" }));
        }

        [Test]
        public void Death2_DeletesEveryValue1Or2InTheChosenLine_FaceDownAndCoveredToo()
        {
            var game = MainSet.Load(@"
                line 1: Fire_1 ~Fire_4 | Plague_2 Plague_3
                hand 0: Death_2
                hand 1: Water_0");
            game.Play("Death_2", 0);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            game.Do(AnswerChoice.WithLine(0, 1));

            Assert.That(game.State.Lines[1].Stack(0), Is.Empty, "Fire 1, and face-down Fire 4 (worth 2)");
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Plague_3" }), "covered Plague 2 went too");
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Death_2" }));
        }

        [Test]
        public void Death3_DeletesAFaceDownCard()
        {
            var game = MainSet.Load(@"
                line 1: ~Fire_1 | Death_4
                line 2: | ~Plague_2
                hand 0: Death_3
                hand 1: Water_0");
            game.Play("Death_3", 0);
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Fire_1"), game.IdOf("Plague_2") }));
            game.Pick("Plague_2");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Plague_2" }));
        }

        [Test]
        public void Death4_DeletesACardWithAValueOf0Or1()
        {
            var game = MainSet.Load(@"
                line 1: Fire_1 | Plague_2
                hand 0: Death_4
                hand 1: Water_0");
            game.Play("Death_4", 0);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Fire_1" }), "the only card worth 0 or 1");
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Plague_2" }));
        }

        // From a real game: the only card worth 0 or 1 is picked without asking, and deleting it uncovers
        // Plague 2, whose text then runs. The log says why each thing happened.
        [Test]
        public void Death4_TheOnlyCardIsPickedForYou_AndTheLogSaysWhatHappened()
        {
            var game = MainSet.Load(@"
                line 2: | Plague_2 Plague_1
                hand 0: Death_4 Death_1 Death_2
                hand 1: Water_0 Water_1");
            game.Play("Death_4", 0);

            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Plague_1" }));
            var forced = game.History.OfType<ChoiceMadeFor>().Single();
            Assert.That(forced.Describe(game.State, 0), Is.EqualTo("Death 4: Plague 1 was the only card that could be deleted, so it was picked for you."));
            var uncovered = game.History.OfType<CardTextRuns>().Single();
            Assert.That(uncovered.Describe(game.State, 0), Is.EqualTo("Plague 2 was uncovered, so its text resolves."));

            Assert.That(game.Pending.Seat, Is.EqualTo(1), "Plague 2: Bob discards 1 or more");
            game.Pick("Water_0");
            Assert.That(game.State.Players[0].Hand.Names(), Is.Empty, "Ann discards 2, which is all she has");
            var discarded = game.History.OfType<ChoiceMadeFor>().Last();
            Assert.That(discarded.Describe(game.State, 0), Is.EqualTo("Plague 2: Death 1 and Death 2 were the only cards that could be discarded, so both were picked for you."));
            Assert.That(discarded.Describe(game.State, 1), Is.EqualTo("Plague 2: Ann had only 2 cards that could be discarded, so both were picked."));
        }
    }
}
