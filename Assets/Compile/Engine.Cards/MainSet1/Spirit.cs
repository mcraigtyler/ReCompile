using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // Middle: "Refresh. Draw 1 card." A normal refresh, Control included (Codex).
    // Bottom: "Skip your check cache phase."
    public sealed class Spirit0 : MainSetCard
    {
        public override Slot RuleSlot => Slot.Bottom;

        public override IEnumerable<Op> Middle(Effect e) => Text(e.Refresh(), e.Draw(1));

        public override bool SkipsCheckCache(Effect e, int seat) => seat == e.Owner;
    }

    // Top: "When you play cards face-up, they may be played without matching protocols." (errata 10/2024)
    // Middle: "Draw 2 cards."
    // Bottom: "Start: Either discard 1 card or flip this card."
    public sealed class Spirit1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2));

        public override bool AllowsFaceUp(Effect e, int seat, CardInstance card, int fieldLine) => seat == e.Owner;

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.Start ? Bottom(Start(e)) : null;

        // With no cards to discard, discarding wouldn't change anything, so it flips (text that can
        // change the game must).
        static IEnumerable<Op> Start(Effect e)
        {
            if (e.Game.State.Players[e.Owner].Hand.Count == 0)
            {
                yield return e.Flip(e.Source);
                yield break;
            }
            var choice = e.Choose("Spirit 1: discard 1 card, or flip Spirit 1?", new[] { "Discard 1 card", "Flip Spirit 1" });
            yield return choice;
            yield return choice.Option == 0 ? e.Discard(1) : e.Flip(e.Source);
        }
    }

    // "You may flip 1 card."
    public sealed class Spirit2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any, optional: true));
    }

    // Top: "After you draw cards: You may shift this card, even if this card is covered."
    public sealed class Spirit3 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.AfterDraw && seat == e.Owner ? Top(AfterDraw(e)) : null;

        static IEnumerable<Op> AfterDraw(Effect e)
        {
            var shift = e.May("Shift Spirit 3");
            yield return shift;
            if (shift.Done)
                yield return e.Shift(e.Source);
        }
    }

    // "Swap the positions of 2 of your protocols."
    public sealed class Spirit4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Swap(e.Owner));
    }
}
