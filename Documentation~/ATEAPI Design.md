# AteApi Design — the ATE Scripting API

`ADKOM.TextEditor.Scripting` is the **stable** scripting surface of the ADKOM Text Editor: the design contract, every public member, and working examples. It follows semantic versioning (`AteApi.ApiVersion`, currently **1.2.0**): the shape changes only on minor/major releases, and everything outside this namespace is internal implementation that may change in any release — do not script against it.

Companion docs: the **Scripting Reference** (Scripting.md) is the quick start with the pitfall checklist; **Game API Design** (Game API Design.md) covers the game surface added in API 1.1 in depth. All three are available under **Help → Documentation**.

## Design principles

- **A stable facade, not an open door.** ATE's internals move fast; scripts and addons talk to one small, versioned surface. Addons declare the `ApiVersion` they target and load when the MAJOR matches and their MINOR is not newer than the host's — old addons keep working across compatible API growth.
- **VS Code-shaped.** Documents are handles, events are named like an editor extension host expects (`documentOpened`, `textChanged`, …), and positions are 1-based line/column on the public surface.
- **Handles, not objects.** An `AteDocument` is a handle to one open tab. It expires when the tab closes or a domain reload occurs; check `IsValid` before using a stored handle — members of an invalid handle throw `InvalidOperationException`. Two handles to the same tab compare equal.
- **Non-modal.** Nothing in the API blocks Unity's main loop with a modal dialog, with one deliberate exception: `Save()` on an untitled document opens a Save As dialog.
- **Main thread only.** No member is safe from background threads or `Task.Run`.
- **Events never nest.** Anything a handler does to ATE raises no further events, and a handler that throws is caught and logged to the ATE console — the editor survives, but your code is not retried.
- **Predictable undo.** Edits to the *active* document are one undo step; edits to a background document bypass the undo system entirely. Call `Activate()` first if the user should be able to Ctrl+Z your change.

## Setup

1. Put your script in an **Editor** folder (or an Editor-only asmdef).
2. If you use asmdefs, add `ADKOM.TextEditor.Editor` to your asmdef's `references` (plain Editor-folder scripts need nothing — the assembly is auto-referenced).
3. `using ADKOM.TextEditor.Scripting;`

A working sample covering the whole CORE surface ships in **Package Manager → ADKOM Text Editor → Samples → "Scripting (AteApi)"** (menu commands under Tools → ATE Samples after import); the Snake and Rogue sample addons are the living reference for the game surface (1.1) and the stateful lifecycle (1.2).

## AteApi — the static entry point

| Member | Description |
|---|---|
| `ApiVersion` | The API's semantic version (`"1.2.0"`). Addons declare the version they target. |
| `OpenWindow()` | Opens (or focuses) the ATE window. |
| `Open(path, line = 1, column = 1)` | Opens a file at a 1-based position; reuses an existing tab for the same file; creates the window if needed. |
| `NewDocument(initialText = "")` | Creates a new untitled document and returns its handle. |
| `Documents` | Handles for every open document tab (Settings and other non-document tabs excluded). Empty when no window exists. |
| `ActiveDocument` | The active document, or null (no window, or the Settings tab is active). |

`Documents` includes **virtual tabs** (release notes, "from metadata" views). They have `Path == null` and `IsUntitled == true`, just like real untitled documents — filter by `DisplayName` if you need to tell them apart.

```csharp
using ADKOM.TextEditor.Scripting;
using UnityEditor;

static class OpenTodoCommand
{
    [MenuItem("Tools/My Project/Open TODO at Line 10")]
    static void OpenTodo() => AteApi.Open("Assets/TODO.md", line: 10);

    [MenuItem("Tools/My Project/New Scratch Buffer")]
    static void Scratch()
    {
        var doc = AteApi.NewDocument("# Scratch\n\n");
        doc.GoTo(3, 1);
    }
}
```

## Events

