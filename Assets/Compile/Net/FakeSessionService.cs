using System;
using System.Collections.Generic;

namespace Compile.Net
{
    // Offline stand-in for the real session service. It simulates an opponent with short delays so
    // every screen and error state can be exercised without a network. Call Advance every frame.
    //
    // Joining: codes starting with X fail with "Room not found", codes starting with F fail with
    // "Room is full", and any other valid code joins a fake host.
    public sealed class FakeSessionService : ISessionService
    {
        public const float OpponentJoinDelay = 3f;
        public const float JoinDelay = 1.5f;
        public const float OpponentReadyDelay = 2f;
        public const float HostStartDelay = 1.5f;

        static readonly string[] OpponentNames = { "Daemon_7", "NullPointer", "Kernel.Panic", "HAL-0", "Recursion", "Overflow" };

        readonly Random _random;
        readonly List<(float at, Action action)> _scheduled = new List<(float, Action)>();
        float _time;

        string _localName;
        string _opponentName;
        bool _localReady;
        bool _opponentReady;
        FirstPlayerChoice _firstPlayer;

        public SessionStatus Status { get; private set; }
        public bool IsHost { get; private set; }
        public LobbyState Lobby { get; private set; }

        public event Action Changed;
        public event Action<SessionError> Failed;
        public event Action<MatchStart> MatchStarted;

        public FakeSessionService(int seed = 0) => _random = seed == 0 ? new Random() : new Random(seed);

        // Runs due events in time order; events they schedule are timed from when they fired.
        public void Advance(float deltaTime)
        {
            var target = _time + deltaTime;
            while (true)
            {
                var next = -1;
                for (var i = 0; i < _scheduled.Count; i++)
                    if (_scheduled[i].at <= target && (next < 0 || _scheduled[i].at < _scheduled[next].at))
                        next = i;
                if (next < 0)
                    break;

                var (at, action) = _scheduled[next];
                _scheduled.RemoveAt(next);
                _time = at;
                action();
            }
            _time = target;
        }

        public void Host(string displayName)
        {
            Reset();
            IsHost = true;
            _localName = displayName;
            Status = SessionStatus.Hosting;
            var code = RoomCode.Generate(_random);
            Lobby = new LobbyState(code, new[] { new LobbyPlayer(_localName, true, true, false) }, _firstPlayer);
            Changed?.Invoke();

            Schedule(OpponentJoinDelay, () =>
            {
                _opponentName = PickOpponentName();
                Status = SessionStatus.InLobby;
                Publish(code);
                Schedule(OpponentReadyDelay, () => { _opponentReady = true; Publish(code); });
            });
        }

        public void Join(string roomCode, string displayName)
        {
            Reset();
            if (!RoomCode.IsValid(roomCode))
            {
                Failed?.Invoke(SessionError.InvalidCode);
                return;
            }

            var code = RoomCode.Normalize(roomCode);
            IsHost = false;
            _localName = displayName;
            Status = SessionStatus.Joining;
            Changed?.Invoke();

            Schedule(JoinDelay, () =>
            {
                if (code[0] == 'X' || code[0] == 'F')
                {
                    Status = SessionStatus.Offline;
                    Changed?.Invoke();
                    Failed?.Invoke(code[0] == 'X' ? SessionError.RoomNotFound : SessionError.RoomFull);
                    return;
                }

                _opponentName = PickOpponentName();
                _opponentReady = true;
                _firstPlayer = (FirstPlayerChoice)_random.Next(3);
                Status = SessionStatus.InLobby;
                Publish(code);
            });
        }

        public void SetReady(bool ready)
        {
            if (Status != SessionStatus.InLobby)
                return;

            _localReady = ready;
            Publish(Lobby.RoomCode);

            // The fake host starts the match once the local guest is ready.
            if (!IsHost && ready)
                Schedule(HostStartDelay, () =>
                {
                    if (Status == SessionStatus.InLobby && _localReady)
                        Begin();
                });
        }

        public void SetFirstPlayer(FirstPlayerChoice choice)
        {
            if (!IsHost || Lobby == null)
                return;

            _firstPlayer = choice;
            Publish(Lobby.RoomCode);
        }

        public void StartMatch()
        {
            if (IsHost && Status == SessionStatus.InLobby && Lobby.AllReady)
                Begin();
        }

        public void Leave()
        {
            Reset();
            Changed?.Invoke();
        }

        // Debug: makes the opponent disconnect.
        public void SimulateOpponentLeft()
        {
            if (Status != SessionStatus.InLobby && Status != SessionStatus.InMatch)
                return;

            Reset();
            Changed?.Invoke();
            Failed?.Invoke(SessionError.OpponentLeft);
        }

        void Begin()
        {
            var firstSeat = _firstPlayer switch
            {
                FirstPlayerChoice.Host => 0,
                FirstPlayerChoice.Guest => 1,
                _ => _random.Next(2),
            };

            Status = SessionStatus.InMatch;
            Changed?.Invoke();
            MatchStarted?.Invoke(new MatchStart(IsHost ? 0 : 1, firstSeat, _random.Next(1, int.MaxValue), _localName, _opponentName));
        }

        void Publish(string code)
        {
            var local = new LobbyPlayer(_localName, IsHost, true, _localReady);
            var players = _opponentName == null
                ? new[] { local }
                : IsHost
                    ? new[] { local, new LobbyPlayer(_opponentName, false, false, _opponentReady) }
                    : new[] { new LobbyPlayer(_opponentName, true, false, _opponentReady), local };
            Lobby = new LobbyState(code, players, _firstPlayer);
            Changed?.Invoke();
        }

        void Reset()
        {
            _scheduled.Clear();
            Status = SessionStatus.Offline;
            IsHost = false;
            Lobby = null;
            _opponentName = null;
            _localReady = false;
            _opponentReady = false;
            _firstPlayer = FirstPlayerChoice.Random;
        }

        void Schedule(float delay, Action action) => _scheduled.Add((_time + delay, action));

        string PickOpponentName() => OpponentNames[_random.Next(OpponentNames.Length)];
    }
}
