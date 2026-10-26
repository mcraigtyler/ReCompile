using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // Middle: "Draw the top card of your opponent's deck." It's yours from then on.
    // Bottom: "End: You may give 1 card from your hand to your opponent. If you do, draw 2 cards."
    public sealed class Love1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.DrawFromOpponentDeck());

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) => trigger == Trigger.End ? Bottom(End(e)) : null;

        static IEnumerable<Op> End(Effect e)
        {
            var give = e.Give(1, optional: true);
            yield return give;
            if (give.Done)
                yield return e.Draw(2);
        }
    }

    // "Your opponent draws 1 card. Refresh."
    public sealed class Love2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(1, e.Opponent), e.Refresh());
    }

    // "Take 1 random card from your opponent's hand. Give 1 card from your hand to your opponent." The
    // sentences stand alone: with nothing to take you still give.
    public sealed class Love3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.TakeRandom(1), e.Give(1));
    }

    // "Reveal 1 card from your hand. Flip 1 card."
    public sealed class Love4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.RevealFromHand(1), e.Flip(Target.Any));
    }

    // "Your opponent draws 2 cards."
    public sealed class Love6 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2, e.Opponent));
    }
}
