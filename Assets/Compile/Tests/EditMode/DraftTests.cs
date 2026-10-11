using System;
using System.Linq;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class DraftTests
    {
        static readonly string[] Pool = { "Darkness", "Death", "Fire", "Gravity", "Life", "Light", "Metal", "Plague" };

        [Test]
        public void PicksGoOneTwoTwoOne()
        {
            var draft = new Draft(Pool, firstSeat: 1);
            var seats = Enumerable.Range(0, 6).Select(_ =>
            {
                var seat = draft.CurrentSeat;
                draft.Pick(draft.Available[0]);
                return seat;
            }).ToArray();

            Assert.That(seats, Is.EqualTo(new[] { 1, 0, 0, 1, 1, 0 }));
            Assert.That(draft.IsComplete, Is.True);
            Assert.That(draft.CurrentSeat, Is.EqualTo(-1));
        }

        [Test]
        public void PicksFillLeftMiddleRightInDraftOrder()
        {
            var draft = new Draft(Pool, firstSeat: 0);
            Assert.That(draft.Pick("Fire"), Is.EqualTo(0));     // first player: left
            Assert.That(draft.Pick("Metal"), Is.EqualTo(0));    // second player: left
            Assert.That(draft.Pick("Death"), Is.EqualTo(1));    // second player: middle
            Assert.That(draft.Pick("Light"), Is.EqualTo(1));    // first player: middle
            Assert.That(draft.Pick("Life"), Is.EqualTo(2));     // first player: right
            Assert.That(draft.Pick("Plague"), Is.EqualTo(2));   // second player: right

            Assert.That(draft.PicksFor(0), Is.EqualTo(new[] { "Fire", "Light", "Life" }));
            Assert.That(draft.PicksFor(1), Is.EqualTo(new[] { "Metal", "Death", "Plague" }));
        }

        [Test]
        public void ReportsPicksRemainingThisTurn()
        {
            var draft = new Draft(Pool, firstSeat: 0);
            Assert.That(draft.PicksRemainingThisTurn, Is.EqualTo(1));
            draft.Pick("Fire");
            Assert.That(draft.PicksRemainingThisTurn, Is.EqualTo(2));
            draft.Pick("Metal");
            Assert.That(draft.PicksRemainingThisTurn, Is.EqualTo(1));
        }

        [Test]
        public void RejectsUnavailableOrRepeatedPicks()
        {
            var draft = new Draft(Pool, firstSeat: 0);
            draft.Pick("Fire");

            Assert.Throws<ArgumentException>(() => draft.Pick("Fire"));
            Assert.Throws<ArgumentException>(() => draft.Pick("Water"));
        }

        [Test]
        public void RejectsPicksAfterCompletion()
        {
            var draft = new Draft(Pool, firstSeat: 0);
            for (var i = 0; i < 6; i++)
                draft.Pick(draft.Available[0]);

            Assert.Throws<InvalidOperationException>(() => draft.Pick(draft.Available[0]));
            Assert.That(draft.Available, Has.Count.EqualTo(2));
        }

        [Test]
        public void NeedsAtLeastSixProtocols()
        {
            Assert.Throws<ArgumentException>(() => new Draft(Pool.Take(5), firstSeat: 0));
        }
    }
}
