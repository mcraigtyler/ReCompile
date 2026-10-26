# ReCompile

A browser version of **Compile**, the two-player card game by Michael Yang (Greater Than Games). Two players meet with a room code, draft Protocols, and race to compile all three. Built with Unity 6 for WebGL, playable on desktop browsers and large tablets.

> **Status: Phase 6 (online play) in progress: written and tested without a network, not yet tried between two browsers.** Two people can play a full game of Compile at one screen or (soon) online, or one person can play against a random stand-in opponent. All 15 Protocols do what their cards say: the 12 of Main Set 1 and the expansion's Apathy, Hate, and Love. Every kind of card effect can also be set off from **Debug → Card effects…**. See [`docs/plan.md`](docs/plan.md).

## Requirements

- **Unity 6000.6.5f1** with the **Web Build Support** module (install it from Unity Hub)
- A Unity account signed in to Unity Hub (Unity Personal is fine)
- Python 3, or any static web server, to test WebGL builds locally

## Run the game in the Editor

1. In Unity Hub, choose **Add → Add project from disk** and select this folder. It's listed as **ReCompile**.
2. Open it. The first import takes a few minutes.
3. Open `Assets/Scenes/Main.unity`.
4. Press **Play**.

The scene contains only an `App` object. Everything else (the desk, play mat, camera, and menus) is built in code at startup, so the Hierarchy looks empty until you press Play.

### Trying it

| To see | Do this |
|---|---|
| A hot-seat game | **Local game** → enter both names → **Start** → take turns drafting (press **Confirm** after your picks) → play. After each turn press **End turn**, then hand over the screen: the next player presses **I'm …: show my hand** |
| A game against the stand-in opponent | **Play the computer** → draft → play. The opponent makes random legal moves |
| An online game | One player: **Host game**, then share the room code. The other: **Join game** with that code. Both press **I'm ready**, and the host presses **Start**. The host's browser runs the game. In the Editor, **Window → Multiplayer → Multiplayer Play Mode** can run a second player, and the Editor can play a browser. Needs Relay and Lobby turned on for the project in the Unity Cloud dashboard |
| Undo | Make a move, then **Undo** (or Ctrl+Z). You can undo every move in your turn until you press **End turn**, except after drawing cards. In the draft, undo a pick until you press **Confirm** |
| Skipping ahead | **Debug → Auto-play your turn** makes random moves for you; **Concede** ends the game |
| Reporting a problem | **Debug → Save game log** (or **Save game log** when the game ends) saves the whole game as a `.txt` file: a download in the browser, `Logs/Games/` in the Editor. **Debug → Copy game log** copies it instead, for pasting into a message. Share the whole file: it replays the game exactly and has a move-by-move transcript. **Copy game notation** gives just the short form, like chess notation ([`docs/notation.md`](docs/notation.md)) |
| Watching a saved game | **Load game** on the title screen, then paste a game's notation or a saved game log (in the browser you can also pick the `.txt` file). Step through it with **Next** / **Back** or the arrow keys, by turn with **Turn >>** / **<< Turn**, and press **Play from here** to carry on as a hot-seat game from that move |
| An opponent disconnecting | Close the guest's tab or stop its player: the host's board counts down 2 minutes, then wins by forfeit. A guest who lost the connection gets **Rejoin** (or reloads and joins with the same code) |
| Wide and tall layouts | Resize the Game view or use a portrait resolution; the camera re-fits each screen |

## Build and run the WebGL version

### Build

Either:

