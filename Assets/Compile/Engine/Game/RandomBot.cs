using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // Picks a random legal move for whoever the engine is waiting on. Plays the simulated opponent in
    // offline games and drives the fuzz tests. It prefers face-up plays so games move along.
    public static class RandomBot
    {
        static readonly int[][] Orders =
        {
            new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
        };

        static readonly int[][] Swaps = { new[] { 1, 0, 2 }, new[] { 2, 1, 0 }, new[] { 0, 2, 1 } };

        // The seat the engine is waiting on, or -1 if it's waiting on nobody (the game is over).
        public static int SeatToMove(Game game)
        {
            var state = game.State;
            switch (state.Stage)
            {
                case GameStage.Draft:
                    return game.DraftSeat;
                case GameStage.Playing:
                    return game.Pending?.Seat ?? state.ActiveSeat;
                default:
                    return -1;
            }
        }

        // debugEffectChance: how often to set off a random debug effect when free to act (the fuzzer uses it).
        public static Intent Choose(Game game, Random random, double debugEffectChance = 0)
        {
            var state = game.State;
            if (state.Stage == GameStage.Draft)
            {
                if (game.DraftConfirmSeat >= 0)
                    return new EndTurn(game.DraftConfirmSeat);
                var available = state.Draft.Available;
                return new DraftPick(state.Draft.CurrentSeat, available[random.Next(available.Count)]);
            }
            if (state.Stage != GameStage.Playing)
                return null;

            var choice = game.Pending;
            if (choice != null)
            {
                if (choice.Optional && random.Next(3) == 0)
                    return AnswerChoice.Declined(choice.Seat);
                return Answer(game, choice, random);
            }

            var seat = state.ActiveSeat;
            if ((game.AwaitingAction || game.AwaitingEndTurn) && random.NextDouble() < debugEffectChance)
                return new DebugEffect(seat, DebugEffects.All[random.Next(DebugEffects.All.Count)].Key);
            if (game.AwaitingEndTurn)
                return new EndTurn(seat);
            if (!game.AwaitingAction)
                return null;

            if (game.CheckRefresh(seat) == null && random.Next(6) == 0)
                return new Refresh(seat);

            var plays = new List<PlayCard>();
            foreach (var card in state.Players[seat].Hand)
                for (var line = 0; line < GameState.LineCount; line++)
                    if (game.CheckPlay(seat, card.Id, line, faceUp: true) == null)
                        plays.Add(new PlayCard(seat, card.Id, line, true));
            if (plays.Count > 0 && random.Next(4) != 0)
                return plays[random.Next(plays.Count)];

            var faceDown = new List<PlayCard>();
            foreach (var card in state.Players[seat].Hand)
                for (var line = 0; line < GameState.LineCount; line++)
                    if (game.CheckPlay(seat, card.Id, line, faceUp: false) == null)
                        faceDown.Add(new PlayCard(seat, card.Id, line, false));
            if (faceDown.Count > 0)
                return faceDown[random.Next(faceDown.Count)];
            return plays.Count > 0 ? plays[random.Next(plays.Count)] : (Intent)new Refresh(seat);
        }

        static Intent Answer(Game game, PendingChoice choice, Random random)
        {
            var seat = choice.Seat;
            int TargetSeat() => choice.TargetSeat >= 0 ? choice.TargetSeat : random.Next(2);
            switch (choice.Kind)
            {
                case ChoiceKind.FieldCards:
                case ChoiceKind.HandCards:
                    var count = random.Next(choice.Min, choice.Max + 1);
                    return AnswerChoice.WithCards(seat, choice.ValidCards.OrderBy(_ => random.Next()).Take(count));
                case ChoiceKind.Line:
                    return AnswerChoice.WithLine(seat, choice.ValidLines[random.Next(choice.ValidLines.Count)]);
                case ChoiceKind.Option:
                    return AnswerChoice.WithOption(seat, random.Next(choice.Options.Count));
                case ChoiceKind.Order:
                    return AnswerChoice.WithOrder(seat, Enumerable.Range(0, choice.Options.Count).OrderBy(_ => random.Next()));
                case ChoiceKind.Swap:
                    return new Rearrange(seat, TargetSeat(), Swaps[random.Next(Swaps.Length)]);
                case ChoiceKind.Play:
                    var plays = new List<PlayCard>();
                    foreach (var card in choice.ValidCards)
                        foreach (var line in choice.ValidLines)
                            foreach (var faceUp in new[] { true, false })
                            {
                                var play = new PlayCard(seat, card, line, faceUp);
                                if (game.CheckChoicePlay(play) == null)
                                    plays.Add(play);
                            }
                    return plays[random.Next(plays.Count)];
                default:
                    return new Rearrange(seat, TargetSeat(), Orders[random.Next(Orders.Length)]);
            }
        }
    }
}
