# Release Notes

Play the latest release at https://mcraigtyler.github.io/ReCompile/. Every release stays playable at `release/v<version>/` (for example https://mcraigtyler.github.io/ReCompile/release/v0.0.1/).

## v0.0.2

The first release meant for sharing.

**What you can do**

- **Play online** with a room code: one player hosts and shares the 6-character code, the other joins. The host's browser runs the game. If the guest drops out, they have 2 minutes to rejoin (Rejoin, or reload and join with the same code) before the host wins by forfeit.
- **Play at one screen** (Local game): two players take turns, with a pass screen between turns so neither sees the other's hand.
- **Play the computer**: a stand-in opponent that makes random legal moves.
- **Draft** 3 Protocols each from all 15: the 12 of Main Set 1 and the expansion's Apathy, Hate, and Love. Every one of the 90 cards does what it says, following The Compile Codex's rulings.
- **Undo** any move in your turn until you press End turn, except once it has shown you something new (a draw, a reveal).
- **Save or copy a game**: Game → Save game log, Copy game log, or Copy game notation, and the same on the result screen. **Load game** on the title screen replays a saved game move by move, and **Play from here** carries it on as a local game.
- **How to play** and **About** on the title screen.
- Wide (landscape) and tall (portrait) layouts.

**Changes since v0.0.1**

- The board's Debug menu is now the **Game** menu: save or copy the game log, copy the notation, concede, and leave. The testing aids (card effects, auto-play, opponent leaves) only appear in development builds.
- New **About** screen with credits.
- Smaller code: the build strips unused code (Medium).

**Known limits**

- The host's browser holds the whole game, hidden cards included, so a determined host could see them. Play with people you trust.
- No online rematch: both players leave and make a new room.
- Not yet checked on iPad or Android tablets.
- Animations are simple glides and flips, and there's no sound yet.

## v0.0.1

First published build, used to try online play between two browsers.
