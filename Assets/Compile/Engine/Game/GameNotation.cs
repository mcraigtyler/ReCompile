using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Compile.Engine
{
    // A game written short, like chess notation, that still replays exactly: tags with the setup, then
    // every decision with what came of it. The full spec is docs/notation.md. In brief:
    //
    //   [P1 "Ann"] [P2 "Bob"] [First "P2"] [Seed "42"] ...
    //   De Fi Pl Me Ap Dk +(Pl5,Fi5,Dk4,Pl2,Pl0) +(Me3,Me0,De3,Ap5,De4)
    //   1. Ap5a(-De3) | Pl2b(-(Pl5,Fi5) -(Me3,Me0,De4))
    //
    // Cards are a Protocol code and a value (De1 = Death 1); a game never has two of one card, so the
    // name says which. Lines are a, b, c from P1's left (P1 is seat 0). Card text is in parentheses after
    // what set it off, nested as it resolved. Undone moves are left out.
    //
    // Reading replays the game: at each point the engine waits, the next token says the decision, and
    // what the game then does must match the tokens that follow.
    public sealed class GameNotation
    {
        static readonly Dictionary<string, string> KnownCodes = new Dictionary<string, string>
        {
            ["Apathy"] = "Ap", ["Darkness"] = "Dk", ["Death"] = "De", ["Fire"] = "Fi", ["Gravity"] = "Gr",
            ["Hate"] = "Ha", ["Life"] = "Li", ["Light"] = "Lt", ["Love"] = "Lo", ["Metal"] = "Me",
            ["Plague"] = "Pl", ["Psychic"] = "Ps", ["Speed"] = "Sd", ["Spirit"] = "Sp", ["Water"] = "Wa",
        };

        static readonly Regex CardPattern = new Regex(@"^([A-Za-z]+)(\d+)$");
        static readonly Regex PlayPattern = new Regex(@"^(~?)([A-Za-z]+\d+)([abc])$");
        static readonly Regex ShiftPattern = new Regex(@"^([A-Za-z]+\d+)>([abc])$");
        static readonly Regex TagPattern = new Regex(@"\[(\w+)\s+""([^""]*)""\]");
        static readonly Regex RoundPattern = new Regex(@"^\d+\.$");

        // An action on several cards, starting a token: "-(Me3,Me0,De4)".
        static readonly Regex CardListPattern = new Regex(@"(?<![A-Za-z0-9:*~])[-x+^v<?=&]\([A-Za-z0-9]+(,[A-Za-z0-9]+)+\)");
        const char GroupMark = '\u0001';

        readonly Dictionary<string, string> _tags = new Dictionary<string, string>();
        readonly List<string> _tokens;

        public IReadOnlyDictionary<string, string> Tags => _tags;
        public IReadOnlyList<string> Tokens => _tokens;

        GameNotation(List<string> tokens) => _tokens = tokens;

        // ---------- Writing ----------

        public static string Write(Game game) => Write(game, null);

        // Each standing decision (Game.AcceptedIntents) as notation: the decision and what came of it.
        // A replay shows these move by move.
        public static IReadOnlyList<string> Steps(Game game)
        {
            var steps = new List<string>();
            Write(game, steps);
            return steps;
        }

        static string Write(Game game, List<string> steps)
        {
            var setup = game.Setup ?? throw new InvalidOperationException("Only games made with Game.Create or Game.CreateDrafted can be written.");
            var replay = setup.NewGame();
            var writer = new Writer(Codes.For(setup.Protocols));
            writer.Events(replay.TakeEvents(), null);
            foreach (var intent in game.AcceptedIntents)
            {
                var from = writer.Tokens.Count;
                var decision = writer.Decision(replay, intent);
                var result = replay.Submit(intent);
                if (!result.Accepted)
                    throw new InvalidOperationException($"The game's moves didn't replay: {result.Error}");
                writer.Events(result.Events, decision);
                steps?.Add(Render(writer.Tokens.Skip(from).ToList()).Trim());
            }
            return TagLines(replay, writer.Codes) + Render(writer.Tokens);
        }

        // Notation as given, or the notation section of a saved game log.
        public static string FromLog(string text)
        {
            var start = text.IndexOf("== Notation ==", StringComparison.Ordinal);
            if (start < 0)
                return text;
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("== Moves ==", start, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        static string TagLines(Game game, Codes codes)
        {
            var setup = game.Setup;
            var s = game.State;
            string Tag(string key, string value) => $"[{key} \"{(value ?? "").Replace("\"", "'")}\"]";
            var text = new StringBuilder();
            text.AppendLine($"{Tag("P1", setup.Names[0])} {Tag("P2", setup.Names[1])} {Tag("First", Seat(setup.FirstSeat))} {Tag("Seed", setup.Seed.ToString(CultureInfo.InvariantCulture))} {Tag("Cards", setup.Effects.Name)}");
            if (setup.Pool != null)
                text.AppendLine(Tag("Pool", string.Join(", ", setup.Pool)));
            else
                text.AppendLine(Tag("Protocols", $"{string.Join(", ", setup.Drafted[0])} | {string.Join(", ", setup.Drafted[1])}"));
            if (s.Stage != GameStage.Draft)
                text.AppendLine(Tag("Lines", string.Join(", ", Enumerable.Range(0, GameState.LineCount)
                    .Select(l => $"{Letter(l)} {codes.Code(s.ProtocolIn(0, l).Name)}/{codes.Code(s.ProtocolIn(1, l).Name)}"))));
            var result = s.Stage != GameStage.Over ? "*"
                : s.Winner < 0 ? "Draw"
                : $"{Seat(s.Winner)} wins ({s.EndReason})";
            text.AppendLine(Tag("Result", result));
            return text.ToString();
        }

        // Tokens with spaces between, except around parentheses; each pair of turns on a new line.
        static string Render(IReadOnlyList<string> tokens)
        {
            var text = new StringBuilder();
            string last = null;
            foreach (var token in tokens)
            {
                if (token == "(" || token == ")" || last == "(" || last == null)
                {
                    // no space
                }
                else if (RoundPattern.IsMatch(token))
                    text.AppendLine();
                else
                    text.Append(' ');
                text.Append(token);
                last = token;
            }
            text.AppendLine();
            return text.ToString();
        }

        static string Seat(int seat) => seat == 0 ? "P1" : "P2";

        static char Letter(int fieldLine) => (char)('a' + fieldLine);

        // Protocol codes for the Protocols in a game: the usual two letters, or the full name if one is new.
        sealed class Codes
        {
            readonly Dictionary<string, string> _code = new Dictionary<string, string>();
            readonly Dictionary<string, string> _protocol = new Dictionary<string, string>();

            public static Codes For(IEnumerable<string> protocols)
            {
                var codes = new Codes();
                foreach (var protocol in protocols.Distinct())
                {
                    var code = KnownCodes.TryGetValue(protocol, out var known) && !codes._protocol.ContainsKey(known)
                        ? known : new string(protocol.Where(char.IsLetter).ToArray());
                    codes._code[protocol] = code;
                    codes._protocol[code] = protocol;
                }
                return codes;
            }

            public string Code(string protocol) => _code.TryGetValue(protocol, out var code) ? code : protocol;

            public string Card(CardSpec spec) => Code(spec.Protocol) + spec.Value.ToString(CultureInfo.InvariantCulture);

            public string Card(CardRef card) => Card(card.CardId);

            public string Card(string cardId)
            {
                var split = cardId.LastIndexOf('_');
                return Code(cardId.Substring(0, split)) + cardId.Substring(split + 1);
            }

            public string Protocol(string code) =>
                _protocol.TryGetValue(code, out var protocol) ? protocol : throw new FormatException($"there's no Protocol '{code}' in this game");

            // "De1" to that card wherever it is now.
            public CardInstance Find(Game game, string name)
            {
                var match = CardPattern.Match(name);
                if (!match.Success)
                    throw new FormatException($"'{name}' isn't a card");
                var protocol = Protocol(match.Groups[1].Value);
                var value = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                return game.State.AllCards().FirstOrDefault(c => c.Spec.Protocol == protocol && c.Spec.Value == value)
                       ?? throw new FormatException($"there's no {name} in this game");
            }
        }

        // ---------- Tokens for decisions and events ----------

        // A token is a symbol and items ("-(Pl5,Fi5)" is "-" with Pl5 and Fi5). A decision's tokens come
        // first; the events it causes follow, minus what the decision already said (a delete you chose
        // isn't written twice).
        sealed class Group
        {
            public readonly string Symbol;
            public readonly List<string> Items;

            public Group(string symbol, IEnumerable<string> items)
            {
                Symbol = symbol;
                Items = items.ToList();
            }

            public Group(string item) : this("", new[] { item }) { }

            // One action on several cards keeps them together: "-(Me3,Me0,De4)".
            public override string ToString() =>
                Items.Count > 1 && Symbol.Length == 1 ? $"{Symbol}({string.Join(",", Items)})" : Symbol + string.Join(",", Items);
        }

        sealed class Writer
        {
            public readonly Codes Codes;
            public readonly List<string> Tokens = new List<string>();

            public Writer(Codes codes) => Codes = codes;

            // What a decision looks like, from the game as it is before it's made.
            public List<Group> Decision(Game game, Intent intent)
            {
                var groups = new List<Group>();
                var choice = game.Pending;
                switch (intent)
                {
                    case DraftPick pick:
                        groups.Add(new Group(Codes.Code(pick.Protocol)));
                        break;
                    case PlayCard play:
                        var card = game.State.AllCards().First(c => c.Id == play.CardId);
                        groups.Add(new Group($"{(play.FaceUp ? "" : "~")}{Codes.Card(card.Spec)}{Letter(GameState.ToField(play.Seat, play.Line))}"));
                        break;
                    case Refresh _:
                        groups.Add(new Group("R"));
                        break;
                    case Concede c:
                        groups.Add(new Group($"concede:{Seat(c.Seat)}"));
                        break;
                    case DebugEffect debug:
                        groups.Add(new Group($"dbg:{debug.Key}"));
                        groups.Add(new Group("("));
                        break;
                    case Rearrange r:
                        var protocols = game.State.Players[r.TargetSeat].Protocols;
                        groups.Add(new Group($"r{Seat(r.TargetSeat)}:{string.Join(",", r.Order.Select(i => Codes.Code(protocols[i].Name)))}"));
                        break;
                    case AnswerChoice a when a.Decline:
                        groups.Add(new Group("no"));
                        break;
                    case AnswerChoice a when choice != null && (choice.Kind == ChoiceKind.FieldCards || choice.Kind == ChoiceKind.HandCards):
                        // A card picked to shift is written with where it went (the line is asked next).
                        if (choice.Verb == "Shift")
                            break;
                        var picked = a.Cards.Select(id => game.State.AllCards().First(c => c.Id == id)).ToList();
                        if (choice.Verb == "Flip")
                        {
                            AddIfAny(groups, "^", picked.Where(c => !c.FaceUp).Select(c => Codes.Card(c.Spec)));
                            AddIfAny(groups, "v", picked.Where(c => c.FaceUp).Select(c => Codes.Card(c.Spec)));
                        }
                        else
                            groups.Add(new Group(VerbSymbol(choice.Verb), picked.Select(c => Codes.Card(c.Spec))));
                        break;
                    case AnswerChoice a when choice != null && choice.Kind == ChoiceKind.Line:
                        var field = GameState.ToField(a.Seat, a.Line);
                        if (choice.Purpose == LinePurpose.Compile)
                        {
                            var slot = game.State.ProtocolIn(a.Seat, field);
                            groups.Add(new Group($"{(slot.Compiled ? "##" : "#")}{Codes.Code(slot.Name)}"));
                        }
                        else if (choice.Purpose == LinePurpose.ShiftTo && choice.Subject != null)
                            groups.Add(new Group($"{Codes.Card(choice.Subject.Spec)}>{Letter(field)}"));
                        else
                            groups.Add(new Group($"/{Letter(field)}"));
                        break;
                    case AnswerChoice a when choice != null && choice.Kind == ChoiceKind.Option:
                        groups.Add(new Group(choice.Options.Count == 1 ? "ok" : $":{a.Options[0] + 1}"));
                        break;
                    case AnswerChoice a when choice != null && choice.Kind == ChoiceKind.Order:
                        groups.Add(new Group($"ord:{string.Join(",", a.Options.Select(i => i + 1))}"));
                        break;
                    case EndTurn _:
                        break;
                    default:
                        throw new InvalidOperationException($"No notation for {intent.GetType().Name}.");
                }
                return groups;
            }

            static void AddIfAny(List<Group> groups, string symbol, IEnumerable<string> items)
            {
                var list = items.ToList();
                if (list.Count > 0)
                    groups.Add(new Group(symbol, list));
            }

            static string VerbSymbol(string verb)
            {
                switch (verb)
                {
                    case "Discard": return "-";
                    case "Delete": return "x";
                    case "Return": return "<";
                    case "Reveal": return "?";
                    case "Give": return "=";
                    default: throw new InvalidOperationException($"No notation for picking cards to {verb}.");
                }
            }

            // What decisions said that hasn't happened yet. A play can land a move or two later (text on the
            // card it covers asks something first); what never happens is forgotten when the turn ends.
            readonly List<string> _said = new List<string>();

            // Writes a decision's tokens and the events it caused, without repeating what the decision said.
            public void Events(IEnumerable<GameEvent> events, List<Group> decision)
            {
                if (decision != null)
                    foreach (var group in decision)
                    {
                        Tokens.Add(group.ToString());
                        _said.AddRange(group.Items.Select(item => group.Symbol + item));
                    }
                foreach (var e in events)
                    foreach (var group in Groups(e))
                    {
                        if (e is TurnStarted)
                            _said.Clear();
                        var items = group.Items.Where(item => !_said.Remove(group.Symbol + item)).ToList();
                        if (items.Count > 0)
                            Tokens.Add(new Group(group.Symbol, items).ToString());
                    }
            }

            IEnumerable<Group> Groups(GameEvent e)
            {
                switch (e)
                {
                    case TurnStarted t:
                        yield return new Group(t.Turn % 2 == 1 ? $"{(t.Turn + 1) / 2}." : "|");
                        break;
                    case ControlChanged c:
                        yield return new Group(c.Holder == GameState.Neutral ? "@-" : $"@{Seat(c.Holder)}");
                        break;
                    case DeckReshuffled d:
                        yield return new Group($"%{Seat(d.Seat)}");
                        break;
                    case CardsDrawn d when d.Cards.Count > 0:
                        yield return new Group("+", d.Cards.Select(Codes.Card));
                        break;
                    case CardPlayed p:
                        yield return new Group($"{(p.FaceUp ? "" : "~")}{Codes.Card(p.Card)}{Letter(p.FieldLine)}{(p.FromDeck ? "*" : "")}");
                        break;
                    case Refreshed r:
                        yield return new Group(r.Forced ? "R*" : "R");
                        break;
                    case ActionSkipped _:
                        yield return new Group("pass");
                        break;
                    case CardsDiscarded d when d.Cards.Count > 0:
                        yield return new Group("-", d.Cards.Select(Codes.Card));
                        break;
                    case CardsDeleted d when d.Cards.Count > 0:
                        yield return new Group("x", d.Cards.Select(Codes.Card));
                        break;
                    case CardsFlipped f:
                        var up = f.Cards.Where(c => c.FaceUp).Select(Codes.Card).ToList();
                        var down = f.Cards.Where(c => !c.FaceUp).Select(Codes.Card).ToList();
                        if (up.Count > 0)
                            yield return new Group("^", up);
                        if (down.Count > 0)
                            yield return new Group("v", down);
                        break;
                    case CardShifted s:
                        yield return new Group($"{Codes.Card(s.Card)}>{Letter(s.ToLine)}");
                        break;
                    case CardsReturned r:
                        yield return new Group("<", r.Cards.Select(Codes.Card));
                        break;
                    case CardsRevealed r:
                        yield return new Group("?", r.Cards.Select(Codes.Card));
                        break;
                    case CardsGiven g:
                        yield return new Group("=", g.Cards.Select(Codes.Card));
                        break;
                    case HandCardsTaken t:
                        yield return new Group("&", t.Cards.Select(Codes.Card));
                        break;
                    case CardTaken t when t.Card != null:
                        yield return new Group("&", new[] { Codes.Card(t.Card) });
                        break;
                    case LineCompiled c:
                        yield return new Group($"{(c.Recompile ? "##" : "#")}{Codes.Code(c.Protocol)}");
                        break;
                    case ProtocolsRearranged r:
                        yield return new Group($"r{Seat(r.TargetSeat)}:{string.Join(",", r.Order.Select(Codes.Code))}");
                        break;
                    case CardTextRuns t:
                        var card = Codes.Card(t.Card);
                        switch (t.Reason)
                        {
                            // Right after the play, the parenthesis is enough; after other text, it says whose it is.
                            case TextReason.Played:
                                if (Tokens.Count == 0 || !Regex.IsMatch(Tokens[Tokens.Count - 1], $@"^~?{card}[abc]\*?$"))
                                    yield return new Group($"M:{card}");
                                break;
                            case TextReason.Uncovered:
                            case TextReason.FlippedUp: yield return new Group($"!{card}"); break;
                            case TextReason.Start: yield return new Group($"S:{card}"); break;
                            case TextReason.End: yield return new Group($"E:{card}"); break;
                            default: yield return new Group($"*{card}"); break;
                        }
                        yield return new Group("(");
                        break;
                    case TextEnded _:
                        yield return new Group(")");
                        break;
                    case GameEnded g:
                        yield return new Group(g.Winner == 0 ? "1-0" : g.Winner == 1 ? "0-1" : "1/2-1/2");
                        break;
                }
            }
        }

        // ---------- Reading ----------

        public static GameNotation Read(string text)
        {
            var tags = new Dictionary<string, string>();
            var body = new StringBuilder();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith(";", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    foreach (Match m in TagPattern.Matches(line))
                        tags[m.Groups[1].Value] = m.Groups[2].Value;
                    continue;
                }
                body.Append(line).Append(' ');
            }
            // Parentheses around the cards of one action ("-(Me3,Me0,De4)") stay part of it; the others
            // (around card text) are tokens of their own.
            var grouped = new List<string>();
            var marked = CardListPattern.Replace(body.ToString(), m =>
            {
                grouped.Add(m.Value);
                return $" {GroupMark}{grouped.Count - 1} ";
            });
            var tokens = marked.Replace("(", " ( ").Replace(")", " ) ")
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t[0] == GroupMark ? grouped[int.Parse(t.Substring(1), CultureInfo.InvariantCulture)] : t)
                .ToList();
            var notation = new GameNotation(tokens);
            foreach (var kv in tags)
                notation._tags[kv.Key] = kv.Value;
            foreach (var key in new[] { "P1", "P2", "First", "Seed" })
                if (!tags.ContainsKey(key))
                    throw new FormatException($"Game notation has no [{key}] tag.");
            if (!tags.ContainsKey("Pool") && !tags.ContainsKey("Protocols"))
                throw new FormatException("Game notation has no [Pool] or [Protocols] tag.");
            return notation;
        }

        GameSetup SetupFor(CardCatalog catalog, CardEffects effects)
        {
            List<string> List(string s) => s.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var first = _tags["First"] == "P2" ? 1 : _tags["First"] == "P1" ? 0 : throw new FormatException("[First] must be P1 or P2.");
            if (!long.TryParse(_tags["Seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                throw new FormatException("[Seed] isn't a number.");
            IReadOnlyList<IReadOnlyList<string>> drafted = null;
            if (_tags.TryGetValue("Protocols", out var protocols))
            {
                var sides = protocols.Split('|');
                if (sides.Length != 2)
                    throw new FormatException("[Protocols] must be two lists with | between.");
                drafted = new[] { List(sides[0]), List(sides[1]) };
            }
            return new GameSetup(catalog, drafted == null ? List(_tags["Pool"]) : null, drafted, first, seed, _tags["P1"], _tags["P2"], true, effects);
        }

        // Plays the game out as written and returns it. Notation that stops partway gives the game at that point.
        public Game Replay(CardCatalog catalog, CardEffects effects)
        {
            var setup = SetupFor(catalog, effects);
            var game = setup.NewGame();
            var writer = new Writer(Codes.For(setup.Protocols));
            writer.Events(game.TakeEvents(), null);
            Check(writer, 0);

            while (game.State.Stage != GameStage.Over)
            {
                var at = writer.Tokens.Count;
                if (at >= _tokens.Count)
                    break;
                var draftConfirm = game.State.Stage == GameStage.Draft && game.DraftConfirmSeat >= 0;
                var intent = draftConfirm ? new EndTurn(game.DraftConfirmSeat) : Decode(game, writer.Codes, at);
                var decision = writer.Decision(game, intent);
                var result = game.Submit(intent);
                if (!result.Accepted)
                    throw Error(at, $"'{Token(at)}' isn't allowed: {result.Error}");
                writer.Events(result.Events, decision);
                Check(writer, at);
            }
            if (writer.Tokens.Count < _tokens.Count)
                throw Error(writer.Tokens.Count, $"the game is over, but the notation goes on: '{Token(writer.Tokens.Count)}'");
            return game;
        }

        string Token(int i) => i < _tokens.Count ? _tokens[i] : "(end)";

        // What the game did must be what's written.
        void Check(Writer writer, int from)
        {
            for (var i = from; i < writer.Tokens.Count; i++)
                if (i >= _tokens.Count || writer.Tokens[i] != _tokens[i])
                    throw Error(i, $"the game did '{writer.Tokens[i]}', but the notation says '{Token(i)}'");
        }

        FormatException Error(int index, string message)
        {
            var round = _tokens.Take(Math.Min(index, _tokens.Count)).LastOrDefault(t => RoundPattern.IsMatch(t));
            return new FormatException($"Game notation, token {index + 1}{(round != null ? $" (turn {round})" : "")}: {message}");
        }

        // The decision the next token says, for whatever the game is waiting on.
        Intent Decode(Game game, Codes codes, int at)
        {
            var token = _tokens[at];
            var s = game.State;
            int LocalLine(int seat, char letter) =>
                letter >= 'a' && letter <= 'c' ? GameState.ToLocal(seat, letter - 'a') : throw Error(at, $"'{letter}' isn't a line");

            if (token.StartsWith("concede:", StringComparison.Ordinal))
                return new Concede(token.EndsWith("P2", StringComparison.Ordinal) ? 1 : 0);

            if (s.Stage == GameStage.Draft)
                return new DraftPick(game.DraftSeat, codes.Protocol(token));

            var choice = game.Pending;
            if (choice == null)
            {
                var seat = s.ActiveSeat;
                if (token.StartsWith("dbg:", StringComparison.Ordinal))
                    return new DebugEffect(seat, token.Substring(4));
                if (game.AwaitingEndTurn)
                    return new EndTurn(seat);
                if (token == "R")
                    return new Refresh(seat);
                return Play(game, codes, seat, at);
            }

            if (token == "no")
                return AnswerChoice.Declined(choice.Seat);
            try
            {
                switch (choice.Kind)
                {
                    case ChoiceKind.FieldCards:
                    case ChoiceKind.HandCards:
                        return AnswerChoice.WithCards(choice.Seat, PickedCards(game, codes, choice, at));
                    case ChoiceKind.Line:
                        if (choice.Purpose == LinePurpose.Compile)
                        {
                            var protocol = codes.Protocol(token.TrimStart('#'));
                            var line = Enumerable.Range(0, GameState.LineCount).FirstOrDefault(l => s.ProtocolIn(choice.Seat, l).Name == protocol);
                            return AnswerChoice.WithLine(choice.Seat, GameState.ToLocal(choice.Seat, line));
                        }
                        return AnswerChoice.WithLine(choice.Seat, LocalLine(choice.Seat, token[token.Length - 1]));
                    case ChoiceKind.Option:
                        return AnswerChoice.WithOption(choice.Seat, token == "ok" ? 0 : int.Parse(token.TrimStart(':'), CultureInfo.InvariantCulture) - 1);
                    case ChoiceKind.Order:
                        return AnswerChoice.WithOrder(choice.Seat, token.Substring(4).Split(',').Select(i => int.Parse(i, CultureInfo.InvariantCulture) - 1));
                    case ChoiceKind.Rearrange:
                    case ChoiceKind.Swap:
                        var target = token.StartsWith("rP2:", StringComparison.Ordinal) ? 1 : 0;
                        var current = s.Players[target].Protocols.Select(p => p.Name).ToList();
                        return new Rearrange(choice.Seat, target, token.Substring(4).Split(',').Select(code => current.IndexOf(codes.Protocol(code))).ToArray());
                    case ChoiceKind.Play:
                        return Play(game, codes, choice.Seat, at);
                    default:
                        throw Error(at, $"can't read an answer to a {choice.Kind} choice");
                }
            }
            catch (Exception e) when (e is FormatException == false && (e is ArgumentException || e is IndexOutOfRangeException || e is OverflowException))
            {
                throw Error(at, $"'{token}' doesn't answer \"{choice.Prompt}\"");
            }
        }

        PlayCard Play(Game game, Codes codes, int seat, int at)
        {
            var match = PlayPattern.Match(_tokens[at]);
            if (!match.Success)
                throw Error(at, $"expected a play (like De1c or ~De1c) or R, not '{_tokens[at]}'");
            var card = codes.Find(game, match.Groups[2].Value);
            return new PlayCard(seat, card.Id, GameState.ToLocal(seat, match.Groups[3].Value[0] - 'a'), faceUp: match.Groups[1].Value.Length == 0);
        }

        IEnumerable<int> PickedCards(Game game, Codes codes, PendingChoice choice, int at)
        {
            // The card to shift is in the shift that comes of it ("Fi3>b"), soon after: after any text that
            // leaving its line set off (a card it uncovered), but before the text this shift is part of ends.
            if (choice.Verb == "Shift")
            {
                var depth = 0;
                for (var i = at; i < _tokens.Count; i++)
                {
                    if (_tokens[i] == "(")
                        depth++;
                    else if (_tokens[i] == ")" && --depth < 0)
                        break;
                    if (depth > 0)
                        continue;
                    var shift = ShiftPattern.Match(_tokens[i]);
                    if (!shift.Success)
                        continue;
                    var card = codes.Find(game, shift.Groups[1].Value);
                    if (choice.ValidCards.Contains(card.Id))
                        return new[] { card.Id };
                }
                throw Error(at, "expected the shift of a card that could be picked");
            }

            // Flips are written in two groups when some go face-up and some face-down.
            var picks = new List<int>();
            for (var i = at; i < _tokens.Count && (i == at || choice.Verb == "Flip" && picks.Count < choice.Min); i++)
            {
                var token = _tokens[i];
                var symbol = token.Length > 0 ? token[0] : ' ';
                if ("-x<?=^v".IndexOf(symbol) < 0)
                    throw Error(i, $"expected the cards picked, not '{token}'");
                picks.AddRange(token.Substring(1).Trim('(', ')').Split(',').Select(name => codes.Find(game, name).Id));
            }
            return picks;
        }

        // The decisions so far, as a notation that stops there (for a game still being played).
        public static bool TryWrite(Game game, out string notation, out string error)
        {
            try
            {
                notation = Write(game);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                notation = null;
                error = e.Message;
                return false;
            }
        }
    }
}
