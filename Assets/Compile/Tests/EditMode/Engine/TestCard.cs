using System;
using System.Collections.Generic;

namespace Compile.Engine.Tests
{
    // Made-up card text for testing the effects framework before real cards have text. Set the parts a test
    // needs; everything else does nothing.
    public sealed class TestCard : CardEffect
    {
        readonly Dictionary<Trigger, (Slot slot, Func<Effect, int, IEnumerable<Op>> text)> _triggers =
            new Dictionary<Trigger, (Slot, Func<Effect, int, IEnumerable<Op>>)>();

        public Func<Effect, IEnumerable<Op>> MiddleText;
        public Slot Rules = Slot.Top;
        public Func<Effect, CardInstance, int, int> Value;
        public Func<Effect, int, int, int, int> Total;
        public Func<Effect, int, CardInstance, int, bool> FaceUpAnywhere;
        public Func<Effect, int, CardInstance, int, bool, string> Forbid;
        public Func<Effect, int, int, bool> NoCompile;
        public Func<Effect, CardInstance, bool> NoFlip;
        public Func<Effect, int, bool> NoCache;
        public Func<Effect, CardInstance, bool> IgnoreMiddle;

        // Text for a trigger. The function gets the seat it happened to and returns null to not react.
        public TestCard On(Trigger trigger, Slot slot, Func<Effect, int, IEnumerable<Op>> text)
        {
            _triggers[trigger] = (slot, text);
            return this;
        }

        public TestCard On(Trigger trigger, Slot slot, Func<Effect, IEnumerable<Op>> text) => On(trigger, slot, (e, _) => text(e));

        public override IEnumerable<Op> Middle(Effect e) => MiddleText?.Invoke(e);

        public override Triggered OnTrigger(Trigger trigger, Effect e, int seat)
        {
            if (!_triggers.TryGetValue(trigger, out var t))
                return null;
            var text = t.text(e, seat);
            return text == null ? null : new Triggered(t.slot, text);
        }

        public override Slot RuleSlot => Rules;
        public override int CardValue(Effect e, CardInstance card, int value) => Value?.Invoke(e, card, value) ?? value;
        public override int LineValue(Effect e, int seat, int fieldLine, int total) => Total?.Invoke(e, seat, fieldLine, total) ?? total;
        public override bool AllowsFaceUp(Effect e, int seat, CardInstance card, int fieldLine) => FaceUpAnywhere?.Invoke(e, seat, card, fieldLine) ?? false;
        public override string ForbidsPlay(Effect e, int seat, CardInstance card, int fieldLine, bool faceUp) => Forbid?.Invoke(e, seat, card, fieldLine, faceUp);
        public override bool ForbidsCompile(Effect e, int seat, int fieldLine) => NoCompile?.Invoke(e, seat, fieldLine) ?? false;
        public override bool ForbidsFlip(Effect e, CardInstance card) => NoFlip?.Invoke(e, card) ?? false;
        public override bool SkipsCheckCache(Effect e, int seat) => NoCache?.Invoke(e, seat) ?? false;
        public override bool IgnoresMiddle(Effect e, CardInstance card) => IgnoreMiddle?.Invoke(e, card) ?? false;

        // Text made of building blocks, one after another.
        public static IEnumerable<Op> Text(params Op[] ops) => ops;
    }
}
