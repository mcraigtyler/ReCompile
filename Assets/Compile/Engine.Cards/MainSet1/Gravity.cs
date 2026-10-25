using System.Collections.Generic;

namespace Compile.Engine.Cards
{
    // "For every 2 cards in this line, play the top card of your deck face-down under this card."
    // Counts both players' cards in the line, this one included, when the text resolves.
    public sealed class Gravity0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var plays = CardsIn(e, e.SourceLine) / 2;
            for (var i = 0; i < plays; i++)
                yield return e.PlayTopOfDeckUnder(e.Source);
        }
    }

    // "Draw 2 cards. Shift 1 card either to or from this line."
    public sealed class Gravity1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2), e.ShiftToOrFrom(e.SourceLine));
    }

    // "Flip 1 card. Shift that card to this line."
    public sealed class Gravity2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var flip = e.Flip(Target.Any);
            yield return flip;
            if (flip.Done)
                yield return e.Shift(flip.Cards[0], e.SourceLine);
        }
    }

    // "Shift 1 face-down card to this line."
    public sealed class Gravity4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Shift(Target.Any.FaceDown(), e.SourceLine));
    }

    // "Your opponent plays the top card of their deck face-down in this line."
    public sealed class Gravity6 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.PlayTopOfDeck(e.SourceLine, e.Opponent));
    }
}
