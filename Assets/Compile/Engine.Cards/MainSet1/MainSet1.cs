using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // The text of the Main Set 1 cards (cards.json, with the Codex errata), by card id, written in Waves
    // A to C of Phase 5, and with them the expansion's (Expansion1, Wave D): the card text new games use.
    // It keeps the name "MainSet1" in game logs and notation.
    public static class MainSet1
    {
        public static CardEffects Effects { get; } = Expansion1.AddTo(new CardEffects("MainSet1")
            // Wave A
            .Add("Death_0", new Death0()).Add("Death_1", new Death1()).Add("Death_2", new Death2())
            .Add("Death_3", new Death3()).Add("Death_4", new Death4()).Add("Death_5", new YouDiscardOne())
            .Add("Fire_0", new Fire0()).Add("Fire_1", new Fire1()).Add("Fire_2", new Fire2())
            .Add("Fire_3", new Fire3()).Add("Fire_4", new Fire4()).Add("Fire_5", new YouDiscardOne())
            .Add("Metal_0", new Metal0()).Add("Metal_1", new Metal1()).Add("Metal_2", new Metal2())
            .Add("Metal_3", new Metal3()).Add("Metal_5", new YouDiscardOne()).Add("Metal_6", new Metal6())
            .Add("Plague_0", new Plague0()).Add("Plague_1", new Plague1()).Add("Plague_2", new Plague2())
            .Add("Plague_3", new Plague3()).Add("Plague_4", new Plague4()).Add("Plague_5", new YouDiscardOne())
            // Wave B
            .Add("Gravity_0", new Gravity0()).Add("Gravity_1", new Gravity1()).Add("Gravity_2", new Gravity2())
            .Add("Gravity_4", new Gravity4()).Add("Gravity_5", new YouDiscardOne()).Add("Gravity_6", new Gravity6())
            .Add("Speed_0", new Speed0()).Add("Speed_1", new Speed1()).Add("Speed_2", new Speed2())
            .Add("Speed_3", new Speed3()).Add("Speed_4", new Speed4()).Add("Speed_5", new YouDiscardOne())
            .Add("Spirit_0", new Spirit0()).Add("Spirit_1", new Spirit1()).Add("Spirit_2", new Spirit2())
            .Add("Spirit_3", new Spirit3()).Add("Spirit_4", new Spirit4()).Add("Spirit_5", new YouDiscardOne())
            .Add("Water_0", new Water0()).Add("Water_1", new Water1()).Add("Water_2", new Water2())
            .Add("Water_3", new Water3()).Add("Water_4", new Water4()).Add("Water_5", new YouDiscardOne())
            // Wave C
            .Add("Darkness_0", new Darkness0()).Add("Darkness_1", new Darkness1()).Add("Darkness_2", new Darkness2())
            .Add("Darkness_3", new Darkness3()).Add("Darkness_4", new Darkness4()).Add("Darkness_5", new YouDiscardOne())
            .Add("Life_0", new Life0()).Add("Life_1", new Life1()).Add("Life_2", new Life2())
            .Add("Life_3", new Life3()).Add("Life_4", new Life4()).Add("Life_5", new YouDiscardOne())
            .Add("Light_0", new Light0()).Add("Light_1", new Light1()).Add("Light_2", new Light2())
            .Add("Light_3", new Light3()).Add("Light_4", new Light4()).Add("Light_5", new YouDiscardOne())
            .Add("Psychic_0", new Psychic0()).Add("Psychic_1", new Psychic1()).Add("Psychic_2", new Psychic2())
            .Add("Psychic_3", new Psychic3()).Add("Psychic_4", new Psychic4()).Add("Psychic_5", new YouDiscardOne()));

        // Protocols whose cards don't have their text yet: none, every card is written.
        public static IReadOnlyList<string> NotYetWritten { get; } = new string[0];
    }

    // Shared helpers for writing card text.
    public abstract class MainSetCard : CardEffect
    {
        // Sentences that don't depend on each other, in order.
        protected static IEnumerable<Op> Text(params Op[] sentences) => sentences;

        protected static IEnumerable<int> Lines => Enumerable.Range(0, GameState.LineCount);

        // The stack this card is in (bottom to top), or null if it isn't in the field.
        protected static IReadOnlyList<CardInstance> StackWith(Effect e) =>
            e.Game.State.Lines.SelectMany(l => new[] { l.Stack(0), l.Stack(1) }).FirstOrDefault(s => s.Contains(e.Source));

        // "left line", from this player's seat.
        protected static string LineName(int seat, int fieldLine) =>
            new[] { "left", "middle", "right" }[GameState.ToLocal(seat, fieldLine)] + " line";

        // How many cards a field line holds, both players' stacks together.
        protected static int CardsIn(Effect e, int fieldLine) =>
            Enumerable.Range(0, GameState.SeatCount).Sum(seat => e.Game.State.Lines[fieldLine].Stack(seat).Count);

        // The lines text like "... all cards in 1 line" can change: those with a matching card. Text that can
        // change the game must, so the others are only offered when no line can.
        protected static IEnumerable<int> LinesWithAny(Effect e, Func<int, Target> target)
        {
            var lines = Lines.Where(l => e.AnyCards(target(l), all: true)).ToList();
            return lines.Count > 0 ? lines : Lines;
        }

        protected static Func<Game, CardInstance, bool> ValueIs(params int[] values) => (g, c) => values.Contains(g.CardValue(c));
    }

    // Death 5, Fire 5, Metal 5, Plague 5: "You discard 1 card."
    public sealed class YouDiscardOne : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(1));
    }
}
