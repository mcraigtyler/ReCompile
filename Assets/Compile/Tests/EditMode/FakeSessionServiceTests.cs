using Compile.Net;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class FakeSessionServiceTests
    {
        [Test]
        public void HostingWaitsThenAnOpponentJoinsAndReadies()
        {
            var session = new FakeSessionService(seed: 1);
            session.Host("Ada");

            Assert.That(session.Status, Is.EqualTo(SessionStatus.Hosting));
            Assert.That(RoomCode.IsValid(session.Lobby.RoomCode), Is.True);

            session.Advance(FakeSessionService.OpponentJoinDelay);
            Assert.That(session.Status, Is.EqualTo(SessionStatus.InLobby));
            Assert.That(session.Lobby.IsFull, Is.True);
            Assert.That(session.Lobby.Opponent.IsReady, Is.False);

            session.Advance(FakeSessionService.OpponentReadyDelay);
            Assert.That(session.Lobby.Opponent.IsReady, Is.True);
        }

        [Test]
        public void HostStartsWhenBothReadyAndChoosesFirstPlayer()
        {
            var session = new FakeSessionService(seed: 1);
            MatchStart start = null;
            session.MatchStarted += s => start = s;

            session.Host("Ada");
            session.Advance(FakeSessionService.OpponentJoinDelay + FakeSessionService.OpponentReadyDelay);
            session.StartMatch();
            Assert.That(start, Is.Null, "Can't start before the host is ready.");

            session.SetReady(true);
            session.SetFirstPlayer(FirstPlayerChoice.Guest);
            session.StartMatch();

            Assert.That(session.Status, Is.EqualTo(SessionStatus.InMatch));
            Assert.That(start.LocalSeat, Is.EqualTo(0));
            Assert.That(start.FirstSeat, Is.EqualTo(1));
            Assert.That(start.LocalName, Is.EqualTo("Ada"));
        }

        [TestCase("XABCDE", SessionError.RoomNotFound)]
        [TestCase("FABCDE", SessionError.RoomFull)]
        public void JoinErrorsCanBeTriggeredByCode(string code, SessionError expected)
        {
            var session = new FakeSessionService(seed: 1);
            SessionError? error = null;
            session.Failed += e => error = e;

            session.Join(code, "Ada");
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Joining));

            session.Advance(FakeSessionService.JoinDelay);
            Assert.That(error, Is.EqualTo(expected));
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Offline));
        }

        [Test]
        public void InvalidCodeFailsImmediately()
        {
            var session = new FakeSessionService(seed: 1);
            SessionError? error = null;
            session.Failed += e => error = e;

            session.Join("ABC", "Ada");

            Assert.That(error, Is.EqualTo(SessionError.InvalidCode));
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Offline));
        }

        [Test]
        public void GuestMatchStartsAfterReadying()
        {
            var session = new FakeSessionService(seed: 1);
            MatchStart start = null;
            session.MatchStarted += s => start = s;

            session.Join("abc-234", "Ada");
            session.Advance(FakeSessionService.JoinDelay);
            Assert.That(session.Status, Is.EqualTo(SessionStatus.InLobby));
            Assert.That(session.Lobby.RoomCode, Is.EqualTo("ABC234"));

            session.SetReady(true);
            session.Advance(FakeSessionService.HostStartDelay);

            Assert.That(start, Is.Not.Null);
            Assert.That(start.LocalSeat, Is.EqualTo(1));
        }

        [Test]
        public void LeavingCancelsPendingEvents()
        {
            var session = new FakeSessionService(seed: 1);
            session.Host("Ada");
            session.Leave();
            session.Advance(10f);

            Assert.That(session.Status, Is.EqualTo(SessionStatus.Offline));
            Assert.That(session.Lobby, Is.Null);
        }

        [TestCase("abc234", true)]
        [TestCase(" ABC-234 ", true)]
        [TestCase("ABC23", false)]
        [TestCase("ABC2345", false)]
        [TestCase("ABC!34", false)]
        public void ValidatesRoomCodes(string code, bool valid)
        {
            Assert.That(RoomCode.IsValid(code), Is.EqualTo(valid));
        }
    }
}
