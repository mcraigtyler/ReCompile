namespace Compile.Engine.Cards
{
    // The text of the Auxiliary Unit 1 expansion's cards (set AX01: Apathy, Hate, Love), by card id, written
    // in Wave D of Phase 5. The expansion has no rules of its own; its cards use the Main Set 1 rules and
    // helpers, and new games get them with the Main Set 1 text (MainSet1.Effects).
    public static class Expansion1
    {
        public static CardEffects AddTo(CardEffects effects) => effects
            .Add("Apathy_0", new Apathy0()).Add("Apathy_1", new Apathy1()).Add("Apathy_2", new Apathy2())
            .Add("Apathy_3", new Apathy3()).Add("Apathy_4", new Apathy4()).Add("Apathy_5", new YouDiscardOne())
            .Add("Hate_0", new Hate0()).Add("Hate_1", new Hate1()).Add("Hate_2", new Hate2())
            .Add("Hate_3", new Hate3()).Add("Hate_4", new Hate4()).Add("Hate_5", new YouDiscardOne())
            .Add("Love_1", new Love1()).Add("Love_2", new Love2()).Add("Love_3", new Love3())
            .Add("Love_4", new Love4()).Add("Love_5", new YouDiscardOne()).Add("Love_6", new Love6());
    }
}
