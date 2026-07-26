---
tags: [project-log]
---

# Project Log — ADKOM Text Editor

Chronological history of features, defects, and decisions. Newest entries at
the bottom. See [[Project State]] for the current snapshot.

## Pre-history (before 2026-07-24 session)

- Package skeleton authored (`com.adkom.text-editor`, Editor-only asmdef) with
  the v1 editor: dockable UIToolkit window, New/Open/Save/Save As, dirty
  guards, external-change detection, EOL/BOM preservation, word wrap, status
  bar, `ITextFormatter` passthrough, and a reserved gutter.
- At that time the package was its own git repo with the dev Unity project
  outside it, linked by a directory junction. **This architecture was later
  replaced** (see 2026-07-24: repo creation).

## 2026-07-24 — v1 verification and first bug

- **Bug: text invisible after opening a file.** Root cause: the USS rule
  `-unity-font-definition: initial` cleared the inherited editor font, leaving
  both font and font-definition NULL — glyphs had nothing to render with.
  Fixed by removing the rule. Lesson: `initial` in UITK clears rather than
  resets to default.

## 2026-07-24 — Repo creation and UPM distribution

- Public repo created: `adkom-games/ADKOM_TextEditor`. **Decision:** one repo
  holding the whole dev project with the package embedded at
  `Packages/com.adkom.text-editor`; the earlier package-repo-plus-junction
  scheme was absorbed (the nested `.git` was removed; its local history is
  recorded here).
