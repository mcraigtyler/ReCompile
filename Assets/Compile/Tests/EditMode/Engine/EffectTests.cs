using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Each building block and kind of question, set off with debug effects. Field line 0 is Ann's left
    // (Fire, facing Bob's Plague), line 1 Speed/Water, line 2 Death/Metal. Bob numbers lines from his own
    // seat, so his line 0 is field line 2.
    public class EffectTests
    {
        const string Protocols = @"
            p0 Ann: Fire Speed Death
            p1 Bob: Metal Water Plague";

        static Game Load(string text) => Scenario.Load(Protocols + text);

        static T Last<T>(IntentResult result) where T : GameEvent => result.Events.OfType<T>().Last();

        // ---- Debug effects ----

        [Test]
        public void DebugEffect_DoesNotUseUpTheAction()
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3
                hand 1: Water_0");
            game.Debug("draw-2");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_1", "Fire_2", "Fire_3" }));
            Assert.That(game.AwaitingAction, Is.True);
            game.Play("Fire_1", 0);
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
        }

        [Test]
        public void DebugEffect_OnlyForTheActivePlayerWhenFree()
        {
            var game = Load(@"
                line 0: ~Fire_1 | ~Metal_2
                hand 0: Fire_3
                hand 1: Water_0");
            Assert.That(game.Submit(new DebugEffect(1, "draw-2")).Accepted, Is.False, "not Bob's turn");
            Assert.That(game.Submit(new DebugEffect(0, "no-such-effect")).Accepted, Is.False);
            game.Debug("flip-1");
            Assert.That(game.Submit(new DebugEffect(0, "draw-2")).Accepted, Is.False, "a question is waiting");
        }

        [Test]
        public void DebugEffect_ThatEmptiesTheHandLeadsToTheForcedRefresh()
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3
                hand 1: Water_0");
            var result = game.Debug("discard-1");
            Assert.That(result.Events.OfType<Refreshed>().Single().Forced, Is.True);
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_1", "Fire_2", "Fire_3" }), "the trash was reshuffled to finish the draw");
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1), "the forced Refresh was Ann's action");
        }

        // ---- Draw and discard ----

        [Test]
        public void Discard_TheChooserIsThePlayerDiscarding()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Water_0 Water_1");
            game.Debug("opponent-discards-1");
            Assert.That(game.Pending.Seat, Is.EqualTo(1));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            game.Pick("Water_1");
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Water_1" }));
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void DiscardAny_DrawsTheAmountDiscardedPlusOne()
        {
            var game = Load(@"
                hand 0: Fire_1 Speed_2 Death_3
                deck 0: Fire_2 Fire_3 Fire_4 Fire_5");
            game.Debug("discard-any");
            Assert.That(game.Pending.Min, Is.EqualTo(1));
            Assert.That(game.Pending.Max, Is.EqualTo(3));
            game.Pick("Fire_1", "Speed_2");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Death_3", "Fire_2", "Fire_3", "Fire_4" }));
        }

        // ---- Flip ----

        [Test]
        public void Flip_ChoosesAnyUncoveredCard()
        {
            var game = Load(@"
                line 0: ~Fire_1 | Metal_2
                hand 0: Fire_3");
            game.Debug("flip-1");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.FieldCards));
            Assert.That(game.Pending.ValidCards, Is.EquivalentTo(new[] { game.IdOf("Fire_1"), game.IdOf("Metal_2") }));

            var result = game.Pick("Fire_1");
            Assert.That(game.Find("Fire_1").FaceUp, Is.True);
            Assert.That(Last<CardsFlipped>(result).Cards.Single().FaceUp, Is.True);
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Flip_CoveredCardsArentChosenUnlessTheTextSaysCovered()
        {
            var game = Load(@"
                line 0: Fire_1 Fire_2 |
                hand 0: Fire_3");
            game.Debug("flip-1");
            Assert.That(game.Pending, Is.Null, "only 1 card could be chosen, so it wasn't asked");
            Assert.That(game.Find("Fire_2").FaceUp, Is.False);
            Assert.That(game.Find("Fire_1").FaceUp, Is.True);

            game.Debug("flip-own-covered");
            Assert.That(game.Pending.ValidCards, Is.EqualTo(new[] { game.IdOf("Fire_1") }));
            game.Pick("Fire_1");
            Assert.That(game.Find("Fire_1").FaceUp, Is.False);
        }

        [Test]
        public void Flip_MayCanBeDeclined()
        {
            var game = Load(@"
                line 0: ~Fire_1 | ~Metal_2
                hand 0: Fire_3");
            game.Debug("may-flip-face-down");
            Assert.That(game.Pending.Optional, Is.True);
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.Find("Fire_1").FaceUp || game.Find("Metal_2").FaceUp, Is.False);
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void FlipEach_TheActivePlayerOrdersThemAndTheyFlipOneAtATime()
        {
            var game = Load(@"
                line 0: Fire_1 | Metal_2
                line 1: ~Speed_1 |
                hand 0: Fire_3");
            game.Debug("flip-each-face-up");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order));
            Assert.That(game.Pending.Options, Is.EqualTo(new[] { "Fire 1", "Metal 2" }));

            var result = game.Do(AnswerChoice.WithOrder(0, 1, 0));
            var flips = result.Events.OfType<CardsFlipped>().Select(f => f.Cards.Single().CardId).ToList();
            Assert.That(flips, Is.EqualTo(new[] { "Metal_2", "Fire_1" }));
        }

        [Test]
        public void FlipAll_IncludesCoveredCardsAndHappensAtOnce()
        {
            var game = Load(@"
                line 0: Fire_1 Fire_2 | Metal_2
                line 1: Speed_1 |
                hand 0: Fire_3");
            game.Debug("flip-all-in-line");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            var result = game.Do(AnswerChoice.WithLine(0, 0));

            Assert.That(result.Events.OfType<CardsFlipped>().Single().Cards, Has.Count.EqualTo(3));
            Assert.That(new[] { "Fire_1", "Fire_2", "Metal_2" }.Any(c => game.Find(c).FaceUp), Is.False);
            Assert.That(game.Find("Speed_1").FaceUp, Is.True);
        }

        // ---- Shift ----

        [Test]
        public void Shift_MovesTheCardToAnotherLineOnItsSide()
        {
            var game = Load(@"
                line 0: Fire_1 Fire_2 |
                hand 0: Fire_3");
            game.Debug("shift-1");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            Assert.That(game.Pending.Purpose, Is.EqualTo(LinePurpose.ShiftTo));
            Assert.That(game.Pending.Subject, Is.SameAs(game.Find("Fire_2")));
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 1, 2 }));

            var result = game.Do(AnswerChoice.WithLine(0, 2));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_1" }));
            Assert.That(game.StackOf(2, 0), Is.EqualTo(new[] { "Fire_2" }));
            Assert.That(Last<CardShifted>(result).ToLine, Is.EqualTo(2));
        }

        [Test]
        public void Shift_ACoveredCardLandsUncoveredOnTheOwnersSide()
        {
            var game = Load(@"
                line 0: Fire_1 | Metal_2 Metal_3
                line 1: | Water_1
                hand 0: Fire_3");
            game.Debug("shift-opponent-covered");
            Assert.That(game.Pending.Seat, Is.EqualTo(0), "Ann's effect, so Ann chooses");
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 1, 2 }), "lines from Ann's seat");
            game.Do(AnswerChoice.WithLine(0, 1));

            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Metal_3" }));
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Water_1", "Metal_2" }));
        }

        [Test]
        public void ShiftAll_MovesEveryMatchingCardTogetherInOrder()
        {
            var game = Load(@"
                line 0: ~Fire_1 ~Fire_2 Fire_3 | ~Metal_2
                hand 0: Fire_4");
            game.Debug("shift-all-face-down");
            game.Do(AnswerChoice.WithLine(0, 0));
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 1, 2 }));
            game.Do(AnswerChoice.WithLine(0, 1));

            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_3" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_1", "Fire_2" }));
            Assert.That(game.StackOf(1, 1), Is.EqualTo(new[] { "Metal_2" }));
        }

        // ---- Delete and return ----

        [Test]
        public void Delete_SendsTheCardFaceUpToItsOwnersTrash()
        {
            var game = Load(@"
                line 0: ~Fire_1 | Metal_2
                hand 0: Fire_3");
            game.Debug("delete-1");
            var result = game.Pick("Fire_1");

            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Fire_1" }));
            Assert.That(game.Find("Fire_1").FaceUp, Is.True);
            Assert.That(Last<CardsDeleted>(result).Seat, Is.EqualTo(0));
        }

        [Test]
        public void DeleteFromEachLine_ResolvesLineByLineInTheChosenOrder()
        {
            var game = Load(@"
                line 0: Fire_1 |
                line 1: | Water_1
                line 2: Death_1 |
                hand 0: Fire_3");
            game.Debug("delete-each-line");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Order));
            Assert.That(game.Pending.Options, Is.EqualTo(new[] { "left line", "middle line", "right line" }));

            var result = game.Do(AnswerChoice.WithOrder(0, 2, 1, 0));
            var deleted = result.Events.OfType<CardsDeleted>().Select(d => d.Cards.Single().CardId);
            Assert.That(deleted, Is.EqualTo(new[] { "Death_1", "Water_1", "Fire_1" }));
        }

        [Test]
        public void DeleteAll_IncludesCoveredAndFaceDownCards()
        {
            // Face-down cards are worth 2, so "values of 1 or 2" includes them.
            var game = Load(@"
                line 0: Fire_1 ~Fire_4 Fire_3 | Metal_2 Metal_0
                hand 0: Fire_5");
            game.Debug("delete-all-low");
            game.Do(AnswerChoice.WithLine(0, 0));

            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_3" }));
            Assert.That(game.StackOf(0, 1), Is.EqualTo(new[] { "Metal_0" }));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EquivalentTo(new[] { "Fire_1", "Fire_4" }));
        }

        [Test]
        public void Return_PutsTheCardInItsOwnersHandWithANewId()
        {
            var game = Load(@"
                line 0: Fire_1 | ~Metal_2
                hand 0: Fire_3");
            var oldId = game.IdOf("Metal_2");
            game.Debug("return-1");
            game.Pick("Metal_2");

            var card = game.State.Players[1].Hand.Single();
            Assert.That(card.Spec.Id, Is.EqualTo("Metal_2"));
            Assert.That(card.Id, Is.Not.EqualTo(oldId));
            Assert.That(card.FaceUp, Is.False);
        }

        // ---- Reveal ----

        [Test]
        public void RevealHand_ShowsTheCardsThenHidesThemWithNewIds()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Water_0 Water_1");
            var ids = game.State.Players[1].Hand.Select(c => c.Id).ToList();
            var result = game.Debug("reveal-opponent-hand");

            Assert.That(result.Events.OfType<CardsRevealed>().Single().Cards.Select(c => c.CardId), Is.EquivalentTo(new[] { "Water_0", "Water_1" }));
            Assert.That(result.Events.OfType<RevealEnded>().Count(), Is.EqualTo(1));
            Assert.That(game.State.Players[1].Hand.Select(c => c.Id).Intersect(ids), Is.Empty);
            Assert.That(game.ViewFor(0).Opponent.RevealedHand, Is.Empty);
        }

        [Test]
        public void RevealFaceDown_ThenFlipThatCard()
        {
            var game = Load(@"
                line 0: Fire_1 | ~Metal_2
                hand 0: Fire_3");
            game.Debug("reveal-face-down");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Option));
            Assert.That(game.Pending.Options, Has.Count.EqualTo(3));
            Assert.That(game.State.Revealed.Contains(game.Find("Metal_2")), Is.True);

            game.Do(AnswerChoice.WithOption(0, 1));
            Assert.That(game.Find("Metal_2").FaceUp, Is.True);
            Assert.That(game.State.Revealed, Is.Empty);
        }

        // ---- Playing cards ----

        [Test]
        public void PlayFromHand_FollowsThePlayRulesAndAnswersWithAPlayCard()
        {
            var game = Load(@"
                hand 0: Fire_1 Speed_2
                hand 1: Water_0");
            game.Debug("play-1");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Play));
            Assert.That(game.Pending.FaceUpAllowed && game.Pending.FaceDownAllowed, Is.True);

            var speed = game.IdOf("Speed_2");
            Assert.That(game.Submit(new PlayCard(0, speed, 0, faceUp: true)).Accepted, Is.False, "Speed doesn't match line 0");
            Assert.That(game.Submit(AnswerChoice.WithCards(0, speed)).Accepted, Is.False, "answered with a play, not cards");
            game.Do(new PlayCard(0, speed, 1, faceUp: true));

            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Speed_2" }));
            Assert.That(game.AwaitingAction, Is.True, "the debug effect didn't use up the action");
        }

        [Test]
        public void PlayFromHand_FaceDownOnly()
        {
            var game = Load(@"
                hand 0: Fire_1 Speed_2");
            game.Debug("play-face-down");
            Assert.That(game.Pending.FaceUpAllowed, Is.False);
            Assert.That(game.Submit(new PlayCard(0, game.IdOf("Fire_1"), 0, faceUp: true)).Accepted, Is.False);
            game.Do(new PlayCard(0, game.IdOf("Fire_1"), 2, faceUp: false));
            Assert.That(game.State.Lines[2].Stack(0).Single().FaceUp, Is.False);
        }

        [Test]
        public void PlayTopOfDeck_FaceDownAndNothingFromAnEmptyDeck()
        {
            // The deck runs out after 2 lines; the trash isn't shuffled for it.
            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3
                trash 0: Death_1");
            game.Debug("play-top-chosen-lines");
            var played = game.Do(AnswerChoice.WithLine(0, 0)).Events.OfType<CardPlayed>().ToList();
            played.AddRange(game.Do(AnswerChoice.WithLine(0, 1)).Events.OfType<CardPlayed>());
            game.Do(AnswerChoice.WithLine(0, 2));

            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_2" }));
            Assert.That(game.StackOf(1, 0), Is.EqualTo(new[] { "Fire_3" }));
            Assert.That(game.State.Lines[2].Stack(0), Is.Empty);
            Assert.That(game.Find("Fire_2").FaceUp, Is.False);
            Assert.That(played.All(p => p.FromDeck), Is.True);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Death_1" }));
            Assert.That(game.Pending, Is.Null, "every line has had its turn");
        }

        [TestCase(new[] { 1 })]
        [TestCase(new[] { 2, 0 })]
        public void PlayTopOfDeck_OnlyInTheLinesChosen(int[] lines)
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3 Fire_4");
            game.Debug("play-top-chosen-lines");
            Assert.That(game.Pending.Optional, Is.False, "at least one line");
            foreach (var line in lines)
                game.Do(AnswerChoice.WithLine(0, line));
            Assert.That(game.Pending.Optional, Is.True, "then another line, or done");
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(Enumerable.Range(0, 3).Except(lines)), "a line only once");
            game.Do(AnswerChoice.Declined(0));

            Assert.That(game.Pending, Is.Null);
            for (var line = 0; line < 3; line++)
                Assert.That(game.State.Lines[line].Stack(0), Has.Count.EqualTo(lines.Contains(line) ? 1 : 0), $"line {line}");
            Assert.That(game.State.Players[0].Deck, Has.Count.EqualTo(3 - lines.Length));
        }

        [Test]
        public void OpponentPlaysTopOfDeck_OnTheirSide()
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 1: Metal_3");
            game.Debug("opponent-plays-top");
            game.Do(AnswerChoice.WithLine(0, 2));
            Assert.That(game.StackOf(2, 1), Is.EqualTo(new[] { "Metal_3" }));
        }

        // ---- Hands ----

        [Test]
        public void Give_TheReceiverOwnsTheCard()
        {
            var game = Load(@"
                hand 0: Fire_1 Speed_2
                hand 1: Water_0");
            game.Debug("give-1");
            game.Pick("Fire_1");
            var card = game.Find("Fire_1");
            Assert.That(game.State.Players[1].Hand, Does.Contain(card));
            Assert.That(card.Owner, Is.EqualTo(1));
        }

        [Test]
        public void TakeRandom_TheTakerOwnsTheCard()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Water_0");
            game.Debug("take-random");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_1", "Water_0" }));
            Assert.That(game.Find("Water_0").Owner, Is.EqualTo(0));
            Assert.That(game.State.Players[1].Hand, Is.Empty);
        }

        [Test]
        public void DrawFromOpponentDeck_TheDrawerOwnsTheCard()
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 1: Metal_3");
            game.Debug("draw-opponent-top");
            Assert.That(game.Find("Metal_3").Owner, Is.EqualTo(0));
            Assert.That(game.State.Players[0].Hand.Names(), Does.Contain("Metal_3"));
        }

        // ---- Protocols ----

        [Test]
        public void Swap_ExactlyTwoOfTheGivenPlayersProtocols()
        {
            var game = Load(@"
                hand 0: Fire_1");
            game.Debug("swap-own");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Swap));
            Assert.That(game.ViewFor(0).Choice.Side, Is.EqualTo(ChoiceSide.Local));
            Assert.That(game.Submit(new Rearrange(0, 0, 1, 2, 0)).Accepted, Is.False, "that moves all 3");
            Assert.That(game.Submit(new Rearrange(0, 1, 1, 0, 2)).Accepted, Is.False, "Bob's Protocols");
            game.Do(new Rearrange(0, 0, 1, 0, 2));
            Assert.That(game.State.Players[0].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Speed", "Fire", "Death" }));
        }

        [Test]
        public void Rearrange_TheOpponentsProtocols()
        {
            var game = Load(@"
                hand 0: Fire_1");
            game.Debug("rearrange-opponent");
            Assert.That(game.Pending.Optional, Is.False);
            Assert.That(game.Submit(new Rearrange(0, 0, 1, 0, 2)).Accepted, Is.False, "Ann's own Protocols");
            game.Do(new Rearrange(0, 1, 2, 0, 1));
            Assert.That(game.State.Players[1].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Plague", "Metal", "Water" }));
        }

        // ---- Options ----

        [Test]
        public void Either_PicksOneOption()
        {
            var game = Load(@"
                hand 0: Fire_1
                deck 0: Fire_2 Fire_3");
            game.Debug("either");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Option));
            Assert.That(game.Submit(AnswerChoice.Declined(0)).Accepted, Is.False, "\"either\" can't be declined");
            Assert.That(game.Submit(AnswerChoice.WithOption(0, 2)).Accepted, Is.False);
            game.Do(AnswerChoice.WithOption(0, 1));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(3));
        }

        [Test]
        public void May_DecliningSkipsTheIfYouDo()
        {
            var game = Load(@"
                line 0: Fire_1 |
                hand 0: Fire_2
                deck 0: Fire_3");
            game.Debug("may-draw");
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(1));
            Assert.That(game.StackOf(0, 0), Is.EqualTo(new[] { "Fire_1" }));

            game.Debug("may-draw");
            game.Do(AnswerChoice.WithOption(0, 0));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(2));
            Assert.That(game.State.Lines[0].Stack(0), Is.Empty, "drew, so deleted the only card");
        }

        // ---- What the board is told ----

        [TestCase("discard-1", ChoiceKind.HandCards, "Discard")]
        [TestCase("give-1", ChoiceKind.HandCards, "Give")]
        [TestCase("reveal-from-hand", ChoiceKind.HandCards, "Reveal")]
        [TestCase("flip-1", ChoiceKind.FieldCards, "Flip")]
        [TestCase("shift-1", ChoiceKind.FieldCards, "Shift")]
        [TestCase("delete-1", ChoiceKind.FieldCards, "Delete")]
        [TestCase("return-1", ChoiceKind.FieldCards, "Return")]
        [TestCase("reveal-face-down", ChoiceKind.FieldCards, "Reveal")]
        public void CardChoice_SaysWhatHappensToThePicks(string effect, ChoiceKind kind, string verb)
        {
            var game = Load(@"
                line 0: ~Fire_1 | ~Metal_2
                line 1: Speed_1 | Water_1
                hand 0: Fire_3 Speed_3
                hand 1: Water_0");
            game.Debug(effect);
            var choice = game.ViewFor(0).Choice;
            Assert.That(choice.Kind, Is.EqualTo(kind));
            Assert.That(choice.Verb, Is.EqualTo(verb));
        }

        [Test]
        public void ClearCache_SaysThePicksAreDiscarded()
        {
            var game = Load(@"
                hand 0: Fire_1 Fire_2 Fire_3 Speed_1 Speed_2 Speed_3 Death_1
                hand 1: Water_0");
            game.Play("Fire_1", 0);
            var choice = game.ViewFor(0).Choice;
            Assert.That(choice.Kind, Is.EqualTo(ChoiceKind.HandCards));
            Assert.That(choice.Verb, Is.EqualTo("Discard"));
        }

        [Test]
        public void DebugEffects_AllHaveAGroupAndAUniqueKey()
        {
            Assert.That(DebugEffects.All.Select(d => d.Key), Is.Unique);
            Assert.That(DebugEffects.All.Where(d => string.IsNullOrEmpty(d.Group)).Select(d => d.Key), Is.Empty);
        }
    }
}
