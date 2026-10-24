using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // The effects framework, driven by made-up card text (TestCard): when text runs, interrupts, losing
    // text, committed cards, triggers, and rule modifiers. Field line 0 is Ann's Fire facing Bob's Plague,
    // line 1 Speed/Water, line 2 Death/Metal.
    public class CardTextTests
    {
        const string Protocols = @"
            p0 Ann: Fire Speed Death
            p1 Bob: Metal Water Plague";

        // Turns wait for EndTurn, so each test sees only the turn it's about.
        static Game Load(string text, CardEffects effects) => Scenario.Load(Protocols + text, confirmEndTurn: true, effects);

        static CardEffects With(string cardId, TestCard card) => new CardEffects().Add(cardId, card);

        static TestCard Middle(System.Func<Effect, IEnumerable<Op>> text) => new TestCard { MiddleText = text };

        static TestCard DrawsOne => Middle(e => TestCard.Text(e.Draw(1)));

        static int[] DrawCounts(IntentResult result) => result.Events.OfType<CardsDrawn>().Select(d => d.Cards.Count).ToArray();

        // ---- When middle text runs ----

        [Test]
        public void Middle_RunsWhenPlayedFaceUpButNotFaceDown()
        {
            const string setup = @"
                hand 0: Fire_1 Fire_2
                deck 0: Speed_1 Speed_2";
            var game = Load(setup, With("Fire_1", DrawsOne));
            game.Play("Fire_1", 0);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_2", "Speed_1" }));

            game = Load(setup, With("Fire_1", DrawsOne));
            game.Play("Fire_1", 0, faceUp: false);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Fire_2" }));
        }

        [Test]
        public void Middle_RunsWhenFlippedFaceUpWhileUncovered()
        {
            var game = Load(@"
                line 0: ~Fire_1 |
                hand 0: Fire_2
                deck 0: Speed_1", With("Fire_1", DrawsOne));
            game.Debug("flip-1");
            Assert.That(game.State.Players[0].Hand.Names(), Does.Contain("Speed_1"));
        }

        [Test]
        public void Middle_DoesntRunWhenACoveredCardIsFlippedFaceUp()
        {
            var effects = new CardEffects()
                .Add("Fire_1", DrawsOne)
                .Add("Death_0", Middle(e => TestCard.Text(e.Flip(Target.Any.Covered()))));
            var game = Load(@"
                line 0: ~Fire_1 Fire_2 |
                hand 0: Death_0
                deck 0: Speed_1", effects);
            game.Play("Death_0", 2);
            Assert.That(game.Find("Fire_1").FaceUp, Is.True);
            Assert.That(game.State.Players[0].Deck.Names(), Is.EqualTo(new[] { "Speed_1" }));
        }

        [Test]
        public void Middle_RunsWhenUncoveredByADelete()
        {
            var game = Load(@"
                line 0: Fire_1 Fire_2 |
                hand 0: Speed_2
                deck 0: Speed_1", With("Fire_1", DrawsOne));
            game.Debug("delete-1");
            Assert.That(game.State.Players[0].Hand.Names(), Does.Contain("Speed_1"));
        }

        [Test]
        public void Middle_OfTheUncoveredCardRunsBeforeTheShiftedCardLands()
        {
            var game = Load(@"
                line 0: Fire_1 Fire_2 |
                hand 0: Speed_2
                deck 0: Speed_1", With("Fire_1", DrawsOne));
            game.Debug("shift-1");
            var result = game.Do(AnswerChoice.WithLine(0, 1));
            var order = result.Events.Where(e => e is CardsDrawn || e is CardShifted).Select(e => e.GetType().Name);
            Assert.That(order, Is.EqualTo(new[] { "CardsDrawn", "CardShifted" }));
        }

        // ---- Resolving ----

        [Test]
        public void Text_ThatSetsOffOtherTextIsInterrupted_LastInFirstOut()
        {
            IEnumerable<Op> FlipThenDraw(Effect e)
            {
                yield return e.Flip(Target.Any.Other());
                yield return e.Draw(1);
            }

            var effects = new CardEffects()
                .Add("Fire_1", Middle(FlipThenDraw))
                .Add("Speed_1", Middle(e => TestCard.Text(e.Draw(2))));
            var game = Load(@"
                line 1: ~Speed_1 |
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3 Fire_4", effects);
            var result = game.Play("Fire_1", 0);
            Assert.That(DrawCounts(result), Is.EqualTo(new[] { 2, 1 }), "Speed 1's text finished before Fire 1's draw");
        }

        [Test]
        public void Text_IsLostWhenItsCardIsFlipped()
        {
            IEnumerable<Op> FlipThenDraw(Effect e)
            {
                yield return e.Flip(Target.Any);        // the only uncovered card is this one
                yield return e.Draw(1);
            }

            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2", With("Fire_1", Middle(FlipThenDraw)));
            var result = game.Play("Fire_1", 0);
            Assert.That(game.Find("Fire_1").FaceUp, Is.False);
            Assert.That(DrawCounts(result).Where(n => n > 0), Is.Empty);
        }

        [Test]
        public void Text_IsLostWhenItsCardIsCovered()
        {
            IEnumerable<Op> PlayThenDraw(Effect e)
            {
                yield return e.PlayFromHand(fieldLineAllowed: line => line == e.SourceLine);
                yield return e.Draw(1);
            }

            var game = Load(@"
                hand 0: Fire_1 Fire_2
                deck 0: Fire_3", With("Fire_1", Middle(PlayThenDraw)));
            game.Play("Fire_1", 0);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Play));
            Assert.That(game.Pending.ValidLines, Is.EqualTo(new[] { 0 }));
            var result = game.Do(new PlayCard(0, game.IdOf("Fire_2"), 0, faceUp: false));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_1", "Fire_2" }));
            Assert.That(DrawCounts(result).Where(n => n > 0), Is.Empty);
        }

        [Test]
        public void IfYouDo_ChecksWhetherTheFirstPartHappened()
        {
            IEnumerable<Op> DiscardThenDraw(Effect e)
            {
                var discard = e.Discard(1);
                yield return discard;
                if (discard.Done)
                    yield return e.Draw(2);
            }

            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3", With("Fire_1", Middle(DiscardThenDraw)));
            var result = game.Play("Fire_1", 0);
            Assert.That(DrawCounts(result).Where(n => n > 0), Is.Empty, "no cards left to discard, so no draw");
        }

        // ---- Committed cards ----

        [Test]
        public void WouldBeCovered_ResolvesFirstAndTheCommittedCardCantBeChosen()
        {
            var cover = new TestCard().On(Trigger.WouldBeCovered, Slot.Bottom, e => TestCard.Text(e.Flip(Target.Any)));
            var game = Load(@"
                line 0: Fire_2 | Metal_2
                hand 0: Fire_3", With("Fire_2", cover));
            game.Play("Fire_3", 0);

            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Fire_2"), game.IdOf("Metal_2") }));
            Assert.That(game.State.CommittedCards.Names(), Is.EqualTo(new[] { "Fire_3" }));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_2" }), "not landed yet");

            game.Pick("Metal_2");
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_2", "Fire_3" }));
            Assert.That(game.State.CommittedCards, Is.Empty);
        }

        [Test]
        public void WouldBeCovered_TheCommittedCardStillLandsIfTheCardIsGone()
        {
            var cover = new TestCard().On(Trigger.WouldBeCovered, Slot.Bottom, e => TestCard.Text(e.Delete(e.Source)));
            var game = Load(@"
                line 0: Fire_2 |
                hand 0: Fire_3", With("Fire_2", cover));
            game.Play("Fire_3", 0);
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_3" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Fire_2" }));
        }

        // ---- Start and End ----

        [Test]
        public void Start_OnlyTheActivePlayersVisibleCards()
        {
            var start = new TestCard().On(Trigger.Start, Slot.Top, e => TestCard.Text(e.Draw(1)));
            var bottomStart = new TestCard().On(Trigger.Start, Slot.Bottom, e => TestCard.Text(e.Draw(1)));
            var effects = new CardEffects().Add("Fire_1", start).Add("Speed_1", bottomStart).Add("Metal_1", start);
            var game = Load(@"
                line 0: Fire_1 Fire_2 |
                line 1: Speed_1 Speed_2 |
                line 2: | Metal_1
                hand 0: Death_0
                deck 0: Death_1 Death_2
                deck 1: Water_0
                turn 0 Start", effects);

            // Fire 1's top text works while covered; Speed 1's bottom text doesn't; Metal 1 is Bob's.
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Death_0", "Death_1" }));
            Assert.That(game.State.Players[1].Deck.Names(), Is.EqualTo(new[] { "Water_0" }));
        }

        [Test]
        public void Start_TheOwnerOrdersSeveral()
        {
            var start = new TestCard().On(Trigger.Start, Slot.Top, e => TestCard.Text(e.Draw(1)));
            var game = Load(@"
                line 0: Fire_1 |
                line 1: Speed_1 |
                hand 0: Death_0
                deck 0: Death_1 Death_2
                turn 0 Start", new CardEffects().Add("Fire_1", start).Add("Speed_1", start));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order));
            Assert.That(game.Pending.Seat, Is.EqualTo(0));
            Assert.That(game.Pending.Options, Is.EquivalentTo(new[] { "Fire 1", "Speed 1" }));
            game.Do(AnswerChoice.WithOrder(0, 1, 0));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(3));
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Start_TextRevealedDuringThePhaseDoesntRun()
        {
            var flipOther = new TestCard().On(Trigger.Start, Slot.Top, e => TestCard.Text(e.Flip(Target.Any.Other())));
            var drawOne = new TestCard().On(Trigger.Start, Slot.Top, e => TestCard.Text(e.Draw(1)));
            var game = Load(@"
                line 0: Fire_1 |
                line 1: ~Speed_1 |
                hand 0: Death_0
                deck 0: Death_1
                turn 0 Start", new CardEffects().Add("Fire_1", flipOther).Add("Speed_1", drawOne));
            Assert.That(game.Find("Speed_1").FaceUp, Is.True);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Death_0" }));
        }

        [Test]
        public void End_RunsAfterTheActionAndBeforeTheTurnEnds()
        {
            var end = new TestCard().On(Trigger.End, Slot.Bottom, e => TestCard.Text(e.Draw(1)));
            var game = Load(@"
                line 0: Fire_1 |
                hand 0: Death_0
                deck 0: Death_1
                hand 1: Water_0", With("Fire_1", end));
            game.Play("Death_0", 2, faceUp: false);
            Assert.That(game.AwaitingEndTurn, Is.True);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Death_1" }));
        }

        // ---- After ... ----

        [Test]
        public void AfterDraw_ReactsOnceToTheWholeBatch()
        {
            var count = 0;
            var card = new TestCard().On(Trigger.AfterDraw, Slot.Top, (e, seat) => seat == e.Owner ? TestCard.Text(e.Do(_ => count++)) : null);
            var game = Load(@"
                line 0: Fire_1 |
                hand 0: Death_0
                deck 0: Death_1 Death_2", With("Fire_1", card));
            game.Debug("draw-2");
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void AfterDiscard_TheOpponentsCardReacts()
        {
            var card = new TestCard().On(Trigger.AfterDiscard, Slot.Top, (e, seat) => seat != e.Owner ? TestCard.Text(e.Draw(1)) : null);
            var game = Load(@"
                line 0: | Plague_1
                hand 0: Fire_1 Fire_2
                deck 1: Water_0", With("Plague_1", card));
            game.Debug("discard-1");
            game.Pick("Fire_2");
            Assert.That(game.State.Players[1].Hand.Names(), Is.EqualTo(new[] { "Water_0" }));
        }

        [Test]
        public void AfterDelete_AndAfterClearCache()
        {
            var deletes = 0;
            var caches = 0;
            var card = new TestCard()
                .On(Trigger.AfterDelete, Slot.Top, (e, seat) => seat == e.Owner ? TestCard.Text(e.Do(_ => deletes++)) : null)
                .On(Trigger.AfterClearCache, Slot.Top, (e, seat) => seat == e.Owner ? TestCard.Text(e.Do(_ => caches++)) : null);
            var game = Load(@"
                line 0: Fire_1 | Metal_2
                hand 0: Death_0 Death_1 Death_2 Death_3 Death_4 Death_5 Speed_0", With("Fire_1", card));
            game.Debug("delete-1");
            game.Pick("Metal_2");
            Assert.That(deletes, Is.EqualTo(1));

            game.Play("Speed_0", 1, faceUp: false);
            game.Pick("Death_0");
            Assert.That(caches, Is.EqualTo(1));
        }

        [Test]
        public void WouldBeFlipped_ResolvesFirst()
        {
            var card = new TestCard().On(Trigger.WouldBeFlipped, Slot.Top, e => TestCard.Text(e.Delete(e.Source)));
            var game = Load(@"
                line 0: Fire_1 |
                line 2: | Metal_0
                hand 0: Death_0", With("Metal_0", card));
            game.Debug("flip-1");
            var result = game.Pick("Metal_0");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Metal_0" }));
            Assert.That(result.Events.OfType<CardsFlipped>(), Is.Empty);
        }

        [Test]
        public void WouldBe_TextDoesntSetItselfOffAgain()
        {
            // "When this card would be flipped: flip 1 card." The only card to flip is itself: its text
            // runs once, then the flips happen (the text's own flip first, then the one that set it off).
            var card = new TestCard().On(Trigger.WouldBeFlipped, Slot.Top, e => TestCard.Text(e.Flip(Target.Any)));
            var game = Load(@"
                line 2: | Metal_0
                hand 0: Death_0", With("Metal_0", card));
            var result = game.Debug("flip-1");
            Assert.That(result.Events.OfType<CardsFlipped>().Count(), Is.EqualTo(1), "the second flip finds the card already flipped");
            Assert.That(game.Find("Metal_0").FaceUp, Is.False);
        }

        [Test]
        public void WouldBeDeletedByCompile_ResolvesBeforeTheDelete()
        {
            // Speed 2 style: a covered card shifts itself out of the line before it's compiled.
            var card = new TestCard().On(Trigger.WouldBeDeletedByCompile, Slot.Top, e => TestCard.Text(e.Shift(e.Source)));
            var game = Load(@"
                line 0: Fire_5 Fire_4 Fire_1 |
                hand 0: Death_0
                turn 0 Start", With("Fire_5", card));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            game.Do(AnswerChoice.WithLine(0, 1));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_5" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EquivalentTo(new[] { "Fire_4", "Fire_1" }));
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.True);
        }

        // ---- Rule modifiers ----

        [Test]
        public void Rules_CardValueAndLineValue()
        {
            // Darkness 2 style: face-down cards in this stack are worth 4. Metal 0 style: the opponent's total
            // in this line is 2 less.
            var effects = new CardEffects()
                .Add("Fire_1", new TestCard { Value = (e, c, v) => !c.FaceUp && e.Game.FieldLineOf(c) == e.SourceLine && c.Owner == e.Owner ? 4 : v })
                .Add("Metal_0", new TestCard { Total = (e, seat, line, total) => seat != e.Owner && line == e.SourceLine ? total - 2 : total });
            var game = Load(@"
                line 0: Fire_1 ~Fire_2 |
                line 2: Death_3 | Metal_0
                hand 0: Death_0", effects);
            Assert.That(game.LineValue(0, 0), Is.EqualTo(5));
            Assert.That(game.LineValue(0, 2), Is.EqualTo(1));

            var view = game.ViewFor(1);
            Assert.That(view.Lines[2].Opponent.Total, Is.EqualTo(5), "Bob sees the same total");
            Assert.That(view.Lines[2].Opponent.Cards[1].Value, Is.EqualTo(4));
        }

        [Test]
        public void Rules_PlayingFaceUpWithoutAMatchingProtocol()
        {
            var game = Load(@"
                line 1: Speed_1 |
                hand 0: Death_0", With("Speed_1", new TestCard { FaceUpAnywhere = (e, seat, c, line) => seat == e.Owner }));
            Assert.That(game.CheckPlay(0, game.IdOf("Death_0"), 0, faceUp: true), Is.Null);
        }

        [Test]
        public void Rules_ForbidPlayOnlyWhileUncoveredForBottomText()
        {
            // Plague 0 style: "Your opponent cannot play cards in this line."
            var plague = new TestCard
            {
                Rules = Slot.Bottom,
                Forbid = (e, seat, c, line, up) => seat != e.Owner && line == e.SourceLine ? "Your opponent can't play cards in this line." : null,
            };
            var game = Load(@"
                line 0: | Plague_0
                hand 0: Fire_1", With("Plague_0", plague));
            Assert.That(game.CheckPlay(0, game.IdOf("Fire_1"), 0, faceUp: false), Is.EqualTo("Your opponent can't play cards in this line."));
            Assert.That(game.CheckPlay(0, game.IdOf("Fire_1"), 1, faceUp: false), Is.Null);

            game = Load(@"
                line 0: | Plague_0 Plague_1
                hand 0: Fire_1", With("Plague_0", plague));
            Assert.That(game.CheckPlay(0, game.IdOf("Fire_1"), 0, faceUp: false), Is.Null, "covered bottom text isn't in play");
        }

        [Test]
        public void Rules_WhenNothingCanBePlayedOrRefreshedTheActionIsSkipped()
        {
            var plague = new TestCard { Forbid = (e, seat, c, line, up) => seat != e.Owner ? "No." : null };
            var game = Load(@"
                line 0: | Plague_0
                hand 0: Fire_1 Fire_2 Fire_3 Fire_4 Fire_5
                hand 1: Water_0
                turn 0 Start", With("Plague_0", plague));
            Assert.That(game.History.OfType<ActionSkipped>().Count(), Is.EqualTo(1));
            Assert.That(game.AwaitingEndTurn, Is.True);
        }

        [Test]
        public void Rules_ForbidCompile()
        {
            var metal = new TestCard { NoCompile = (e, seat, line) => seat != e.Owner };
            var game = Load(@"
                line 0: Fire_5 Fire_4 Fire_1 |
                line 2: | Metal_1
                hand 0: Death_0
                turn 0 Start", With("Metal_1", metal));
            Assert.That(game.CanCompile(0, 0), Is.False);
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Rules_ForbidFlip()
        {
            var game = Load(@"
                line 0: Fire_1 |
                line 2: | Metal_2
                hand 0: Death_0", With("Metal_2", new TestCard { NoFlip = (e, c) => c == e.Source }));
            game.Debug("flip-1");
            Assert.That(game.Pending, Is.Null, "only Fire 1 can be flipped");
            Assert.That(game.Find("Fire_1").FaceUp, Is.False);
            Assert.That(game.Find("Metal_2").FaceUp, Is.True);
        }

        [Test]
        public void Rules_SkipCheckCache()
        {
            var game = Load(@"
                line 1: Speed_0 |
                hand 0: Death_0 Death_1 Death_2 Death_3 Death_4 Death_5 Fire_0
                hand 1: Water_0", With("Speed_0", new TestCard { Rules = Slot.Bottom, NoCache = (e, seat) => seat == e.Owner }));
            game.Play("Fire_0", 2, faceUp: false);
            Assert.That(game.Pending, Is.Null);
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(6));
        }

        [Test]
        public void Rules_IgnoreMiddleCommands()
        {
            // Apathy 2 style: "Ignore all middle commands of cards in this line."
            var apathy = new TestCard { IgnoreMiddle = (e, c) => e.Game.FieldLineOf(c) == e.SourceLine };
            var game = Load(@"
                line 0: | Plague_2
                hand 0: Fire_1
                deck 0: Fire_2", new CardEffects().Add("Fire_1", DrawsOne).Add("Plague_2", apathy));
            game.Play("Fire_1", 0);
            Assert.That(game.State.Players[0].Deck.Names(), Is.EqualTo(new[] { "Fire_2" }));
        }
    }
}
