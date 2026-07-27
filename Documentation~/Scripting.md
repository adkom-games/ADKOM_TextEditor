# Scripting ATE — the AteApi

`ADKOM.TextEditor.Scripting` is the **stable** scripting surface of the
ADKOM Text Editor. It follows semantic versioning: its shape changes
only on minor/major releases. Everything outside this namespace is
internal implementation and may change in any release — do not script
against it.

## Setup

1. Put your script in an **Editor** folder (or Editor-only asmdef).
2. If you use asmdefs, add `ADKOM.TextEditor.Editor` to your asmdef's
   `references` (plain Editor-folder scripts need nothing — the
   assembly is auto-referenced).
3. `using ADKOM.TextEditor.Scripting;`

A complete working sample covering **every** API member is available in
**Package Manager → ADKOM Text Editor → Samples → "Scripting (AteApi)"**
(menu commands under Tools → ATE Samples after import).

## ⚠ Things you must know

1. **Domain reloads erase your event subscriptions.** Every script
   recompile and play-mode entry wipes static event handlers. Always
   subscribe from a `[InitializeOnLoad]` static constructor so they
   re-attach automatically — never from a one-shot menu command.
2. **Handles expire.** An `AteDocument` becomes invalid when its tab
   closes **and after any domain reload**. Check `IsValid` before using
   a stored handle; members of an invalid handle throw
   `InvalidOperationException`. Re-query `AteApi.Documents` rather than
   caching handles long-term.
3. **Background edits are not undoable.** Edits to the *active*
   document are one undo step; edits to any other open document bypass
   the undo system entirely. Call `Activate()` first if the user should
   be able to Ctrl+Z your change.
4. **`Close()` on a dirty document is asynchronous.** Without
   `discardChanges: true` it shows ATE's non-modal banner and returns
   immediately — the tab is still open, and closes only when the user
   decides. Don't assume the document is gone when the call returns.
5. **`Save()` can block.** On an untitled document it opens a modal
   Save As dialog; it returns false if the user cancels (or the write
   fails). File-backed documents save silently.
6. **`textChanged` is debounced (~400 ms) for typing** — you get one
   event per pause, not per keystroke. Programmatic writes raise it
   once, immediately. Don't build per-keystroke logic on it.
7. **Events don't nest.** Anything your handler does to ATE raises no
   further events, and a handler that throws is caught and logged to
   the ATE console — it won't break the editor, but your code won't be
   retried either.
8. **Main thread only.** No API member is safe from background threads
   or `Task.Run`.
