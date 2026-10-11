using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Cards;
using Compile.Engine.Views;

namespace Compile.Client.Board
{
    // Stand-in for the rules engine until Phase 3: just enough state to deal, play, draw, flip, and
    // compile so the board has something real to show. It checks where a card may be played face-up
    // but doesn't enforce turn structure or card text. Replaced by the engine in Phase 4.
    public sealed class BoardSandbox
    {
        public const int HandSize = 5;
        const int Local = 0, Opponent = 1;

        sealed class Card
        {
            public int Id;
            public CardDefinition Definition;
            public bool FaceUp;
            public int Owner;
        }

        readonly CardDatabase _cards;
        readonly Random _random;
        readonly string[] _names;
        readonly string[][] _protocols = new string[2][];   // each player's own left → right
        readonly bool[][] _compiled = { new bool[3], new bool[3] };
        readonly List<Card>[] _deck = { new List<Card>(), new List<Card>() };
        readonly List<Card>[] _hand = { new List<Card>(), new List<Card>() };
        readonly List<Card>[] _trash = { new List<Card>(), new List<Card>() };
        readonly List<Card>[,] _stacks = new List<Card>[2, 3];  // [seat, line from the local player's left]
        readonly List<LogEntry> _log = new List<LogEntry>();
        int _nextId = 1;
        Action<int> _onChoice;

        public int Turn { get; private set; } = 1;
        public bool LocalTurn { get; private set; } = true;
        public TurnPhase Phase { get; private set; } = TurnPhase.Action;
        public ControlHolder Control { get; private set; }
        public ChoiceInfo Choice { get; private set; }

        public event Action Changed;

        public BoardSandbox(CardDatabase cards, IReadOnlyList<string> localProtocols, IReadOnlyList<string> opponentProtocols,
            string localName, string opponentName, int seed)
        {
            _cards = cards;
            _random = new Random(seed);
            _names = new[] { localName, opponentName };
            _protocols[Local] = localProtocols.ToArray();
            _protocols[Opponent] = opponentProtocols.ToArray();
            for (var seat = 0; seat < 2; seat++)
            for (var line = 0; line < 3; line++)
                _stacks[seat, line] = new List<Card>();

            for (var seat = 0; seat < 2; seat++)
            {
                foreach (var protocol in _protocols[seat])
                foreach (var definition in cards.CardsFor(protocol))
                    _deck[seat].Add(new Card { Id = _nextId++, Definition = definition, Owner = seat });
                Shuffle(_deck[seat]);
                Draw(seat, HandSize);
            }

            Log(Local, "The game begins. Both players draw 5 cards.");
        }

        // ---------- Queries ----------

        public string ProtocolInLine(int line, bool local) => local ? _protocols[Local][line] : _protocols[Opponent][2 - line];

        public string LineName(int line) => $"{ProtocolInLine(line, true)} vs {ProtocolInLine(line, false)}";

        public bool CanPlayFaceUp(int handCardId, int line)
        {
            var card = _hand[Local].FirstOrDefault(c => c.Id == handCardId);
            return card != null && (ProtocolInLine(line, true) == card.Definition.Protocol || ProtocolInLine(line, false) == card.Definition.Protocol);
        }

        public bool CanPlayFaceUpAnywhere(int handCardId) => Enumerable.Range(0, 3).Any(line => CanPlayFaceUp(handCardId, line));

        public bool CanRefresh => _hand[Local].Count < HandSize;

        // ---------- Local actions ----------

        public bool Play(int handCardId, int line, bool faceUp, out string error)
        {
            var card = _hand[Local].FirstOrDefault(c => c.Id == handCardId);
            if (card == null)
            {
                error = "That card isn't in your hand.";
                return false;
            }
            if (faceUp && !CanPlayFaceUp(handCardId, line))
            {
                error = $"{Name(card)} can only be played face-up into a {card.Definition.Protocol} line. Play it face-down instead.";
                return false;
            }

            _hand[Local].Remove(card);
            card.FaceUp = faceUp;
            _stacks[Local, line].Add(card);
            Log(Local, faceUp
                ? $"You played {Name(card)} face-up into {LineName(line)}."
                : $"You played a card face-down into {LineName(line)}.");
            error = null;
            Notify();
            return true;
        }

        public void Refresh()
        {
            if (!CanRefresh)
                return;
            var drawn = HandSize - _hand[Local].Count;
            Draw(Local, drawn);
            Log(Local, $"You refreshed, drawing {drawn} card{(drawn == 1 ? "" : "s")}.");
            Notify();
        }