| Event | Raised when |
|---|---|
| `documentOpened` | A document tab was opened (file, untitled, or restored). |
| `documentClosed` | A document tab was closed. |
| `documentSaved` | A document was written to disk. |
| `activeDocumentChanged` | The active tab changed. Carries **null** when the last tab closes. |
| `textChanged` | The active document's text changed. **Debounced (~400 ms) for typing** — one event per pause, not per keystroke; raised once, immediately, per programmatic write. |

**Domain reloads erase static event subscriptions.** Every script recompile and play-mode entry wipes them, so subscribe from a `[InitializeOnLoad]` static constructor — never from a one-shot menu command:

```csharp
using ADKOM.TextEditor.Scripting;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class SaveLogger
{
    static SaveLogger()
    {
        AteApi.documentSaved += doc => Debug.Log($"[SaveLogger] saved {doc.DisplayName} ({doc.Path})");
        AteApi.activeDocumentChanged += doc => Debug.Log(doc != null ? $"[SaveLogger] now editing {doc.DisplayName}" : "[SaveLogger] no active document");
    }
}
```

## AteDocument — the document handle

### Identity & state

| Member | Description |
|---|---|
| `IsValid` | False once the tab has been closed (or the window died). Always check after holding a handle across frames. |
| `Path` | Absolute file path, or null for untitled (and virtual) documents. |
| `DisplayName` | The tab's display name. |
| `IsDirty` | Unsaved changes exist. |
| `IsUntitled` | No backing file. |
| `StateTag` | (API 1.2) Addon-set string that persists with the session: stateful addons stamp their documents so `RestoreState` can re-find the same tabs after a reload. Clear it when a document stops being yours. |

### Content

| Member | Description |
|---|---|
| `GetText()` | The full text, current even while the user is typing. |
| `SetText(text)` | Replaces the whole text. Active document: one undo step. Background document: **not undoable**. |
| `ReplaceRange(start, end, replacement)` | Replaces the character range `[start, end)`. Same undo rule as `SetText`. |

### Navigation & lifecycle

| Member | Description |
|---|---|
| `GoTo(line, column = 1)` | Activates the tab and places the caret (1-based, clamped). |
| `Activate()` | Brings the tab to the front. |
| `ScrollToEnd()` | Activates and scrolls to the very end — brings a growing transcript's input line into view. |
| `Save()` | Saves to disk; prompts Save As for untitled documents; returns false on cancel or write failure. |
| `Close(discardChanges = false)` | Closes the tab. On a dirty document without `discardChanges`, ATE shows its **non-modal** unsaved-changes banner and returns immediately — the tab closes only when the user decides. |

```csharp
// Normalize trailing whitespace in every open, file-backed document.
using ADKOM.TextEditor.Scripting;
using System.Text.RegularExpressions;
using UnityEditor;

static class TrimAllCommand
{
    [MenuItem("Tools/My Project/Trim Trailing Whitespace In Open Tabs")]
    static void TrimAll()
    {
        foreach (var doc in AteApi.Documents)
        {
            if (!doc.IsValid || doc.IsUntitled) continue;
            string text = doc.GetText();
            string trimmed = Regex.Replace(text, @"[ \t]+(?=\r?\n)", "");
            if (trimmed != text) doc.SetText(trimmed); // background docs: applied directly, not undoable
        }
    }
}
```

## The game surface (API 1.1)

API 1.1 added everything a text-mode game needs: per-document **game mode**, `WriteAt`/`ReadAt` grid drawing, a **color overlay** (`SetColor`), consumable **keyboard/mouse events** plus key-state polling (`IsKeyDown`), the ≤30 Hz **game tick** (`StartTick`), the status-bar **`Prompt`**, a pinned **`SetStatusBar`** row, and per-document `SetTitle`/`SetFont`. It is documented in depth, with a complete game skeleton, in **Game API Design** (Help → Documentation → Game API Design).

## The stateful lifecycle (API 1.2)

API 1.2 adds the mobile-app state model, so addons survive domain reloads, addon reloads, and editor restarts:

