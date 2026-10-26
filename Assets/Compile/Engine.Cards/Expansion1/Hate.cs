using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // "Delete 1 card."
    public sealed class Hate0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Delete(Target.Any));
    }

    // "Discard 3 cards. Delete 1 card. Delete 1 card." Each sentence stands alone: with fewer than 3 cards
    // you discard what you have, and the deletes happen anyway.
    public sealed class Hate1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(3), e.Delete(Target.Any), e.Delete(Target.Any));
    }

    // "Delete your highest value uncovered card. Delete your opponent's highest value uncovered card."
    // (errata 10/2024) Hate 2's owner picks among ties. If Hate 2 is your highest it deletes itself, and the second
    // sentence no longer happens (Codex).
    public sealed class Hate2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) =>
            Text(e.Delete(Target.Any.Own().Where(Highest, "with the highest value")),
                e.Delete(Target.Any.Opponents().Where(Highest, "with the highest value")));

        // A card in the field is in its owner's stack.
        static bool Highest(Game game, CardInstance card) =>
            game.CardValue(card) == Lines.Select(l => game.State.Lines[l].Uncovered(card.Owner)).Where(c => c != null).Max(game.CardValue);
    }

    // Top: "After you delete cards: Draw 1 card." Deleting by card text, either player's cards; compiling isn't deleting.
    public sealed class Hate3 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.AfterDelete && seat == e.Owner ? Top(Text(e.Draw(1))) : null;
    }

    // Bottom: "When this card would be covered: First, delete the lowest value covered card in this line."
    // Either player's covered cards; Hate 4's owner picks among ties, theirs or the opponent's.
    public sealed class Hate4 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.WouldBeCovered ? Bottom(WouldBeCovered(e)) : null;

        static IEnumerable<Op> WouldBeCovered(Effect e)
        {
            var line = e.SourceLine;
            bool Lowest(Game game, CardInstance card)
            {
                var stacks = game.State.Lines[line];
                var covered = Enumerable.Range(0, GameState.SeatCount).SelectMany(s => stacks.Stack(s).Take(stacks.Stack(s).Count - 1));
                return game.CardValue(card) == covered.Min(game.CardValue);
            }
            yield return e.Delete(Target.Any.Covered().InLine(line).Where(Lowest, "with the lowest value"));
        }
    }
}
