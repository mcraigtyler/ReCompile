using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // "Delete 1 card from each other line." One line at a time, in the active player's order.
    public sealed class Death0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) =>
            Text(e.Each(Lines.Where(l => l != e.SourceLine), l => LineName(e.Game.State.ActiveSeat, l), l => e.Delete(Target.Any.InLine(l))));
    }

    // Top: "Start: You may draw 1 card. If you do, delete 1 other card. Then, delete this card." (errata 10/2024)
    // The last sentence stands on its own, so the card deletes itself whether or not you drew. Top text, so
    // it does so even while covered (Codex FAQ).
    public sealed class Death1 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.Start ? Top(Start(e)) : null;

        static IEnumerable<Op> Start(Effect e)
        {
            var may = e.May("Draw 1 card");
            yield return may;
            if (may.Done)
            {
                var draw = e.Draw(1);
                yield return draw;
                if (draw.Done)
                    yield return e.Delete(Target.Any.Other());
            }
            yield return e.Delete(e.Source);
        }
    }

    // "Delete all cards in 1 line with values of 1 or 2." Face-down cards are worth 2, so they go too.
    public sealed class Death2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            Target OneOrTwo(int fieldLine) => Target.Any.InLine(fieldLine).Where(ValueIs(1, 2), "with a value of 1 or 2");
            var line = e.ChooseLine("Delete all cards with values of 1 or 2 in which line?", LinesWithAny(e, OneOrTwo));
            yield return line;
            if (line.Done)
                yield return e.DeleteAll(OneOrTwo(line.Line));
        }
    }

    // "Delete 1 face-down card."
    public sealed class Death3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Delete(Target.Any.FaceDown()));
    }

    // "Delete a card with a value of 0 or 1."
    public sealed class Death4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Delete(Target.Any.Where(ValueIs(0, 1), "with a value of 0 or 1")));
    }
}
