using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // "Draw 2 cards. Your opponent discards 2 cards, then reveals their hand."
    public sealed class Psychic0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2), e.Discard(2, e.Opponent), e.RevealHand(e.Opponent));
    }

    // Top: "Your opponent can only play cards face-down." (Top text, so also while covered.)
    // Bottom: "Start: Flip this card."
    public sealed class Psychic1 : MainSetCard
    {
        public override string ForbidsPlay(Effect e, int seat, CardInstance card, int fieldLine, bool faceUp) =>
            seat == e.Opponent && faceUp ? "Psychic 1: you can only play cards face-down." : null;

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.Start ? Bottom(Text(e.Flip(e.Source))) : null;
    }

    // "Your opponent discards 2 cards. Rearrange their protocols."
    public sealed class Psychic2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(2, e.Opponent), e.Rearrange(e.Opponent));
    }

    // "Your opponent discards 1 card. Shift 1 of their cards."
    public sealed class Psychic3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Discard(1, e.Opponent), e.Shift(Target.Any.Opponents()));
    }

    // Bottom: "End: You may return 1 of your opponent's cards. If you do, flip this card."
    public sealed class Psychic4 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.End ? Bottom(End(e)) : null;

        static IEnumerable<Op> End(Effect e)
        {
            var back = e.Return(Target.Any.Opponents(), optional: true);
            yield return back;
            if (back.Done)
                yield return e.Flip(e.Source);
        }
    }
}
