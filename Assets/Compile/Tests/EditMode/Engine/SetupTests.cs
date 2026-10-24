using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class SetupTests
    {
        static readonly string[] Left = { "Fire", "Speed", "Death" };
        static readonly string[] Right = { "Metal", "Water", "Plague" };

        static Game NewGame(long seed = 1, int firstSeat = 0) =>
            Game.CreateDrafted(Scenario.Catalog, Left, Right, firstSeat, seed);

        [Test]
        public void Setup_Builds18CardDecksFromThe3ProtocolsAndDeals5()
        {
            var game = NewGame();
            for (var seat = 0; seat < 2; seat++)
            {
                var player = game.State.Players[seat];
                Assert.That(player.Hand, Has.Count.EqualTo(5));
                Assert.That(player.Deck, Has.Count.EqualTo(13));
                var all = player.Hand.Concat(player.Deck).ToList();
                Assert.That(all.Select(c => c.Spec.Protocol).Distinct(), Is.EquivalentTo(seat == 0 ? Left : Right));
                Assert.That(all.All(c => c.Owner == seat), Is.True);
            }
            Assert.That(game.State.AllCards().Count(), Is.EqualTo(36));
        }

        [Test]
        public void Setup_ProtocolsStartLoadingAndControlNeutral()
        {
            var game = NewGame();
            Assert.That(game.State.Players.SelectMany(p => p.Protocols).Any(p => p.Compiled), Is.False);
            Assert.That(game.State.Control, Is.EqualTo(GameState.Neutral));
        }

        [Test]
        public void Setup_FirstPlayerTakesTheFirstTurnAndWaitsInTheActionStep()
        {
            var game = NewGame(firstSeat: 1);
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1));
            Assert.That(game.State.Turn, Is.EqualTo(1));
            Assert.That(game.State.Phase, Is.EqualTo(TurnPhase.Action));
            Assert.That(game.AwaitingAction, Is.True);
        }

        [Test]
        public void Shuffle_IsDeterministicForASeed()
        {
            var a = NewGame(seed: 42);
            var b = NewGame(seed: 42);
            var c = NewGame(seed: 43);
            Assert.That(a.State.Players[0].Hand.Names(), Is.EqualTo(b.State.Players[0].Hand.Names()));
            Assert.That(a.State.Players[1].Deck.Names(), Is.EqualTo(b.State.Players[1].Deck.Names()));
            Assert.That(a.State.Players[0].Deck.Names(), Is.Not.EqualTo(c.State.Players[0].Deck.Names()));
        }

        [Test]
        public void Draft_PicksGoOneTwoTwoOneThenTheGameStarts()
        {
            var pool = new[] { "Darkness", "Death", "Fire", "Gravity", "Life", "Light", "Metal", "Plague" };
            var game = Game.Create(Scenario.Catalog, pool, firstSeat: 1, seed: 3, confirmEndTurn: false);
            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Draft));

            Assert.That(game.Submit(new DraftPick(0, "Fire")).Accepted, Is.False, "seat 1 picks first");
            game.Do(new DraftPick(1, "Fire"));
            game.Do(new DraftPick(0, "Metal"));
            game.Do(new DraftPick(0, "Death"));
            Assert.That(game.Submit(new DraftPick(1, "Metal")).Accepted, Is.False, "already taken");
            game.Do(new DraftPick(1, "Light"));
            game.Do(new DraftPick(1, "Life"));
            game.Do(new DraftPick(0, "Plague"));

            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Playing));
            Assert.That(game.State.Players[1].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Fire", "Light", "Life" }));
            Assert.That(game.State.Players[0].Protocols.Select(p => p.Name), Is.EqualTo(new[] { "Metal", "Death", "Plague" }));
            Assert.That(game.State.ActiveSeat, Is.EqualTo(1), "the first drafter takes the first turn");
        }

        [Test]
        public void Draft_ViewShowsPicksAndWhoseTurn()
        {
            var pool = new[] { "Darkness", "Death", "Fire", "Gravity", "Life", "Light", "Metal", "Plague" };
            var game = Game.Create(Scenario.Catalog, pool, firstSeat: 0, seed: 3, confirmEndTurn: false);
            game.Do(new DraftPick(0, "Fire"));
            var view = game.ViewFor(1);
            Assert.That(view.Stage, Is.EqualTo(GameStage.Draft));
            Assert.That(view.Draft.LocalPick, Is.True);
            Assert.That(view.Draft.PicksRemaining, Is.EqualTo(2));
            Assert.That(view.Draft.OpponentPicks, Is.EqualTo(new[] { "Fire" }));
            Assert.That(view.Draft.Available, Has.No.Member("Fire"));
        }

        [Test]
        public void Lines_SeatOnesLeftIsFieldLine2()
        {
            var game = Scenario.Load(@"
                p0: Fire Speed Death
                p1: Metal Water Plague
                hand 1: Metal_0
                turn 1 Action");
            game.Play("Metal_0", line: 0);

            Assert.That(game.State.Lines[2].Stack(1).Names(), Is.EqualTo(new[] { "Metal_0" }));
            Assert.That(game.State.ProtocolIn(1, 2).Name, Is.EqualTo("Metal"));
            Assert.That(game.State.ProtocolIn(0, 2).Name, Is.EqualTo("Death"), "opposite seat 1's left is seat 0's right");
            Assert.That(game.ViewFor(1).Lines[0].Local.Cards.Single().CardId, Is.EqualTo("Metal_0"));
            Assert.That(game.ViewFor(0).Lines[2].Opponent.Cards.Single().CardId, Is.EqualTo("Metal_0"));
        }
    }
}
