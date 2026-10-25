using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // Middle: "Your opponent discards 1 card."
    // Bottom: "Your opponent cannot play cards in this line."
    public sealed class Plague0 : MainSetCard
    {
        public override Slot RuleSlot => Slot.Bottom;

        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(1, e.Opponent));

        public override string ForbidsPlay(Effect e, int seat, CardInstance card, int fieldLine, bool faceUp) =>
            seat == e.Opponent && fieldLine == e.SourceLine ? "Plague 0: you can't play cards in this line." : null;
    }

    // Top: "After your opponent discards cards: Draw 1 card."
    // Middle: "Your opponent discards 1 card."
    public sealed class Plague1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(1, e.Opponent));

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.AfterDiscard && seat == e.Opponent ? Top(Text(e.Draw(1))) : null;
    }

    // "Discard 1 or more cards. Your opponent discards the amount of cards discarded plus 1."
    public sealed class Plague2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var discard = e.DiscardAny();
            yield return discard;
            yield return e.Discard(discard.Count + 1, e.Opponent);
        }
    }

    // "Flip each other face-up card." Uncovered cards only: it doesn't say "covered" or "all" (Codex).
    public sealed class Plague3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.EachCard(Target.Any.FaceUp().Other(), e.Flip));
    }

    // Bottom: "End: Your opponent deletes 1 of their face-down cards. You may flip this card."
    public sealed class Plague4 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.End ? Bottom(End(e)) : null;

        static IEnumerable<Op> End(Effect e)
        {
            yield return e.Delete(Target.Any.Opponents().FaceDown(), chooser: e.Opponent);
            var flip = e.May("Flip Plague 4");
            yield return flip;
            if (flip.Done)
                yield return e.Flip(e.Source);
        }
    }
}
