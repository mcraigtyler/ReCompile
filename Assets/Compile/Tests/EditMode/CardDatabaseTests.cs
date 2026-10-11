using System.IO;
using System.Linq;
using Compile.Engine.Cards;
using NUnit.Framework;

namespace Compile.Engine.Tests
{
    public class CardDatabaseTests
    {
        const string DataFolder = "Assets/Compile/Data";
        const string ArtFolder = "Assets/Compile/Art/Cards";

        static CardDatabase Load() =>
            CardDatabase.Parse(File.ReadAllText($"{DataFolder}/cards.json"), File.ReadAllText($"{DataFolder}/protocols.json"));

        [Test]
        public void Loads15ProtocolsWith6CardsEach()
        {
            var db = Load();

            Assert.That(db.Protocols, Has.Count.EqualTo(15));
            Assert.That(db.Cards, Has.Count.EqualTo(90));
            foreach (var protocol in db.Protocols)
                Assert.That(db.CardsFor(protocol.Name).Count(), Is.EqualTo(6), protocol.Name);
        }

        [Test]
        public void EveryCardAndProtocolHasArt()
        {
            var db = Load();

            foreach (var card in db.Cards)
                FileAssert.Exists($"{ArtFolder}/{card.Protocol}/{card.Value}.gif", card.Id);
            foreach (var protocol in db.Protocols)
            {
                FileAssert.Exists($"{ArtFolder}/{protocol.Name}/A.gif", $"{protocol.Name} Loading side");
                FileAssert.Exists($"{ArtFolder}/{protocol.Name}/B.gif", $"{protocol.Name} Compiled side");
            }
        }

        [Test]
        public void ValuesMatchPrintedCards()
        {
            var db = Load();

            Assert.That(db.CardsFor("Gravity").Select(c => c.Value), Is.EqualTo(new[] { 0, 1, 2, 4, 5, 6 }));
            Assert.That(db.CardsFor("Metal").Select(c => c.Value), Is.EqualTo(new[] { 0, 1, 2, 3, 5, 6 }));
            Assert.That(db.CardsFor("Love").Select(c => c.Value), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void CodexErrataAreApplied()
        {
            var db = Load();

            Assert.That(db.GetCard("Death_1").Top.Text, Does.EndWith("Then, delete this card."));
            Assert.That(db.GetCard("Metal_1").Middle.Text, Is.EqualTo("Draw 2 cards. Your opponent cannot compile on their next turn."));
            Assert.That(db.GetCard("Spirit_1").Top.Text, Does.Contain("without matching protocols"));
        }

        [Test]
        public void SplitsEmphasisFromText()
        {
            var bottom = Load().GetCard("Spirit_1").Bottom;

            Assert.That(bottom.Emphasis, Is.EqualTo("Start:"));
            Assert.That(bottom.Text, Is.EqualTo("Either discard 1 card or flip this card."));
        }

        [Test]
        public void UnknownCardThrows()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Load().GetCard("Fire_9"));
        }
    }
}