        public void Discard(int handCardId)
        {
            var card = _hand[Local].FirstOrDefault(c => c.Id == handCardId);
            if (card == null)
                return;
            _hand[Local].Remove(card);
            card.FaceUp = true;
            _trash[Local].Add(card);
            Log(Local, $"You discarded {Name(card)}.");
            Notify();
        }

        // ---------- Turn flow (simulated) ----------

        public void SetPhase(TurnPhase phase, bool localTurn)
        {
            if (localTurn != LocalTurn && localTurn)
                Turn++;
            Phase = phase;
            LocalTurn = localTurn;
            Notify();
        }

        // The simulated opponent plays one card: face-up when it can, otherwise face-down into a random line.
        public void OpponentPlays()
        {
            if (_hand[Opponent].Count == 0)
            {
                Draw(Opponent, HandSize);
                Log(Opponent, $"{_names[Opponent]} refreshed.");
                Notify();
                return;
            }

            var card = _hand[Opponent][_random.Next(_hand[Opponent].Count)];
            var faceUpLines = Enumerable.Range(0, 3)
                .Where(l => ProtocolInLine(l, false) == card.Definition.Protocol || ProtocolInLine(l, true) == card.Definition.Protocol)
                .ToList();
            var faceUp = faceUpLines.Count > 0 && _random.NextDouble() < 0.7;
            var line = faceUp ? faceUpLines[_random.Next(faceUpLines.Count)] : _random.Next(3);

            _hand[Opponent].Remove(card);
            card.FaceUp = faceUp;
            _stacks[Opponent, line].Add(card);
            Log(Opponent, faceUp
                ? $"{_names[Opponent]} played {Name(card)} face-up into {LineName(line)}."
                : $"{_names[Opponent]} played a card face-down into {LineName(line)}.");
            Notify();
        }

        // ---------- Debug operations ----------

        // Plays random cards from each deck into every stack (1-3 per stack).
        public void DealSampleBoard()
        {
            for (var seat = 0; seat < 2; seat++)
            for (var line = 0; line < 3; line++)
            {
                var count = 1 + _random.Next(3);
                for (var i = 0; i < count; i++)
                    PutFromDeck(seat, line, faceUp: _random.NextDouble() < 0.65);
            }
            Log(Local, "Debug: dealt a sample board.");
            Notify();
        }

