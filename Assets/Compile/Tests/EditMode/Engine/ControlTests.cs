using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class ControlTests
    {
        const string Protocols = @"
            p0: Fire Speed Death
            p1: Metal Water Plague
            hand 0: Fire_0
            hand 1: Metal_0";

        [Test]
        public void Control_TakenWhenLeadingIn2Lines()
        {
            var game = Scenario.Load(Protocols + @"
                line 0: Fire_1 |
                line 1: Speed_1 |
                turn 0 Start");
            Assert.That(game.State.Control, Is.EqualTo(0));
            Assert.That(game.History.OfType<ControlChanged>().Single().Holder, Is.EqualTo(0));
        }

        [Test]
        public void Control_NotTakenWithALeadInOnly1Line()
        {
            var game = Scenario.Load(Protocols + @"
                line 0: Fire_1 |
                turn 0 Start");
            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
        }

        [Test]
        public void Control_TiesDontCountAsALead()
        {
            var game = Scenario.Load(Protocols + @"
                line 0: Fire_1 | Plague_1
                line 1: Speed_1 | Water_1
                line 2: Death_3 |
                turn 0 Start");
            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
        }

        [Test]
        public void Control_TakenFromTheOpponent()
        {
            var game = Scenario.Load(Protocols + @"
                line 0: Fire_1 |
                line 1: Speed_1 |
                control 1
                turn 0 Start");
            Assert.That(game.State.Control, Is.EqualTo(0));
        }

        [Test]
        public void Control_OnlyTheActivePlayerChecks()
        {
            var game = Scenario.Load(Protocols + @"
                line 0: Fire_1 |
                line 1: Speed_1 |
                turn 1 Start");
            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
        }

        [Test]
        public void Refresh_WithControl_ReturnsItToNeutralAndOffersARearrangeFirst()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                hand 0: Fire_0 Fire_1
                deck 0: Fire_2 Fire_3 Fire_4
                hand 1: Metal_0
                control 0");
            game.Do(new Refresh(0));

            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.Rearrange));
            Assert.That(game.Pending.Optional, Is.True);
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(2), "the refresh waits for the rearrange");

            game.Do(AnswerChoice.Declined(0));
            Assert.That(game.State.Players[0].Hand, Has.Count.EqualTo(5));
        }

        [Test]
        public void Rearrange_MovesTheOpponentsProtocolsButNotTheCards()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                hand 0: Fire_0 Fire_1
                deck 0: Fire_2 Fire_3 Fire_4
                hand 1: Metal_0
                line 2: | Metal_3
                control 0");
            game.Do(new Refresh(0));
            game.Do(new Rearrange(0, 1, 2, 1, 0));

            Assert.That(game.State.Players[1].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Plague", "Water", "Metal" }));
            Assert.That(game.State.Lines[2].Stack(1).Names(), Is.EqualTo(new[] { "Metal_3" }), "cards stay in their line");
            Assert.That(game.State.ProtocolIn(1, 2).Name, Is.EqualTo("Plague"));
        }

        [Test]
        public void Rearrange_MustDifferFromTheStartingArrangement()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                hand 0: Fire_0
                deck 0: Fire_2
                hand 1: Metal_0
                control 0");
            game.Do(new Refresh(0));
            Assert.That(game.Submit(new Rearrange(0, 0, 0, 1, 2)).Accepted, Is.False);
            Assert.That(game.Submit(new Rearrange(0, 0, 0, 0, 1)).Accepted, Is.False, "not a permutation");
            Assert.That(game.Submit(new Rearrange(1, 0, 1, 0, 2)).Accepted, Is.False, "not their choice");
            Assert.That(game.Submit(new Rearrange(0, 0, 1, 0, 2)).Accepted, Is.True);
        }

        [Test]
        public void Rearrange_CompiledStatusMovesWithTheProtocol()
        {
            var game = Scenario.Load(@"
                p0: Fire* Speed Death
                p1: Metal Water Plague
                hand 0: Fire_0
                deck 0: Fire_2
                hand 1: Metal_0
                control 0");
            game.Do(new Refresh(0));
            game.Do(new Rearrange(0, 0, 2, 1, 0));
            Assert.That(game.State.Players[0].Protocols[2].Name, Is.EqualTo("Fire"));
            Assert.That(game.State.Players[0].Protocols[2].Compiled, Is.True);
        }
    }
}
