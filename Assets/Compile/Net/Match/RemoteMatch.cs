using System;
using Compile.Engine;
using Compile.Engine.Views;

namespace Compile.Net
{
    // An online match as one player's screen sees it: only the views and events the host sends, and moves
    // sent to the host as text. Both players use this, the host's own screen over a LoopbackLink, so both go
    // through the same path. Updates are handled in Tick, on the main thread.
    public sealed class RemoteMatch : IMatch
    {
        IMatchLink _link;
        int _version = -1;

        public RemoteMatch(IMatchLink link, int seat)
        {
            Viewer = seat;
            Relink(link);
        }

        // A new connection to the host (after rejoining): the failure is over, and the host sends the whole
        // view again.
        public void Relink(IMatchLink link)
        {
            if (_link != null)
                _link.Received -= OnReceived;
            _link = link;
            _link.Received += OnReceived;
            _version = -1;
            Failure = null;
        }

        public string Waiting { get; set; }

        public int Viewer { get; }
        public PlayerView View { get; private set; }

        // The first view has arrived, so the screens have something to show.
        public bool HasView => View != null;

        public bool Hotseat => false;
        public SeatKind KindOf(int seat) => seat == Viewer ? SeatKind.Human : SeatKind.Remote;

        public string NameOf(int seat)
        {
            if (View == null)
                return seat == Viewer ? "You" : "Opponent";
            return seat == Viewer ? View.Local.Name : View.Opponent.Name;
        }

        public int SeatToMove => -1;
        public bool NeedsHandoff => false;
        public void HandTo(int seat) { }

        // The host's own screen has the engine (for game logs and debug tools); a guest doesn't.
        public Game LocalGame { get; set; }

        // The whole game's notation, once it's over (the host sends it then). Replaying it gives the full log.
        public string Notation { get; private set; }

        public event Action<MatchUpdate> Changed;
        public event Action<string> Rejected;
        public event Action<string> Failed;
        public event Action LogArrived;

        public string Failure { get; private set; }
        public bool HasFailed => Failure != null;

        public string Submit(Intent intent)
        {
            if (Failure != null)
                return Failure;
            if (View == null)
                return "Waiting for the host.";
            if (intent.Seat != Viewer)
                return "You can only make your own moves.";
            _link.Send(new MatchMessage { Kind = MatchMessage.Move, Text = GameRecord.Format(intent) }.ToJson());
            return null;
        }

        public void Tick(float deltaTime) => _link?.Pump();

        // The connection was lost and can't come back (the host left).
        public void Fail(string reason)
        {
            if (Failure != null)
                return;
            Failure = reason;
            Failed?.Invoke(reason);
        }

        void OnReceived(string text)
        {
            var message = MatchMessage.FromJson(text);
            if (message == null)
                return;         // not a message at all: ignored
            if (message.Version != MatchMessage.ProtocolVersion)
            {
                Fail("The host is running a different version of the game. Both players need to reload the page.");
                return;
            }
            switch (message.Kind)
            {
                case MatchMessage.Update when message.UpdateData?.View != null:
                    // Updates come in order; an older one (a snapshot that crossed a move) is already out of date.
                    if (message.UpdateData.Version < _version)
                        return;
                    _version = message.UpdateData.Version;
                    View = message.UpdateData.View;
                    Changed?.Invoke(message.UpdateData);
                    break;
                case MatchMessage.Rejected:
                    Rejected?.Invoke(message.Text);
                    break;
                case MatchMessage.Failed:
                    Fail(message.Text);
                    break;
                case MatchMessage.GameLog:
                    Notation = message.Notation;
                    LogArrived?.Invoke();
                    break;
            }
        }
    }
}
