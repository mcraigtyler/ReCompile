using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // "Flip 1 card. Draw cards equal to that card's value." Codex: its value after the flip and whatever
    // that set off; and "that card" is still the chosen one if it left the field (Metal 6 deleting itself
    // instead of flipping: face-up in the trash, so its printed value).
    public sealed class Light0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var flip = e.Flip(Target.Any);
            yield return flip;
            var card = flip.Picked.FirstOrDefault();
            if (card != null)
                yield return e.Draw(e.Game.CardValue(card));
        }
    }

    // Bottom: "End: Draw 1 card."
    public sealed class Light1 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.End ? Bottom(Text(e.Draw(1))) : null;
    }

    // "Draw 2 cards. Reveal 1 face-down card. You may shift or flip that card." Codex: the card is shown to
    // both players, then the choice is made with it face-down again, where it is.
    public sealed class Light2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            yield return e.Draw(2);
            var reveal = e.RevealField(Target.Any);
            yield return reveal;
            if (!reveal.Done)
                yield break;
            var card = reveal.Cards[0];
            var name = e.Game.CardName(card, e.Owner);
            var choice = e.Choose($"Shift or flip {name}?", new[] { $"Shift {name}", $"Flip {name}", "Leave it" });
            yield return choice;
            if (choice.Option == 0)
                yield return e.Shift(card);
            else if (choice.Option == 1)
                yield return e.Flip(card);
        }
    }

    // "Shift all face-down cards in this line to another line." Both players' face-down cards, covered or
    // not; Codex: they keep their order and all go to the same line, each on its own side.
    public sealed class Light3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.ShiftAll(Target.Any.FaceDown().InLine(e.SourceLine)));
    }

    // "Your opponent reveals their hand."
    public sealed class Light4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.RevealHand(e.Opponent));
    }
}
