using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Cards;
using Compile.Engine.Views;
using Compile.Net;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // An online match without a network: a host and two screens joined by LoopbackLinks, so every move and
    // update goes through the same messages it would online.
    public class MatchProtocolTests
    {
        sealed class Online
        {
            public readonly Game Game;
            public readonly MatchHost Host;
            public readonly RemoteMatch[] Screens = new RemoteMatch[2];
            public readonly LoopbackLink[] HostEnds = new LoopbackLink[2];
            public readonly LoopbackLink[] ScreenEnds = new LoopbackLink[2];
            public readonly List<string>[] Rejections = { new List<string>(), new List<string>() };

            public Online(int seed, bool allowDebug = false)
            {
                Game = Game.Create(Scenario.Catalog, Scenario.Catalog.Protocols.ToList(), seed % 2, seed, "Ann", "Bob", effects: MainSet1.Effects, record: true);
                Host = new MatchHost(Game, allowDebug);
                for (var seat = 0; seat < 2; seat++)
                    ConnectScreen(seat);
                Pump();
            }

            public void ConnectScreen(int seat)
            {
                var (screen, host) = LoopbackLink.Pair();
                ScreenEnds[seat] = screen;
                HostEnds[seat] = host;
                Screens[seat] = new RemoteMatch(screen, seat);
                var s = seat;
                Screens[seat].Rejected += reason => Rejections[s].Add(reason);
                Host.Connect(seat, host);
            }

            // Screens send, the host answers, the screens take the answers in.
            public void Pump()
            {
                Host.Pump();
                foreach (var screen in Screens)
                    screen.Tick(0f);
            }
        }

        static string Json(PlayerView view) => new MatchMessage { Kind = "view", UpdateData = new MatchUpdate { View = view } }.ToJson();

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void WholeGamesPlayedThroughMessages_KeepBothScreensInStep(int seed)
        {
            var online = new Online(seed);
            var game = online.Game;
            var random = new Random(seed);
            for (var seat = 0; seat < 2; seat++)
                Assert.That(Json(online.Screens[seat].View), Is.EqualTo(Json(game.ViewFor(seat))), "the first view");

            for (var step = 0; step < 5000 && game.State.Stage != GameStage.Over; step++)
            {
                var seat = RandomBot.SeatToMove(game);
                var intent = RandomBot.Choose(game, random);
                Assert.That(online.Screens[seat].Submit(intent), Is.Null, $"step {step}");
                online.Pump();
                Assert.That(online.Rejections[seat], Is.Empty, $"step {step}: {GameRecord.Format(intent)}");
                for (var s = 0; s < 2; s++)
                    Assert.That(Json(online.Screens[s].View), Is.EqualTo(Json(game.ViewFor(s))), $"step {step}: seat {s}'s screen");
            }

            Assert.That(game.State.Stage, Is.EqualTo(GameStage.Over));
            foreach (var screen in online.Screens)
            {
                Assert.That(screen.View.Stage, Is.EqualTo(GameStage.Over));
                // Both players get the whole game at the end, and it replays to the same finish.
                var replayed = GameNotation.Read(screen.Notation).Replay(Scenario.Catalog, MainSet1.Effects);
                Assert.That(replayed.State.Winner, Is.EqualTo(game.State.Winner));
                Assert.That(replayed.AcceptedIntents, Has.Count.EqualTo(game.AcceptedIntents.Count));
            }

            // Every message fits comfortably in one network message.
            var longest = online.HostEnds.Concat(online.ScreenEnds).SelectMany(l => l.Sent).Max(m => m.Length);
            TestContext.WriteLine($"longest message: {longest} characters");
            Assert.That(longest, Is.LessThan(NetcodeLink.MaxMessageBytes / 2));
        }

        [Test]
        public void TheGuestCantMoveForTheHost()
        {
            var online = new Online(1);
            var game = online.Game;
            var hostSeat = RandomBot.SeatToMove(game);
            var guestSeat = 1 - hostSeat;
            var intent = RandomBot.Choose(game, new Random(1));
            Assert.That(online.Screens[guestSeat].Submit(intent), Is.Not.Null, "the screen won't send it");

            // Sent by hand, the host refuses it.
            online.ScreenEnds[guestSeat].Send(new MatchMessage { Kind = MatchMessage.Move, Text = GameRecord.Format(intent) }.ToJson());
            online.Pump();
            Assert.That(online.Rejections[guestSeat], Has.Count.EqualTo(1));
            Assert.That(game.AcceptedIntents, Is.Empty);
        }

        [TestCase("not json")]
        [TestCase("{\"v\":1,\"k\":\"move\",\"text\":\"p1 play\"}")]
        [TestCase("{\"v\":1,\"k\":\"move\",\"text\":\"p1 fly away\"}")]
        [TestCase("{\"v\":1,\"k\":\"move\",\"text\":\"p1 play x y z\"}")]
        [TestCase("{\"v\":1,\"k\":\"move\",\"text\":\"p1 play 99999999999 0 up\"}")]
        [TestCase("{\"v\":1,\"k\":\"move\",\"$type\":\"System.IO.FileInfo, mscorlib\",\"text\":\"p1 end\"}")]
        [TestCase("{\"v\":1,\"k\":\"nonsense\"}")]
        public void BadMessagesAreRefused_AndTheGameGoesOn(string message)
        {
            var online = new Online(2);
            online.ScreenEnds[1].Send(message);
            online.Pump();
            Assert.That(online.Game.AcceptedIntents, Is.Empty);
            Assert.That(online.Screens[1].HasFailed, Is.False);
            Assert.That(online.Host.Failure, Is.Null);
        }

        [Test]
        public void AnotherProtocolVersion_StopsThatPlayer()
        {
            var online = new Online(2);
            online.ScreenEnds[1].Send("{\"v\":999,\"k\":\"move\",\"text\":\"p1 end\"}");
            online.Pump();
            Assert.That(online.Screens[1].HasFailed, Is.True);
            Assert.That(online.Screens[1].Failure, Does.Contain("different version"));
        }

        [Test]
        public void DebugEffects_OnlyWhenTheHostAllowsThem()
        {
            var online = new Online(3);
            var game = online.Game;
            var random = new Random(3);
            while (game.State.Stage == GameStage.Draft)
            {
                online.Screens[RandomBot.SeatToMove(game)].Submit(RandomBot.Choose(game, random));
                online.Pump();
            }
            var seat = RandomBot.SeatToMove(game);
            var moves = game.AcceptedIntents.Count;
            online.Screens[seat].Submit(new DebugEffect(seat, "draw-1"));
            online.Pump();
            Assert.That(online.Rejections[seat], Has.Count.EqualTo(1));
            Assert.That(game.AcceptedIntents, Has.Count.EqualTo(moves));
        }

        // A guest who drops and comes back gets the whole view again and plays on.
        [Test]
        public void AReconnectingPlayerGetsTheWholeView()
        {
            var online = new Online(4);
            var game = online.Game;
            var random = new Random(4);
            for (var i = 0; i < 40 && game.State.Stage != GameStage.Over; i++)
            {
                online.Screens[RandomBot.SeatToMove(game)].Submit(RandomBot.Choose(game, random));
                online.Pump();
            }

            online.Host.Disconnect(1);
            for (var i = 0; i < 40 && game.State.Stage != GameStage.Over && RandomBot.SeatToMove(game) == 0; i++)
            {
                online.Screens[0].Submit(RandomBot.Choose(game, random));
                online.Pump();
            }

            online.ConnectScreen(1);
            online.Pump();
            Assert.That(Json(online.Screens[1].View), Is.EqualTo(Json(game.ViewFor(1))));
        }
    }
}
