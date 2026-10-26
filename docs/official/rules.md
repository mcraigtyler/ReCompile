# Compile — Rules (Main Set 1, MN01)

> Markdown version of `COMP-MN01_Rulesheet.pdf`, updated with the general rules explanations, FAQ answers, and clarifications from `The_Compile_Codex.pdf` (revision 16 DEC 2024). Card-specific errata and rulings are not included.

## Summary

Compile is a competitive card game in which players are rogue AIs competing one-on-one in a race to compile their 3 Protocols, rewriting reality in their new image.

- Combine 3 Protocols and fight for control of the field.
- Take control by increasing the **Value** of your **Lines**.
- Use your **Commands** (cards) to change the state of the field.
- **Compile all 3 of your Protocols to win.**

## Components

### Protocols (Main Set 1)

Each Protocol has 6 Command cards. Its "Loading..." side lists the mechanics its cards focus on.

| Protocol | Tagline | Focus |
|---|---|---|
| Darkness | An absence of light. | Draw, Shift, Manipulate |
| Death | Nothing shall survive. | Delete, Draw |
| Fire | Burn at both ends. | Discard for effect |
| Gravity | Draw ever inward. | Shift, Flip, Draw |
| Life | Bring about new growth. | Flip, Top deck play, Draw |
| Light | Burn away the dark. | Draw, Flip, Shift |
| Metal | Hardened against all. | Prevent, Draw, Flip |
| Plague | Slow death from within. | Forced discard, Flip |
| Psychic | Know your foe's mind. | Draw, Manipulate, Shift |
| Speed | Quicken with every step. | Draw, Play, Shift |
| Spirit | True strength from within. | Flip, Shift, Draw |
| Water | Wash away and renew. | Return, Draw, Flip |

### Other components

