# Playing Games in ATE

ATE ships three complete text-mode games, all launched from the **Games** menu in the menu bar. This is the player's guide — for *writing* games, see **Game API Design** under Help → Documentation.

All games run in **game mode**: the document becomes a game screen (editor chrome hidden, block cursor), and closing the tab or pressing the game's quit key returns you to normal editing. Your open documents are untouched.

## The Z-Machine — Zork I, II, III (built in)

A clean-room Z-Machine interpreter (version 3) is built into ATE — no addon or setup needed.

- **Start**: **Games → Z-Machine (Zork)** — pick a Zork episode or open any `.z3` story file you own.
- **Several games at once**: every pick starts a new game in its own tab — running Zork I twice gives you "Zork I" and "Zork I (1)", and each plays independently (input goes to whichever game's tab is active). Each game gets its **own map tab** in the console area, labeled with the game's title and keeping its own zoom and layout; switching to a game's document always pops that game's map tab to the front.
- **Getting the games**: the menu offers a download of the MIT-licensed **Zork trilogy**. Picking a game you do not have yet opens a confirmation window first, showing the source repository, the exact file and the pinned commit it is fetched from (both link to GitHub), the licence, the size, the **SHA-256 fingerprint**, and the folder it will be written to — outside your Unity project, never imported as an asset. **Copy Link to Clipboard** hands you the exact download URL if you would rather fetch and check it yourself. Nothing is downloaded until you press **Download**, and what arrives must match both the expected size and the expected fingerprint or it is deleted instead of played. ATE ships no game file.
- **Playing**: type commands at the prompt, like 1980 — `open mailbox`, `go north`, `take lamp`. The transcript scrolls and you can scroll back through everything that happened; a pinned status line at the top shows your location and score without scrolling away.
- **Saving**: the game's own `save` and `restore` commands work, and `restart` starts over. The transcript and the map are saved and restored along with the game.
- **Games survive domain reloads**: a script compile, play-mode entry, or even an editor restart no longer ends your game. ATE snapshots every running game the moment before the reload (interpreter state, transcript, and map) and resumes it automatically afterwards — same tab, same input prompt, same map, with a console note per resumed game. If a snapshot can't be taken or restored (for example the story file was deleted), the tab falls back to a plain-text transcript renamed "…**(unloaded)**" with a tooltip explaining why. Note that the game's *random rolls* after a resume differ — that is the only thing not carried across.

### The auto-map

Turn on the mapper and ATE draws the world as you explore, in the **Map** tab of the console area:

- **Rooms** are colour-coded (each room keeps the same colour every game), with directional connection arrows — two-headed when the passage works both ways.
- **Spoiler-free**: nothing appears until you have actually seen it; things inside closed containers stay hidden until you open them.
- **Objects you have found** are listed with the room; passages and doors are drawn as **◇**, up/down exits as **▲/▼**.
- **Interiors get their own page**: walk into a building and its rooms lay out on a fresh grid instead of tangling with the streets outside.
- **Zoom** with the slider (0.4×–2.5×) or Ctrl+scroll; the current room stays centred.
- **SVG export**: one click saves the whole map — every level and interior, cross-page links, and an alphabetical object legend with each item's location — as a standalone `.svg`.

## Snake and Rogue (sample addons)

Both ship as sample addons and need two one-time steps:

1. **Semantic Features** must be enabled (ATE Settings) — addons are compiled by ATE's bundled Roslyn.
2. **Tools → Addons → Install Sample Addons**, then start a game from the **Games** menu, where every installed addon game is listed. The first run shows the addon **security review** — every addon, games included, needs your one-time approval before it executes.

### Snake

**Games → Snake.** The game starts **paused** — press **Space** to begin. Steer with the **arrow keys or WASD**, **Space** pauses and resumes (or restarts after a crash), hold **Shift** for turbo, **Escape** quits.

### Rogue

**Games → Rogue** — a faithful port of the 1980 BSD classic, version 5.4.4: the real dungeon generator, all 26 monsters, potions, scrolls, wands and rings, the identification game, hunger, running, the tombstone, and the total-winner screen. It plays with the original's keyboard commands.

### Addon games survive reloads too

Like the Z-Machine, Snake and Rogue now come back from domain reloads and editor restarts. **Snake resumes paused** — press **Space** when you're ready. Rogue resumes on the same dungeon turn; a prompt that was open when the reload hit (--More--, a direction question) is dropped, and — as everywhere — future die rolls differ after a resume. A crashed or quit game stays quit.

## Tips

- Game ticks pause while the ATE window is unfocused and resume when you come back; held keys are released automatically on focus loss.
- A game's tab behaves like any other tab — you can switch away mid-game and return.
- If the Addons menu says addons need Semantic Features, enable that in Settings first; if a game refuses to start after an ATE update, reinstall the samples (**Tools → Addons → Install Sample Addons**).
