using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Compile.Engine.Cards;
using Compile.Engine.Views;

namespace Compile.Engine.Tests
{
    // Builds a game in a given state from a compact text description, one fact per line:
    //
    //   p0 Alice: Fire Speed* Death       Protocols left to right from that seat; * = Compiled
    //   p1 Bob: Metal Water Plague
    //   hand 0: Fire_1 Speed_3
    //   deck 0: Fire_2 Fire_3             top card first
    //   trash 1: Metal_0
    //   line 0: Fire_4 ~Speed_2 | Metal_3 field line (numbered from seat 0's left): seat 0's stack | seat 1's,
    //                                     bottom to top; ~ = face-down; @1 = owned by seat 1
    //   turn 0 Action                     active seat and the phase to resume from (default: 0 Action)
    //   control 1                         0, 1, or neutral (default)
    //   seed 7
    //
    // Lines starting with # are comments.
    public static class Scenario
    {
        static CardCatalog _catalog;

        public static CardCatalog Catalog => _catalog ?? (_catalog = CardDatabase.Parse(
            File.ReadAllText("Assets/Compile/Data/cards.json"), File.ReadAllText("Assets/Compile/Data/protocols.json")).ToCatalog());

        // confirmEndTurn: the game waits for EndTurn after each turn (as real games do). Rule tests leave it
        // off so turns flow straight on.
        // effects: card text for this game (tests give cards made-up text to exercise the framework).
        public static Game Load(string text, bool confirmEndTurn = false, CardEffects effects = null) =>
            Game.Resume(Catalog, () => Build(text), confirmEndTurn, effects);

        static GameState Build(string text)
        {
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

            var seed = 1L;
            var seedLine = lines.FirstOrDefault(l => l.StartsWith("seed "));
            if (seedLine != null)
                seed = long.Parse(seedLine.Substring(5).Trim());

            var names = new[] { "Player 1", "Player 2" };
            var protocols = new string[2][];
            foreach (var line in lines.Where(l => l.StartsWith("p0") || l.StartsWith("p1")))
            {
                var seat = line[1] - '0';
                var colon = line.IndexOf(':');
                var name = line.Substring(2, colon - 2).Trim();
                if (name.Length > 0)
                    names[seat] = name;
                protocols[seat] = Words(line.Substring(colon + 1));
            }
            if (protocols[0] == null || protocols[1] == null)
                throw new FormatException("A scenario needs p0 and p1 Protocol lines.");

            var state = new GameState(seed, names[0], names[1]) { Turn = 1, Phase = TurnPhase.Action };
            var nextId = 1000;
            CardInstance Card(string token, int defaultOwner, bool inField)
            {
                var faceUp = true;
                if (token.StartsWith("~"))
                {
                    faceUp = false;
                    token = token.Substring(1);
                }
                var owner = defaultOwner;
                var at = token.IndexOf('@');
                if (at >= 0)
                {
                    owner = int.Parse(token.Substring(at + 1));
                    token = token.Substring(0, at);
                }
                var id = nextId++;
                state.UsedIds.Add(id);
                return new CardInstance(id, Catalog.Get(token), owner) { FaceUp = inField && faceUp };
            }

            for (var seat = 0; seat < 2; seat++)
            {
                if (protocols[seat].Length != 3)
                    throw new FormatException($"p{seat} needs 3 Protocols.");
                for (var i = 0; i < 3; i++)
                {
                    var name = protocols[seat][i].TrimEnd('*');
                    state.Players[seat].ProtocolSlots[i] = new ProtocolSlot(name, protocols[seat][i].EndsWith("*"));
                }
            }

            foreach (var line in lines)
            {
                var colon = line.IndexOf(':');
                var head = colon >= 0 ? line.Substring(0, colon).Trim() : line;
                var body = colon >= 0 ? line.Substring(colon + 1) : "";
                var parts = head.Split(' ');

                switch (parts[0])
                {
                    case "hand":
                    case "deck":
                    case "trash":
                    {
                        var seat = int.Parse(parts[1]);
                        var player = state.Players[seat];
                        var list = parts[0] == "hand" ? player.HandCards : parts[0] == "deck" ? player.DeckCards : player.TrashCards;
                        foreach (var token in Words(body))
                        {
                            var card = Card(token, seat, inField: false);
                            card.FaceUp = parts[0] == "trash";
                            list.Add(card);
                        }
                        break;
                    }
                    case "line":
                    {
                        var fieldLine = int.Parse(parts[1]);
                        var stacks = body.Split('|');
                        for (var seat = 0; seat < stacks.Length && seat < 2; seat++)
                            foreach (var token in Words(stacks[seat]))
                                state.Lines[fieldLine].StackCards[seat].Add(Card(token, seat, inField: true));
                        break;
                    }
                    case "turn":
                        state.ActiveSeat = int.Parse(parts[1]);
                        if (parts.Length > 2)
                            state.Phase = (TurnPhase)Enum.Parse(typeof(TurnPhase), parts[2], ignoreCase: true);
                        break;
                    case "control":
                        state.Control = parts[1] == "neutral" ? GameState.Neutral : int.Parse(parts[1]);
                        break;
                }
            }

            return state;
        }

        static string[] Words(string text) => text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        // ---- Helpers for tests ----

        // The id of the first card with this card id (e.g. "Fire_4") anywhere in the game.
        public static int IdOf(this Game game, string cardId) => game.Find(cardId).Id;

        public static CardInstance Find(this Game game, string cardId) =>
            game.State.AllCards().FirstOrDefault(c => c.Spec.Id == cardId) ?? throw new ArgumentException($"No {cardId} in the game.");

        public static IReadOnlyList<string> Names(this IEnumerable<CardInstance> cards) => cards.Select(c => c.Spec.Id).ToList();

        // Submits an intent that the test expects to be accepted.
        public static IntentResult Do(this Game game, Intent intent)
        {
            var result = game.Submit(intent);
            if (!result.Accepted)
                throw new InvalidOperationException($"Intent {intent.GetType().Name} was rejected: {result.Error}");
            return result;
        }

        public static IntentResult Play(this Game game, string cardId, int line, bool faceUp = true) =>
            game.Do(new PlayCard(game.State.ActiveSeat, game.IdOf(cardId), line, faceUp));

        // Sets off a debug effect (DebugEffects) for the active player.
        public static IntentResult Debug(this Game game, string key) => game.Do(new DebugEffect(game.State.ActiveSeat, key));

        // Answers the pending choice with these cards (by card id), for whoever is choosing.
        public static IntentResult Pick(this Game game, params string[] cardIds) =>
            game.Do(AnswerChoice.WithCards(game.Pending.Seat, cardIds.Select(game.IdOf)));

        // The card ids in one player's stack in a field line, bottom to top.
        public static IReadOnlyList<string> StackOf(this Game game, int fieldLine, int seat) => game.State.Lines[fieldLine].Stack(seat).Names();
    }
}
