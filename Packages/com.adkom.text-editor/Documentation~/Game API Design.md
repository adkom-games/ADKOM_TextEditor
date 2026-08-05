# Game API Design — Games on the AteApi (1.1)

> **Note:** players see game addons in the **Games** menu (and under Tools → Addons) only when **Settings → Games → Enable In-Editor Games** is on — it is off by default. Tell your players; nothing else about developing a game changes.

The game surface of the ATE scripting API: how to turn a document into a game screen, and every member that supports it, with examples. Designed 2026-07-27, shipped in ATE 0.12.0 as **AteApi 1.1**. The shipped **Snake** and **Rogue** sample addons and the built-in Z-Machine interpreter all run on exactly this surface — read their source for full-scale examples.

General API concepts (handles, events, threading, undo, addon lifecycle) live in **AteApi Design** (Help → Documentation → AteApi Design); this document covers the game-specific members.

## The design

- **A document is the screen.** Games render into a real ATE document, gated by a per-document **game mode** flag — no separate view type, so tabs, sessions, and the window chrome all keep working.
- **Color is an overlay, never content.** Per-character foreground and background colors are render attributes stored beside the text; the buffer stays plain text.
- **Input is owned by the game.** Key events are consumable, key state is pollable, and everything resets when the window loses focus.
- **Text coordinates everywhere.** The mouse reports 1-based line/column, like the rest of the API — these are text games, not pixel games.
- **The tick is capped at 30 Hz** and pauses while the window is unfocused.
- **Games are addons** with the full load/unload/focus lifecycle, so nothing leaks across reloads.

## Quick start — a complete skeleton

Save this as `Skeleton/SkeletonGame.cs` in the shared addons folder (**Tools → Addons → Open Addons Folder…** — every addon is one subfolder), approve it when the security gate asks, then run it from **Games → Skeleton**:

```csharp
using ADKOM.TextEditor.Scripting;
using UnityEngine;

[AteAddon(Name = "Skeleton", Category = "Games", ApiVersion = "1.1")]
public class SkeletonGame : IAteAddonLifecycle
{
    const int W = 40, H = 20;
    AteDocument _doc;
    AteTick _tick;
    int _x = W / 2, _y = H / 2;

    public void OnLoad()
    {
        AteApi.keyDown += OnKey;
        AteApi.documentClosed += d => { if (Equals(d, _doc)) Stop(); };
    }

    public void OnUnload() => Stop();
    public void OnFocusGained() { }
    public void OnFocusLost() { } // ticks pause and key states reset automatically

    public void Run()
    {
        if (_doc != null && _doc.IsValid) { _doc.Activate(); return; }
        _doc = AteApi.NewDocument(BlankBoard());
        _doc.SetTitle("Skeleton");
        _doc.GameMode = true;
        _tick = AteApi.StartTick(15, Step);
    }

    void Step()
    {
        if (_doc == null || !_doc.IsValid) return;
        _doc.WriteAt(_y, _x, " ");                       // erase
        if (AteApi.IsKeyDown(KeyCode.RightArrow)) _x = Mathf.Min(_x + 1, W);
        if (AteApi.IsKeyDown(KeyCode.LeftArrow))  _x = Mathf.Max(_x - 1, 1);
        if (AteApi.IsKeyDown(KeyCode.DownArrow))  _y = Mathf.Min(_y + 1, H);
        if (AteApi.IsKeyDown(KeyCode.UpArrow))    _y = Mathf.Max(_y - 1, 1);
        _doc.WriteAt(_y, _x, "@");                       // draw
        _doc.SetColor(_y, _x, _x + 1, Color.green, null);
    }

    void OnKey(AteKeyEvent e)
    {
        if (_doc == null || !_doc.IsValid || !Equals(AteApi.ActiveDocument, _doc)) return;
        if (e.Key == KeyCode.Escape) { Stop(); e.Handled = true; }
    }

    void Stop()
    {
        _tick?.Stop();
        if (_doc != null && _doc.IsValid) { _doc.GameMode = false; _doc.Close(discardChanges: true); }
        _doc = null;
    }

    string BlankBoard()
    {
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < H; r++) sb.Append(new string(' ', W)).Append('\n');
        return sb.ToString();
    }
}
```

