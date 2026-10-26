using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Compile.Engine
{
    // A game log for bug reports: how the game was set up and every move, which is enough to replay it
    // exactly (the engine is deterministic), plus a transcript for reading. The game keeps it as it goes
    // (Game.Create with record: true), so it can be saved at any time, even after the engine fails.
    //
    // The text has three parts: "key: value" header lines, "== Moves ==" with one move per line
    // ("12 p0 play 17 0 up"), and "== Transcript ==", which Load ignores.
    public sealed class GameRecord
    {
        public const int FormatVersion = 1;
        const string NotationHeading = "== Notation ==";
        const string MovesHeading = "== Moves ==";
        const string TranscriptHeading = "== Transcript ==";

        readonly List<Intent> _moves = new List<Intent>();
        readonly List<string> _comments = new List<string>();      // per move, or null
        readonly StringBuilder _transcript = new StringBuilder();
        readonly List<KeyValuePair<string, string>> _info = new List<KeyValuePair<string, string>>();
        HashSet<GameEvent> _setupEvents = new HashSet<GameEvent>();     // written under [Setup]; the first move's events repeat them

        public long Seed { get; private set; }
        public int FirstSeat { get; private set; }
        public string[] Names { get; private set; } = { "Player 1", "Player 2" };
        public bool ConfirmEndTurn { get; private set; } = true;
        public string CardText { get; private set; } = "None";
        public IReadOnlyList<string> Pool { get; private set; }                  // a game that starts with the draft
        public IReadOnlyList<string>[] Drafted { get; private set; }             // or one with the Protocols chosen

        // Every move submitted and accepted, in order, undos included. A move the engine failed on is last.
        public IReadOnlyList<Intent> Moves => _moves;

        // The exception the engine threw, if it did.
        public string Error { get; private set; }

        public string Transcript => _transcript.ToString();

        GameRecord() { }

        internal static GameRecord ForDraft(IReadOnlyList<string> pool, int firstSeat, long seed, string name0, string name1, bool confirmEndTurn, CardEffects effects) =>
            new GameRecord { Pool = pool.ToList(), FirstSeat = firstSeat, Seed = seed, Names = new[] { name0, name1 }, ConfirmEndTurn = confirmEndTurn, CardText = (effects ?? CardEffects.None).Name };

        internal static GameRecord ForDrafted(IReadOnlyList<string> protocols0, IReadOnlyList<string> protocols1, int firstSeat, long seed, string name0, string name1, bool confirmEndTurn, CardEffects effects) =>
            new GameRecord { Drafted = new IReadOnlyList<string>[] { protocols0.ToList(), protocols1.ToList() }, FirstSeat = firstSeat, Seed = seed, Names = new[] { name0, name1 }, ConfirmEndTurn = confirmEndTurn, CardText = (effects ?? CardEffects.None).Name };

        // Extra header lines from whoever saves the log (build, platform, which card text is written, ...).
        public void SetInfo(string key, string value)
        {
            key = OneLine(key).Replace(":", "");
            _info.RemoveAll(kv => kv.Key == key);
            _info.Add(new KeyValuePair<string, string>(key, OneLine(value)));
        }

        // ---------- Writing, as the game goes ----------

        // What the game looked like before a move, so the transcript can say what was asked and what was chosen.
        internal sealed class Note
        {
            public int Step;
            public Intent Intent;
            public string Heading;
            public string Question;
            public string Move;
        }

        internal void Started(Game game)
        {
            _transcript.AppendLine("[Setup]");
            _setupEvents = new HashSet<GameEvent>(game.History);
            WriteEvents(game, game.History);
            WriteBoard(game);
        }

        internal Note Before(Game game, Intent intent) => new Note
        {
            Step = _moves.Count + 1,
            Intent = intent,
            Heading = Heading(game),
            Question = game.Pending == null ? null : DescribeQuestion(game, game.Pending),
            Move = DescribeMove(game, intent),
        };

        internal void After(Game game, Note note, IntentResult result)
        {
            if (!result.Accepted)
            {
                _transcript.AppendLine($"  (not allowed) {note.Move}: {result.Error}");
                return;
            }
            _moves.Add(note.Intent);
            _comments.Add(null);
            WriteMove(note);
            WriteEvents(game, result.Events.Where(e => !_setupEvents.Contains(e)));
            _setupEvents.Clear();
            WriteBoard(game);
        }

        internal void Crashed(Game game, Note note, Exception error)
        {
            Error = error.ToString();
            _moves.Add(note.Intent);
            _comments.Add("the engine failed on this move");
            WriteMove(note);
            _transcript.AppendLine("  !! The engine failed on this move:");
            foreach (var line in Error.Split('\n'))
                _transcript.AppendLine("     " + line.TrimEnd('\r'));
        }

        void WriteMove(Note note)
        {
            _transcript.AppendLine();
            _transcript.AppendLine($"[{note.Step}] {note.Heading}");
            if (note.Question != null)
                _transcript.AppendLine($"  Question: {note.Question}");
            _transcript.AppendLine($"  Move: {note.Move}");
        }

        void WriteEvents(Game game, IEnumerable<GameEvent> events)
        {
            foreach (var e in events)
            {
                var text = e.Describe(game.State, GameEvent.Everyone);
                if (!string.IsNullOrEmpty(text))
                    _transcript.AppendLine($"  - {text}");
            }
        }

        void WriteBoard(Game game) => _transcript.Append(Board(game));

        // ---------- Saving ----------

        // With the game's notation (GameNotation) in its own section first, if given.
        public string ToText(string notation = null)
        {
            var text = new StringBuilder();
            text.AppendLine("# Compile game log. Share the whole file to report a problem: the Moves replay the game, the Transcript is for reading.");
            text.AppendLine($"format: {FormatVersion}");
            foreach (var kv in _info)
                text.AppendLine($"{kv.Key}: {kv.Value}");
            text.AppendLine($"cards: {CardText}");
            text.AppendLine($"seed: {Seed.ToString(CultureInfo.InvariantCulture)}");
            text.AppendLine($"first: {FirstSeat}");
            text.AppendLine($"names: {OneLine(Names[0]).Replace("|", "/")} | {OneLine(Names[1]).Replace("|", "/")}");
            text.AppendLine($"confirm-end-turn: {(ConfirmEndTurn ? "true" : "false")}");
            if (Pool != null)
                text.AppendLine($"pool: {string.Join(", ", Pool)}");
            if (Drafted != null)
                text.AppendLine($"drafted: {string.Join(", ", Drafted[0])} | {string.Join(", ", Drafted[1])}");
            if (Error != null)
                text.AppendLine($"error: {OneLine(Error.Split('\n')[0])}");
            text.AppendLine();
            if (notation != null)
            {
                text.AppendLine(NotationHeading);
                text.Append(notation);
                text.AppendLine();
            }
            text.AppendLine(MovesHeading);
            for (var i = 0; i < _moves.Count; i++)
                text.AppendLine($"{i + 1} {Format(_moves[i])}{(_comments[i] != null ? "  # " + _comments[i] : "")}");
            text.AppendLine();
            text.AppendLine(TranscriptHeading);
            text.Append(_transcript);
            return text.ToString();
        }

        static string OneLine(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

        // ---------- Loading and replaying ----------

        public static GameRecord Load(string text)
        {
            var record = new GameRecord();
            var inMoves = false;
            var inNotation = false;
            var lineNumber = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line == TranscriptHeading)
                    break;
                if (line == MovesHeading)
                {
                    inMoves = true;
                    inNotation = false;
                    continue;
                }
                if (line == NotationHeading)
                {
                    inNotation = true;
                    continue;
                }
                if (inNotation)
                    continue;
                var hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash).Trim();
                if (line.Length == 0)
                    continue;
                try
                {
                    if (inMoves)
                        record.ReadMove(line);
                    else
                        record.ReadHeader(line);
                }
                catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException || e is ArgumentException)
                {
                    throw new FormatException($"Game log line {lineNumber}: {e.Message} ({raw.Trim()})", e);
                }
            }
            if (record.Pool == null && record.Drafted == null)
                throw new FormatException("Game log has no 'pool' or 'drafted' line.");
            return record;
        }

        void ReadHeader(string line)
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
                throw new FormatException("expected 'key: value'");
            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            List<string> List(string s) => s.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            switch (key)
            {
                case "format":
                    if (int.Parse(value, CultureInfo.InvariantCulture) > FormatVersion)
                        throw new FormatException($"format {value} is newer than this version understands ({FormatVersion})");
                    break;
                case "cards": CardText = value; break;
                case "seed": Seed = long.Parse(value, CultureInfo.InvariantCulture); break;
                case "first": FirstSeat = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "names":
                    var names = value.Split('|');
                    Names = new[] { names[0].Trim(), names[1].Trim() };
                    break;
                case "confirm-end-turn": ConfirmEndTurn = value == "true"; break;
                case "pool": Pool = List(value); break;
                case "drafted":
                    var sides = value.Split('|');
                    Drafted = new IReadOnlyList<string>[] { List(sides[0]), List(sides[1]) };
                    break;
                case "error": Error = value; break;
                default: _info.Add(new KeyValuePair<string, string>(key, value)); break;
            }
        }

        void ReadMove(string line)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var step = int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (step != _moves.Count + 1)
                throw new FormatException($"expected move {_moves.Count + 1}");
            if (parts[1].Length != 2 || parts[1][0] != 'p')
                throw new FormatException("expected a seat (p0 or p1)");
            _moves.Add(Parse(parts[1][1] - '0', parts.Skip(2).ToArray()));
            _comments.Add(null);
        }

        // The game as it was after this many moves (all of them by default). The card text must be the same
        // set the log was made with; a move that's no longer accepted means the engine's behaviour changed.
        public Game Replay(CardCatalog catalog, CardEffects effects, int moves = int.MaxValue)
        {
            var game = Drafted != null
                ? Game.CreateDrafted(catalog, Drafted[0], Drafted[1], FirstSeat, Seed, Names[0], Names[1], ConfirmEndTurn, effects, record: true)
                : Game.Create(catalog, Pool, FirstSeat, Seed, Names[0], Names[1], ConfirmEndTurn, effects, record: true);
            foreach (var (move, i) in _moves.Take(moves).Select((m, i) => (m, i)))
            {
                var result = game.Submit(move);
                if (!result.Accepted)
                    throw new InvalidOperationException($"Move {i + 1} ({Format(move)}) was rejected on replay: {result.Error}");
            }
            return game;
        }

        // ---------- Moves as text ----------

        static string Ints(IEnumerable<int> values) => values.Any() ? string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))) : "-";

        static int[] ParseInts(string s) => s == "-" ? Array.Empty<int>() : s.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

        public static string Format(Intent intent)
        {
            var seat = $"p{intent.Seat}";
            switch (intent)
            {
                case DraftPick pick: return $"{seat} pick {pick.Protocol}";
                case PlayCard play: return $"{seat} play {play.CardId} {play.Line} {(play.FaceUp ? "up" : "down")}";
                case Refresh _: return $"{seat} refresh";
                case EndTurn _: return $"{seat} end";
                case Undo _: return $"{seat} undo";
                case Concede _: return $"{seat} concede";
                case DebugEffect debug: return $"{seat} debug {debug.Key}";
                case Rearrange r: return $"{seat} rearrange {r.TargetSeat} {Ints(r.Order)}";
                case AnswerChoice a when a.Decline: return $"{seat} decline";
                case AnswerChoice a when a.Line >= 0: return $"{seat} line {a.Line}";
                case AnswerChoice a when a.Options.Count > 0: return $"{seat} options {Ints(a.Options)}";
                case AnswerChoice a: return $"{seat} cards {Ints(a.Cards)}";
                default: throw new ArgumentException($"Unknown intent {intent.GetType().Name}");
            }
        }

        // A game's whole log, with its notation first. The log must still be written if the notation can't be
        // (that's a bug to report too).
        public static string FullText(Game game)
        {
            var notation = GameNotation.TryWrite(game, out var text, out var error) ? text : $"; The notation couldn't be written: {error}{Environment.NewLine}";
            return game.Record.ToText(notation);
        }

        // A move as Format writes it ("p1 play 1042 0 up"). Throws FormatException for anything else.
        public static Intent ParseMove(string text)
        {
            var parts = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].Length != 2 || parts[0][0] != 'p' || parts[0][1] != '0' && parts[0][1] != '1')
                throw new FormatException("expected a seat (p0 or p1) and a move");
            try
            {
                return Parse(parts[0][1] - '0', parts.Skip(1).ToArray());
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is OverflowException)
            {
                throw new FormatException($"bad move '{text}'");
            }
        }

        static Intent Parse(int seat, string[] args)
        {
            switch (args[0])
            {
                case "pick": return new DraftPick(seat, args[1]);
                case "play": return new PlayCard(seat, int.Parse(args[1], CultureInfo.InvariantCulture), int.Parse(args[2], CultureInfo.InvariantCulture), args[3] == "up");
                case "refresh": return new Refresh(seat);
                case "end": return new EndTurn(seat);
                case "undo": return new Undo(seat);
                case "concede": return new Concede(seat);
                case "debug": return new DebugEffect(seat, args[1]);
                case "rearrange": return new Rearrange(seat, int.Parse(args[1], CultureInfo.InvariantCulture), ParseInts(args[2]));
                case "decline": return AnswerChoice.Declined(seat);
                case "line": return AnswerChoice.WithLine(seat, int.Parse(args[1], CultureInfo.InvariantCulture));
                case "options": return AnswerChoice.WithOrder(seat, ParseInts(args[1]));
                case "cards": return AnswerChoice.WithCards(seat, ParseInts(args[1]));
                default: throw new FormatException($"unknown move '{args[0]}'");
            }
        }

        // ---------- The transcript ----------

        static string Name(Game game, int seat) => game.State.Players[seat].Name;

        static string Side(int local) => new[] { "left", "middle", "right" }[local];

        static string LocalLine(Game game, int seat, int local) =>
            local >= 0 && local < GameState.LineCount ? $"{Name(game, seat)}'s {Side(local)} line ({GameEvent.FieldLineName(GameState.ToField(seat, local))})" : $"line {local}?";

        static string Card(Game game, int id)
        {
            var card = game.State.AllCards().FirstOrDefault(c => c.Id == id);
            return card == null ? $"#{id}?" : Card(game, card);
        }

        static string Card(Game game, CardInstance card) => $"{card.Spec}{(card.FaceUp || !game.IsInField(card) ? "" : " (face-down)")} #{card.Id}{Where(game, card)}";

        static string Where(Game game, CardInstance card)
        {
            for (var line = 0; line < GameState.LineCount; line++)
                for (var seat = 0; seat < GameState.SeatCount; seat++)
                {
                    var stack = game.State.Lines[line].StackCards[seat];
                    var i = stack.IndexOf(card);
                    if (i >= 0)
                        return $" [{Name(game, seat)}'s stack in the {GameEvent.FieldLineName(line)}{(i < stack.Count - 1 ? ", covered" : "")}]";
                }
            foreach (var player in game.State.Players)
            {
                if (player.Hand.Contains(card))
                    return $" [{player.Name}'s hand]";
                if (player.Trash.Contains(card))
                    return $" [{player.Name}'s trash]";
                if (player.Deck.Contains(card))
                    return $" [{player.Name}'s deck]";
            }
            return "";
        }

        static string Heading(Game game)
        {
            var s = game.State;
            if (s.Stage == GameStage.Draft)
                return game.DraftSeat < 0 ? "Draft" : $"Draft - {Name(game, game.DraftSeat)} to {(game.DraftConfirmSeat >= 0 ? "confirm" : "pick")}";
            if (s.Stage == GameStage.Over)
                return "Game over";
            return $"Turn {s.Turn} - {Name(game, s.ActiveSeat)} - {s.Phase}{(game.AwaitingEndTurn ? " (turn done, waiting for End turn)" : "")}";
        }

        static string DescribeQuestion(Game game, PendingChoice c)
        {
            var text = new StringBuilder($"{Name(game, c.Seat)} is asked \"{c.Prompt}\" ({c.Kind}");
            if (c.Kind == ChoiceKind.FieldCards || c.Kind == ChoiceKind.HandCards)
                text.Append(c.Min == c.Max ? $", choose {c.Min}" : $", choose {c.Min} to {c.Max}");
            if (c.Optional)
                text.Append(", optional");
            if (c.Source != null)
                text.Append($", from {c.Source.Spec}");
            text.Append(")");
            if (c.ValidCards.Count > 0 && c.Kind != ChoiceKind.Order)
                text.Append($". Could pick: {string.Join("; ", c.ValidCards.Select(id => Card(game, id)))}");
            if (c.ValidLines.Count > 0)
                text.Append($". Lines: {string.Join("; ", c.ValidLines.Select(l => LocalLine(game, c.Seat, l)))}");
            if (c.Options.Count > 0)
                text.Append($". Options: {string.Join("; ", c.Options.Select((o, i) => $"{i}: {o}"))}");
            if (c.Kind == ChoiceKind.Play)
                text.Append($". Face-up {(c.FaceUpAllowed ? "allowed" : "not allowed")}, face-down {(c.FaceDownAllowed ? "allowed" : "not allowed")}");
            if (c.TargetSeat >= 0)
                text.Append($". Whose Protocols: {Name(game, c.TargetSeat)}'s");
            return text.ToString();
        }

        static string DescribeMove(Game game, Intent intent)
        {
            var who = Name(game, intent.Seat);
            var c = game.Pending;
            switch (intent)
            {
                case DraftPick pick: return $"{who} drafts {pick.Protocol}";
                case PlayCard play: return $"{who} plays {Card(game, play.CardId)} {(play.FaceUp ? "face-up" : "face-down")} into {LocalLine(game, play.Seat, play.Line)}";
                case Refresh _: return $"{who} refreshes";
                case EndTurn _: return game.State.Stage == GameStage.Draft ? $"{who} confirms their picks" : $"{who} ends the turn";
                case Undo _: return $"{who} undoes their last move";
                case Concede _: return $"{who} concedes";
                case DebugEffect debug: return $"{who} uses the debug effect \"{DebugEffects.Label(debug.Key)}\"";
                case Rearrange r:
                    var protocols = game.State.Players[r.TargetSeat].Protocols;
                    return $"{who} rearranges {Name(game, r.TargetSeat)}'s Protocols to {string.Join(", ", r.Order.Select(i => i >= 0 && i < protocols.Count ? protocols[i].Name : "?"))}";
                case AnswerChoice a when a.Decline: return $"{who} declines";
                case AnswerChoice a when a.Line >= 0: return $"{who} picks {LocalLine(game, a.Seat, a.Line)}";
                case AnswerChoice a when a.Options.Count > 0:
                    string Option(int i) => c != null && i >= 0 && i < c.Options.Count ? c.Options[i] : $"option {i}";
                    return a.Options.Count == 1 ? $"{who} picks \"{Option(a.Options[0])}\"" : $"{who} orders {string.Join(", then ", a.Options.Select(Option))}";
                case AnswerChoice a: return $"{who} picks {(a.Cards.Count == 0 ? "nothing" : string.Join("; ", a.Cards.Select(id => Card(game, id))))}";
                default: return $"{who}: {Format(intent)}";
            }
        }

        // Everything on the table, hidden cards included ("~" marks face-down cards in the field). Lines are
        // field lines, from seat 0's left; stacks are listed bottom to top, decks top first
        // (without ids, which change when a card is drawn).
        static string Board(Game game)
        {
            var s = game.State;
            var text = new StringBuilder();
            if (s.Stage == GameStage.Draft)
            {
                text.AppendLine($"  Draft: available {string.Join(", ", s.Draft.Available)}; " +
                                string.Join("; ", Enumerable.Range(0, GameState.SeatCount).Select(seat => $"{Name(game, seat)} has {string.Join(", ", s.Draft.PicksFor(seat))}")));
                return text.ToString();
            }
            string Cards(IEnumerable<CardInstance> cards, bool field = false) =>
                cards.Any() ? string.Join(", ", cards.Select(c => $"{(c.FaceUp || !field ? "" : "~")}{c.Spec}#{c.Id}")) : "none";
            text.AppendLine($"  Board: Control {(s.Control == GameState.Neutral ? "neutral" : Name(game, s.Control))}" +
                            (s.Stage == GameStage.Over ? $"; game over, {(s.Winner < 0 ? "a draw" : Name(game, s.Winner) + " wins")} ({s.EndReason})" : ""));
            for (var line = 0; line < GameState.LineCount; line++)
            {
                string Side(int seat)
                {
                    var protocol = s.ProtocolIn(seat, line);
                    return $"{Name(game, seat)} {protocol?.Name}{(protocol != null && protocol.Compiled ? " (compiled)" : "")} = {game.LineValue(seat, line)} [{Cards(s.Lines[line].Stack(seat), field: true)}]";
                }
                text.AppendLine($"    {GameEvent.FieldLineName(line)}: {Side(0)}  vs  {Side(1)}");
            }
            foreach (var p in s.Players)
                text.AppendLine($"    {p.Name}: hand [{Cards(p.Hand)}]; deck {p.Deck.Count} [{(p.Deck.Any() ? string.Join(", ", p.Deck.Select(c => c.Spec)) : "none")}]; trash [{Cards(p.Trash)}]");
            return text.ToString();
        }
    }
}
