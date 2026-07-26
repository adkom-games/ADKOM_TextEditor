# Scripting ATE — the AteApi

`ADKOM.TextEditor.Scripting` is the **stable** scripting surface of the
ADKOM Text Editor. It follows semantic versioning: its shape changes
only on minor/major releases. Everything outside this namespace is
internal implementation and may change in any release — do not script
against it.

## Setup

1. Put your script in an **Editor** folder (or Editor-only asmdef).
2. If you use asmdefs, add `ADKOM.TextEditor.Editor` to your asmdef's
   `references`.
3. `using ADKOM.TextEditor.Scripting;`

All members must be called from the main thread.

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
