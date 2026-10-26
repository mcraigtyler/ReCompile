using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Compile.Engine;
using Compile.Engine.Views;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using Services = Unity.Services.Multiplayer;

namespace Compile.Net
{
    // Online play through Unity Gaming Services. Both players sign in anonymously; the host creates a
    // private Session (2 players, Relay over secure WebSockets on every platform, so the Editor and browsers
    // can play each other) whose join code is the room code; the guest joins it by code. Sessions start
    // Netcode for GameObjects as host and client, and everything after that (the lobby, then the match) is
    // MatchMessages over named messages.
    //
    // The host runs the match (MatchHost); both players' screens use a RemoteMatch, the host's over a
    // LoopbackLink. A guest who drops can rejoin with the same code within ForfeitSeconds; after that the
    // host wins by forfeit. If the host leaves, the match is over.
    public sealed class UgsSessionService : ISessionService
    {
        public const float ForfeitSeconds = 120f;
        const float LeaveDelay = 0.5f;          // time for a last message (a concession) to go out
        const int HostSeat = 0;
        const int GuestSeat = 1;
        const ulong NoClient = ulong.MaxValue;

        readonly Func<MatchStart, Game> _createGame;
        readonly bool _allowDebug;
        readonly System.Random _random = new System.Random();

        NetworkManager _network;
        ISession _session;
        string _sessionId;
        string _localName;
        string _code;
        int _attempt;                   // bumped by Leave, so late replies to an old request are ignored

        // Host
        ulong _guestClient = NoClient;
        NetcodeLink _guestLink;
        string _guestName;
        string _guestPlayerId;
        bool _hostReady, _guestReady;
        FirstPlayerChoice _firstPlayer;
        MatchHost _host;
        float _guestGoneFor = -1f;

        // Guest
        NetcodeLink _hostLink;
        bool _rejoinable;

        // Both
        RemoteMatch _match;
        MatchStart _start;
        bool _startRaised;
        readonly List<(float at, Action action)> _later = new List<(float, Action)>();
        float _time;

        public SessionStatus Status { get; private set; }
        public bool IsHost { get; private set; }
        public LobbyState Lobby { get; private set; }
        public IMatch Match => _match;
        public bool CanRejoin => !IsHost && _rejoinable && _code != null && _match != null && _match.HasFailed;

        public event Action Changed;
        public event Action<SessionError> Failed;
        public event Action<MatchStart> MatchStarted;

        // createGame makes the engine for a new match (the host's only), from the start with the host at seat 0.
        public UgsSessionService(Func<MatchStart, Game> createGame, bool allowDebug)
        {
            _createGame = createGame;
            _allowDebug = allowDebug;
        }

        // ---------- Hosting and joining ----------

        public async void Host(string displayName)
        {
            var attempt = Restart();
            IsHost = true;
            _localName = displayName;
            Status = SessionStatus.Hosting;
            Changed?.Invoke();
            try
            {
                await SignIn();
                EnsureNetwork();
                var options = new SessionOptions { MaxPlayers = 2, IsPrivate = true }
                    .WithRelayNetwork(new RelayNetworkOptions(RelayProtocol.WSS));
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                if (attempt != _attempt)
                {
                    await LeaveQuietly(session);
                    return;
                }
                Attach(session);
                PublishLobby();
            }
            catch (Exception e)
            {
                if (attempt == _attempt)
                    Fail(e, SessionError.ConnectionFailed);
            }
        }

        public async void Join(string roomCode, string displayName)
        {
            var attempt = Restart();
            if (!RoomCode.IsValid(roomCode))
            {
                Failed?.Invoke(SessionError.InvalidCode);
                return;
            }
            IsHost = false;
            _localName = displayName;
            _code = RoomCode.Normalize(roomCode);
            Status = SessionStatus.Joining;
            Changed?.Invoke();
            try
            {
                await SignIn();
                EnsureNetwork();
                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(_code);
                if (attempt != _attempt)
                {
                    await LeaveQuietly(session);
                    return;
                }
                Attach(session);
                SayHello();
            }
            catch (Exception e)
            {
                if (attempt == _attempt)
                    Fail(e, JoinError(e));
            }
        }

