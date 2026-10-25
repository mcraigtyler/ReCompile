using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // "Flip 1 other card. Flip this card."
    public sealed class Water0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any.Other()), e.Flip(e.Source));
    }

    // "Play the top card of your deck face-down in each other line." An empty deck isn't shuffled (Codex).
    public sealed class Water1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) =>
            Text(e.Each(Lines.Where(l => l != e.SourceLine), l => LineName(e.Game.State.ActiveSeat, l), l => e.PlayTopOfDeck(l)));
    }

    // "Draw 2 cards. Rearrange your protocols."
    public sealed class Water2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2), e.Rearrange(e.Owner));
    }

    // "Return all cards with a value of 2 in 1 line." Face-down cards are worth 2, so they go too.
    public sealed class Water3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            Target ValueTwo(int fieldLine) => Target.Any.InLine(fieldLine).Where(ValueIs(2), "with a value of 2");
            var line = e.ChooseLine("Return all cards with a value of 2 in which line?", LinesWithAny(e, ValueTwo));
            yield return line;
            if (line.Done)
                yield return e.ReturnAll(ValueTwo(line.Line));
        }
    }

    // "Return 1 of your cards." It can be itself (Codex).
    public sealed class Water4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Return(Target.Any.Own()));
    }
}
