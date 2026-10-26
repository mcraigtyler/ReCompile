using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Net
{
    public enum SessionStatus
    {
        Offline,
        Hosting,    // room created, waiting for an opponent
        Joining,
        InLobby,
        InMatch,
    }

    public enum FirstPlayerChoice
    {
        Random,
        Host,
        Guest,
    }

    public enum SessionError
    {
        InvalidCode,
        RoomNotFound,
        RoomFull,
        ConnectionFailed,
        OpponentLeft,
        ServicesUnavailable,    // Unity Gaming Services couldn't be reached or aren't set up
    }

    public sealed class LobbyPlayer
    {
        public string DisplayName { get; }
        public bool IsHost { get; }
        public bool IsLocal { get; }
        public bool IsReady { get; }

        public LobbyPlayer(string displayName, bool isHost, bool isLocal, bool isReady)
        {
            DisplayName = displayName;
            IsHost = isHost;
            IsLocal = isLocal;
            IsReady = isReady;
        }
    }

    public sealed class LobbyState
    {
        public string RoomCode { get; }
        public IReadOnlyList<LobbyPlayer> Players { get; }
        public FirstPlayerChoice FirstPlayer { get; }

        public LobbyPlayer Local => Players.FirstOrDefault(p => p.IsLocal);
        public LobbyPlayer Opponent => Players.FirstOrDefault(p => !p.IsLocal);
        public bool IsFull => Players.Count == 2;
        public bool AllReady => IsFull && Players.All(p => p.IsReady);

        public LobbyState(string roomCode, IReadOnlyList<LobbyPlayer> players, FirstPlayerChoice firstPlayer)
        {
            RoomCode = roomCode;
            Players = players;
            FirstPlayer = firstPlayer;
        }
    }

    // Seats: the host is seat 0 and the guest is seat 1.
    public sealed class MatchStart
    {
        public int LocalSeat { get; }
        public int FirstSeat { get; }
        public int Seed { get; }
        public string LocalName { get; }
        public string OpponentName { get; }

        public MatchStart(int localSeat, int firstSeat, int seed, string localName, string opponentName)
        {
            LocalSeat = localSeat;
            FirstSeat = firstSeat;
            Seed = seed;
            LocalName = localName;
            OpponentName = opponentName;
        }
    }

    // Finding an opponent, agreeing to start, and (online) the match itself. Implemented by Unity Gaming
    // Services (UgsSessionService) and by an offline stand-in (FakeSessionService) for tests. All callbacks
    // arrive on the main thread.
    public interface ISessionService
    {
        SessionStatus Status { get; }
        bool IsHost { get; }
        LobbyState Lobby { get; }       // null unless Hosting, InLobby, or InMatch

        // The online match once it has started; null when the match runs locally (the stand-in's).
        Compile.Engine.Views.IMatch Match { get; }

        // The connection to the host was lost during a match, and joining the room again may work.
        bool CanRejoin { get; }
        void Rejoin();

        // Called every frame.
        void Tick(float deltaTime);

        event Action Changed;
        event Action<SessionError> Failed;
        event Action<MatchStart> MatchStarted;

        void Host(string displayName);
        void Join(string roomCode, string displayName);
        void SetReady(bool ready);
        void SetFirstPlayer(FirstPlayerChoice choice);  // host only
        void StartMatch();                              // host only, when everyone is ready
        void Leave();
    }

    public static class RoomCode
    {
        public const int Length = 6;

        // Generated codes skip look-alike characters (0/O, 1/I/L).
        const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        public static string Normalize(string code) =>
            new string((code ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());

        public static bool IsValid(string code)
        {
            var normalized = Normalize(code);
            return normalized.Length == Length && normalized.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));
        }

        public static string Generate(Random random) =>
            new string(Enumerable.Range(0, Length).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray());
    }
}
