using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // The text of the Main Set 1 cards (cards.json, with the Codex errata), by card id. Phase 5 adds the
    // Protocols in waves; see NotYetWritten.
    public static class MainSet1
    {
        public static CardEffects Effects { get; } = new CardEffects("MainSet1")
            // Wave A
            .Add("Death_0", new Death0()).Add("Death_1", new Death1()).Add("Death_2", new Death2())
            .Add("Death_3", new Death3()).Add("Death_4", new Death4()).Add("Death_5", new YouDiscardOne())
            .Add("Fire_0", new Fire0()).Add("Fire_1", new Fire1()).Add("Fire_2", new Fire2())
            .Add("Fire_3", new Fire3()).Add("Fire_4", new Fire4()).Add("Fire_5", new YouDiscardOne())
            .Add("Metal_0", new Metal0()).Add("Metal_1", new Metal1()).Add("Metal_2", new Metal2())
            .Add("Metal_3", new Metal3()).Add("Metal_5", new YouDiscardOne()).Add("Metal_6", new Metal6())
            .Add("Plague_0", new Plague0()).Add("Plague_1", new Plague1()).Add("Plague_2", new Plague2())
            .Add("Plague_3", new Plague3()).Add("Plague_4", new Plague4()).Add("Plague_5", new YouDiscardOne());

        // Protocols whose cards don't have their text yet (Waves B and C).
        public static IReadOnlyList<string> NotYetWritten { get; } = new[]
        {
            "Gravity", "Water", "Spirit", "Speed",
            "Life", "Light", "Darkness", "Psychic",
        };
    }

    // Shared helpers for writing card text.
    public abstract class MainSetCard : CardEffect
    {
        // Sentences that don't depend on each other, in order.
        protected static IEnumerable<Op> Text(params Op[] sentences) => sentences;

        protected static IEnumerable<int> Lines => Enumerable.Range(0, GameState.LineCount);

        // "left line", from this player's seat.
        protected static string LineName(int seat, int fieldLine) =>
            new[] { "left", "middle", "right" }[GameState.ToLocal(seat, fieldLine)] + " line";

        // How many cards a field line holds, both players' stacks together.
        protected static int CardsIn(Effect e, int fieldLine) =>
            Enumerable.Range(0, GameState.SeatCount).Sum(seat => e.Game.State.Lines[fieldLine].Stack(seat).Count);

        protected static Func<Game, CardInstance, bool> ValueIs(params int[] values) => (g, c) => values.Contains(g.CardValue(c));
    }

    // Death 5, Fire 5, Metal 5, Plague 5: "You discard 1 card."
    public sealed class YouDiscardOne : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(1));
    }
}
