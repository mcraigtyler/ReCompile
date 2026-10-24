using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class CompileTests
    {
        [Test]
        public void Compile_DeletesBothSidesAndFlipsTheProtocol()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 | Plague_3
                hand 1: Metal_0
                turn 0 Start");

            Assert.That(game.State.Lines[0].Stack(0), Is.Empty);
            Assert.That(game.State.Lines[0].Stack(1), Is.Empty);
            Assert.That(game.State.Players[0].Trash.Names(), Is.EquivalentTo(new[] { "Fire_5", "Fire_4", "Fire_1" }));
            Assert.That(game.State.Players[1].Trash.Names(), Is.EqualTo(new[] { "Plague_3" }), "cards go to their owner's trash");
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.True);

            var deleted = game.History.OfType<CardsDeleted>().Single();
            Assert.That(deleted.Cards, Has.Count.EqualTo(4), "one simultaneous delete");
        }

        [Test]
        public void Compile_IsTheActionForTheTurn()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                hand 0: Fire_0
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1), "no Action step after compiling");
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Fire_0" }));
        }

        [Test]
        public void Compile_FaceDownCardsCount2AndGoFaceUpToTheTrash()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_3 ~Speed_5 |
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.True);
            Assert.That(game.State.Players[0].Trash.All(c => c.FaceUp), Is.True);
        }

        [Test]
        public void Compile_NeedsAtLeast10()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 |
                hand 0: Fire_0
                turn 0 Start");
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.False);
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Compile_NeedsMoreValueThanTheOpponentInThatLine()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 | Plague_5 Plague_4 Plague_1
                hand 0: Fire_0
                turn 0 Start");
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.False);
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Compile_PlayerChoosesWhenSeveralLinesQualify_OnlyOnePerTurn()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                line 1: Speed_5 Speed_4 Speed_1 |
                hand 1: Metal_0
                turn 0 Start");

            var choice = game.Pending;
            Assert.That(choice.Kind, Is.EqualTo(ChoiceKind.Line));
            Assert.That(choice.ValidLines, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(game.Submit(AnswerChoice.WithLine(0, 2)).Accepted, Is.False);

            game.Do(AnswerChoice.WithLine(0, 1));
            game.Do(AnswerChoice.Declined(0));      // leading in 2 lines took Control: decline its rearrange
            Assert.That(game.State.Players[0].Protocols[1].Compiled, Is.True);
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.False);
            Assert.That(game.State.Lines[0].Stack(0), Has.Count.EqualTo(3), "the other line waits for next turn");
        }

        [Test]
        public void Compile_LineChoiceIsFromTheChoosersSeat()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 2: | Metal_6 Metal_5
                line 1: | Water_5 Water_4 Water_1
                hand 0: Fire_0
                turn 1 Start");
            Assert.That(game.Pending.ValidLines, Is.EquivalentTo(new[] { 0, 1 }), "field line 2 is seat 1's left");
            game.Do(AnswerChoice.WithLine(1, 0));
            game.Do(AnswerChoice.Declined(1));      // Control's rearrange
            Assert.That(game.State.Players[1].Protocols[0].Compiled, Is.True);
            Assert.That(game.State.Lines[2].Stack(1), Is.Empty);
        }

        [Test]
        public void Compile_WithControl_ControlReturnsAndTheRearrangeHappensFirst()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                hand 1: Metal_0
                control 0
                turn 0 Start");

            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));
            Assert.That(game.State.Lines[0].Stack(0), Has.Count.EqualTo(3), "nothing is deleted yet");

            // Swap Fire and Speed: Speed is now "your Protocol in that line", so Speed compiles.
            game.Do(new Rearrange(0, 0, 1, 0, 2));
            Assert.That(game.State.Players[0].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Speed", "Fire", "Death" }));
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.True);
            Assert.That(game.State.Players[0].Protocols[1].Compiled, Is.False);
            Assert.That(game.State.Lines[0].Stack(0), Is.Empty);
        }

        [Test]
        public void Compile_WithControl_RearrangeIsOptional()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                hand 1: Metal_0
                control 0
                turn 0 Start");
            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.State.Players[0].Protocols[0].Compiled, Is.True);
            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
        }

        [Test]
        public void Recompile_DrawsTheOpponentsTopCardAndTakesOwnership()
        {
            var game = Scenario.Load(@"
                p0: Fire* Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                deck 1: Metal_5 Metal_6
                hand 1: Metal_0
                turn 0 Start");

            var taken = game.Find("Metal_5");
            Assert.That(game.State.Players[0].Hand, Does.Contain(taken));
            Assert.That(taken.Owner, Is.EqualTo(0));
            Assert.That(game.State.Players[1].Deck.Names(), Is.EqualTo(new[] { "Metal_6" }));
            Assert.That(game.History.OfType<ProtocolCompiled>(), Is.Empty);
            Assert.That(game.History.OfType<LineCompiled>().Single().Recompile, Is.True);
        }

        [Test]
        public void Recompile_ShufflesTheOpponentsTrashIfTheirDeckIsEmpty()
        {
            var game = Scenario.Load(@"
                p0: Fire* Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                trash 1: Metal_5
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.Find("Metal_5").Owner, Is.EqualTo(0));
            Assert.That(game.State.Players[0].Hand.Names(), Does.Contain("Metal_5"));
        }

        [Test]
        public void Recompile_TakenCardGoesToItsNewOwnersTrash()
        {
            var game = Scenario.Load(@"
                p0: Fire* Speed Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                deck 1: Metal_5
                hand 0: Fire_0 Fire_2 Fire_3 Speed_0 Speed_1
                hand 1: Metal_0
                turn 0 Start");

            // 5 cards plus the taken one: Clear Cache.
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            Assert.That(game.Pending.Min, Is.EqualTo(1));
            game.Do(AnswerChoice.WithCards(0, game.IdOf("Metal_5")));
            Assert.That(game.State.Players[0].Trash.Names(), Does.Contain("Metal_5"));
            Assert.That(game.State.Players[1].Trash.Names(), Has.No.Member("Metal_5"));
        }

        [Test]
        public void Victory_CompilingTheThirdProtocolWins()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed* Death*
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Over));
            Assert.That(game.State.Winner, Is.EqualTo(0));
            Assert.That(game.State.EndReason, Is.EqualTo(EndReason.Compiled));
            Assert.That(game.History.OfType<GameEnded>().Single().Winner, Is.EqualTo(0));
        }

        [Test]
        public void Victory_RecompilingDoesNotWin()
        {
            var game = Scenario.Load(@"
                p0: Fire* Speed* Death
                p1: Metal Water Plague
                line 0: Fire_5 Fire_4 Fire_1 |
                hand 1: Metal_0
                turn 0 Start");
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Playing));
        }
    }
}