- **Defect (issue #1): UPM install failed on clean machines.** The Unity
  `.gitattributes` template marked binaries as Git LFS, but the repo never
  used LFS; git-lfs machines aborted UPM's checkout. Fixed by neutering the
  LFS macro to `-text`. Also moved `.gitattributes` to repo root (attribute
  macros are only legal there).
- **Decision: package-only `upm` branch** (via `git subtree split`, kept in
  sync by a GitHub Action on every push to main) so consumers don't clone the
  full Unity project. Install: `...git#upm`, pinned: `...git#0.1.0` (tag
  points at the split commit). Tagged **0.1.0**.

## 2026-07-24 — Feature wave 1

- **Assets context menu** ("Open in ADKOM Text Editor"), initially `.cs` only;
  later widened to any text asset (TextAsset/MonoScript types + an extension
  list) and changed to always open a new tab (switch if already open).
- **Multi-file tabs** with per-tab dirty guards, middle-click close, and
  serialization across domain reloads. External-change checks run on tab
  activation as well as window focus.
- **Fix:** status bar was pushed off-window by tall documents (`flex-grow`
  with auto basis); pinned with `flex-basis: 0; min-height: 0` and
  `flex-shrink: 0` on the status bar.
- **Line numbers** (Lines toggle) in the gutter, scroll-synced. Later fixed to
  stay aligned under word wrap by measuring each logical line's height and
  padding blank gutter rows for wrapped continuations.
- **Window title** prefixed "ATE - filename" so the dock tab is identifiable.

## 2026-07-24 — Syntax highlighting (and the editor-hang saga)

- **Architecture decision:** an editable TextField cannot render rich text, so
  highlighting is a rich-text overlay Label showing `CSharpFormatter` output
  while the field's glyphs go transparent (caret/selection stay).
- **Defect (issue #2): editor hard-hang on opening .cs.** The overlay was
  parented inside the editable TextElement; in Unity 6 nested TextElements
  join the parent's text generation, which loops. First fix (reparent to
  TextInput) failed because TextInput is *also* a TextElement. Final fix: the
  overlay lives entirely outside the TextField in a rect-synced clipping
  container. The tokenizer was exonerated by running it standalone via the
  dotnet CLI. Highlighting is capped at 200k chars as engine-load insurance.
- **Lesson:** never nest any TextElement inside another in Unity 6.

## 2026-07-24 — Themes, settings, and editing ergonomics

- **Color themes:** `HighlightTheme` with dark/light palettes per theme.
  Built-ins: VS Code (Dark+/Light+), JetBrains Rider (Darcula/IntelliJ
  Light), later Visual Studio (VS Dark/Light). Applied to tokens, background,
  gutter, caret. **Light/Dark mode** selector: Auto (follow editor skin,
  default) / Dark / Light.
- **Settings tab:** a gear toolbar button opens Settings as a single-instance
  document tab (replacing the toolbar's Theme/Lines/Wrap controls). Gear
  behavior: open → bring to front → close when already frontmost.
- **Tabs as spaces:** files load with tabs expanded (column-aware) at the
  configurable Tab Size; documents remember whether the file indents with
  tabs and convert leading indentation back on save so formatting is
  preserved. Tab key inserts spaces to the next tab stop; arrows jump leading
  indentation in tab-size steps.
- **Keyboard layouts:** Visual Studio, Rider, and later VS Code defaults for
  supported commands (save/save all, new/open, close/next/prev tab, duplicate
  /delete/move line, toggle comment, indent/unindent, settings).
- **Defect: indentation looked like it did nothing.** Two causes: the overlay
  drew *over* the field, hiding the caret (fixed by drawing the overlay
  *under* the transparent-glyph field — background moved to the row, input
  made transparent); and programmatic caret placement was clamped by the text
  engine's pre-edit state (fixed by re-asserting the caret a frame later).

## 2026-07-24 — Performance saga and virtualization

- **Defect: seconds-per-keystroke typing.** Profiled in stages: first the
  wrap-aware gutter (measuring every logical line per keystroke, twice) —
  debounced; then the overlay's whole-document rich-text re-shape — made
  asynchronous (plain text while typing, colors on idle); finally the
  editable TextField's own whole-document re-shape proved to be the floor
  (~176ms @ 1k lines, ~930ms @ 5k). A gap buffer was considered and
  rejected: string splices are microseconds; TEXT SHAPING was the cost.
- **Decision: full virtualization (CodeView).** The document renders as
  pooled per-line Labels (only visible lines laid out); caret, selection,
  mouse, keyboard, clipboard, and undo/redo implemented in-house; the
  formatter emits per-line tags so lines colorize independently. Result:
  14.7ms keystroke-to-frame on 5,000 lines (63x). Trade-off: word wrap
  removed (long lines scroll horizontally); the TextField-era overlay,
  gutter, and wrap machinery were deleted.

## 2026-07-25 — Application polish wave (0.4.0)

- **Native menu bar** (File/Edit/View/Tools/Window/Help via GenericMenu,
  built per click for live state) replaced the toolbar buttons; tab
  right-click context menu (Save/Save As/Close/Close Other Tabs).
- **No auto-Untitled:** empty window is a valid state (hint + "No file
  open" status); Tools menu focuses the existing window instead of
  creating duplicates.
- **Automatic updates:** GitHub latest-release polling (min once/day,
  configurable days, disable-able), console announcement, idle-time
  install dialog with a settings-synced checkbox; embedded dev copies
  never auto-install. GitHub Releases are now the update channel.
- **External Script Editor:** ATE registers via IExternalCodeEditor
  (installation path = Unity itself); text files open at line/column;
  configurable fallback editor receives solutions/binaries/sync.
- **Fonts:** monospace by default (bundled RobotoMono; fixed the
  non-mono inheritance), any OS font + size in Settings, browser-style
  zoom (Ctrl+wheel, Ctrl+±, Ctrl+0).
- **Fixes:** cumulative gutter drift (per-row gutter labels — one
  spacing authority); status bar hardening (empty-state text,
  scrollable settings pane); focus on new/switched documents.
- **Smooth scrolling** (optional, same per-notch velocity, exponential
  ease).

## 2026-07-25 — Semantic highlighting and navigation (0.4.1)

- **Span-based highlighting engine**: classifiers emit per-line
  (start,len,TokenClass) spans; markup built lazily per visible row;
  wrap slicing clips spans. Replaced CSharpFormatter/ITextFormatter.
- **Heuristic identifier classification** (types/methods/variables/
  params) + identifier colors in all six palettes.
- **Companion package com.adkom.text-editor.semantics** (Roslyn):
  asmdef gated on ADKOM_TE_ROSLYN (set by main-package bootstrap when
  Roslyn is detected — avoids duplicate-DLL conflicts, e.g. with the
  MCP's copies). Builds cached CSharpCompilations from
  CompilationPipeline data; bg-thread classification (~80ms warm,
  515 spans) replaces heuristics version-checked.
- **Go to Definition**: Ctrl+Click / F12 / Ctrl+B; cross-file, locals,
  metadata provenance. Distributed via new upm-semantics split branch.
- **Known gap**: no bundled Roslyn for projects without one (module
  stays dormant); planned: bundled DLLs + installer.

## 2026-07-26 — Opt-in semantics with auto-install (0.5.0)

- **Semantic Features setting (OFF by default)**: enabling drives
  SemanticSetup across reloads — module install (Client.Add
  #upm-semantics), bundled Roslyn copy (only when the project has none),
  compile-gate define. Roslyn 4.8 netstandard2.0 binaries (10 DLLs,
  ~14MB) ship inert in the module's RoslynBinaries~ with
  THIRD-PARTY-NOTICES.md (MIT, .NET Foundation) satisfying
  redistribution. Decision: bundle rather than NuGetForUnity (Cary:
  no NuGet); prefer any Roslyn already present.
- Go to Definition without the feature now opens a dialog with
  one-click Enable and Install (was a transient status message).
- Ctrl+Alt+8 opens the ATE window ("Ctrl-Alt-ATE").
- Field-test pending: Client.Add and DLL-copy paths can't run in the
  dev project (module embedded, Roslyn present via MCP).

## 2026-07-26 — Overview & selection wave (0.5.1)

- **Console pane** (bottom, tab strip, closable, Window menu restores,
  on by default): AteConsole thread-safe sink; all ATE tool logs and
  status-bar messages route through it; status messages now pinned 5s
  in the bar (fixes the too-short-to-read complaint).
- **Minimap** between content and scrollbar: Painter2D single-mesh
  code-shape overview, syntax-colorized from the span classifier,
  batched per color; viewport indicator; click/drag jump; Window menu
  toggle, on by default (verified stale serialized layouts keep the
  initializer default — update-safe).
- **Double-click word selection** with whole-word snap drag.
- **Selection occurrence highlighting** on visible rows in a weaker
  color than the active selection.

## 2026-07-26 — Semantics in the box (0.6.0)

- **Merged the semantics module into the main package**: provider under
  Editor/Semantics (nested asmdef, same ADKOM_TE_ROSLYN gate),
  RoslynBinaries~ + THIRD-PARTY-NOTICES at package root. Consent flow
  (first-use dialog / Settings) now only copies bundled Roslyn when the
  project has none — the never-field-tested module Client.Add path was
  eliminated rather than tested. upm-semantics branch retired
  (semantics-0.2.0 tag kept for history). Download +~14MB, binaries
  inert until consented.

## 2026-07-26 — Upgrade-path fixes (0.6.1)

- **Field-tested the update path at last** — and found two defects.
  (1) Duplicate-assembly collision: projects upgrading to 0.6.0 with
  the old semantics module installed failed compilation and silently
  kept running 0.5.x while About said 0.6.0. Fixed: built-in semantics
  asmdef renamed ADKOM.TextEditor.Editor.Semantics + auto-removal of
  the obsolete module on load. (2) Client.Add was fire-and-forget;
  failures now log to the ATE console and show a dialog with the
  manual URL. Lesson: never ship a merged assembly under a name a
  previous optional package used.

## 2026-07-26 — Metadata view and modality policy (0.7.0)

- **"From metadata" view**: F12 on referenced-assembly symbols opens a
  Roslyn-generated signature stub as a virtual document
  (TextDocument.VirtualName/VirtualCSharp; deduped by title; caret on
  the invoked member).
- **Non-modal policy** (field-driven: modal file-changed dialog blocked
  Unity's main loop and the MCP): file-changed prompt is a banner with
  Reload/Keep Mine; async update-failure and semantics-compiling
  notices are console/status. Decision dialogs immediately after user
  actions stay modal by design.
- Console text selectable/copyable.

## Conventions

- Branch per feature/fix from main; merge with `--no-ff`; branches are kept.
- Defects get GitHub issues (#1 LFS install, #2 editor hang) even when fixed
  immediately.
- Every feature is verified live in the editor via the ai-game-developer MCP
  before merging to main.
