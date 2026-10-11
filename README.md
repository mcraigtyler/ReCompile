# Compile Online

A browser version of **Compile**, the two-player card game by Michael Yang (Greater Than Games). Two players meet with a room code, draft Protocols, and race to compile all three. Built with Unity 6 for WebGL, playable on desktop browsers and large tablets.

> **Status: Phase 1 (setup screens).** All menus, the Protocol draft, and the result screen work against a simulated opponent. The game board, rules engine, and real online play come in later phases. See [`docs/plan.md`](docs/plan.md).

## Requirements

- **Unity 6000.6.5f1** with the **Web Build Support** module (install it from Unity Hub)
- A Unity account signed in to Unity Hub (Unity Personal is fine)
- Python 3, or any static web server, to test WebGL builds locally

## Run the game in the Editor

1. In Unity Hub, choose **Add → Add project from disk** and select this folder. It's listed as **UnityClaude**.
2. Open it. The first import takes a few minutes.
3. Open `Assets/Scenes/Main.unity`.
4. Press **Play**.

The scene contains only an `App` object. Everything else (the desk, play mat, camera, and menus) is built in code at startup, so the Hierarchy looks empty until you press Play.

### Trying the offline preview

Opponents are simulated, so you can test every screen alone:

| To see | Do this |
|---|---|
| A full game flow | **Host game** → wait about 3 s for the opponent → **I'm ready** → **Start** → draft → **Simulate win** or **Simulate loss** → **Rematch** |
| Joining a room | **Join game** with any 6-character code |
| Join errors | Codes starting with **X** show "Room not found"; codes starting with **F** show "Room is full" |
| An opponent disconnecting | **Opponent leaves** on the board preview |
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

Tests cover the card data, the Protocol draft order, the simulated session service, and room code validation.

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
- [`docs/official/rules.md`](docs/official/rules.md): the rules, with Compile Codex clarifications
- [`docs/board-setup.html`](docs/board-setup.html): an early mockup of the opening board

## Credits

- **Compile** © 2024 Flat River Group, LLC / Greater Than Games. Game design by Michael Yang. This is an unofficial fan project; card art and rules belong to their owners.
- Card text from the community database at [chuckplayer/compile](https://github.com/chuckplayer/compile), building on Ryan Scherr's Compile database.
- Fonts: [Chakra Petch](https://fonts.google.com/specimen/Chakra+Petch) and [Share Tech Mono](https://fonts.google.com/specimen/Share+Tech+Mono), both under the SIL Open Font License (license files are next to the fonts in `Assets/Compile/Fonts/`).
