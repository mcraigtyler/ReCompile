using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // "Draw 3 cards. Shift 1 of your opponent's covered cards."
    public sealed class Darkness0 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(3), e.Shift(Target.Any.Opponents().Covered()));
    }

    // "Flip 1 of your opponent's cards. You may shift that card."
    public sealed class Darkness1 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var flip = e.Flip(Target.Any.Opponents());
            yield return flip;
            if (!flip.Done)
                yield break;
            var card = flip.Cards[0];
            var shift = e.May($"Shift {e.Game.CardName(card, e.Owner)}");
            yield return shift;
            if (shift.Done)
                yield return e.Shift(card);
        }
    }

    // Top: "All face-down cards in this stack have a value of 4." (Top text, so also while covered.)
    // Middle: "You may flip 1 covered card in this line."
    public sealed class Darkness2 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any.Covered().InLine(e.SourceLine), optional: true));

        public override int CardValue(Effect e, CardInstance card, int value) =>
            !card.FaceUp && StackWith(e)?.Contains(card) == true ? 4 : value;
    }

    // "Play 1 card face-down in another line."
    public sealed class Darkness3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            var line = e.SourceLine;
            return Text(e.PlayFromHand(faceDownOnly: true, fieldLineAllowed: l => l != line));
        }
    }

    // "Shift 1 face-down card."
    public sealed class Darkness4 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e) => Text(e.Shift(Target.Any.FaceDown()));
    }
}