        // The connection to the host was lost mid-match: join the same room again. The host recognizes
        // the player and sends the match again.
        public async void Rejoin()
        {
            if (!CanRejoin)
                return;
            var attempt = ++_attempt;
            _rejoinable = false;
            CloseNetwork(leaveSession: false);
            try
            {
                await SignIn();
                EnsureNetwork();
                ISession session;
                try
                {
                    session = await MultiplayerService.Instance.ReconnectToSessionAsync(_sessionId);
                }
                catch (SessionException)
                {
                    session = await MultiplayerService.Instance.JoinSessionByCodeAsync(_code);
                }
                if (attempt != _attempt)
                {
                    await LeaveQuietly(session);
                    return;
                }
                Attach(session);
                SayHello();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (attempt == _attempt)
                    _match.Fail("Couldn't reconnect. The host may have left the game.");
            }
        }

        public void SetReady(bool ready)
        {
            if (Status != SessionStatus.InLobby)
                return;
            if (IsHost)
            {
                _hostReady = ready;
                PublishLobby();
            }
            else
                _hostLink?.Send(new MatchMessage { Kind = MatchMessage.Ready, Flag = ready }.ToJson());
        }

        public void SetFirstPlayer(FirstPlayerChoice choice)
        {
            if (!IsHost || Status != SessionStatus.InLobby)
                return;
            _firstPlayer = choice;
            PublishLobby();
        }

        public void StartMatch()
        {
            if (!IsHost || Status != SessionStatus.InLobby || !_hostReady || !_guestReady || _guestLink == null)
                return;

            var firstSeat = _firstPlayer switch
            {
                FirstPlayerChoice.Host => HostSeat,
                FirstPlayerChoice.Guest => GuestSeat,
                _ => _random.Next(2),
            };
            _start = new MatchStart(HostSeat, firstSeat, _random.Next(1, int.MaxValue), _localName, _guestName);
            var game = _createGame(_start);
            _host = new MatchHost(game, _allowDebug);
            _host.Failed += error => Debug.LogError($"The match stopped: {error}");

            var (screen, hostEnd) = LoopbackLink.Pair();
            _match = new RemoteMatch(screen, HostSeat) { LocalGame = game };
            _host.Connect(HostSeat, hostEnd);

            // The guest makes its match on Start, then the host's first update arrives.
            _guestLink.Send(new MatchMessage { Kind = MatchMessage.Start, StartData = StartData() }.ToJson());
            _host.Connect(GuestSeat, _guestLink);

            Status = SessionStatus.InMatch;
            Changed?.Invoke();
        }

        public void Leave()
        {
            // Leaving a match that's still going concedes it, so the other player isn't left waiting.
            if (_match != null && !_match.HasFailed && _match.View != null && _match.View.Stage != GameStage.Over)
            {
                if (IsHost)
                    _host?.SubmitFor(new Concede(HostSeat));
                else
                    _match.Submit(new Concede(GuestSeat));
                _host?.Pump();
            }
            Restart();
            Changed?.Invoke();
        }

        // ---------- Every frame ----------

        public void Tick(float deltaTime)
        {
            _time += deltaTime;
            for (var i = _later.Count - 1; i >= 0; i--)
                if (_later[i].at <= _time)
                {
                    var action = _later[i].action;
                    _later.RemoveAt(i);
                    action();
                }

            _host?.Pump();
            _match?.Tick(deltaTime);

            // The match starts for the screens once there's a view to show.
            if (_match != null && !_startRaised && _match.HasView)
            {
                _startRaised = true;
                MatchStarted?.Invoke(_start);
            }

            if (IsHost && _guestGoneFor >= 0f && _match != null)
            {
                _guestGoneFor += deltaTime;
                var left = Mathf.Max(0, Mathf.CeilToInt(ForfeitSeconds - _guestGoneFor));
                if (_host.Game.State.Stage == GameStage.Over)
                {
                    _guestGoneFor = -1f;
                    _match.Waiting = null;
                }
                else if (left == 0)
                {
                    _guestGoneFor = -1f;
                    _match.Waiting = null;
                    _host.SubmitFor(new Concede(GuestSeat));
                }
                else
                    _match.Waiting = $"{_guestName} lost the connection. Waiting for them to come back: {left / 60}:{left % 60:00}. After that you win by forfeit.";
            }
        }

        // ---------- Messages ----------

        void OnNamedMessage(ulong sender, FastBufferReader reader)
        {
            var text = NetcodeLink.Read(reader);
            if (text == null)
                return;
            if (IsHost)
                FromGuest(sender, text);
            else
                FromHost(text);
        }

