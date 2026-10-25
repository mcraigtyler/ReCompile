using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // Top: "End: If this card is covered, delete this card." (errata 10/2024; no bottom command)
    // Middle: "Play the top card of your deck face-down in each line where you have a card." An empty deck
    // isn't shuffled (Codex). The lines are the ones with a card of yours when the text resolves, this one included.
    public sealed class Life0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var lines = Lines.Where(l => e.Game.State.Lines[l].Stack(e.Owner).Count > 0).ToList();
            return Text(e.Each(lines, l => LineName(e.Game.State.ActiveSeat, l), l => e.PlayTopOfDeck(l)));
        }

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.End && !e.Game.IsUncovered(e.Source) ? Top(Text(e.Delete(e.Source))) : null;
    }

    // "Flip 1 card. Flip 1 card." Two flips; the second may pick the same card again.
    public sealed class Life1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any), e.Flip(Target.Any));
    }

    // "Draw 1 card. You may flip 1 face-down card."
    public sealed class Life2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(1), e.Flip(Target.Any.FaceDown(), optional: true));
    }

    // Bottom: "When this card would be covered: First, play the top card of your deck face-down in another line."
    public sealed class Life3 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.WouldBeCovered ? Bottom(WouldBeCovered(e)) : null;

        static IEnumerable<Op> WouldBeCovered(Effect e)
        {
            if (e.Game.State.Players[e.Owner].Deck.Count == 0)
                yield break;
            var from = e.SourceLine;
            var line = e.ChooseLine("Play the top card of your deck face-down in which line?", Lines.Where(l => l != from));
            yield return line;
            if (line.Done)
                yield return e.PlayTopOfDeck(line.Line);
        }
    }

    // "If this card is covering a card, draw 1 card."
    public sealed class Life4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var stack = StackWith(e);
            if (stack != null && stack[0] != e.Source)
                yield return e.Draw(1);
        }
    }
}