- **`IAteAddonStateful : IAteAddonLifecycle`** — `SaveState()` is called by the host before every teardown, *before* `OnUnload` ("this may be your last callback"): return your state as a string in any format, or null when there is nothing to restore. `RestoreState(state)` is called at most once per load, after `OnLoad`, once the editor window and its restored session documents exist.
- **`AteDocument.StateTag`** — a string the addon stamps on its documents; it persists with the session, so `RestoreState` can re-find the same tabs via `AteApi.Documents`. Clear it when a document stops being yours (e.g. the player quits).
- **The host owns storage** — the state string is persisted by ATE (addons never touch disk, keeping the security story clean) and handed back exactly once; an undelivered state survives further reloads untouched.
- **Contract for `OnUnload`**: when your `SaveState` just returned non-null, do NOT close your documents — the session carries them across, and `RestoreState` re-claims them. When it returned null (or you are not stateful), close them as before.

The shipped **Snake** (resumes paused) and **Rogue** (full dungeon state via a reflection graph serializer, `RogueSave.cs`) samples are the reference implementations.

## Addons

An addon is a **folder** in the machine-shared addons folder (**Tools → Addons → Open Addons Folder…**; `%APPDATA%/ADKOM/TextEditor/Addons` on Windows): every `.cs` file inside it (recursive) compiles together into one in-memory assembly, so it is always unambiguous which files belong to which addon. Addons appear under **Tools → Addons → Category → Name** and load in every project on the machine. Stray top-level `.cs` files from the retired single-file era are migrated into folders of their own automatically.

- `[AteAddon(Name = "...", Category = "...", ApiVersion = "1.1")]` on exactly one class declares the addon: menu identity plus the targeted API version. Categories compare case-insensitively.
- `IAteAddon` — `Run()` executes when the user picks the menu item.
- `IAteAddonResident` — adds `OnLoad()`, called when addons are (re)loaded; subscribe to `AteApi` events there. Resident instances are single: the same object receives `OnLoad` and every `Run`.
- `IAteAddonLifecycle` — the full lifecycle: adds `OnUnload()` (addon reload, before domain reloads, editor shutdown — release ticks, subscriptions, and documents here) and `OnFocusGained()`/`OnFocusLost()` tracking the ATE window. Polled key state is already reset when `OnFocusLost` fires.
- **Security**: every addon's source is scanned against known-dangerous API patterns before anything runs; a risk report opens with clickable findings in the Scanner Results tab, and nothing executes until you approve that exact file content once — any change re-prompts. Addons can also be **signed** (`.atesig`), and publishers endorsed; the shipped samples are signed.

```csharp
// Save as WordCount/WordCount.cs in the addons folder.
using ADKOM.TextEditor.Scripting;

[AteAddon(Name = "Word Count", Category = "Text", ApiVersion = "1.0")]
public class WordCountAddon : IAteAddon
{
    public void Run()
    {
        var doc = AteApi.ActiveDocument;
        if (doc == null) return;
        int words = doc.GetText().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries).Length;
        UnityEngine.Debug.Log($"{doc.DisplayName}: {words} words");
    }
}
```

Install the shipped examples with **Tools → Addons → Install Sample Addons** — one folder each: HelloAddon, InsertTimestamp, WordCount, SnakeGame, and RogueGame (~13 files).

## Versioning & compatibility

- The namespace follows **semver**: PATCH releases never change the surface, MINOR releases only add, MAJOR releases may break.
- Addons are gated at load: their declared `ApiVersion` MAJOR must equal the host's, and their MINOR must not be newer. Incompatible addons stay visible in the menu, disabled, with the reason.
- Editor scripts (compiled by Unity against the assembly) get the same stability promise at the source level.

## Rules that bite (checklist)

1. Subscribe to events from `[InitializeOnLoad]` — domain reloads erase static handlers.
2. Check `IsValid` before using a stored handle; re-query `AteApi.Documents` rather than caching long-term.
3. Background edits are not undoable; `Activate()` first when undo matters.
4. `Close()` on a dirty document is asynchronous (non-modal banner); `Save()` on an untitled document can block (Save As dialog).
5. `textChanged` is debounced — don't build per-keystroke logic on it.
6. Events don't nest, and throwing handlers are logged, not retried.
7. Main thread only.
8. Virtual tabs are listed in `Documents` with `Path == null`.