        void FromGuest(ulong sender, string text)
        {
            var message = MatchMessage.FromJson(text);
            if (message == null)
                return;

            if (message.Kind == MatchMessage.Hello)
            {
                if (Status == SessionStatus.InMatch)
                {
                    // Only the guest who was playing can come back.
                    if (message.Text != _guestPlayerId || _guestClient != NoClient)
                        return;
                    _guestClient = sender;
                    _guestLink = new NetcodeLink(_network, sender);
                    _guestLink.Send(new MatchMessage { Kind = MatchMessage.Start, StartData = StartData() }.ToJson());
                    _host.Connect(GuestSeat, _guestLink);
                    _guestGoneFor = -1f;
                    _match.Waiting = null;
                    return;
                }
                if (_guestClient != NoClient && _guestClient != sender)
                    return;
                _guestClient = sender;
                _guestLink = new NetcodeLink(_network, sender);
                _guestName = string.IsNullOrWhiteSpace(message.Name) ? "Guest" : message.Name.Trim();
                _guestPlayerId = message.Text;
                _guestReady = false;
                Status = SessionStatus.InLobby;
                PublishLobby();
                return;
            }

            if (sender != _guestClient)
                return;
            if (message.Kind == MatchMessage.Ready)
            {
                _guestReady = message.Flag;
                PublishLobby();
                return;
            }
            _guestLink.Deliver(text);
        }

        void FromHost(string text)
        {
            var message = MatchMessage.FromJson(text);
            if (message == null)
                return;
            switch (message.Kind)
            {
                case MatchMessage.Lobby when message.LobbyData != null:
                    ShowLobby(message.LobbyData);
                    break;
                case MatchMessage.Start when message.StartData != null:
                    var s = message.StartData;
                    _start = new MatchStart(GuestSeat, s.FirstSeat, s.Seed, s.GuestName, s.HostName);
                    if (_match == null)
                        _match = new RemoteMatch(_hostLink, GuestSeat);
                    else
                        _match.Relink(_hostLink);       // back after a lost connection: the screens carry on
                    Status = SessionStatus.InMatch;
                    Changed?.Invoke();
                    break;
                default:
                    _hostLink.Deliver(text);
                    break;
            }
        }

        void SayHello() =>
            _hostLink.Send(new MatchMessage { Kind = MatchMessage.Hello, Name = _localName, Text = AuthenticationService.Instance.PlayerId }.ToJson());

        StartMessage StartData() => new StartMessage { FirstSeat = _start.FirstSeat, Seed = _start.Seed, HostName = _localName, GuestName = _guestName };

        // The host keeps the lobby and sends it to the guest whenever it changes.
        void PublishLobby()
        {
            var players = new List<LobbyPlayer> { new LobbyPlayer(_localName, true, true, _hostReady) };
            if (_guestLink != null)
                players.Add(new LobbyPlayer(_guestName, false, false, _guestReady));
            Lobby = new LobbyState(_code, players, _firstPlayer);
            Changed?.Invoke();

            _guestLink?.Send(new MatchMessage
            {
                Kind = MatchMessage.Lobby,
                LobbyData = new LobbyMessage
                {
                    HostName = _localName, GuestName = _guestName, HostReady = _hostReady, GuestReady = _guestReady, FirstPlayer = _firstPlayer,
                },
            }.ToJson());
        }

        void ShowLobby(LobbyMessage lobby)
        {
            if (Status == SessionStatus.InMatch)
                return;
            Lobby = new LobbyState(_code, new[]
            {
                new LobbyPlayer(lobby.HostName, true, false, lobby.HostReady),
                new LobbyPlayer(_localName, false, true, lobby.GuestReady),
            }, lobby.FirstPlayer);
            Status = SessionStatus.InLobby;
            Changed?.Invoke();
        }

        // ---------- Connections ----------

        void OnClientDisconnected(ulong client)
        {
            if (IsHost)
            {
                if (client != _guestClient)
                    return;
                _guestClient = NoClient;
                if (Status == SessionStatus.InMatch)
                {
                    _host.Disconnect(GuestSeat);
                    _guestLink = null;
                    if (_host.Game.State.Stage != GameStage.Over)
                        _guestGoneFor = 0f;
                    return;
                }
                // In the lobby: the room goes back to waiting for an opponent.
                _guestLink = null;
                _guestName = null;
                _guestReady = false;
                Status = SessionStatus.Hosting;
                PublishLobby();
                return;
            }

            // The guest lost the host (or its own connection).
            if (Status == SessionStatus.InMatch && _match != null)
            {
                var over = _match.View != null && _match.View.Stage == GameStage.Over;
                if (!over)
                {
                    _rejoinable = true;
                    _match.Fail("The connection to the host was lost.");
                }
                return;
            }
            var attempt = Restart();
            Changed?.Invoke();
            if (attempt == _attempt)
                Failed?.Invoke(SessionError.OpponentLeft);
        }

