using System.IO;
using System.Linq;
using Compile.Engine.Cards;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    // Scenarios for the Main Set 1 cards, with their real text. Field line 0 is Ann's Death facing Bob's
    // Water, line 1 Fire/Speed, line 2 Metal/Plague. Bob numbers lines from his own seat, so his line 0 is
    // field line 2. Cards with only middle text (e.g. Plague 5, Death 3) fill stacks, since sitting in the
    // field they do nothing. A card flipped face-up has no middle text to run: Bob's Fire 3 or Death 1,
    // whose End and Start text never comes up in these tests.
    public static class MainSet
    {
        const string Protocols = @"
            p0 Ann: Death Fire Metal
            p1 Bob: Plague Speed Water";

        public static Game Load(string text, bool confirmEndTurn = false) =>
            Scenario.Load(Protocols + text, confirmEndTurn, MainSet1.Effects);

        // Wave B: field line 0 is Ann's Gravity facing Bob's Plague, line 1 Spirit/Death, line 2 Water/Speed.
        // Bob's own lines: 0 is field line 2 (Speed), 1 is field line 1 (Death), 2 is field line 0 (Plague).
        const string ProtocolsB = @"
            p0 Ann: Gravity Spirit Water
            p1 Bob: Speed Death Plague";

        public static Game LoadB(string text, bool confirmEndTurn = false) =>
            Scenario.Load(ProtocolsB + text, confirmEndTurn, MainSet1.Effects);
    }

    public class MainSet1Tests
    {
        [Test]
        public void EveryCardWithTextHasItsEffect_ExceptProtocolsStillToCome()
        {
            var db = CardDatabase.Parse(File.ReadAllText("Assets/Compile/Data/cards.json"), File.ReadAllText("Assets/Compile/Data/protocols.json"));
            var cards = db.Cards.Where(c => c.Set == "MN01").ToList();
            Assert.That(cards.Select(c => c.Protocol).Distinct().Count(), Is.EqualTo(12));
            Assert.That(cards, Has.Count.EqualTo(72));

            foreach (var card in cards)
            {
                var hasText = !(card.Top.IsEmpty && card.Middle.IsEmpty && card.Bottom.IsEmpty);
                var written = MainSet1.Effects.For(card.Id) != null;
                if (MainSet1.NotYetWritten.Contains(card.Protocol))
                    Assert.That(written, Is.False, $"{card.Id}: its Protocol is listed as not written yet");
                else
                    Assert.That(written, Is.EqualTo(hasText), hasText ? $"{card.Id} has no effect" : $"{card.Id} has no text but has an effect");
            }
        }

        [TestCase("Death_5", 0)]
        [TestCase("Fire_5", 1)]
        [TestCase("Metal_5", 2)]
        [TestCase("Plague_5", 2)]
        public void YouDiscardOneCard(string card, int line)
        {
            var game = MainSet.Load($@"
                hand 0: {card} Death_1 Death_2
                hand 1: Water_0");
            game.Play(card, line);
            Assert.That(game.Pending.Kind, Is.EqualTo(ChoiceKind.HandCards));
            Assert.That(game.Pending.Seat, Is.EqualTo(0));
            game.Pick("Death_2");
            Assert.That(game.State.Players[0].Trash.Names(), Is.EqualTo(new[] { "Death_2" }));
            Assert.That(game.State.Players[0].Hand.Names(), Is.EqualTo(new[] { "Death_1" }));
        }
    }
}
