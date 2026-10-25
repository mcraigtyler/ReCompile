using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // "Play 1 card."
    public sealed class Speed0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.PlayFromHand());
    }

    // Top: "After you clear cache: Draw 1 card."
    // Middle: "Draw 2 cards."
    public sealed class Speed1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2));

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.AfterClearCache && seat == e.Owner ? Top(Text(e.Draw(1))) : null;
    }

    // Top: "When this card would be deleted by compiling: Shift this card, even if this card is covered."
    // Codex: the cards in the line are deleted together; Speed 2 is shifted out first instead, and the
    // compile goes on without it. Its owner picks the line, whoever is compiling.
    public sealed class Speed2 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.WouldBeDeletedByCompile ? Top(Text(e.Shift(e.Source))) : null;
    }

    // Middle: "Shift 1 of your other cards."
    // Bottom: "End: You may shift 1 of your cards. If you do, flip this card."
    public sealed class Speed3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Shift(Target.Any.Own().Other()));

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.End ? Bottom(End(e)) : null;

        static IEnumerable<Op> End(Effect e)
        {
            var shift = e.Shift(Target.Any.Own(), optional: true);
            yield return shift;
            if (shift.Done)
                yield return e.Flip(e.Source);
        }
    }

    // "Shift 1 of your opponent's face-down cards."
    public sealed class Speed4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Shift(Target.Any.Opponents().FaceDown()));
    }
}