9. **`Documents` includes virtual tabs** (release notes, "from
   metadata" views). They have `Path == null` and `IsUntitled == true`,
   just like real untitled documents — filter by `DisplayName` if you
   need to tell them apart. The Settings tab is never listed.

## API

### AteApi (static)

| Member | Description |
|---|---|
| `OpenWindow()` | Opens (or focuses) the ATE window. |
| `Open(path, line = 1, column = 1)` | Opens a file at a 1-based position; reuses an existing tab. |
| `NewDocument(initialText = "")` | New untitled document; returns its handle. |
| `Documents` | Handles for every open document tab. |
| `ActiveDocument` | The active document, or null. |

### Events

`documentOpened`, `documentClosed`, `documentSaved`,
`activeDocumentChanged` (null when the last tab closes), and
`textChanged` — debounced (~400 ms) for typing, once per programmatic
write. Handlers that throw are caught and logged to the ATE console.
Events are **not raised re-entrantly**: work a handler does to ATE
fires no nested events. Static event subscriptions are lost on domain
reload — subscribe from `[InitializeOnLoad]`.

### AteDocument (handle)

| Member | Description |
|---|---|
| `IsValid` | False once the tab closes; other members then throw. |
| `Path`, `DisplayName`, `IsDirty`, `IsUntitled` | Metadata. |
| `GetText()` | Full text, current even mid-typing. |
| `SetText(text)`, `ReplaceRange(start, end, replacement)` | Edits — see undo note. |
| `GoTo(line, column = 1)` | Activates the tab and places the caret (clamped). |
| `Activate()` | Brings the tab to the front. |
| `Save()` | Writes to disk (Save As prompt for untitled); false on cancel. |
| `Close(discardChanges = false)` | Closes; a dirty doc shows the non-modal banner unless discarding. |

**Undo note:** edits to the **active** document go through the undo
system as one undo step. Edits to a **background** document are applied
directly to the model and are **not undoable**. Activate the document
first if the user should be able to undo your edit.

## Game API (1.1)

API 1.1 adds everything needed for text-based in-editor games, shipped
as addons. The shipped **Snake** sample (`Samples~/Addons/SnakeGame.cs`,
installed by Tools → Addons → Install Sample Addons) uses every member
below and is the reference implementation.

### Game mode

`AteDocument.GameMode = true` turns a document into a game screen:
word wrap and syntax highlighting are off, programmatic writes bypass
undo history (the stack is cleared on entry — Ctrl+Z is inert), keys
the game doesn't consume don't edit the buffer, and the right-click
context menu is suppressed. Set it back to false to return the
document to normal editing.

### Drawing

| Member | Description |
|---|---|
| `LineCount`, `GetLine(line)` | Line-based reads (1-based). |
| `ReadAt(line, col, length)` | Text at a position, clamped to the line end. |
| `WriteAt(line, col, text)` | **Overwrites** text at a position (pads short lines, no newlines — draw row by row). Keeps the caret put in game mode. |
| `TryGetCursor(out line, out col)` | Caret position; false when the doc isn't the active tab. |
| `SetColor(line, colStart, colEnd, fg, bg = null)` | Colors a column range — foreground and/or background. A **render overlay**, never document text, and positional: repaint colors with the text. Null for both clears the range. |
| `ClearColors(line)` / `ClearColors()` | Clears one line / everything. |

### Input

| Member | Description |
|---|---|
| `AteApi.keyDown` / `keyUp` | Every key over the ATE window, **before** the editor. Set `e.Handled = true` to consume (arrows move your game, not the caret). Not raised while the status-bar prompt is open. |
| `AteApi.IsKeyDown(key)` | Polled key state for held-key movement. All keys reset to up on window focus loss. |
| `AteApi.mouseMoved` | Pointer moved to a new **(line, column)** over the code area (text coordinates only). |
| `AteApi.mouseButtonDown` | Consumable (`Handled`) — a handled press never places the caret. |
| `AteApi.mouseButtonUp` | Notification only. |

### Loop, prompt, lifecycle

| Member | Description |
|---|---|
| `AteApi.StartTick(hz, callback)` | Repeating game tick, clamped to [1, 30] Hz. Pauses while the window is unfocused. Returns an `AteTick` — keep it and `Stop()` it in `OnUnload`. |
| `AteApi.Prompt(prompt, onCommit, onCancel = null, digitsOnly = false, initialValue = "")` | The status-bar mini-buffer (as used by Goto Line) with your own prompt text. Enter → onCommit, Escape/focus loss → onCancel. |
| `IAteAddonLifecycle` | `OnLoad` / `OnUnload` / `OnFocusGained` / `OnFocusLost`. Resident addons are **single instances**: the same object gets OnLoad, every menu Run, focus events, and OnUnload — game state lives in instance fields. OnUnload fires on addon reload, before domain reloads, and at editor shutdown: stop ticks and close game documents there. |

Game addons declare `ApiVersion = "1.1"`; they load on this ATE and
are cleanly refused (with the reason) on older ones.

## Examples

Log every save:

```csharp
using UnityEditor;
using ADKOM.TextEditor.Scripting;

[InitializeOnLoad]
static class SaveLogger
{
    static SaveLogger()
    {
        AteApi.documentSaved += d => UnityEngine.Debug.Log($"ATE saved {d.Path}");
    }
}
```

Open every TODO-containing file in the project:

```csharp
[MenuItem("Tools/Open TODO Files in ATE")]
static void OpenTodos()
{
    foreach (var path in System.IO.Directory.GetFiles(
        UnityEngine.Application.dataPath, "*.cs", System.IO.SearchOption.AllDirectories))
        if (System.IO.File.ReadAllText(path).Contains("TODO"))
            AteApi.Open(path);
}
```

Stamp a header into the active document (undoable — it's active):

```csharp
[MenuItem("Tools/Stamp ATE Header")]
static void Stamp()
{
    var doc = AteApi.ActiveDocument;
    if (doc != null) doc.ReplaceRange(0, 0, "// (c) ADKOM Games\n");
}
```


## Addons

Addons are single-file `.cs` scripts in the machine-shared folder
`%APPDATA%/ADKOM/TextEditor/Addons/` — every ATE instance on the
machine compiles them in-memory (bundled Roslyn; Semantic Features must
be enabled) and lists them under **Tools > Addons > {Category} >
{Name}**. Categories compare case-insensitively. No project is ever
modified.

An addon is one class carrying `[AteAddon]` and implementing
`IAteAddon` (menu-invoked `Run()`) or `IAteAddonResident`
(additionally `OnLoad()` at every addon load — subscribe to AteApi
events there):

```csharp
using ADKOM.TextEditor.Scripting;

[AteAddon(Name = "Hello Addon", Category = "Samples", ApiVersion = "1.0")]
public class HelloAddon : IAteAddonResident
{
    public void OnLoad() =>
        AteApi.documentSaved += d => UnityEngine.Debug.Log("saved " + d.DisplayName);

    public void Run() =>
        UnityEngine.Debug.Log("active: " + (AteApi.ActiveDocument?.DisplayName ?? "none"));
}
```

**Tools > Addons > Install Sample Addons** copies three working
samples (resident events, document editing, document reading) into the
folder — the fastest way to start.

Compatibility is semantic versioning against `AteApi.ApiVersion`
(currently 1.1.0): your declared MAJOR must match and your MINOR must
not be newer. Incompatible or broken addons stay visible in the menu,
disabled, with the reason; compile errors are reported in the ATE
console with file and line. **Tools > Addons > Reload Addons** rescans
without restarting.
