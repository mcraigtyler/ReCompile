using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class UndoTests
    {
        const string Protocols = @"
            p0 Ann: Fire Speed Death
            p1 Bob: Metal Water Plague";

        static Game Load(string text) => Scenario.Load(Protocols + text, confirmEndTurn: true);

        static int[] HandIds(Game game, int seat) => game.State.Players[seat].Hand.Select(c => c.Id).ToArray();

        [Test]
        public void EndTurn_TheTurnWaitsForTheActivePlayerToEndIt()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Metal_0");
            game.Play("Fire_1", 0);

            Assert.That(game.AwaitingEndTurn, Is.True);
            Assert.That(game.State.ActiveSeat, Is.EqualTo(0));
            Assert.That(game.State.Phase, Is.EqualTo(TurnPhase.End));
            Assert.That(game.ViewFor(0).CanEndTurn, Is.True);
            Assert.That(game.ViewFor(1).CanEndTurn, Is.False);
            Assert.That(game.Submit(new EndTurn(1)).Accepted, Is.False, "only the active player ends their turn");

            game.Do(new EndTurn(0));
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void EndTurn_NotBeforeTheTurnIsDone()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Metal_0");
            Assert.That(game.Submit(new EndTurn(0)).Accepted, Is.False);
        }

        [Test]
        public void Undo_TakesBackAPlay()
        {
            var game = Load(@"
                hand 0: Fire_1 Speed_2
                hand 1: Metal_0");
            var hand = HandIds(game, 0);
            game.Play("Fire_1", 0);

            game.Do(new Undo(0));
            Assert.That(HandIds(game, 0), Is.EqualTo(hand), "same cards, same ids");
            Assert.That(game.State.Lines[0].Stack(0), Is.Empty);
            Assert.That(game.AwaitingAction, Is.True);

            game.Play("Speed_2", 1, faceUp: false);
            Assert.That(game.State.Lines[1].Stack(0).Names(), Is.EqualTo(new[] { "Speed_2" }));
        }

        [Test]
        public void Undo_IsUnlimitedBackToTheStartOfTheTurn()
        {
            // Two lines can compile and Ann leads in 2 lines, so she takes Control: choose the line, then
            // Control's rearrange. Both decisions can be undone, one at a time.
            var game = Load(@"
                line 0: Fire_5 Fire_4 Fire_1 |
                line 1: Speed_5 Speed_4 Speed_1 |
                hand 0: Fire_0
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            Assert.That(game.CheckUndo(0), Is.Not.Null, "nothing done yet");

            game.Do(AnswerChoice.WithLine(0, 1));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));
            game.Do(new Rearrange(0, 1, 2, 1, 0));
            Assert.That(game.AwaitingEndTurn, Is.True);
            Assert.That(game.State.Players[0].Protocols[1].Compiled, Is.True);

            game.Do(new Undo(0));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));
            Assert.That(game.State.Players[1].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Metal", "Water", "Plague" }));
            Assert.That(game.State.Players[0].Protocols[1].Compiled, Is.False);

            game.Do(new Undo(0));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Line));
            Assert.That(game.Submit(new Undo(0)).Accepted, Is.False, "back at the start of the turn");

            game.Do(AnswerChoice.WithLine(0, 0));
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.True);
        }

        [Test]
        public void Undo_TakesBackAClearCacheDiscard()
        {
            var game = Load(@"
                hand 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4 Fire_5
                hand 1: Metal_0
                turn 0 Cache");
            game.Do(AnswerChoice.WithCards(0, game.IdOf("Fire_0")));
            Assert.That(game.AwaitingEndTurn, Is.True);

            game.Do(new Undo(0));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(6));
            Assert.That(game.State.Players[0].Trash, Is.Empty);
        }

        [Test]
        public void Undo_NotPastADraw()
        {
            // Undoing a Refresh after seeing the drawn cards would let a player peek at their deck.
            var game = Load(@"
                hand 0: Fire_0 Fire_1
                deck 0: Fire_2 Fire_3 Fire_4
                hand 1: Metal_0");
            game.Do(new Refresh(0));

            Assert.That(game.CheckUndo(0), Does.Contain("drew"));
            Assert.That(game.ViewFor(0).CanUndo, Is.False);
            Assert.That(game.ViewFor(0).UndoBlockedReason, Does.Contain("drew"));
            Assert.That(game.Submit(new Undo(0)).Accepted, Is.False);
        }

        [Test]
        public void Undo_ARefreshWithControlBeforeTheCardsAreDrawn()
        {
            // Control returns to neutral and offers a rearrange first; nothing has been drawn yet.
            var game = Load(@"
                hand 0: Fire_0
                deck 0: Fire_2
                hand 1: Metal_0
                control 0");
            game.Do(new Refresh(0));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));

            game.Do(new Undo(0));
            Assert.That(game.AwaitingAction, Is.True);
            Assert.That(game.State.Control, Is.EqualTo(0));
        }

        [Test]
        public void Undo_OnlyByTheActivePlayerAndNotAcrossEndTurn()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Metal_0");
            game.Play("Fire_1", 0);
            Assert.That(game.Submit(new Undo(1)).Accepted, Is.False);

            game.Do(new EndTurn(0));
            Assert.That(game.Submit(new Undo(0)).Accepted, Is.False, "the turn has passed");
            Assert.That(game.Submit(new Undo(1)).Accepted, Is.False, "Bob hasn't done anything yet");
            Assert.That(game.State.Lines[0].Stack(0).Names(), Is.EqualTo(new[] { "Fire_1" }));
        }

        [Test]
        public void Undo_IsLoggedForBothPlayers()
        {
            var game = Load(@"
                hand 0: Fire_1
                hand 1: Metal_0");
            game.Play("Fire_1", 0);
            game.Do(new Undo(0));
            Assert.That(game.ViewFor(0).Log.Last().Text, Is.EqualTo("You undid a play."));
            Assert.That(game.ViewFor(1).Log.Last().Text, Is.EqualTo("Ann undid a play."));
        }

        static readonly string[] Pool = { "Darkness", "Death", "Fire", "Gravity", "Life", "Light", "Metal", "Plague" };

        [Test]
        public void Draft_EachRoundOfPicksIsConfirmed()
        {
            var game = Game.Create(Scenario.Catalog, Pool, firstSeat: 0, seed: 4);
            game.Do(new DraftPick(0, "Fire"));
            Assert.That(game.DraftConfirmSeat, Is.EqualTo(0));
            Assert.That(game.ViewFor(0).Draft.LocalConfirm, Is.True);
            Assert.That(game.ViewFor(0).CanEndTurn, Is.True);
            Assert.That(game.ViewFor(1).Draft.LocalPick, Is.False, "seat 1 waits for the confirmation");
            Assert.That(game.Submit(new DraftPick(1, "Metal")).Accepted, Is.False);
            Assert.That(RandomBot.SeatToMove(game), Is.EqualTo(0));

            game.Do(new EndTurn(0));
            Assert.That(game.ViewFor(1).Draft.LocalPick, Is.True);
            game.Do(new DraftPick(1, "Metal"));
            Assert.That(game.DraftConfirmSeat, Is.EqualTo(-1), "one more pick this round");
            game.Do(new DraftPick(1, "Death"));
            Assert.That(game.DraftConfirmSeat, Is.EqualTo(1));
        }

        [Test]
        public void Draft_PicksCanBeUndoneUntilConfirmed()
        {
            var game = Game.Create(Scenario.Catalog, Pool, firstSeat: 0, seed: 4);
            game.Do(new DraftPick(0, "Fire"));
            game.Do(new Undo(0));
            Assert.That(game.State.Draft.Available, Does.Contain("Fire"));
            Assert.That(game.DraftConfirmSeat, Is.EqualTo(-1));
            Assert.That(game.ViewFor(0).Draft.LocalPick, Is.True);

            game.Do(new DraftPick(0, "Light"));
            game.Do(new EndTurn(0));
            Assert.That(game.Submit(new Undo(0)).Accepted, Is.False, "confirmed picks are final");
            Assert.That(game.Submit(new Undo(1)).Accepted, Is.False, "seat 1 hasn't picked yet");

            game.Do(new DraftPick(1, "Metal"));
            game.Do(new DraftPick(1, "Death"));
            game.Do(new Undo(1));
            game.Do(new Undo(1));
            Assert.That(game.State.Draft.PicksFor(1), Is.Empty);
            Assert.That(game.ViewFor(1).Log.Last().Text, Is.EqualTo("You undid drafting Metal."));
        }

        [Test]
        public void Draft_TheGameStartsWhenTheLastPicksAreConfirmed()
        {
            var game = Game.Create(Scenario.Catalog, Pool, firstSeat: 0, seed: 4);
            foreach (var (seat, protocol) in new[] { (0, "Fire"), (1, "Metal"), (1, "Death"), (0, "Light"), (0, "Life"), (1, "Plague") })
            {
                game.Do(new DraftPick(seat, protocol));
                if (game.DraftConfirmSeat >= 0)
                    game.Do(new EndTurn(seat));
                else
                    Assert.That(seat, Is.EqualTo(game.State.Draft.CurrentSeat));
            }
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Playing));
            Assert.That(game.State.Players[1].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Metal", "Death", "Plague" }));
        }

        [Test]
        public void Undo_WorksInADraftedGame()
        {
            var game = Game.CreateDrafted(Scenario.Catalog, new[] { "Fire", "Speed", "Death" }, new[] { "Metal", "Water", "Plague" },
                firstSeat: 0, seed: 9);
            Assert.That(game.CheckUndo(0), Is.Not.Null, "the opening draw is a barrier");

            var card = game.State.Players[0].Hand[0];
            var before = HandIds(game, 0);
            var deck = game.State.Players[1].Deck.Select(c => c.Spec.Id).ToArray();
            game.Do(new PlayCard(0, card.Id, 0, faceUp: false));
            game.Do(new Undo(0));
            Assert.That(HandIds(game, 0), Is.EqualTo(before));
            Assert.That(game.State.Players[1].Deck.Select(c => c.Spec.Id), Is.EqualTo(deck), "the replay shuffles the same way");
        }
    }
}
