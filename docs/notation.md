# Game notation

A short way to write down a game of Compile, like chess notation (PGN). It fits a whole game in a few dozen lines, reads at a glance, and still replays the game exactly: `GameNotation.Read(text).Replay(catalog, effects)` plays it out again. The full game log (`GameRecord`) is still the one to use for bug reports. The notation is for reading a game back and replaying it.

To get it in a game: **Debug → Copy game notation**, or **Copy game notation** on the result screen. It's also the first section of every saved game log. To watch one, use **Load game** on the title screen: it replays the game move by move, and **Play from here** carries it on as a hot-seat game.

## Example

A real hot-seat game, up to the start of turn 4's second half:

<!-- example -->
```
[P1 "Tyler"] [P2 "Player 2"] [First "P2"] [Seed "1808029177"] [Cards "MainSet1"]
[Pool "Apathy, Darkness, Death, Fire, Gravity, Hate, Life, Light, Love, Metal, Plague, Psychic, Speed, Spirit, Water"]
[Lines "a Fi/Ap, b Pl/Me, c Dk/De"]
[Result "*"]
De Fi Pl Me Ap Dk +(Pl5,Fi5,Dk4,Pl2,Pl0) +(Me3,Me0,De3,Ap5,De4)
1. Ap5a dbg:discard-1(-De3) | Pl2b(-(Pl5,Fi5) -(Me3,Me0,De4))
2. R* +(De5,Me6,Ap1,De1,Ap3) | Pl0b(-Ap3)
3. De1c | R +(Fi2,Fi0,Fi1,Dk5)
4. S:De1(ok +Ap0 xPl0 !Pl2(-(Dk4,Dk5) -(Me6,Ap1,Ap0)) xDe1) De5c() |
```

Reading it:

- **The draft:** Player 2 drafts Death, Tyler drafts Fire and Plague, Player 2 drafts Metal and Apathy, and Tyler drafts Darkness. Then both players draw their opening hands.
- **1.**
  - Player 2 (who goes first) plays Apathy 5 face-up into line a, and uses the debug effect "discard 1 card" to discard Death 3.
  - Tyler plays Plague 2 into line b. Its text has Tyler discard Plague 5 and Fire 5, then Player 2 discards their last three cards.
- **2.** Player 2 has no cards, so they must refresh and draw five. Tyler plays Plague 0, and Player 2 discards Apathy 3.
- **3.** Player 2 plays Death 1 into line c. Tyler refreshes.
- **4.** At the start of Player 2's turn, Death 1's Start text runs:
  - they accept "you may draw 1 card" and draw Apathy 0;
  - they delete Plague 0, which uncovers Plague 2, and its text cuts in: Tyler discards two, Player 2 discards three;
  - then Death 1 deletes itself.

  Then Player 2 plays Death 5, whose discard does nothing because their hand is empty. The `|` at the end is the start of Tyler's turn.

## Tags

Tags come first, `[Name "value"]`, several to a line if you like. A line starting with `;` is a comment.

| Tag | Meaning |
|---|---|
| `P1`, `P2` | The players' names. P1 is seat 0, the player whose left is line a; P2 sits opposite |
| `First` | Who took the first turn: `P1` or `P2`. They also drafted first |
| `Seed` | The game's random seed (shuffles). Replaying needs it |
| `Cards` | Which card text the game used (`MainSet1`). Replaying needs the same text |
| `Pool` | The Protocols the draft was from, for a game that started with a draft |
| `Protocols` | For a game that started with the Protocols chosen: P1's, then P2's, left to right from each player's seat (`Death, Fire, Metal \| Plague, Speed, Water`) |
| `Lines` | For reading only: each line's Protocols at the end, P1's then P2's |
| `Result` | For reading only: `*` for a game still going, `Draw`, or who won and how |

Replaying needs `P1`, `P2`, `First`, `Seed`, and `Pool` or `Protocols`.

## Cards and lines

A card is its Protocol's code and its value: `De1` is Death 1.

| Protocol | Code | Protocol | Code | Protocol | Code |
|---|---|---|---|---|---|
| Apathy | Ap | Hate | Ha | Plague | Pl |
| Darkness | Dk | Life | Li | Psychic | Ps |
| Death | De | Light | Lt | Speed | Sd |
| Fire | Fi | Love | Lo | Spirit | Sp |
| Gravity | Gr | Metal | Me | Water | Wa |