        public void FillStack(int line, bool local, int count)
        {
            var seat = local ? Local : Opponent;
            while (_stacks[seat, line].Count < count && PutFromDeck(seat, line, faceUp: _random.NextDouble() < 0.6))
            {
            }
            Log(Local, $"Debug: filled {(local ? "your" : "the opponent's")} stack in {LineName(line)}.");
            Notify();
        }

        public void CycleControl()
        {
            Control = (ControlHolder)(((int)Control + 1) % 3);
            Log(Local, Control switch
            {
                ControlHolder.Local => "You took the Control component.",
                ControlHolder.Opponent => $"{_names[Opponent]} took the Control component.",
                _ => "The Control component returned to neutral.",
            });
            Notify();
        }

        // Deletes every card in the line and flips the local player's Protocol there to Compiled.
        public void Compile(int line)
        {
            for (var seat = 0; seat < 2; seat++)
            {
                foreach (var card in _stacks[seat, line])
                {
                    card.FaceUp = true;
                    _trash[card.Owner].Add(card);
                }
                _stacks[seat, line].Clear();
            }
            _compiled[Local][line] = true;
            Log(Local, $"You compiled {LineName(line)}.");
            Notify();
        }

        public void SetAllCompiled(bool local, bool compiled)
        {
            var seat = local ? Local : Opponent;
            for (var i = 0; i < 3; i++)
                _compiled[seat][i] = compiled;
            Notify();
        }

        // Swaps the local player's Protocols into a new order. The cards in each line stay where they are.
        public bool Rearrange(IReadOnlyList<int> order)
        {
            if (order.Count != 3 || order.Distinct().Count() != 3 || order.SequenceEqual(new[] { 0, 1, 2 }))
                return false;

            var protocols = order.Select(i => _protocols[Local][i]).ToArray();
            var compiled = order.Select(i => _compiled[Local][i]).ToArray();
            _protocols[Local] = protocols;
            _compiled[Local] = compiled;
            Log(Local, $"You rearranged your Protocols: {string.Join(", ", protocols)}.");
            Notify();
            return true;
        }

        // Asks the local player to pick one uncovered card on either side; the callback gets its id, or -1 if skipped.
        public void RequestChoice(string prompt, bool optional, Action<int> onChosen)
        {
            var valid = new List<int>();
            for (var seat = 0; seat < 2; seat++)
            for (var line = 0; line < 3; line++)
                if (_stacks[seat, line].Count > 0)
                    valid.Add(_stacks[seat, line][_stacks[seat, line].Count - 1].Id);

            Choice = new ChoiceInfo { Prompt = prompt, ValidCards = valid, Optional = optional };
            _onChoice = onChosen;
            Notify();
        }

        public void AnswerChoice(int cardId)
        {
            if (Choice == null || (cardId < 0 && !Choice.Optional) || (cardId >= 0 && !Choice.ValidCards.Contains(cardId)))
                return;
            var callback = _onChoice;
            Choice = null;
            _onChoice = null;
            callback?.Invoke(cardId);
            Notify();
        }

        public void Flip(int cardId)
        {
            var card = AllStackCards().FirstOrDefault(c => c.Id == cardId);
            if (card == null)
                return;
            card.FaceUp = !card.FaceUp;
            Log(Local, card.FaceUp ? $"{Name(card)} was flipped face-up." : $"{Name(card)} was flipped face-down.");
            Notify();
        }

        // ---------- View ----------

        public PlayerView View()
        {
            return new PlayerView
            {
                Local = Side(Local),
                Opponent = Side(Opponent),
                Hand = _hand[Local].Select(c => Info(c, visible: true)).ToList(),
                Lines = Enumerable.Range(0, 3).Select(line => new LineInfo
                {
                    Index = line,
                    LocalProtocol = new ProtocolInfo { Name = _protocols[Local][line], Compiled = _compiled[Local][line] },
                    OpponentProtocol = new ProtocolInfo { Name = _protocols[Opponent][2 - line], Compiled = _compiled[Opponent][2 - line] },
                    Local = new StackInfo { Cards = _stacks[Local, line].Select(c => Info(c, visible: true)).ToList() },
                    Opponent = new StackInfo { Cards = _stacks[Opponent, line].Select(c => Info(c, visible: c.FaceUp)).ToList() },
                }).ToList(),
                Turn = Turn,
                LocalTurn = LocalTurn,
                Phase = Phase,
                Control = Control,
                Choice = Choice,
                Log = _log.ToList(),
            };
        }

        SideInfo Side(int seat) => new SideInfo
        {
            Name = _names[seat],
            HandCount = _hand[seat].Count,
            DeckCount = _deck[seat].Count,
            Trash = _trash[seat].Select(c => Info(c, visible: true)).ToList(),
        };

        static CardInfo Info(Card card, bool visible) => new CardInfo
        {
            InstanceId = card.Id,
            CardId = visible ? card.Definition.Id : null,
            Protocol = visible ? card.Definition.Protocol : null,
            PrintedValue = visible ? card.Definition.Value : 0,
            FaceUp = card.FaceUp,
            LocalOwner = card.Owner == Local,
        };

        // ---------- Helpers ----------

        IEnumerable<Card> AllStackCards()
        {
            for (var seat = 0; seat < 2; seat++)
            for (var line = 0; line < 3; line++)
                foreach (var card in _stacks[seat, line])
                    yield return card;
        }

        bool PutFromDeck(int seat, int line, bool faceUp)
        {
            if (!EnsureDeck(seat))
                return false;
            var card = _deck[seat][_deck[seat].Count - 1];
            _deck[seat].RemoveAt(_deck[seat].Count - 1);
            card.FaceUp = faceUp;
            _stacks[seat, line].Add(card);
            return true;
        }

        void Draw(int seat, int count)
        {
            for (var i = 0; i < count && EnsureDeck(seat); i++)
            {
                var card = _deck[seat][_deck[seat].Count - 1];
                _deck[seat].RemoveAt(_deck[seat].Count - 1);
                _hand[seat].Add(card);
            }
        }

        // Shuffles the trash into a new deck when the deck runs out.
        bool EnsureDeck(int seat)
        {
            if (_deck[seat].Count > 0)
                return true;
            if (_trash[seat].Count == 0)
                return false;
            _deck[seat].AddRange(_trash[seat]);
            _trash[seat].Clear();
            Shuffle(_deck[seat]);
            Log(seat, $"{(seat == Local ? "You" : _names[seat])} shuffled the trash into a new deck.");
            return true;
        }

        void Shuffle(List<Card> cards)
        {
            for (var i = cards.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }

        static string Name(Card card) => $"{card.Definition.Protocol} {card.Definition.Value}";

        void Log(int seat, string text)
        {
            _log.Add(new LogEntry { Text = text, Local = seat == Local });
            if (_log.Count > 60)
                _log.RemoveAt(0);
        }

        void Notify() => Changed?.Invoke();
    }
}