- In the Editor: **File → Build Profiles → WebGL → Build** (or **Build And Run**, which also starts a local server and opens your browser), or
- From a terminal with the [Unity CLI](https://docs.unity.com/), with the Editor closed:

  ```bash
  unity build . --profile WebGL --output-path Builds/WebGL
  ```

A first build takes a while (shader compilation and IL2CPP); later builds are much faster. Output goes to `Builds/WebGL/`, which git ignores. The download is about 21 MB compressed.

### Play it in a browser

WebGL builds must be served over HTTP; opening `index.html` from disk won't work.

```bash
python -m http.server 8000 --directory Builds/WebGL
```

Then open **http://localhost:8000**. Stop the server with Ctrl+C.

The server only serves the files already in `Builds/WebGL/`, so rebuild after changing code. The build files keep the same names, so reload with **Ctrl+Shift+R** (or disable the cache in the browser's dev tools) to make sure the browser isn't running the previous build.

Any static server works. The build uses Brotli compression with a decompression fallback, so it still loads from servers that don't send `Content-Encoding` headers (like Python's), just a little more slowly the first time.

### Play it on a tablet

1. Start the Python server above on your computer.
2. Find your computer's local IP address (`ipconfig` on Windows, `ipconfig getifaddr en0` on macOS).
3. On a tablet on the same Wi-Fi network, open `http://<your-ip>:8000`.
4. If your firewall asks, allow Python on private networks.

Supported screens are desktops and tablets whose shorter side is at least 768 CSS pixels. Smaller screens (phones) show a "screen too small" message.

## Run the tests

- In the Editor: **Window → General → Test Runner → EditMode → Run All**, or
- From a terminal, with the Editor closed:

  ```bash
  unity test . --mode EditMode
  ```

Tests cover the card data, the rules engine (setup, the turn, Control, compiling, hidden information, undo, and the card-effects framework), the Main Set 1 cards written so far, game logs and notation (writing and replaying), the simulated session service, room code validation, and online play without a network (whole games through the match messages between a host and a guest, bad messages, and reconnecting). Fuzz tests play 200 random games three ways, checking the rules after every move: without card text, with random text built from the debug effects on every card, and with the real Main Set 1 text. `Fuzz_10000RandomGames`, `Fuzz_10000RandomGamesWithEffects`, and `Fuzz_10000RandomGamesWithMainSet1` run 10,000 each when you ask for them by name.

## Project layout

| Path | Contents |
|---|---|
| `Assets/Compile/Engine/` | Rules engine (`Compile.Engine`), plain C# with no Unity references |
| `Assets/Compile/Engine.Cards/` | Card data model and loader (`CardDatabase`) |
| `Assets/Compile/Net/` | Session service interface and the offline `FakeSessionService` |
| `Assets/Compile/Client/` | Everything on screen: `App/` (startup, navigation), `Table/` (desk, mat, camera), `Cards/`, `UI/`, `Screens/` |
| `Assets/Compile/Editor/` | Editor tools: art import settings, sprite atlas generation, `ClientSetup` |
| `Assets/Compile/Tests/EditMode/` | Unit tests |
| `Assets/Compile/Art/` | Card art, the play mat, generated UI sprites, and sprite atlases |
| `Assets/Compile/Data/` | `cards.json`, `protocols.json`, and the in-game rules summary |
| `Assets/WebGLTemplates/Compile/` | The web page the game runs in |
| `docs/` | Design document, implementation plan, rules, and card data sources |
| `resources/img/` | Original source art |

## Regenerating assets

Most generated assets are committed, so you rarely need these.

- **`ClientSetup`** recreates the TextMeshPro font assets, UI sprites, card art library, desk material, Theme, and the Main scene. Run it in batch mode (it exits by itself, so don't add `-quit`):

  ```bash
  "<Unity 6000.6.5f1>/Editor/Unity.exe" -batchmode -projectPath . -executeMethod Compile.Editor.ClientSetup.Run
  ```

- **Sprite atlases** (one per Protocol, plus `Shared`) are rebuilt automatically before every build by `CardAtlasGenerator`.
- **Art import settings** are applied automatically to anything new under `Assets/Compile/Art/`. To re-apply them to everything, run `Compile.Editor.ArtImportSettings.ApplyToAll` the same way.

New card art goes in `Assets/Compile/Art/Cards/<Protocol>/` as `0.gif`–`6.gif` (by value), `A.gif` (Loading side), and `B.gif` (Compiled side); then re-run `ClientSetup` to add it to the art library.

## Documentation

- [`docs/design.md`](docs/design.md): design document (architecture, networking, tabletop presentation, supported screens)
- [`docs/plan.md`](docs/plan.md): phased implementation plan and current status
- [`docs/notation.md`](docs/notation.md): the short game notation, for reading back and replaying games
- [`docs/official/rules.md`](docs/official/rules.md): the rules, with Compile Codex clarifications
- [`docs/board-setup.html`](docs/board-setup.html): an early mockup of the opening board

## Credits

- **Compile** © 2024 Flat River Group, LLC / Greater Than Games. Game design by Michael Yang. This is an unofficial fan project; card art and rules belong to their owners.
- Card text from the community database at [chuckplayer/compile](https://github.com/chuckplayer/compile), building on Ryan Scherr's Compile database.
- Fonts: [Chakra Petch](https://fonts.google.com/specimen/Chakra+Petch) and [Share Tech Mono](https://fonts.google.com/specimen/Share+Tech+Mono), both under the SIL Open Font License (license files are next to the fonts in `Assets/Compile/Fonts/`).
