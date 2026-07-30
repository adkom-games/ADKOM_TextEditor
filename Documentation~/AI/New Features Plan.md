---
tags: [plan, new-features]
---

# New Features Plan — newfeatures.md campaign

Implementation plan for the feature list in `Documentation~/newfeatures.md`
(started 2026-07-30). Statuses updated as items land. See [[Project State]]
for the release snapshot and [[Project Log]] for per-item history.

**Scope decisions (Cary, 2026-07-30):**
- Inspection features act on **play-mode live values** (reflect over the
  running game), not edit-time metadata.
- Git integration is **full** (markers, blame/history, stage/commit/push)
  including an **interactive branch-history tree** (click to checkout /
  create branch).
- Find/Replace in Files defaults to **Assets + Packages, text files only**,
  with the option to point at any other folder.
- Edit-menu and Settings reorganizations: done by Claude, reviewed and
  approved by Cary (2026-07-30).

## Status

| # | Feature | Phase | Status |
|---|---|---|---|
| 1 | IntelliSense (semantic C# completion) | 1 | **Done** (6b37909) |
| 2 | Customizable code snippets | 2 | **Done** (4774b3d) |
| 3 | Unity magic-method generator | 2 | **Done** (with #4) |
| 4 | Override-method generator | 2 | **Done** (with #3) |
| 5 | Automated saving and reloading | 0 | **Done** (auto-save on focus loss; auto-reload existed) |
| 6–7 | Static fields/properties inspection popup | 4 | **Done** (AteInspectorWindow) |
| 8–9 | MonoBehaviour fields/properties inspection popup | 4 | **Done** (scene-instance picker in the same window) |
| 10 | Execute parameterless static methods | 4 | **Done** (Run buttons in the same window) |
| 11 | Code #region navigation | 0 | **Done** (f9b87a3) |
| 12 | Find All References with filtering | 0 | **Done** (cef5ae2) |
| 13 | Find/Replace in Files (+preview/selections, global undo) | 3 | **Done** (dedicated window, not the console tab — better checkbox/preview UX) |
| 14 | History navigation (visual undo/redo timeline) | 1.5 | **Done** (dc37c6c) |
| 15 | Error highlighting | 1 | **Done** (7dbf05c) |
| 16 | Read/write reference highlighting | 1 | **Done** (c049b7e) |
| 17 | Git integration + branch-history tree | 5 | Pending |
| 18 | Clean up the Edit menu | 0 | **Done** (be4139e, approved) |
| 19 | Organize Settings | 0 | **Done** (27e7047, approved) |
| 20 | Optional spell checking | 6 | Pending |
| 21 | Syntax coloring: JSON, Shaders | 2.5 | **Done** (08b6c8a) |

All completed items are on `main`, unreleased as of 2026-07-30 — they ship
with the next release (working name: 0.13.0, "the IDE release").

## Architecture notes (how the done items are built)

- **Semantic capabilities pattern**: each Roslyn feature is an optional
  interface the provider implements — `ISemanticCompletion` (IntelliSense),
  `ISemanticDiagnostics` (error underlines), `ISemanticOccurrences`
  (read/write highlighting) — discovered by cast, so the core degrades
  gracefully without the semantics module. All queries run on background
  threads and post back version-gated (stale results never touch edited text).
- **IntelliSense**: member completion resolves the expression left of `.`
  (instance / static / namespace contexts, accessibility via
  `IsAccessible`, base-chain walk); scope completion via `LookupSymbols`.
  One query per word start, cached and filtered locally as the prefix grows.
  Snippets, words, and keywords blend into the same popup.
- **Snippets** (`SnippetStore`, `CodeView.Snippets`): one plain-text file
  (`%APPDATA%/ADKOM/TextEditor/Snippets.txt`), `[trigger]` + body format,
  `$name$` tab stops / `$END$` caret, hot-reload on timestamp. Live session:
  stops track edits (grow inside, shift after, session ends on boundary
  crossings); Tab/Shift+Tab cycle; expansion is one undo step.
- **History** (`CodeView.History`, `HistoryWindow`): timeline reconstructed
  from undo deltas (never snapshots); restore walks real Undo()/Redo() so it
  stays reversible; snapshots open as virtual tabs.

## Per-feature designs (pending items)

### 3–4. Code generators (Unity magic methods, overrides) — Phase 2, next
- New "Generate" entries (Edit → Code submenu + completion popup):
  - **Unity magic methods**: menu lists `Awake/Start/Update/FixedUpdate/
    LateUpdate/OnEnable/OnDisable/OnDestroy/OnCollision*/OnTrigger*/OnGUI/…`
    with correct signatures; inserts via the snippet path ($END$ inside the
    body). Context-aware when semantics are on (offered inside a
    MonoBehaviour-derived class; already-present methods dimmed).
  - **Override methods**: Roslyn lists overridable members of the base chain
    at the caret's class (virtual/abstract, accessible, not already
    overridden); picking one generates the stub with `base.` call.
- Both are `ISemanticGeneration`-style provider capabilities + a picker menu.

### 5. Automated saving and reloading — Phase 0 leftover (small)
- Settings (Files & Saving): **Auto-Save on Focus Loss** (save all dirty
  file-backed docs when the window/editor loses focus) and, evaluated:
  auto-save-interval-to-disk (distinct from the session autosave that already
  protects unsaved content). Auto-reload already exists
  (`EditorConfig.AutoReloadFromDisk`).

### 6–10. Play-mode inspection + static method execution — Phase 4
- New utility window(s) opened from the editor context menu on a symbol
  (and a Tools entry):
  - **Static inspector**: for the type under the caret, reflect static
    fields/properties and show live values (poll ~2Hz in play mode; values
    readable in edit mode too where safe). Writable primitives editable.
  - **MonoBehaviour inspector**: pick a scene instance of the type under the
    caret (list instances via `UnityEngine.Object.FindObjectsByType`), show
    its fields/properties live.
  - **Execute static method**: context-menu on a parameterless static method
    symbol → invoke, result/exception to the ATE console. Uses the addon
    security mindset: only on explicit user click, never automatic.
- All reflection over loaded assemblies (edit + play); play-mode values are
  the headline per Cary's scope decision.

### 13. Find/Replace in Files — Phase 3
- Search engine over **Assets + Packages** text files (binary/Library
  excluded), with a root-folder override; glob filter; regex/case/word
  options shared with the existing dialog.
- Results tree in the Search Results tab (per-file groups); **preview** pane
  for the selected match; **checkbox selection** per match/file;
  **Replace Selected** applies an atomic multi-file edit.
- **Global undo**: a journal of (file, before, after) per replace operation;
  "Undo Replace in Files" restores every file (and re-runs as redo). Open
  buffers route through the normal undo system; closed files restore via the
  journal with external-change detection suppressed.

### 17. Git integration — Phase 5 (full, per Cary)
- `git` CLI backend (like the `gh` usage elsewhere; no bundled lib).
- **Gutter markers**: added/modified/deleted vs HEAD, updated on save/switch.
- **Blame** (line annotations toggle) and per-file **history** viewer.
- **Stage / unstage / commit / push** panel; status in the ATE status bar.
- **Branch-history tree**: `git log --graph`-style visual (Painter2D lanes),
  **interactive**: click a branch head to checkout, context menu to create a
  branch at a commit. Guard rails: refuses with dirty working tree unless
  stashing is confirmed. Layout is switchable **vertical ⇄ horizontal** with a
  toggle button in the view (newfeatures.md addition, 2026-07-30).

### 20. Spell checking — Phase 6 (optional, off by default)
- **Default dictionary**: bundled SCOWL-derived English word list
  (public-domain/MIT-compatible — same license care as Roslyn/Zork; never
  GPL Hunspell data).
- **Other languages**: load user-supplied Hunspell `.dic`/`.aff` from a
  shared folder — we ship none.
- **Custom dictionaries**: global + per-project user word lists; "Add to
  dictionary" quick action.
- Scope: comments/strings/markdown prose only in code files (identifier
  camelCase splitting later, maybe); squiggle underline reuses the
  diagnostics rendering.

### 21. JSON + Shader syntax coloring — Phase 2.5
- Two new `ISyntaxClassifier`s registered in `SyntaxClassifiers.ForPath`:
  - **JSON** (`.json`, `.asmdef`, `.uxml`? no — xml later): keys, strings,
    numbers, keywords (true/false/null), punctuation.
  - **Shaders** (`.shader`, `.hlsl`, `.cginc`, `.compute`): ShaderLab
    blocks + HLSL keywords/types/comments/strings/numbers.
- Heuristic lexers in the CSharpClassifier style; no semantic module needed.

## Order of work
1. ~~Phase 0 quick wins~~ ✓  2. ~~Phase 1 Roslyn trio~~ ✓
3. ~~History window~~ ✓  4. ~~Snippets~~ ✓
5. Generators (magic methods, overrides)
6. JSON/Shader classifiers + auto-save toggles (small batch)
7. Find/Replace in Files
8. Inspection + static-method execution
9. Git integration + branch tree
10. Spell checking
Release checkpoints at Cary's call between any of these.
