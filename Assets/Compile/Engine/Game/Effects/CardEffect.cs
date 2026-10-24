using System;
using System.Collections.Generic;

namespace Compile.Engine
{
    // The three text slots on a card. Top text is in play whenever the card is face-up, even covered; middle
    // text runs when the card is played face-up, flipped face-up, or uncovered; bottom text is in play only
    // while the card is face-up and uncovered.
    public enum Slot
    {
        Top,
        Middle,
        Bottom,
    }

    // Moments card text can react to. The seat passed with a trigger is the player it happened to.
    public enum Trigger
    {
        Start,                  // the owner's Start phase (only cards visible when it began)
        End,                    // the owner's End phase (likewise)
        AfterDraw,              // seat drew cards
        AfterDiscard,           // seat discarded cards
        AfterDelete,            // seat's effect deleted cards (not compiling)
        AfterClearCache,        // seat discarded down to 5 in Check Cache
        WouldBeCovered,         // this card is about to be covered: resolves before the committed card lands
        WouldBeFlipped,         // this card is about to be flipped
        WouldBeDeletedByCompile,    // this card's line is about to be compiled
    }

    // Text a card has for a trigger: which slot it's printed in (so the engine knows whether it works while
    // covered) and the text itself.
    public sealed class Triggered
    {
        public Slot Slot { get; }
        public IEnumerable<Op> Text { get; }

        public Triggered(Slot slot, IEnumerable<Op> text)
        {
            Slot = slot;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }
    }

    // A card's text. Each card with text gets a subclass (registered in CardEffects by card id) that
    // overrides the hooks its text uses. Effects are stateless and shared: everything about one use of the
    // text lives in the Effect passed in.
    //
    // Text is written as an iterator of building blocks, one per sentence, so it reads like the card:
    //
    //     public override IEnumerable<Op> Middle(Effect e)
    //     {
    //         var discard = e.Discard(1);
    //         yield return discard;
    //         if (discard.Done)
    //             yield return e.Delete(Target.Any);
    //     }
    //
    // Each yielded Op finishes (including any questions it asks and any text it sets off, which interrupts
    // last in, first out) before the iterator continues. If the card is covered, flipped, or removed while
    // its text is resolving, the rest of the text doesn't run.
    public abstract class CardEffect
    {
        // ---- Text ----

        // Middle text. Null or empty for none.
        public virtual IEnumerable<Op> Middle(Effect e) => null;

        // Text for a trigger, or null if this card doesn't react to it. For Start and End, only the owner's
        // cards are asked, in the owner's turn; for the others, the seat is who it happened to.
        public virtual Triggered OnTrigger(Trigger trigger, Effect e, int seat) => null;

        protected static Triggered Top(IEnumerable<Op> text) => new Triggered(Slot.Top, text);
        protected static Triggered Bottom(IEnumerable<Op> text) => new Triggered(Slot.Bottom, text);

        // ---- Rule modifiers ----
        // Persistent text that changes the rules while the card is in play. RuleSlot says which slot that
        // text is printed in: Top rules work while covered, Bottom rules only while uncovered.

        public virtual Slot RuleSlot => Slot.Top;

        // The value of a card in the field (face-down cards start at 2).
        public virtual int CardValue(Effect e, CardInstance card, int value) => value;

        // A player's total in a line, after card values are added up.
        public virtual int LineValue(Effect e, int seat, int fieldLine, int total) => total;

        // Lets a card be played face-up without a matching Protocol.
        public virtual bool AllowsFaceUp(Effect e, int seat, CardInstance card, int fieldLine) => false;

        // Why a play is forbidden (e.g. "Your opponent can't play cards in this line."), or null.
        public virtual string ForbidsPlay(Effect e, int seat, CardInstance card, int fieldLine, bool faceUp) => null;

        public virtual bool ForbidsCompile(Effect e, int seat, int fieldLine) => false;

        public virtual bool ForbidsFlip(Effect e, CardInstance card) => false;

        public virtual bool SkipsCheckCache(Effect e, int seat) => false;

        // Stops a card's middle text from running (e.g. "Ignore all middle commands of cards in this line").
        public virtual bool IgnoresMiddle(Effect e, CardInstance card) => false;
    }

    // Card id ("Fire_4") → its effect. Cards without an entry have no text.
    public sealed class CardEffects
    {
        readonly Dictionary<string, CardEffect> _effects = new Dictionary<string, CardEffect>();

        // No card has text yet; Phase 5 registers them here.
        public static CardEffects None { get; } = new CardEffects();

        public bool IsEmpty => _effects.Count == 0;

        public CardEffects Add(string cardId, CardEffect effect)
        {
            if (this == None)
                throw new InvalidOperationException("Make a new CardEffects to add effects to.");
            _effects[cardId] = effect ?? throw new ArgumentNullException(nameof(effect));
            return this;
        }

        public CardEffect For(string cardId) => _effects.TryGetValue(cardId, out var effect) ? effect : null;
    }
}
