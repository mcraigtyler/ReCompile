using System;
using System.Collections.Generic;
using Compile.Engine;
using Compile.Engine.Views;

namespace Compile.Net
{
    // The host side of an online match: it runs the engine and is the only place the whole game exists.
    // Each seat has a link (the host's own screen over a LoopbackLink, the guest over the network). Moves
    // come in as text, are checked by the engine, and every player then gets their own update: their view
    // and the events, redacted for them. A seat can only move for itself.
    public sealed class MatchHost
    {
        readonly IMatchLink[] _links = new IMatchLink[GameState.SeatCount];
        readonly Action<string>[] _handlers = new Action<string>[GameState.SeatCount];
        readonly bool _allowDebug;
        int _version;
        bool _logSent;

        public Game Game { get; }

        // Why the match stopped (the engine threw), or null.
        public string Failure { get; private set; }
        public event Action<string> Failed;

        // After every accepted move, with the seat that made it (for the session's bookkeeping).
        public event Action<int> Moved;

        public MatchHost(Game game, bool allowDebug)
        {
            Game = game;
            _allowDebug = allowDebug;
        }

        public bool IsConnected(int seat) => _links[seat] != null;

        // A player's screen is connected (again, after a disconnect): it gets the whole view at once.
        public void Connect(int seat, IMatchLink link)
        {
            Disconnect(seat);
            _links[seat] = link;
            _handlers[seat] = text => OnReceived(seat, text);
            link.Received += _handlers[seat];
            SendSnapshot(seat);
            if (_logSent)
                SendLog(seat);
        }

        public void Disconnect(int seat)
        {
            if (_links[seat] == null)
                return;
            _links[seat].Received -= _handlers[seat];
            _links[seat] = null;
            _handlers[seat] = null;
        }

        // Handles what has arrived from both players.
        public void Pump()
        {
            for (var seat = 0; seat < _links.Length; seat++)
                _links[seat]?.Pump();
        }

        // A move made for a player by the host itself (a forfeit after a disconnect).
        public void SubmitFor(Intent intent) => Apply(intent.Seat, intent);

        void OnReceived(int seat, string text)
        {
            var message = MatchMessage.FromJson(text);
            if (message == null)
                return;         // not a message at all: ignored
            if (message.Version != MatchMessage.ProtocolVersion)
            {
                Send(seat, new MatchMessage { Kind = MatchMessage.Failed, Text = "The other player is running a different version of the game. Both players need to reload the page." });
                return;
            }
            switch (message.Kind)
            {
                case MatchMessage.Move:
                    OnMove(seat, message.Text);
                    break;
                case MatchMessage.Resync:
                    SendSnapshot(seat);
                    break;
            }
        }

        void OnMove(int seat, string text)
        {
            Intent intent;
            try
            {
                intent = GameRecord.ParseMove(text);
            }
            catch (FormatException)
            {
                Reject(seat, "That move couldn't be read.");
                return;
            }
            if (intent.Seat != seat)
            {
                Reject(seat, "You can only make your own moves.");
                return;
            }
            if (intent is DebugEffect && !_allowDebug)
            {
                Reject(seat, "Debug effects are only for development builds.");
                return;
            }
            Apply(seat, intent);
        }

        void Apply(int seat, Intent intent)
        {
            if (Failure != null)
            {
                Reject(seat, "The game stopped after an error.");
                return;
            }
            IntentResult result;
            try
            {
                result = Game.Submit(intent);
            }
            catch (Exception e)
            {
                Failure = e.Message;
                for (var s = 0; s < _links.Length; s++)
                    Send(s, new MatchMessage { Kind = MatchMessage.Failed, Text = "Something went wrong on the host, and the game can't continue." });
                Failed?.Invoke(e.ToString());
                return;
            }
            if (!result.Accepted)
            {
                Reject(seat, result.Error);
                return;
            }

            _version++;
            for (var s = 0; s < _links.Length; s++)
                Send(s, new MatchMessage { Kind = MatchMessage.Update, UpdateData = Game.UpdateFor(s, result.Events, _version) });
            if (Game.State.Stage == GameStage.Over && !_logSent)
            {
                _logSent = true;
                for (var s = 0; s < _links.Length; s++)
                    SendLog(s);
            }
            Moved?.Invoke(seat);
        }

        void SendSnapshot(int seat) =>
            Send(seat, new MatchMessage { Kind = MatchMessage.Update, UpdateData = Game.UpdateFor(seat, Array.Empty<GameEvent>(), _version) });

        // Once the game is over nothing is hidden any more, so both players get the whole game: its notation,
        // which is short and replays into the full log.
        void SendLog(int seat)
        {
            if (GameNotation.TryWrite(Game, out var notation, out _))
                Send(seat, new MatchMessage { Kind = MatchMessage.GameLog, Notation = notation });
        }

        void Reject(int seat, string reason) => Send(seat, new MatchMessage { Kind = MatchMessage.Rejected, Text = reason });

        void Send(int seat, MatchMessage message) => _links[seat]?.Send(message.ToJson());
    }
}
