using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class RedactionTests
    {
        const string Setup = @"
            p0 Ann: Fire Speed Death
            p1 Bob: Metal Water Plague
            hand 0: Fire_0 Speed_5
            deck 0: Fire_2 Fire_3
            hand 1: Metal_0 Water_4
            deck 1: Metal_1";

        [Test]
        public void OpponentsHandAndDeckAreOnlyCounts()
        {
            var game = Scenario.Load(Setup);
            var view = game.ViewFor(1);
            Assert.That(view.Hand.Select(c => c.CardId), Is.EquivalentTo(new[] { "Metal_0", "Water_4" }));
            Assert.That(view.Opponent.HandCount, Is.EqualTo(2));
            Assert.That(view.Opponent.DeckCount, Is.EqualTo(2));
            Assert.That(view.Local.DeckCount, Is.EqualTo(1));
        }

        [Test]
        public void OpponentsFaceDownCardsHaveNoIdentity()
        {
            var game = Scenario.Load(Setup);
            game.Play("Speed_5", 0, faceUp: false);

            var seen = game.ViewFor(1).Lines[2].Opponent.Cards.Single();
            Assert.That(seen.IsHidden, Is.True);
            Assert.That(seen.Protocol, Is.Null);
            Assert.That(seen.PrintedValue, Is.EqualTo(0));
            Assert.That(seen.Value, Is.EqualTo(2));

            var own = game.ViewFor(0).Lines[0].Local.Cards.Single();
            Assert.That(own.CardId, Is.EqualTo("Speed_5"), "the owner knows their face-down card");
            Assert.That(own.FaceUp, Is.False);
        }

        [Test]
        public void FaceUpCardsAndTrashesArePublic()
        {
            var game = Scenario.Load(Setup + @"
                trash 0: Death_1");
            game.Play("Fire_0", 0);
            var view = game.ViewFor(1);
            Assert.That(view.Lines[2].Opponent.Cards.Single().CardId, Is.EqualTo("Fire_0"));
            Assert.That(view.Opponent.Trash.Single().CardId, Is.EqualTo("Death_1"));
            Assert.That(view.Opponent.Trash.Single().LocalOwner, Is.False);
        }

        [Test]
        public void LogTellsTheOpponentOnlyWhatTheyMayKnow()
        {
            var game = Scenario.Load(Setup);
            game.Play("Speed_5", 1, faceUp: false);
            var theirs = string.Join("\n", game.ViewFor(1).Log.Select(l => l.Text));
            var mine = string.Join("\n", game.ViewFor(0).Log.Select(l => l.Text));
            Assert.That(theirs, Does.Contain("Ann played a card face-down"));
            Assert.That(theirs, Does.Not.Contain("Speed 5"));
            Assert.That(mine, Does.Contain("You played Speed 5 face-down"));
        }

        [Test]
        public void DrawnCardsAreNamedOnlyToTheirOwner()
        {
            var game = Scenario.Load(Setup);
            game.Do(new Refresh(0));
            var theirs = string.Join("\n", game.ViewFor(1).Log.Select(l => l.Text));
            Assert.That(theirs, Does.Contain("Ann drew 2 cards"));
            Assert.That(theirs, Does.Not.Contain("Fire 2"));
            Assert.That(string.Join("\n", game.ViewFor(0).Log.Select(l => l.Text)), Does.Contain("You drew Fire 2, Fire 3"));
        }

        [Test]
        public void CardsGetNewIdsWhenTheyGoSomewhereHidden()
        {
            // A card seen in the trash must not be recognizable when it comes back face-down.
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                hand 0: Fire_0
                trash 0: Speed_0 Speed_1 Speed_2 Speed_3 Speed_4
                hand 1: Metal_0");
            var trashIds = game.State.Players[0].Trash.Select(c => c.Id).ToList();
            game.Do(new Refresh(0));

            var handIds = game.State.Players[0].Hand.Select(c => c.Id).ToList();
            Assert.That(handIds.Intersect(trashIds), Is.Empty);
            Assert.That(game.State.AllCards().Select(c => c.Id).Distinct().Count(), Is.EqualTo(game.State.AllCards().Count()));
        }

        [Test]
        public void RevealedCardsAreVisibleToBothOnlyWhileRevealed()
        {
            var game = Scenario.Load(Setup + @"
                line 0: Fire_1 | ~Metal_2");
            game.Debug("reveal-face-down");

            var seen = game.ViewFor(0).Lines[0].Opponent.Cards.Single();
            Assert.That(seen.CardId, Is.EqualTo("Metal_2"), "revealed to Ann while she decides what to do with it");
            Assert.That(seen.Revealed, Is.True);
            Assert.That(seen.FaceUp, Is.False);

            game.Do(AnswerChoice.WithOption(0, 2));
            seen = game.ViewFor(0).Lines[0].Opponent.Cards.Single();
            Assert.That(seen.CardId, Is.Null, "hidden again once the text is done");
            Assert.That(seen.Revealed, Is.False);
        }

        [Test]
        public void RevealedHandCardsShowInTheOpponentsViewAndLog()
        {
            var game = Scenario.Load(Setup);
            game.Debug("reveal-from-hand");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            game.Pick("Speed_5");

            var log = string.Join(" | ", game.ViewFor(1).Log.Select(l => l.Text));
            Assert.That(log, Does.Contain("Ann revealed Speed 5 from their hand."));
            Assert.That(game.ViewFor(1).Opponent.RevealedHand, Is.Empty, "the reveal is over");
            Assert.That(game.State.Players[0].Hand.Select(c => c.Id), Has.No.Member(game.History.OfType<CardsRevealed>().Single().Cards.Single().InstanceId),
                "the revealed card has a new id, since Ann could have shuffled her hand");
        }

        [Test]
        public void ShiftedAndReturnedFaceDownCardsArentNamedToTheOpponent()
        {
            var game = Scenario.Load(Setup + @"
                line 0: ~Fire_1 ~Fire_4 |");
            game.Debug("shift-1");
            game.Do(AnswerChoice.WithLine(0, 1));
            game.Debug("return-1");
            game.Pick("Fire_1");

            var theirs = string.Join(" | ", game.ViewFor(1).Log.Select(l => l.Text));
            Assert.That(theirs, Does.Contain("Ann shifted a face-down card"));
            Assert.That(theirs, Does.Contain("Ann returned a face-down card"));
            Assert.That(theirs, Does.Not.Contain("Fire 4").And.Not.Contain("Fire 1"));
        }
    }
}
