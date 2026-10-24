using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine
{
    // Every building block and kind of question, as text a player can set off with a DebugEffect intent.
    // Lets effects be tested and played before any card has text. Each is written the way card text will be.
    public static class DebugEffects
    {
        public sealed class Entry
        {
            public string Key { get; }
            public string Label { get; }
            public string Group { get; }         // a heading for the Debug menu ("Flip", "Reveal", ...)
            internal Func<Effect, IEnumerable<Op>> Text { get; }

            internal Entry(string group, string key, string label, Func<Effect, IEnumerable<Op>> text)
            {
                Group = group;
                Key = key;
                Label = label;
                Text = text;
            }
        }

        static readonly string[] LineNames = { "left line", "middle line", "right line" };

        public static IReadOnlyList<Entry> All { get; } = new List<Entry>
        {
            // Draw and discard
            new Entry("Draw and discard", "draw-1", "Draw 1 card", e => One(e.Draw(1))),
            new Entry("Draw and discard", "draw-2", "Draw 2 cards", e => One(e.Draw(2))),
            new Entry("Draw and discard", "discard-1", "Discard 1 card", e => One(e.Discard(1))),
            new Entry("Draw and discard", "discard-any", "Discard 1 or more cards. Draw the amount discarded plus 1", DiscardThenDraw),
            new Entry("Draw and discard", "opponent-discards-1", "Your opponent discards 1 card", e => One(e.Discard(1, e.Opponent))),
            new Entry("Draw and discard", "refresh", "Refresh", e => One(e.Refresh())),

            // Flip
            new Entry("Flip", "flip-1", "Flip 1 card", e => One(e.Flip(Target.Any))),
            new Entry("Flip", "flip-opponent-face-up", "Flip 1 of your opponent's face-up cards", e => One(e.Flip(Target.Any.Opponents().FaceUp()))),
            new Entry("Flip", "may-flip-face-down", "You may flip 1 face-down card", e => One(e.Flip(Target.Any.FaceDown(), optional: true))),
            new Entry("Flip", "flip-own-covered", "You may flip 1 of your face-up covered cards", e => One(e.Flip(Target.Any.Own().FaceUp().Covered(), optional: true))),
            new Entry("Flip", "flip-each-face-up", "Flip each face-up card", e => One(e.EachCard(Target.Any.FaceUp(), e.Flip))),
            new Entry("Flip", "flip-all-in-line", "Flip all face-up cards in 1 line", FlipAllInLine),

            // Shift
            new Entry("Shift", "shift-1", "Shift 1 card", e => One(e.Shift(Target.Any))),
            new Entry("Shift", "shift-opponent-covered", "Shift 1 of your opponent's covered cards", e => One(e.Shift(Target.Any.Opponents().Covered()))),
            new Entry("Shift", "shift-all-face-down", "Shift all face-down cards in 1 line to another line", ShiftAllFaceDown),

            // Delete and return
            new Entry("Delete and return", "delete-1", "Delete 1 card", e => One(e.Delete(Target.Any))),
            new Entry("Delete and return", "delete-each-line", "Delete 1 card from each line", DeleteFromEachLine),
            new Entry("Delete and return", "delete-all-low", "Delete all cards in 1 line with values of 1 or 2", DeleteAllLow),
            new Entry("Delete and return", "return-1", "Return 1 card", e => One(e.Return(Target.Any))),
            new Entry("Delete and return", "return-own", "Return 1 of your cards", e => One(e.Return(Target.Any.Own()))),

            // Reveal
            new Entry("Reveal", "reveal-face-down", "Reveal 1 face-down card. You may shift or flip that card", RevealThenShiftOrFlip),
            new Entry("Reveal", "reveal-opponent-hand", "Your opponent reveals their hand", e => One(e.RevealHand(e.Opponent))),
            new Entry("Reveal", "reveal-from-hand", "Reveal 1 card from your hand", e => One(e.RevealFromHand())),

            // Play
            new Entry("Play", "play-1", "Play 1 card", e => One(e.PlayFromHand())),
            new Entry("Play", "play-face-down", "Play 1 card face-down", e => One(e.PlayFromHand(faceDownOnly: true))),
            new Entry("Play", "play-top-chosen-lines", "Play the top card of your deck face-down in the lines you choose", PlayTopInChosenLines),
            new Entry("Play", "opponent-plays-top", "Your opponent plays the top card of their deck face-down in 1 line", OpponentPlaysTop),

            // Hands
            new Entry("Hands", "give-1", "Give 1 card from your hand to your opponent", e => One(e.Give())),
            new Entry("Hands", "take-random", "Take 1 random card from your opponent's hand", e => One(e.TakeRandom())),
            new Entry("Hands", "draw-opponent-top", "Draw the top card of your opponent's deck", e => One(e.DrawFromOpponentDeck())),

            // Protocols
            new Entry("Protocols", "swap-own", "Swap the positions of 2 of your Protocols", e => One(e.Swap(e.Owner))),
            new Entry("Protocols", "rearrange-opponent", "Rearrange your opponent's Protocols", e => One(e.Rearrange(e.Opponent))),

            // Options
            new Entry("Options", "either", "Either discard 1 card or draw 2 cards", EitherDiscardOrDraw),
            new Entry("Options", "may-draw", "You may draw 1 card. If you do, delete 1 card", MayDrawThenDelete),
        };

        public static Func<Effect, IEnumerable<Op>> Find(string key) => All.FirstOrDefault(d => d.Key == key)?.Text;

        public static string Label(string key) => All.FirstOrDefault(d => d.Key == key)?.Label ?? key;

        static IEnumerable<Op> One(Op op)
        {
            yield return op;
        }

        static IEnumerable<int> Lines => Enumerable.Range(0, GameState.LineCount);

        static IEnumerable<Op> DiscardThenDraw(Effect e)
        {
            var discard = e.DiscardAny();
            yield return discard;
            yield return e.Draw(discard.Count + 1);
        }

        static IEnumerable<Op> FlipAllInLine(Effect e)
        {
            var line = e.ChooseLine("Flip all face-up cards in which line?", Lines);
            yield return line;
            if (line.Done)
                yield return e.FlipAll(Target.Any.FaceUp().InLine(line.Line));
        }

        static IEnumerable<Op> ShiftAllFaceDown(Effect e)
        {
            var line = e.ChooseLine("Shift the face-down cards in which line?", Lines);
            yield return line;
            if (line.Done)
                yield return e.ShiftAll(Target.Any.FaceDown().InLine(line.Line));
        }

        static IEnumerable<Op> DeleteFromEachLine(Effect e) =>
            One(e.Each(Lines, l => LineNames[GameState.ToLocal(e.Owner, l)], l => e.Delete(Target.Any.InLine(l))));

        static IEnumerable<Op> DeleteAllLow(Effect e)
        {
            var line = e.ChooseLine("Delete the cards with values of 1 or 2 in which line?", Lines);
            yield return line;
            if (line.Done)
                yield return e.DeleteAll(Target.Any.InLine(line.Line).Where((g, c) => g.CardValue(c) == 1 || g.CardValue(c) == 2, "with a value of 1 or 2"));
        }

        static IEnumerable<Op> RevealThenShiftOrFlip(Effect e)
        {
            var reveal = e.RevealField(Target.Any);
            yield return reveal;
            if (!reveal.Done)
                yield break;
            var card = reveal.Cards[0];
            var choice = e.Choose("Shift or flip the revealed card?", new[] { "Shift it", "Flip it", "Leave it" });
            yield return choice;
            if (choice.Option == 0)
                yield return e.Shift(card);
            else if (choice.Option == 1)
                yield return e.Flip(card);
        }

        // A line at a time: each line picked gets a card straight away, until the player is done (after
        // at least one) or every line has one. Stands in for "each line where ..." and "each other line" text.
        static IEnumerable<Op> PlayTopInChosenLines(Effect e)
        {
            var remaining = Lines.ToList();
            while (remaining.Count > 0)
            {
                var first = remaining.Count == GameState.LineCount;
                var line = e.ChooseLine(first ? "Play the top card of your deck face-down in which line?" : "Play another in which line?",
                    remaining, optional: !first);
                yield return line;
                if (!line.Done)
                    yield break;
                remaining.Remove(line.Line);
                yield return e.PlayTopOfDeck(line.Line);
            }
        }

        static IEnumerable<Op> OpponentPlaysTop(Effect e)
        {
            var line = e.ChooseLine("Your opponent plays their top card into which line?", Lines);
            yield return line;
            if (line.Done)
                yield return e.PlayTopOfDeck(line.Line, e.Opponent);
        }

        static IEnumerable<Op> EitherDiscardOrDraw(Effect e)
        {
            var choice = e.Choose("Either discard 1 card or draw 2 cards", new[] { "Discard 1 card", "Draw 2 cards" });
            yield return choice;
            yield return choice.Option == 0 ? e.Discard(1) : e.Draw(2);
        }

        static IEnumerable<Op> MayDrawThenDelete(Effect e)
        {
            var draw = e.May("Draw 1 card");
            yield return draw;
            if (!draw.Done)
                yield break;
            var drew = e.Draw(1);
            yield return drew;
            if (drew.Done)
                yield return e.Delete(Target.Any);
        }
    }
}
