using System;
using System.Collections.Generic;
using System.Linq;

namespace Compile.Engine.Cards
{
    // Top: "Your opponent's total value in this line is reduced by 2." (not below 0)
    // Middle: "Flip 1 card."
    public sealed class Metal0 : MainSetCard
    {
        public override int LineValue(Effect e, int seat, int fieldLine, int total) =>
            seat == e.Opponent && fieldLine == e.SourceLine ? Math.Max(0, total - 2) : total;

        public override IEnumerable<Op> Middle(Effect e) => Text(e.Flip(Target.Any));
    }

    // "Draw 2 cards. Your opponent cannot compile on their next turn." (errata 12/2024)
    // The Codex: this works "provided the text is visible", so only while Metal 1 is face-up and
    // uncovered, like a bottom rule. They play or refresh instead.
    public sealed class Metal1 : MainSetCard
    {
        public override Slot RuleSlot => Slot.Bottom;

        public override IEnumerable<Op> Middle(Effect e) => Text(e.Draw(2), e.MarkTurn(e.NextTurnOf(e.Opponent)));

        public override bool ForbidsCompile(Effect e, int seat, int fieldLine) =>
            seat == e.Opponent && e.Game.State.Turn == e.Source.MarkedTurn;
    }

    // Top: "Your opponent cannot play cards face-down in this line."
    public sealed class Metal2 : MainSetCard
    {
        public override string ForbidsPlay(Effect e, int seat, CardInstance card, int fieldLine, bool faceUp) =>
            seat == e.Opponent && fieldLine == e.SourceLine && !faceUp ? "Metal 2: you can't play cards face-down in this line." : null;
    }

    // "Draw 1 card. Delete all cards in 1 other line with 8 or more cards." A line counts both players' cards.
    public sealed class Metal3 : MainSetCard
    {
        public override IEnumerable<Op> Middle(Effect e)
        {
            yield return e.Draw(1);
            var full = Lines.Where(l => l != e.SourceLine && CardsIn(e, l) >= 8).ToList();
            var line = e.ChooseLine("Delete all cards in which line?", full);
            yield return line;
            if (line.Done)
                yield return e.DeleteAll(Target.Any.InLine(line.Line));
        }
    }

    // Top: "When this card would be covered or flipped: First, delete this card." The covering card then
    // lands on what was under it; a flip aimed at it is used up (Codex).
    public sealed class Metal6 : MainSetCard
    {
        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat) =>
            trigger == Trigger.WouldBeCovered || trigger == Trigger.WouldBeFlipped ? Top(Text(e.Delete(e.Source))) : null;
    }
}