## Game mode

`AteDocument.GameMode { get; set; }` — while true: word wrap and syntax highlighting are off, programmatic writes bypass undo history (the document's undo stack is cleared on entry), editor chrome yields to the game, the cursor renders as a block, and the game owns the input it consumes. Set it false to return the document to normal editing.

## Drawing

| Member | Description |
|---|---|
| `WriteAt(line, column, text, mode = Overwrite)` | The "draw text" call, 1-based. **Overwrite** replaces characters in place (the fixed-grid behavior games need); **Insert** shifts the rest of the line right (for text-tool addons). The line is padded with spaces when shorter than `column`, and text past the end lengthens it. Text must not contain newlines — draw row by row. In game mode, writes bypass undo and keep the caret put. |
| `ReadAt(line, column, length)` | Reads up to `length` characters at a 1-based position, clamped to the end of that line. |
| `GetLine(line)` | The text of a 1-based line, without the newline. |
| `LineCount` | Number of lines in the document (≥ 1). |
| `TryGetViewport(out rows, out cols)` | The visible size of the game screen in whole characters — sized to the window, so a full-screen game can fit itself exactly. False until the view has laid out or when the document is not the active tab. |
| `TryGetCursor(out line, out column)` | The caret position (1-based) when this document is the active tab; false otherwise. |

**Stick to ASCII (or glyphs your monospace font really has).** A glyph missing from the font renders at fallback width and visually misaligns the column grid even though the buffer is perfectly rectangular. Coloring spaces via `SetColor`'s background is the alignment-proof way to draw solid shapes.

## Color

| Member | Description |
|---|---|
| `SetColor(line, colStart, colEnd, foreground, background = null)` | Colors the 1-based column range `[colStart, colEnd)` on a line. Passing null for both clears the range. |
| `ClearColors(line)` | Clears the overlay of one line. |
| `ClearColors()` | Clears the whole overlay. |

Colors are a **render overlay** — never part of the document text — and they are **positional**: they do not move with edits, so repaint text and color together:

```csharp
void DrawFood((int x, int y) p)
{
    _doc.WriteAt(p.y, p.x, "*");
    _doc.SetColor(p.y, p.x, p.x + 1, new Color(1f, 0.35f, 0.25f), null);
}

void DrawWall(int line, int colStart, int colEnd) // solid block, glyph-proof
{
    _doc.WriteAt(line, colStart, new string(' ', colEnd - colStart));
    _doc.SetColor(line, colStart, colEnd, null, Color.gray);
}
```

## Input

### Keyboard events

`AteApi.keyDown` / `AteApi.keyUp` fire for every key over the ATE window, **before** the editor handles it. The `AteKeyEvent` carries `Key` (KeyCode), `Character` (`'\0'` on pure keycode events — Unity sends each press as both a keycode and a character event), and `Ctrl`/`Shift`/`Alt`. Set `e.Handled = true` to consume the key: no typing, no command dispatch. Events are not raised while the status-bar prompt is open, so your own `Prompt` keeps working.

```csharp
void OnKey(AteKeyEvent e)
{
    if (!Equals(AteApi.ActiveDocument, _doc)) return; // only when our tab is front
    switch (e.Key)
    {
        case KeyCode.UpArrow:    Turn(0, -1); e.Handled = true; break;
        case KeyCode.DownArrow:  Turn(0, 1);  e.Handled = true; break;
        case KeyCode.Space:      if (_dead) NewGame(); e.Handled = true; break;
        case KeyCode.Escape:     Stop(); e.Handled = true; break;
    }
}
```

### Key-state polling

`AteApi.IsKeyDown(KeyCode)` — for held-key movement (run while held, turbo modifiers). State is maintained from key-down/up over the ATE window and **every key resets to up when the window loses focus** — a key-up missed while unfocused would otherwise stick forever. `IsKeyDown` is already reset when `OnFocusLost` fires.

### Mouse

`AteApi.mouseMoved`, `mouseButtonDown`, `mouseButtonUp` — all in **text coordinates** (1-based `Line`/`Column`; `Button` 0 = left, 1 = right, 2 = middle, -1 for moves). Set `Handled` on **button-down** events to consume the click (no caret placement, selection, or context menu); move and button-up events are notifications only.

## Timing — the game tick

`AteApi.StartTick(hz, callback)` starts a repeating tick on the ATE window, clamped to **[1, 30] Hz**, and returns an `AteTick` handle. Ticks pause while the window is unfocused and resume on focus. Exceptions in the callback are logged, not fatal. Keep the handle and call `Stop()` in `OnUnload` and when the game ends — ATE also stops all ticks and drops all input-event subscribers whenever addons reload, but a well-behaved game cleans up after itself.

## Status line & prompts

- `AteDocument.SetStatusBar(left, right, foreground, background)` — a pinned status row across the top of the game screen that does **not** scroll with the document (a text adventure's location/score header). `left` is left-aligned, `right` is right-aligned; the reserved row pushes the text down so nothing hides behind it. Only takes effect while the document is the active tab and in game mode; pass both empty to hide it.
- `AteApi.Prompt(prompt, onCommit, onCancel = null, digitsOnly = false, initialValue = "")` — the status-bar mini-buffer (the Goto Line prompt). Enter → `onCommit(text)`, Escape or focus loss → `onCancel`. One prompt at a time; opening a new one cancels the previous.

```csharp
void PromptSave() =>
    AteApi.Prompt("Save game as: ", name => SaveTo(name), onCancel: () => { /* resume */ });
```

## Tab title, font & transcripts

- `SetTitle(title)` — names the tab after the game (display only; null/empty restores the default).
- `SetFont(fontName, size = 0)` / `ClearFont()` — a per-document font override (OS font family; size clamped to [8, 40], 0 keeps the current size). Missing fonts fall back to the editor default; the override is runtime-only (a domain reload drops it), and user zoom gestures adjust the override rather than the global size. Monospace fonts keep the character grid aligned.
- `ScrollToEnd()` — for scrolling-transcript games (the Z-Machine pattern): append output, then scroll so the input line stays in view — e.g. after restoring a saved game.

## Lifecycle & cleanup

Implement `IAteAddonLifecycle` (see AteApi Design → Addons) and follow the skeleton's pattern:

1. `OnLoad` — subscribe `keyDown` (and watch `documentClosed` for your own document).
2. `Run` — re-activate the existing game document if it is still valid; otherwise create, title, and enter game mode, then start the tick.
3. `OnUnload` — stop the tick, leave game mode, close/release the document.
4. `OnFocusLost` — usually empty: ticks pause and key state resets automatically.

## Surviving domain reloads (API 1.2)

Upgrade to `IAteAddonStateful` and your game outlives script compiles, play mode, and editor restarts (see **AteApi Design → The stateful lifecycle** for the contract):

- `SaveState()` — serialize your game to a string (JSON via `JsonUtility` works for simple games — see Snake; Rogue ships a reflection graph serializer for its full dungeon). Stamp your document with `AteDocument.StateTag` so you can find it again, and skip the document-close in `OnUnload` when you returned state.
- `RestoreState(state)` — re-find the document by its `StateTag`, re-enter game mode (`GameMode` does not survive the reload), repaint everything from state (the color overlay does not survive either — the text does), and restart your tick. Resuming **paused** is good manners: the player was not at the keyboard when the reload hit (Snake does exactly this).
- Declare `ApiVersion = "1.2"`.

## Shipping your game

- Your game is one **subfolder** of the shared addons folder — all `.cs` files under it compile together as ONE addon, with exactly one `[AteAddon]` class in the folder. A small game is a folder with a single file; Rogue is a folder with thirteen.
- `[AteAddon(Name = "...", Category = "Games", ApiVersion = "1.1")]` lists it in the **Games** menu (and under Tools → Addons → Games).
- Every addon passes the **security gate**: a source scan, a risk report, and one-time consent keyed to the folder's exact content. Addons can be **signed** (an `author.atesig` in the folder — see **Addon Signing** in Help → Documentation); the shipped samples are.
- Install the samples with **Tools → Addons → Install Sample Addons**, then read `Addons~/SnakeGame/SnakeGame.cs` (every game feature in ~300 lines) and `Addons~/RogueGame/` (a full 1980 BSD Rogue port).