        void OnRemovedFromSession()
        {
            if (IsHost)
                return;
            if (Status == SessionStatus.InMatch && _match != null)
            {
                if (_match.View == null || _match.View.Stage != GameStage.Over)
                {
                    _rejoinable = false;
                    _match.Fail("The host left the game.");
                }
                return;
            }
            Restart();
            Changed?.Invoke();
            Failed?.Invoke(SessionError.OpponentLeft);
        }

        void Attach(ISession session)
        {
            _session = session;
            _sessionId = session.Id;
            _code = session.Code ?? _code;
            _session.RemovedFromSession += OnRemovedFromSession;
            _session.Deleted += OnRemovedFromSession;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            _network.CustomMessagingManager.RegisterNamedMessageHandler(NetcodeLink.Channel, OnNamedMessage);
            if (!IsHost)
                _hostLink = new NetcodeLink(_network, NetworkManager.ServerClientId);
        }

        // The network object, made once: the scene has nothing in it to begin with.
        void EnsureNetwork()
        {
            if (_network != null)
                return;
            var go = new GameObject("Network");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetcodeLink.MaxMessageBytes;
            transport.MaxSendQueueSize = 4 * NetcodeLink.MaxMessageBytes;
            _network = go.AddComponent<NetworkManager>();
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
            };
        }

        static async Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // Each Editor instance (Multiplayer Play Mode runs several) signs in as a player of its own.
                options.SetProfile("editor" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static SessionError JoinError(Exception e)
        {
            var text = e.Message ?? "";
            if (e is SessionException se && se.Error == Services.SessionError.SessionNotFound || text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                return SessionError.RoomNotFound;
            if (text.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0)
                return SessionError.RoomFull;
            return e is ServicesInitializationException || e is AuthenticationException ? SessionError.ServicesUnavailable : SessionError.ConnectionFailed;
        }

        void Fail(Exception e, SessionError error)
        {
            Debug.LogException(e);
            Restart();
            Changed?.Invoke();
            Failed?.Invoke(error is SessionError.ConnectionFailed && (e is ServicesInitializationException || e is AuthenticationException)
                ? SessionError.ServicesUnavailable : error);
        }

        // Back to nothing: leaves the session and stops the network (after a moment, so a last message
        // can go out). Returns the new attempt number.
        int Restart()
        {
            var attempt = ++_attempt;
            CloseNetwork();
            Status = SessionStatus.Offline;
            IsHost = false;
            Lobby = null;
            _code = null;
            _sessionId = null;
            _guestClient = NoClient;
            _guestLink = null;
            _guestName = null;
            _guestPlayerId = null;
            _hostReady = _guestReady = false;
            _firstPlayer = FirstPlayerChoice.Random;
            _host = null;
            _guestGoneFor = -1f;
            _hostLink = null;
            _rejoinable = false;
            _match = null;
            _start = null;
            _startRaised = false;
            return attempt;
        }

        // Stops listening; leaves the session too unless rejoining it (leaving would drop the player for good).
        void CloseNetwork(bool leaveSession = true)
        {
            var session = _session;
            var network = _network;
            _session = null;
            if (network != null)
            {
                network.OnClientDisconnectCallback -= OnClientDisconnected;
                if (network.CustomMessagingManager != null)
                    network.CustomMessagingManager.UnregisterNamedMessageHandler(NetcodeLink.Channel);
            }
            if (session != null)
            {
                session.RemovedFromSession -= OnRemovedFromSession;
                session.Deleted -= OnRemovedFromSession;
            }
            if (session != null && leaveSession)
            {
                _later.Add((_time + LeaveDelay, async () =>
                {
                    await LeaveQuietly(session);
                    if (network != null && network.IsListening)
                        network.Shutdown();
                }));
            }
            else if (network != null && network.IsListening)
                network.Shutdown();
        }

        static async Task LeaveQuietly(ISession session)
        {
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Leaving the session: {e.Message}");
            }
        }
    }
}