A Protocol without a code here is written in full. No two players have the same Protocol, so there's only one of each card, and the name alone says which card it is, wherever it goes.

Lines are `a`, `b`, `c` from P1's left (P2's right). The notation shows hidden cards: it's a record of the whole game, not of what one player saw.

## Turns

The draft picks come first, as Protocol codes in pick order, then the opening draws.

Turns go in pairs, like moves in chess. `4.` starts the first player's 4th turn, and `|` starts the second player's turn. Each pair starts a new line. Within a turn, in order, come its Start text, its action, any Compile, and its End text.

## Decisions and what happened

| Written | Meaning |
|---|---|
| `De1c` | Play Death 1 face-up into line c |
| `~Me6b` | Play Metal 6 face-down into line b |
| `~Fi2b*` | Card text played the top card of a deck face-down into line b |
| `R` | Refresh. The cards drawn follow (`+(Fi2,Fi0,Fi1,Dk5)`) |
| `R*` | A refresh the player had to make, with no cards in hand |
| `pass` | The player couldn't act, so the Action step was skipped |
| `#Dk` | Compile Darkness (the deleted cards follow, `x(...)`). `##Dk` recompiles it, and the card taken from the opponent's deck follows (`&...`) |
| `@P1`, `@P2`, `@-` | Control goes to P1, to P2, or back to neutral |
| `rP2:Dk,Ap,Me` | P2's Protocols are rearranged to Darkness, Apathy, Metal (left to right from P2's seat) |
| `+Ap0`, `+(Ap0,Fi2)` | Cards drawn |
| `-Pl5`, `-(Pl5,Fi5)` | Cards discarded |
| `xPl0`, `x(Pl0,Fi3)` | Cards deleted |
| `^Fi3`, `vFi3` | A card flipped face-up, or face-down |
| `Fi3>b` | A card shifted to line b |
| `<Fi3` | A card returned to its owner's hand |
| `?Fi3` | A card revealed |
| `=Fi3` | A card given to the other player |
| `&Fi3` | A card taken from the other player |
| `%P1` | P1's trash shuffled into a new deck |
| `ok`, `no` | Accepted or declined a "may" (`no` also skips any optional choice) |
| `:2` | Chose the 2nd of several options |
| `/b` | Chose line b (for text like "delete all cards in 1 line") |
| `ord:2,1` | Put effects that happened at once in order: the 2nd first, then the 1st |
| `dbg:draw-1` | A debug effect (development builds only) |
| `concede:P2` | P2 conceded |
| `1-0`, `0-1`, `1/2-1/2` | The game ended: P1 won, P2 won, or a draw |

One action on several cards keeps them together in parentheses: `-(Me3,Me0,De4)` is a single discard of three cards. That's also how flips and the other actions work, for example `^(Fi3,Pl2)`. A single card needs no parentheses (`-De3`). These parentheses hold only card names, so they can't be confused with the ones around card text.

Card text goes in parentheses after whatever set it off. The parentheses nest in the order the text resolved, so text that cuts in shows up inside:

| Written | Meaning |
|---|---|
| `Pl2b(...)` | The middle text of the card just played |
| `M:Fi5(...)` | The same, when other text came between the play and its middle text (say, the card it covered reacting first) |
| `!Pl2(...)` | Middle text set off by uncovering the card or flipping it face-up |
| `S:De1(...)`, `E:Fi3(...)` | Start and End text |
| `*Pl1(...)` | Text that reacts to something ("after your opponent discards cards", "when this card would be covered") |
| `()` | The text ran but did nothing (for example, "discard 1 card" with an empty hand) |

When a player had a choice, the result says what they chose, with no separate notation. For example, `xPl0` in Death 1's text means they picked Plague 0 to delete. When there was only one possible pick, the game made it, and the notation looks the same.

Undone moves aren't written. The notation is the game as it was finally played.

## Replaying

Replaying starts a new game from the tags and plays it through the notation. Whenever the game waits, the next item says what was decided. Everything the game then does must match what's written. If it doesn't, replaying stops and says where:

```
Game notation, token 41 (turn 4.): the game did 'xPl0', but the notation says 'xAp5'
```

Notation that stops partway replays to that point, so a game in progress can be written down and picked up again.
