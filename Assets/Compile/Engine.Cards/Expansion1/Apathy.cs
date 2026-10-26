using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // Top: "Your total value in this line is increased by 1 for each face-down card in this line." Both
    // players' face-down cards count: "this line" is both stacks, as with Metal 3 and Gravity 0.
    public sealed class Apathy0 : MainSetCard
    {
        public override int LineValue(Effect e, int seat, int fieldLine, int total)
        {
            if (seat != e.Owner || fieldLine != e.SourceLine)
                return total;
            var line = e.Game.State.Lines[fieldLine];
            return total + Enumerable.Range(0, GameState.SeatCount).Sum(s => line.Stack(s).Count(c => !c.FaceUp));
        }
    }

    // "Flip all other face-up cards in this line." Both players' cards, covered ones too ("all").
    public sealed class Apathy1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.FlipAll(Target.Any.FaceUp().Other().InLine(e.SourceLine)));
    }

    // Top: "Ignore all middle commands of cards in this line." Both players' cards; top text, so also while covered.
    // Bottom: "When this card would be covered: First, flip this card."
    public sealed class Apathy2 : MainSetCard
    {
        public override bool IgnoresMiddle(Effect e, CardInstance card) => e.Game.FieldLineOf(card) == e.SourceLine;

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.WouldBeCovered ? Bottom(Text(e.Flip(e.Source))) : null;
    }

    // "Flip 1 of your opponent's face-up cards."
    public sealed class Apathy3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any.Opponents().FaceUp()));
    }

    // "You may flip 1 of your face-up covered cards."
    public sealed class Apathy4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any.Own().FaceUp().Covered(), optional: true));
    }
}
