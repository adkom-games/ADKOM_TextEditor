# Game API Design (AteApi 1.1)

Design for extending the addon API to support text-based in-editor games, shipped as addons. Decided 2026-07-27 with Cary; not yet implemented. See [[Project State]].

## Decisions (from Cary)

1. **Surface**: games render into a real document, gated by a per-document **game mode flag**. Game mode = no wrap, no undo recording for programmatic writes, input captured by the game.
2. **Color**: per-character **foreground AND background** color, as a render overlay — colors are attributes, never buffer content.
3. **Keyboard**: **state polling** (`IsKeyDown`) in addition to events. On **focus lost** all key states reset; game receives focus-lost/gained lifecycle callbacks.
4. **Mouse**: line/col coordinates only (text games, not pixel games).
5. **Tick**: 30 Hz max.
6. **Lifecycle**: full load / unload / focus-on / focus-off (etc.) addon lifecycle.

## Proposed API additions (sketch)

All 1-based line/col on the public surface (internals are 0-based). Bumps `AteApi.ApiVersion` → `1.1.0`; existing semver gate in `AteAddonManager.IsCompatible` handles compatibility.

### Game mode
- `AteDocument.GameMode { get; set; }` — while true: word wrap off, programmatic writes bypass undo, CodeView routes input to the game hooks first, caret blink optionally suppressed.

### Cursor
- `AteDocument.GetCursor(out int line, out int col)` (or a `(int,int)` tuple property) — wraps `CodeView._caretLine/_caretCol`.
- `GoTo(line, col)` already exists.

### Read/write at position
- `string ReadAt(int line, int col, int length)` — clamped, within-line.
- `string GetLine(int line)`, `int LineCount`.
- `WriteAt(int line, int col, string text)` — **overwrite** semantics, pads line with spaces if short; undo-bypassed in game mode. Internally via `ReplaceRangeInternal(..., EditKind.Programmatic)`.

### Color overlay
- `SetColor(int line, int colStart, int colEnd, Color fg, Color? bg = null)`
- `ClearColors(int line)` / `ClearAllColors()`
- Implementation: per-line attribute spans stored beside the line list; applied in `RefreshVisible()` when building row Labels — fg via rich-text `<color>` tags at render time, bg via absolutely-positioned quads behind the Label (same technique as selection highlight). Biggest work item.

### Keyboard
- Event: `AteApi.keyDown` (`Action<AteKeyEvent>` with keycode, char, modifiers, repeat) with a `Handled` flag to consume before command dispatch. Hook: top of `TextEditorWindow.OnGlobalKeyDown` and `CodeView.OnKeyDown` (need KeyUp tracking too for polling).
- Polling: `AteApi.IsKeyDown(KeyCode)` — state table maintained from KeyDown/KeyUp; **cleared on focus lost**.

### Mouse
- `AteApi.mouseMoved` (`Action<int line, int col>`), `AteApi.mouseButton` (button, down/up, line, col) — via `CodeView.HitTest`. In game mode, consumed before normal caret placement.

### Status-bar prompt
- `AteApi.Prompt(string prompt, Action<string> onCommit, Action onCancel = null, bool digitsOnly = false, string initial = "")` — exposes the existing private `StartStatusPrompt` (`TextEditorWindow.Banners.cs`), plus a cancel callback.

### Tick
- `AteApi.StartTick(int hz, Action tick)` / `StopTick()` — clamped to ≤30 Hz, built on `rootVisualElement.schedule.Execute().Every(ms)`. Paused while window unfocused (focus events tell the game why).

### Lifecycle
Replace/extend `IAteAddonResident` with a fuller contract, e.g.:
```csharp
interface IAteAddonLifecycle : IAteAddon {
    void OnLoad();      // registry load / domain reload
    void OnUnload();    // reload/removal/editor shutdown — release ticks & hooks
    void OnFocusGained();
    void OnFocusLost(); // key states already reset by API
}
```
Manager must keep the **single resident instance** (today it re-instantiates per call — fix that) and call OnUnload on reload/domain teardown. Focus events driven by `EditorWindow.OnFocus/OnLostFocus` on `TextEditorWindow`.

## Open implementation notes
- Undo bypass: active-doc writes normally go through `UndoWorld`; game mode must skip recording (a 30 Hz repaint would flood history).
- Domain reload: ticks/schedulers and key hooks must be torn down (OnUnload) or they leak dead delegates.
- Color overlay interacts with syntax highlighting — in game mode, syntax highlighting should be disabled for the doc and the overlay wins.
- Games ship as sample addons via the existing `InstallSamples()` path.
