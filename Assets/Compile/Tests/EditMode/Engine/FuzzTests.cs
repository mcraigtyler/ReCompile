using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Plays many seeded games between random players and checks the rules' invariants after every move.
    public class FuzzTests
    {
        const int MaxIntentsPerGame = 5000;
        const int CardsPerGame = 36;

        [Test]
        public void Fuzz_200RandomGames() => RunGames(200, firstSeed: 1, viewCheckEvery: 1);

        // The Phase 3 done-when check. Takes a few minutes, so it only runs when asked for by name.
        [Test, Explicit("Long run")]
        public void Fuzz_10000RandomGames() => RunGames(10000, firstSeed: 100000, viewCheckEvery: 4);

        static void RunGames(int count, int firstSeed, int viewCheckEvery)
        {
            var timer = Stopwatch.StartNew();
            var stats = new Dictionary<EndReason, int>();
            var turns = 0L;
            for (var i = 0; i < count; i++)
            {
                var game = PlayOne(firstSeed + i, viewCheckEvery);
                stats[game.State.EndReason] = stats.TryGetValue(game.State.EndReason, out var n) ? n + 1 : 1;
                turns += game.State.Turn;
            }
            UnityEngine.Debug.Log($"Fuzz: {count} games in {timer.Elapsed.TotalSeconds:F1}s, {turns / (double)count:F1} turns on average. " +
                                  string.Join(", ", stats.Select(kv => $"{kv.Key}: {kv.Value}")));
        }

        static Game PlayOne(int seed, int viewCheckEvery)
        {
            var random = new Random(seed);
            var protocols = Scenario.Catalog.Protocols.ToList();
            var game = Game.Create(Scenario.Catalog, protocols, firstSeat: random.Next(2), seed: seed);

            for (var step = 0; step < MaxIntentsPerGame; step++)
            {
                if (game.State.Stage == GameStage.Over)
                {
                    CheckEnd(game, seed);
                    return game;
                }

                var intent = Choose(game, random);
                Assert.That(intent, Is.Not.Null, $"seed {seed}: the engine is waiting on nothing ({game.State.Phase})");
                var result = game.Submit(intent);
                Assert.That(result.Accepted, Is.True, $"seed {seed}: {intent.GetType().Name} rejected: {result.Error}");
                CheckInvariants(game, seed, checkViews: step % viewCheckEvery == 0);
            }

            Assert.Fail($"seed {seed}: the game didn't end within {MaxIntentsPerGame} moves");
            return game;
        }

        // A random legal move for whoever the engine is waiting on.
        static Intent Choose(Game game, Random random)
        {
            var state = game.State;
            if (state.Stage == GameStage.Draft)
            {
                var available = state.Draft.Available;
                return new DraftPick(state.Draft.CurrentSeat, available[random.Next(available.Count)]);
            }

            var choice = game.Pending;
            if (choice != null)
            {
                if (choice.Optional && random.Next(3) == 0)
                    return AnswerChoice.Declined(choice.Seat);
                switch (choice.Kind)
                {
                    case ChoiceKind.Cards:
                        var count = random.Next(choice.Min, choice.Max + 1);
                        return AnswerChoice.WithCards(choice.Seat, choice.ValidCards.OrderBy(_ => random.Next()).Take(count));
                    case ChoiceKind.Line:
                        return AnswerChoice.WithLine(choice.Seat, choice.ValidLines[random.Next(choice.ValidLines.Count)]);
                    default:
                        var orders = new[] { new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
                        return new Rearrange(choice.Seat, random.Next(2), orders[random.Next(orders.Length)]);
                }
            }

            if (!game.AwaitingAction)
                return null;

            var seat = state.ActiveSeat;
            if (game.CheckRefresh(seat) == null && random.Next(6) == 0)
                return new Refresh(seat);

            // Prefer face-up plays so games move along; any card can always go face-down.
            var plays = new List<PlayCard>();
            foreach (var card in state.Players[seat].Hand)
                for (var line = 0; line < GameState.LineCount; line++)
                    if (game.CheckPlay(seat, card.Id, line, faceUp: true) == null)
                        plays.Add(new PlayCard(seat, card.Id, line, true));
            if (plays.Count > 0 && random.Next(4) != 0)
                return plays[random.Next(plays.Count)];

            var hand = state.Players[seat].Hand;
            return new PlayCard(seat, hand[random.Next(hand.Count)].Id, random.Next(GameState.LineCount), faceUp: false);
        }

        static void CheckInvariants(Game game, int seed, bool checkViews)
        {
            var state = game.State;
            if (state.Stage != GameStage.Playing && state.Stage != GameStage.Over)
                return;
            string Where(string what) => $"seed {seed}, turn {state.Turn}: {what}";

            var all = state.AllCards().ToList();
            Assert.That(all, Has.Count.EqualTo(CardsPerGame), Where("cards were created or lost"));
            Assert.That(all.Select(c => c.Id).Distinct().Count(), Is.EqualTo(CardsPerGame), Where("duplicate card ids"));

            foreach (var player in state.Players)
            {
                Assert.That(player.Trash.All(c => c.FaceUp && c.Owner == player.Seat), Is.True, Where("trash cards are face-up in their owner's trash"));
                Assert.That(player.Deck.All(c => !c.FaceUp && c.Owner == player.Seat), Is.True, Where("deck cards belong to their owner"));
                Assert.That(player.Hand.All(c => !c.FaceUp && c.Owner == player.Seat), Is.True, Where("hand cards belong to the player"));
                Assert.That(player.Protocols.Select(p => p.Name).Distinct().Count(), Is.EqualTo(3), Where("3 distinct Protocols"));
            }

            Assert.That(state.Control, Is.InRange(GameState.Neutral, 1), Where("Control"));
            var waiting = state.Players[GameState.Other(state.ActiveSeat)];
            Assert.That(waiting.Hand.Count, Is.LessThanOrEqualTo(Game.HandSize), Where("the waiting player's hand is at most 5"));
            if (game.AwaitingAction)
                Assert.That(state.Players[state.ActiveSeat].Hand.Count, Is.InRange(1, Game.HandSize), Where("hand at the Action step"));

            if (checkViews)
                for (var viewer = 0; viewer < 2; viewer++)
                    CheckView(game, viewer, Where);
        }

        // Nothing hidden from a viewer may appear in their view.
        static void CheckView(Game game, int viewer, Func<string, string> where)
        {
            var state = game.State;
            var view = game.ViewFor(viewer);
            var opponent = GameState.Other(viewer);

            Assert.That(view.Hand.Select(c => c.InstanceId), Is.EquivalentTo(state.Players[viewer].Hand.Select(c => c.Id)), where("own hand"));
            var hiddenIds = new HashSet<int>(state.Players[opponent].Hand.Concat(state.Players[opponent].Deck)
                .Concat(state.Players[viewer].Deck).Select(c => c.Id));
            foreach (var line in view.Lines)
                foreach (var card in line.Local.Cards.Concat(line.Opponent.Cards))
                {
                    Assert.That(hiddenIds.Contains(card.InstanceId), Is.False, where("a hidden card's id is visible"));
                    if (!card.FaceUp && !card.LocalOwner)
                        Assert.That(card.CardId, Is.Null, where("an opponent's face-down card is identified"));
                }
            if (view.Choice != null)
                Assert.That(game.Pending.Seat, Is.EqualTo(viewer), where("a choice shown to the wrong player"));
        }

        static void CheckEnd(Game game, int seed)
        {
            var state = game.State;
            if (state.EndReason == EndReason.Compiled)
                Assert.That(state.Players[state.Winner].CompiledCount, Is.EqualTo(3), $"seed {seed}: winner compiled all 3");
            else
                Assert.That(state.EndReason, Is.EqualTo(EndReason.Stalemate), $"seed {seed}: unexpected end");
        }
    }
}
