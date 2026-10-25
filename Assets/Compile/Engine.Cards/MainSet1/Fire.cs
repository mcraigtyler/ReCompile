using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // Middle: "Flip 1 other card. Draw 2 cards."
    // Bottom: "When this card would be covered: First, draw 1 card. Then, flip 1 other card." (errata 12/2024)
    // The card about to cover it is committed, so it can't be the card flipped.
    public sealed class Fire0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any.Other()), e.Draw(2));

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.WouldBeCovered ? Bottom(Text(e.Draw(1), e.Flip(Target.Any.Other()))) : null;
    }

    // "Discard 1 card. If you do, delete 1 card."
    public sealed class Fire1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var discard = e.Discard(1);
            yield return discard;
            if (discard.Done)
                yield return e.Delete(Target.Any);
        }
    }

    // "Discard 1 card. If you do, return 1 card."
    public sealed class Fire2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var discard = e.Discard(1);
            yield return discard;
            if (discard.Done)
                yield return e.Return(Target.Any);
        }
    }

    // Bottom: "End: You may discard 1 card. If you do, flip 1 card."
    public sealed class Fire3 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.End ? Bottom(End(e)) : null;

        static IEnumerable<Op> End(Effect e)
        {
            var discard = e.Discard(1, optional: true);
            yield return discard;
            if (discard.Done)
                yield return e.Flip(Target.Any);
        }
    }

    // "Discard 1 or more cards. Draw the amount discarded plus 1." With an empty hand, still draw 1 (Codex).
    public sealed class Fire4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var discard = e.DiscardAny();
            yield return discard;
            yield return e.Draw(discard.Count + 1);
        }
    }
}
