using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Compile.Engine.Views;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Plays many seeded games between random players and checks the rules' invariants after every move.
    // The effects runs give every card random text made of debug effects and set off random debug effects
    // too, so middle text, triggers, interrupts, committed cards, reveals, and every kind of question get played.
    public class FuzzTests
    {
        const int MaxIntentsPerGame = 5000;
        const int CardsPerGame = 36;
        const double DebugEffectChance = 0.05;

        // What text the cards have: none, random text made of debug effects (with debug effects set off as
        // well), or their real Main Set 1 text.
        public enum CardText
        {
            None,
            Random,
            MainSet1,
        }

        [Test]
        public void Fuzz_200RandomGames() => RunGames(200, firstSeed: 1, viewCheckEvery: 1, undoChance: 0.1, CardText.None);

        [Test]
        public void Fuzz_200RandomGamesWithEffects() => RunGames(200, firstSeed: 1, viewCheckEvery: 1, undoChance: 0.1, CardText.Random);

        [Test]
        public void Fuzz_200RandomGamesWithMainSet1() => RunGames(200, firstSeed: 1, viewCheckEvery: 1, undoChance: 0.1, CardText.MainSet1);

        // The done-when checks for Phases 3, 4, and 5. They take several minutes, so they only run when asked for by name.
        [Test, Explicit("Long run"), Timeout(30 * 60 * 1000)]
        public void Fuzz_10000RandomGames() => RunGames(10000, firstSeed: 100000, viewCheckEvery: 4, undoChance: 0.01, CardText.None);

        [Test, Explicit("Long run"), Timeout(30 * 60 * 1000)]
        public void Fuzz_10000RandomGamesWithEffects() => RunGames(10000, firstSeed: 200000, viewCheckEvery: 4, undoChance: 0.02, CardText.Random);

        [Test, Explicit("Long run"), Timeout(30 * 60 * 1000)]
        public void Fuzz_10000RandomGamesWithMainSet1() => RunGames(10000, firstSeed: 300000, viewCheckEvery: 4, undoChance: 0.02, CardText.MainSet1);

        static void RunGames(int count, int firstSeed, int viewCheckEvery, double undoChance, CardText text)
        {
            var timer = Stopwatch.StartNew();
            var stats = new Dictionary<EndReason, int>();
            var seen = new Dictionary<string, int>();      // events and questions, to show what the run covered
            var turns = 0L;
            for (var i = 0; i < count; i++)
            {
                var game = PlayOne(firstSeed + i, viewCheckEvery, undoChance, text, seen);
                stats[game.State.EndReason] = stats.TryGetValue(game.State.EndReason, out var n) ? n + 1 : 1;
                turns += game.State.Turn;
            }
            UnityEngine.Debug.Log($"Fuzz: {count} games in {timer.Elapsed.TotalSeconds:F1}s, {turns / (double)count:F1} turns on average. " +
                                  string.Join(", ", stats.Select(kv => $"{kv.Key}: {kv.Value}")) + ". Seen: " +
                                  string.Join(", ", seen.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        }

        static Game PlayOne(int seed, int viewCheckEvery, double undoChance, CardText text, Dictionary<string, int> seen)
        {
            var random = new Random(seed);
            var protocols = Scenario.Catalog.Protocols.ToList();
            var game = Game.Create(Scenario.Catalog, protocols, firstSeat: random.Next(2), seed: seed,
                effects: text == CardText.Random ? RandomText(seed) : text == CardText.MainSet1 ? Compile.Engine.Cards.MainSet1.Effects : null);
            var effects = text != CardText.None;

            for (var step = 0; step < MaxIntentsPerGame; step++)
            {
                if (game.State.Stage == GameStage.Over)
                {
                    CheckEnd(game, seed);
                    return game;
                }

                var intent = RandomBot.Choose(game, random, text == CardText.Random ? DebugEffectChance : 0);
                if (game.Pending != null)
                    Count(seen, "?" + game.Pending.Kind);
                Assert.That(intent, Is.Not.Null, $"seed {seed}: the engine is waiting on nothing ({game.State.Phase})");

                // Now and then, make the move, undo it, and check the game is exactly as it was.
                var tryUndo = game.State.Stage != GameStage.Over && random.NextDouble() < undoChance;
                var before = tryUndo ? Fingerprint(game) : null;

                IntentResult result;
                try
                {
                    result = game.Submit(intent);
                }
                catch (InvalidOperationException e)
                {
                    var recent = game.History.Skip(Math.Max(0, game.History.Count - 40))
                        .Select(h => h.GetType().Name + ": " + (h.Describe(game.State, 0) ?? ""));
                    Assert.Fail($"seed {seed}: {intent.GetType().Name} threw: {e.Message}\nLast events:\n{string.Join("\n", recent)}");
                    throw;
                }
                foreach (var e in result.Events)
                    Count(seen, e.GetType().Name);
                Assert.That(result.Accepted, Is.True, $"seed {seed}: {intent.GetType().Name} rejected: {result.Error}");
                CheckInvariants(game, seed, checkViews: step % viewCheckEvery == 0, effects);

                if (tryUndo && game.CheckUndo(intent.Seat) == null && !(intent is EndTurn))
                {
                    Assert.That(game.Submit(new Undo(intent.Seat)).Accepted, Is.True, $"seed {seed}: undo rejected");
                    Assert.That(Fingerprint(game), Is.EqualTo(before), $"seed {seed}: undoing {intent.GetType().Name} didn't restore the game");
                }
            }

            Assert.Fail($"seed {seed}: the game didn't end within {MaxIntentsPerGame} moves");
            return game;
        }

        static void Count(Dictionary<string, int> seen, string key) => seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;

        // Random text for every card, made of debug effects. Text that reacts to draws, discards, and the
        // like only uses effects that can't set off the same trigger again by themselves, so it can't loop.
        static CardEffects RandomText(int seed)
        {
            var random = new Random(seed * 31 + 7);
            var keys = DebugEffects.All.Select(d => d.Key).ToList();
            var quiet = new[] { "flip-1", "may-flip-face-down", "shift-1", "return-own", "swap-own" };
            Func<Effect, IEnumerable<Op>> Any() => DebugEffects.Find(keys[random.Next(keys.Count)]);
            Func<Effect, IEnumerable<Op>> Quiet() => DebugEffects.Find(quiet[random.Next(quiet.Length)]);
            var after = new[] { Trigger.AfterDraw, Trigger.AfterDiscard, Trigger.AfterDelete, Trigger.AfterClearCache };

            var effects = new CardEffects();
            foreach (var protocol in Scenario.Catalog.Protocols)
                foreach (var spec in Scenario.Catalog.CardsFor(protocol))
                {
                    var card = new TestCard();
                    switch (random.Next(7))
                    {
                        case 0:
                            continue;
                        case 1:
                        case 2:
                            card.MiddleText = Any();
                            break;
                        case 3:
                            card.On(random.Next(2) == 0 ? Trigger.Start : Trigger.End, random.Next(2) == 0 ? Slot.Top : Slot.Bottom, Any());
                            break;
                        case 4:
                            card.On(Trigger.WouldBeCovered, Slot.Bottom, Quiet());
                            break;
                        case 5:
                            var text = Quiet();
                            card.On(after[random.Next(after.Length)], Slot.Top, (e, seat) => seat == e.Owner ? text(e) : null);
                            break;
                        default:
                            card.MiddleText = Any();
                            card.On(random.Next(2) == 0 ? Trigger.WouldBeFlipped : Trigger.WouldBeDeletedByCompile, Slot.Top, Quiet());
                            break;
                    }
                    effects.Add(spec.Id, card);
                }
            return effects;
        }

        // Everything about a game state that a player or the rules could notice.
        static string Fingerprint(Game game)
        {
            var state = game.State;
            string Cards(IEnumerable<CardInstance> cards) => string.Join(",", cards.Select(c => $"{c.Id}:{c.Spec.Id}:{c.Owner}:{(c.FaceUp ? 1 : 0)}:{c.MarkedTurn}"));
            var pending = game.Pending;
            var parts = new List<string>
            {
                $"{state.Stage} t{state.Turn} a{state.ActiveSeat} {state.Phase} c{state.Control} w{state.Winner}",
                $"pending {pending?.Kind}:{pending?.Seat}:{pending?.Prompt}:{string.Join(",", pending?.ValidCards ?? new int[0])}:" +
                $"{string.Join(",", pending?.ValidLines ?? new int[0])}:{string.Join(",", pending?.Options ?? new string[0])}",
                $"committed {Cards(state.CommittedCards)} revealed {Cards(state.Revealed.OrderBy(c => c.Id))}",
                $"await {game.AwaitingAction} {game.AwaitingEndTurn} draft {game.DraftSeat}/{game.DraftConfirmSeat}",
            };
            if (state.Draft != null)
                parts.Add($"draft {string.Join(",", state.Draft.Available)} | {string.Join(",", state.Draft.PicksFor(0))} | {string.Join(",", state.Draft.PicksFor(1))}");
            foreach (var player in state.Players)
            {
                parts.Add(string.Join(",", player.Protocols.Select(p => p == null ? "-" : p.Name + (p.Compiled ? "*" : ""))));
                parts.Add("hand " + Cards(player.Hand));
                parts.Add("deck " + Cards(player.Deck));
                parts.Add("trash " + Cards(player.Trash));
            }
            foreach (var line in state.Lines)
                parts.Add($"line {Cards(line.Stack(0))} | {Cards(line.Stack(1))}");
            return string.Join("\n", parts);
        }

        static void CheckInvariants(Game game, int seed, bool checkViews, bool effects)
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

            for (var line = 0; line < GameState.LineCount; line++)
                for (var seat = 0; seat < GameState.SeatCount; seat++)
                    Assert.That(state.Lines[line].Stack(seat).All(c => c.Owner == seat), Is.True, Where("field cards are on their owner's side"));

            Assert.That(state.Control, Is.InRange(GameState.Neutral, 1), Where("Control"));
            if (game.Pending == null)
            {
                Assert.That(state.CommittedCards, Is.Empty, Where("committed cards have landed"));
                Assert.That(state.Revealed, Is.Empty, Where("reveals end with their text"));
            }

            // Card text can give the waiting player cards, or draw before the Action step.
            var waiting = state.Players[GameState.Other(state.ActiveSeat)];
            if (!effects)
                Assert.That(waiting.Hand.Count, Is.LessThanOrEqualTo(Game.HandSize), Where("the waiting player's hand is at most 5"));
            if (game.AwaitingAction)
                Assert.That(state.Players[state.ActiveSeat].Hand.Count, Is.InRange(1, effects ? 100 : Game.HandSize), Where("hand at the Action step"));

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
            var hiddenIds = new HashSet<int>(state.Players[opponent].Hand.Where(c => !state.Revealed.Contains(c)).Concat(state.Players[opponent].Deck)
                .Concat(state.Players[viewer].Deck).Select(c => c.Id));
            foreach (var line in view.Lines)
                foreach (var card in line.Local.Cards.Concat(line.Opponent.Cards))
                {
                    Assert.That(hiddenIds.Contains(card.InstanceId), Is.False, where("a hidden card's id is visible"));
                    if (!card.FaceUp && !card.LocalOwner && !card.Revealed)
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