- **Control component** — a shared token that starts in a neutral position and moves between players (see [Control Component](#control-component)).

## Setup

### 1. The Draft

Lay all Protocol cards out on the table. The **youngest player** drafts first (or the players may agree on another way to pick the first player). Drafting goes 1-2-2-1, and each Protocol is placed in a specific space:

| Pick | Player | Takes | Placed in |
|---|---|---|---|
| 1 | First player | 1 Protocol | Their **left** space |
| 2 | Second player | 2 Protocols | Their **left** and **middle** spaces, in the order drafted |
| 3 | First player | 2 Protocols | Their **middle** and **right** spaces, in the order drafted |
| 4 | Second player | 1 Protocol | Their **right** space |

Return the remaining Protocols to the box.

### 2. The Deck

Each player takes the 6 Command cards for each of their 3 Protocols and shuffles them together into an **18-card deck**.

### 3. The Field

- Each player's Protocols sit **"Loading..." side up** in the center of the play area (the **field**), opposite their opponent's Protocols. This forms **three lines**, each defined by the two opposing Protocols in it.
- Each player places their deck beside the field, with space next to it for their **trash**.
- Each player draws a starting hand of **5 cards**.
- The Control component starts in its **neutral** position.

```
            PLAYER 2
   [Deck] [Trash]
   [Proto A] [Proto B] [Proto C]     <- Player 2's Protocols
   ---------- line 1 / line 2 / line 3 ----------
   [Proto X] [Proto Y] [Proto Z]     <- Player 1's Protocols
                        [Trash] [Deck]
            PLAYER 1
```

### Lines and Stacks

- A **line** is one third of the field. It contains the 2 opposing Protocols and is made up of **two stacks**.
- A **stack** is one player's portion of a line: the cards that player has played into it.
- A line's **value** for a player is the total value of the cards in that player's stack.

## Gameplay

The player who drafted first takes the first turn.

### Turn Order

1. **Start** — Activate the "Start" effects visible on cards in your stacks, in any order you choose.
2. **Check Control** — If you have a higher value than your opponent in **at least 2 lines**, you gain the Control component, either from the neutral position or from your opponent.
3. **Check Compile** — If you meet the conditions for Compiling, you **must** Compile. This is your action for the turn (you don't Play or Refresh). All other steps of the turn happen as normal.
4. **Action** — Either **Play** 1 card or **Refresh** your hand.
   - If you have no cards to play, you **must** Refresh.
   - If you can't Compile, draw cards, or play cards, skip the Action step.
5. **Check Cache** — If you have more than 5 cards in hand, you must **Clear Cache**: discard down to 5.
6. **End** — Activate the "End" effects visible on cards in your stacks, in any order you choose.

> **Start/End timing:** Only the Start (or End) effects that are visible **when the phase begins** activate. If a new Start/End effect becomes visible during that phase, it does not activate this turn.

### Actions

#### Play

Choose a card from your hand, a line on **your side** of the field, and an orientation:

- **Face-up:** The card must be played into the line containing its **matching Protocol**. Resolve the card's active effect (the middle text box). Its passive text (top and bottom boxes) is only active while the card is face-up.
- **Face-down:** The card may be played into **any** line. A face-down card has a value of **2**.

Some effects let you play a card from somewhere else (for example, the top of your deck). Follow that effect's instructions for how and where to play the card.

#### Refresh

Draw cards from the top of your deck until you have **5 cards** in hand.

- You can only Refresh if you have **fewer than 5** cards in hand.
- If you have **zero cards** in hand, you must Refresh.
- If you have the Control component, Control applies first (see [Control Component](#control-component)).

#### Compile

During **Check Compile**, you meet the requirements to Compile a line if you have:

- a total value of **10 or more** in your stack, **AND**
- **more value than your opponent** in that same line.

If you can Compile, you **must**. Resolve it in this order:

1. **Control:** If you have the Control component, return it to neutral. You may rearrange one player's Protocols (yours or your opponent's).
2. **Delete:** All cards in the line, on both sides, are deleted and go to their owners' trashes. This is an "all" effect: the deletions are **simultaneous**, so no card text enters play or triggers.
3. **Flip:** Flip your Protocol in that line to its **"Compiled"** side.
   - **Recompile:** If that Protocol is already Compiled, instead **draw the top card of your opponent's deck** (it becomes yours; see [Ownership](#ownership)).

Notes:

- You may Compile only **one line per turn**. If several lines qualify, choose one. If another line still qualifies on your next turn, Compile it then.
- Once Compiling has begun, it still resolves even if card effects reduce your value below 10 or below your opponent's.

### Victory

The first player to flip **all 3** of their Protocols to the **"Compiled"** side wins.

## Card Anatomy

```
+---------------------------------+
| (A) Spirit               (B) 1  |   A: Protocol indicator   B: Value
| (C) [icon]                      |   C: Protocol icon
|---------------------------------|
| (D) Top Command                 |   D: Persistent
|---------------------------------|
| (E) Middle Command              |   E: Immediate
|---------------------------------|
| (F) Bottom Command              |   F: Auxiliary
+---------------------------------+
```

| Part | Name | Meaning |
|---|---|---|
| A | Protocol Indicator | Which line the card can be played face-up into. |
| B | Value | Added to the line's total value for its owner. Face-down cards are value 2. |
| C | Protocol Icon | Identifies the Protocol. |
| D | Top Command — **Persistent** | Passive text that is active whenever the card is face-up, **even when covered**. Stack cards so it stays visible. |
| E | Middle Command — **Immediate** | Active text. Resolves when the card is played, flipped, or uncovered. |
| F | Bottom Command — **Auxiliary** | Passive text, often (but not only) triggered effects. Only active while the card is face-up and **uncovered**. |

## Rules Details

### Covering and Stacks

- Playing a card into a stack that already contains cards **covers** the previous top card. Stack cards so the covered cards' **Value and Top Command stay visible**.
- Each stack has exactly one **uncovered** card: the one with no cards on top of it (furthest from the Protocol). It counts as covering every other card in the stack.
- A covered card becomes uncovered only when it becomes the top card of its stack.

### Selecting Cards for Effects

- When flipping, deleting, shifting, returning, or revealing, you may only choose an **uncovered** card in the field, unless the text says **"covered"** or **"all"**.
- Unless an effect specifies whose card it affects (e.g. "1 of your cards", "1 of your opponent's cards"), it can target any uncovered card on either side of the field.
- **"All"** affects every matching card **simultaneously**, covered or uncovered.
- **"Each"** affects every matching **uncovered** card (unless it says otherwise), as separate effects resolved one at a time in an order the active player chooses.

### Resolving Card Text

- **Effects, not costs:** All card text is an effect. If you can't complete it (e.g. "Discard 1 card" with an empty hand), the card is still played.
- **"If you do":** If the text has an "if" clause tied to an instruction you couldn't complete, you don't get the benefit.
- **Sentences are separate:** Resolve each sentence on its own. Failing one sentence doesn't cancel the next.
- **Mandatory unless "may":** Unless the text says "may", resolve it as fully as possible, even if the only valid target is the card itself. If an effect can change the game state, it must.
- **Who decides:** When a card's text enters play (by being played, flipped, or uncovered), that card's **owner** decides how it resolves.
- **Interrupts (LIFO):** Whenever active text enters play — by playing, flipping, moving, deleting, returning, or any other means — resolve it immediately. It interrupts any other text until it finishes (**last in, first out**).
- **Losing active text:** If a card is covered, flipped, or removed while its active text is resolving, the rest of that text no longer resolves.
- **Top commands on covered cards:** Top command text is in play while the card is face-up, so a covered card's top command can still shift or delete that card.
- **Exceptions:** If a card gives permission to do something "against the rules", the card wins.

### Committed Cards

- A card is **committed** while it is entering the field: when played from hand, the top of a deck, the trash, or anywhere else, and when it is being **shifted**.
- A committed card's line and orientation are locked. It **cannot be selected** by any effect while committed, and nothing can stop it from entering the field in that line and orientation.
- If a committed card would cover a card with a "When this card would be covered: first..." effect, that effect (and any triggers it causes) resolves **before** the committed card enters the line. The committed card is not a valid target for anything that happens during that.
- Several cards can be committed to a line at once. They enter one at a time, in the order they were committed.

### Shifting

- **Shift** moves a card to a **different line on the same side** of the field.
- The card directly beneath the shifting card becomes uncovered right away, and any text that activates as a result resolves.
- The shifting card is committed and can't be selected until the shift finishes. It becomes the uncovered card of the destination stack.

### Rearranging Protocols

- **Rearrange** changes the **position** of one player's Protocols, never their side.
- The cards in those lines **do not move** with the Protocols.
- The end arrangement must be **different** from the starting arrangement.

### Control Component

- You gain the Control component during **Check Control** if you have a higher value than your opponent in at least 2 lines. You can take it from the neutral position or from your opponent.
- When the player holding Control **Compiles or Refreshes** (including a Refresh caused by a card effect):
  1. The Control component returns to **neutral**. This happens even if you choose not to rearrange.
  2. You **may** rearrange one player's Protocols (yours or your opponent's).
  3. Then you complete the Compile or Refresh.

### Drawing, Discarding, and the Deck

- **Draw X:** Take the top X cards of your deck into your hand. Draw them all at once, and then any effects triggered by drawing activate (so "draw 3" is not the same as drawing 1 three times).
- **Discard X:** Choose X cards and put them in your trash. Discard them all at once, and then any effects triggered by discarding activate. Discarding includes discarding from the top of a deck unless the effect says otherwise; anything that uses the word "discard" is a discard.
- **Reveal:** Show both players the information, then return it to its previous state (e.g. a revealed face-down card goes back to face-down).
- **Empty deck:** If you need to draw from an empty deck, shuffle your trash face-down to form your new deck, then finish the draw. This applies to **all** draws, not just Refresh. This is the only way a deck refills, unless a card says otherwise.
- Effects that discard, play, or reveal the top card of a deck do **nothing** if that deck is empty. They do not cause a reshuffle.

### Trash

Cards in the trash are always **face-up** and are **public knowledge**. Whenever a card is discarded or deleted, put it face-up in its **owner's** trash.

### Ownership

- Cards can change owners, most often when you Recompile. A card keeps its new owner for the rest of the game (even in the trash or after being shuffled into a deck) unless its ownership changes again.
- A card you own may be played face-up into the line with its matching Protocol **even if that Protocol is your opponent's**.

### Public and Private Information

| Information | Who can see it |
|---|---|
| Deck order and contents | Nobody (decks are randomized at the start and after every reshuffle). |
| Your hand | Only you. |
| Face-up cards in the field | Everyone. Anyone may read them at any time without changing their placement or orientation. |
| Cards in either trash | Everyone, at any time. |
| Face-down cards in the field | Only their owner, once they are in the field. |
| Hand size and deck size | Either player may ask how many cards the other has, but not what they are. |

When you play the top card of your deck face-down, you can't look at it until it's in the field.

### Miscellaneous

- **Zero** is an even number.

## Key Terms

| Term | Definition |
|---|---|
| **All** | Affects every matching card simultaneously, including covered cards. |
| **Clear Cache** | Discard down to 5 cards in hand during Check Cache. |
| **Committed** | A card that is entering the field (being played or shifted). Its line and orientation are locked, and it can't be targeted. |
| **Compile** | Delete all cards in a line on both sides and flip your Protocol to its Compiled side (or draw your opponent's top card when Recompiling). |
| **Covered** | A card with another card on top of it. |
| **Delete** | Move a card from the field to its owner's trash. |
| **Each** | Affects every matching uncovered card, one at a time in the active player's chosen order. |
| **Flip** | Turn a card from face-down to face-up, or from face-up to face-down. |
| **Line** | One third of the field, passing through 2 opposing Protocols and made up of 2 stacks. |
| **Protocol** | The header that decides which line you may play a card face-up into. |
| **Rearrange** | Change the position of one player's Protocols. The result must differ from the starting arrangement. |
| **Refresh** | Draw until you have 5 cards in hand. Requires fewer than 5 cards in hand. |
| **Return** | Move a card from the field to its owner's hand. |
| **Reveal** | Show information to both players, then return it to its previous state. |
| **Shift** | Move a card to another line on the same side of the field. |
| **Stack** | One player's cards in a line. |
| **Trash** | Where discarded and deleted cards go. Shuffled into a new deck when you need to draw from an empty deck. |
| **Uncovered** | The top card of a stack (furthest from the Protocol). Each stack has only one. |

## House Rulings

Situations the rulesheet and Codex don't settle, and how this game handles them. Each one has a test.

| Situation | Ruling |
|---|---|
| Neither player can play or draw | If two turns in a row pass with no action (no card played, no Refresh, no Compile), nothing can change, so the game ends. The player with more Compiled Protocols wins; with the same number, it's a draw. |
| Refresh with nothing to draw | You can't Refresh if your deck and trash are both empty (it would draw nothing). With cards in hand you can always play one face-down instead. |
| Forced Refresh | With no cards in hand, the Refresh happens automatically at the start of your Action step. |
| Recompile from an empty deck | Drawing your opponent's top card is a draw, so if their deck is empty, their trash is shuffled into a new deck first. If both are empty, you get nothing. |

### Card rulings

How this game reads card text the Codex doesn't spell out. Each one has a test in `Tests/EditMode/Engine/Cards/`.

| Card | Ruling |
|---|---|
| "All cards in 1 line" text (Death 2, Water 3) | Only lines with a card it would affect are offered, if any line has one: text that can change the game must (Codex, Card Text) |
| Death 1 | "Then, delete this card" is its own sentence, so it deletes itself even if you decline to draw (and, as top text, even while covered) |
| Death 2, Water 3 | Face-down cards are worth 2, so they count as "values of 1 or 2" and "a value of 2" |
| Metal 0 | Reduces the opponent's total in its line by 2, but not below 0 |
| Metal 1 | "On their next turn" is the opponent's next turn after the text resolves, and only while Metal 1 is face-up and uncovered |
| Metal 3, Gravity 0 | "Cards in this line" counts both players' cards (Gravity 0 counts itself) |
| Gravity 0 | The cards go straight in under it, so nothing is covered or uncovered and no text is set off |
| Gravity 1 | Any uncovered card on either side; one in this line goes to a line its chooser picks, one elsewhere comes to this line, on its own side |
| Gravity 2 | A card flipped face-up has its middle text resolve before it's shifted |
| Speed 2 | Its owner picks where it goes, whoever is compiling |
| Spirit 1 | Its Start "either discard 1 card or flip this card" flips without asking when its owner has no cards |
| Spirit 3 | "After you draw cards" includes drawing from the opponent's deck on a Recompile; its owner may decline |
| Water 1, Life 0 | When the order of "each" matters, the player whose turn it is picks it |
| Life 0 | "Each line where you have a card" is fixed when the text resolves and includes its own line, so it usually covers itself, and its End text then deletes it |
| Life 1 | The second flip may pick the same card again |
| Life 3 | With an empty deck nothing happens, and it doesn't ask for a line |
| Darkness 1 | The shift is optional, and Darkness 1's owner picks the line |
| Darkness 2 | Only face-down cards in its own stack are worth 4, also while it's covered; its middle text can flip a covered card on either side of the line |
| Light 2 | After the reveal, its owner may shift the card (still face-down), flip it where it is, or leave it |
| Psychic 1 | Also covers plays made by card text, and works while covered |
| Psychic 3 | Psychic 3's owner picks which of the opponent's cards is shifted, and where |
| Apathy 0 | Both players' face-down cards in the line count, and only its owner's total goes up; it works while covered |
| Apathy 1 | Flips both players' face-up cards in the line, covered ones too |
| Apathy 2 | Ignores both players' middle commands in its line, and does so while covered |
| Hate 1 | With fewer than 3 cards you discard what you have, and both deletes happen anyway |
| Hate 2, Hate 4 | When cards tie for highest (or lowest), Hate 2's or Hate 4's owner picks which is deleted, even when one of the tied cards is the opponent's |
| Hate 3 | Counts deletes by its owner's card text, of either player's cards, not compiles; it works while covered |
| Hate 4 | "Covered card in this line" means either player's |
| Love 1 | The card drawn from the opponent's deck is its drawer's from then on, like a given or taken card |
| Love 3 | With nothing to take you still give |

---

*Compile © 2024 Flat River Group, LLC — Greater Than Games. Design by Michael Yang.*
