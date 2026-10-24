using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class TurnTests
    {
        const string Protocols = @"
            p0 Ann: Fire Speed Death
            p1 Bob: Metal Water Plague";

        [Test]
        public void Turn_PhasesRunStartControlCompileActionCacheEnd()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_1 Fire_2
                hand 1: Metal_0 Metal_1
                turn 0 Start");
            Assert.That(Phases(game.TakeEvents()), Is.EqualTo(new[] { TurnPhase.Start, TurnPhase.Control, TurnPhase.Compile, TurnPhase.Action }));

            var events = game.Play("Fire_1", 0).Events;
            Assert.That(Phases(events), Is.EqualTo(new[]
            {
                TurnPhase.Cache, TurnPhase.End,
                TurnPhase.Start, TurnPhase.Control, TurnPhase.Compile, TurnPhase.Action,
            }));
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
            Assert.That(game.State.Turn, Is.EqualTo(2));
        }

        static TurnPhase[] Phases(System.Collections.Generic.IEnumerable<GameEvent> events) =>
            events.OfType<PhaseChanged>().Select(e => e.Phase).ToArray();

        [Test]
        public void Play_FaceUpOnlyIntoALineWithAMatchingProtocolOnEitherSide()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_1 Metal_2@0 Speed_3
                hand 1: Water_0");

            Assert.That(game.CheckPlay(0, game.IdOf("Fire_1"), 1, faceUp: true), Is.Not.Null, "Fire into Speed vs Water");
            Assert.That(game.CheckPlay(0, game.IdOf("Fire_1"), 0, faceUp: true), Is.Null, "Fire into its own line");
            // Seat 0's right line faces seat 1's left (Metal), so a Metal card may go face-up there.
            Assert.That(game.CheckPlay(0, game.IdOf("Metal_2"), 2, faceUp: true), Is.Null, "matching the opponent's Protocol");
            Assert.That(game.CheckPlay(0, game.IdOf("Metal_2"), 0, faceUp: true), Is.Not.Null);

            var rejected = game.Submit(new PlayCard(0, game.IdOf("Speed_3"), 0, faceUp: true));
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.Error, Does.Contain("Speed"));
        }

        [Test]
        public void Play_FaceDownGoesIntoAnyLineWorth2()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Speed_5
                hand 1: Water_0");
            game.Play("Speed_5", 0, faceUp: false);

            var card = game.State.Lines[0].Stack(0).Single();
            Assert.That(card.FaceUp, Is.False);
            Assert.That(game.LineValue(0, 0), Is.EqualTo(2));
        }

        [Test]
        public void Play_CoversTheCardBelow()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_1
                hand 1: Water_0
                line 0: Fire_4 |");
            game.Play("Fire_1", 0);
            Assert.That(game.State.Lines[0].Uncovered(0).Spec.Id, Is.EqualTo("Fire_1"));
            Assert.That(game.LineValue(0, 0), Is.EqualTo(5));
        }

        [Test]
        public void Play_OnlyByTheActivePlayerInTheirActionStep()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_1
                hand 1: Metal_0");
            var result = game.Submit(new PlayCard(1, game.IdOf("Metal_0"), 0, faceUp: false));
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Error, Does.Contain("turn"));
        }

        [Test]
        public void Play_RejectsACardNotInHand()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_1
                deck 0: Fire_2");
            Assert.That(game.Submit(new PlayCard(0, game.IdOf("Fire_2"), 0, faceUp: false)).Accepted, Is.False);
        }

        [Test]
        public void Refresh_OnlyWithFewerThan5Cards()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4
                deck 0: Fire_5");
            Assert.That(game.Submit(new Refresh(0)).Accepted, Is.False);
        }

        [Test]
        public void Refresh_DrawsUpTo5AndIsTheAction()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0 Fire_1
                deck 0: Fire_2 Fire_3 Fire_4 Fire_5
                hand 1: Metal_0");
            game.Do(new Refresh(0));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EquivalentTo(new[] { "Fire_0", "Fire_1", "Fire_2", "Fire_3", "Fire_4" }));
            Assert.That(game.State.Players[0].Deck.Names(), Is.EqualTo(new[] { "Fire_5" }));
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
        }

        [Test]
        public void Refresh_ForcedWhenTheHandIsEmpty()
        {
            var game = Scenario.Load(Protocols + @"
                deck 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4 Fire_5
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(5));
            Assert.That(game.History.OfType<Refreshed>().Single().Forced, Is.True);
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
        }

        [Test]
        public void Action_SkippedWhenThePlayerCanNeitherPlayNorDraw()
        {
            var game = Scenario.Load(Protocols + @"
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.History.OfType<ActionSkipped>().Single().Seat, Is.EqualTo(0));
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Refresh_NotAllowedWhenThereIsNothingToDraw()
        {
            // House ruling: a Refresh that can't draw anything isn't an action.
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_1");
            Assert.That(game.CheckRefresh(0), Is.Not.Null);
            Assert.That(game.ViewFor(0).CanRefresh, Is.False);
        }

        [Test]
        public void Draw_ShufflesTheTrashIntoANewDeckWhenTheDeckRunsOut()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0 Fire_1
                deck 0: Fire_2
                trash 0: Speed_0 Speed_1 Speed_2 Speed_3
                hand 1: Metal_0");
            var events = game.Do(new Refresh(0)).Events;

            var player = game.State.Players[0];
            Assert.That(player.Hand, Has.Count.EqualTo(5));
            Assert.That(player.Hand.Names(), Does.Contain("Fire_2"), "the deck's last card is drawn first");
            Assert.That(player.Trash, Is.Empty);
            Assert.That(player.Deck, Has.Count.EqualTo(2));
            Assert.That(events.OfType<DeckReshuffled>().Single().Count, Is.EqualTo(4));
            Assert.That(events.OfType<CardsDrawn>().Single().Cards, Has.Count.EqualTo(3), "drawn as one batch");
        }

        [Test]
        public void Draw_StopsWhenDeckAndTrashAreBothEmpty()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0
                deck 0: Fire_1
                hand 1: Metal_0");
            game.Do(new Refresh(0));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(2));
        }

        [Test]
        public void ClearCache_DiscardsDownTo5()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4 Fire_5 Speed_0
                hand 1: Metal_0
                turn 0 Cache");
            var choice = game.Pending;
            Assert.That(choice.Kind, Is.EqualTo(ChoiceKind.HandCards));
            Assert.That(choice.Min, Is.EqualTo(2));
            Assert.That(choice.Max, Is.EqualTo(2));
            Assert.That(game.ViewFor(0).Choice.Prompt, Does.Contain("discard 2"));
            Assert.That(game.ViewFor(1).Choice, Is.Null, "only the chooser sees the choice");

            Assert.That(game.Submit(AnswerChoice.WithCards(0, game.IdOf("Fire_0"))).Accepted, Is.False, "must discard exactly 2");
            Assert.That(game.Submit(AnswerChoice.WithCards(1, game.IdOf("Metal_0"))).Accepted, Is.False, "not their choice");
            Assert.That(game.Submit(AnswerChoice.Declined(0)).Accepted, Is.False, "not optional");

            game.Do(AnswerChoice.WithCards(0, game.IdOf("Fire_0"), game.IdOf("Speed_0")));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(5));
            Assert.That(game.State.Players[0].Trash.Names(), Is.EquivalentTo(new[] { "Fire_0", "Speed_0" }));
            Assert.That(game.State.Players[0].Trash.All(c => c.FaceUp), Is.True);
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
        }

        [Test]
        public void Choice_BlocksOtherIntentsUntilAnswered()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0 Fire_1 Fire_2 Fire_3 Fire_4 Fire_5
                hand 1: Metal_0
                turn 0 Cache");
            Assert.That(game.Submit(new PlayCard(0, game.IdOf("Fire_0"), 0, true)).Accepted, Is.False);
            Assert.That(game.AwaitingAction, Is.False);
        }

        [Test]
        public void Concede_EndsTheGameForTheOtherPlayer()
        {
            var game = Scenario.Load(Protocols + @"
                hand 0: Fire_0
                hand 1: Metal_0");
            game.Do(new Concede(1));
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Over));
            Assert.That(game.State.Winner, Is.EqualTo(0));
            Assert.That(game.State.EndReason, Is.EqualTo(EndReason.Conceded));
            Assert.That(game.ViewFor(0).Outcome, Is.EqualTo(Outcome.LocalWon));
            Assert.That(game.ViewFor(1).Outcome, Is.EqualTo(Outcome.OpponentWon));
            Assert.That(game.Submit(new PlayCard(0, game.IdOf("Fire_0"), 0, true)).Accepted, Is.False);
        }

        [Test]
        public void Stalemate_EndsTheGameWhenNeitherPlayerCanActForTwoTurns()
        {
            // House ruling: nothing can change, so the game ends; more compiled Protocols wins.
            var game = Scenario.Load(@"
                p0: Fire* Speed Death
                p1: Metal Water Plague
                line 0: Fire_1 | Metal_1
                turn 0 Start");
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Over));
            Assert.That(game.State.EndReason, Is.EqualTo(EndReason.Stalemate));
            Assert.That(game.State.Winner, Is.EqualTo(0));
        }

        [Test]
        public void Stalemate_IsADrawWithEqualCompiledProtocols()
        {
            var game = Scenario.Load(Protocols + @"
                turn 1 Start");
            Assert.That(game.State.EndReason, Is.EqualTo(EndReason.Stalemate));
            Assert.That(game.State.Winner, Is.EqualTo(-1));
            Assert.That(game.ViewFor(0).Outcome, Is.EqualTo(Outcome.Draw));
        }
    }
}
