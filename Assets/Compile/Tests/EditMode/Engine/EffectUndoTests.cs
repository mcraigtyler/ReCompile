using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Undo with card text: effect questions can be taken back, a play takes its text back with it, and the
    // new barriers (top-of-deck plays, reveals, random takes, the opponent's answers) stop it.
    public class EffectUndoTests
    {
        const string Protocols = @"
            p0 Ann: Fire Speed Death
            p1 Bob: Metal Water Plague";

        static Game Load(string text, CardEffects effects = null) => Scenario.Load(Protocols + text, confirmEndTurn: true, effects);

        [Test]
        public void Undo_PutsAnEffectsQuestionBack()
        {
            var game = Load(@"
                line 0: ~Fire_1 | Metal_2
                hand 0: Fire_3");
            game.Debug("flip-1");
            game.Pick("Metal_2");
            Assert.That(game.Find("Metal_2").FaceUp, Is.False);

            game.Do(new Undo(0));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.FieldCards));
            Assert.That(game.Find("Metal_2").FaceUp, Is.True);

            game.Do(new Undo(0));
            Assert.That(game.Pending, Is.Null);
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Undo_TakesBackAPlayAndEverythingItsTextDid()
        {
            IEnumerable<Op> FlipOther(Effect e)
            {
                yield return e.Flip(Target.Any.Other());
            }

            var game = Load(@"
                line 1: ~Speed_1 |
                hand 0: Fire_1", new CardEffects().Add("Fire_1", new TestCard { MiddleText = FlipOther }));
            game.Play("Fire_1", 0);
            Assert.That(game.Find("Speed_1").FaceUp, Is.True);

            game.Do(new Undo(0));
            Assert.That(game.Find("Speed_1").FaceUp, Is.False);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Fire_1" }));
        }

        [TestCase("play-top-each-line", "A card was played from the top of a deck this turn.")]
        [TestCase("reveal-opponent-hand", "Cards were revealed this turn.")]
        [TestCase("take-random", "A card was taken at random this turn.")]
        public void Undo_BarriersFromEffects(string effect, string reason)
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3 Fire_4
                hand 1: Water_0 Water_1");
            game.Debug(effect);
            if (game.Pending != null)
                game.Do(AnswerChoice.WithOrder(0, 0, 1, 2));
            Assert.That(game.CheckUndo(0), Is.EqualTo(reason));
            Assert.That(game.ViewFor(0).UndoBlockedReason, Is.EqualTo(reason));
        }

        [Test]
        public void Undo_NotPastTheOpponentsAnswer()
        {
            var game = Load(@"
                hand 0: Fire_1 Fire_2
                hand 1: Water_0 Water_1");
            game.Play("Fire_1", 0, faceUp: false);
            game.Debug("opponent-discards-1");
            Assert.That(game.CheckUndo(1), Is.Not.Null, "Bob can't undo on Ann's turn");
            game.Pick("Water_0");
            Assert.That(game.CheckUndo(0), Is.EqualTo("Your opponent made a choice this turn."));
        }

        [Test]
        public void Undo_OfAChoiceThatLedToTheOpponentsQuestion()
        {
            // Ann's debug effect asks Bob something; until he answers, Ann can still take it back.
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Water_0 Water_1");
            game.Debug("opponent-discards-1");
            Assert.That(game.Pending.Seat, Is.EqualTo(1));
            game.Do(new Undo(0));
            Assert.That(game.Pending, Is.Null);
            Assert.That(game.AwaitingAction, Is.True);
        }
    }
}
