# Release Notes — ADKOM Text Editor

## 0.9.0 — 2026-07-26

### Features
- **Localized interface**: menus, settings, dialogs, prompts, and
  status messages follow Unity's Editor Language — Japanese, Korean,
  Simplified Chinese, and Traditional Chinese ship in the box.
- **Recent Files** (File menu): per project, most recent first, count
  configurable in Settings (default 5); missing files clean themselves
  up; "Clear Recent Files" empties the list.
- **Goto Line** (Ctrl+G): an emacs-style prompt in the status bar;
  numeric input, clamped to the file; line numbers not required.
- **Document context menu**: right-click in the editor for Go to
  Definition, Find Occurrences of the word under the cursor, clipboard,
  Save / Save As / Close Tab / Show in File Explorer, Find/Replace/
  Goto Line, and language-specific commands (C# comment toggle,
  Markdown mode switch).
- **Drag-and-drop tab reordering** — tabs move live as you drag.
- **Tabs survive closing the window**: the session (including each
  dirty tab's unsaved content) is restored when ATE reopens, even
  across editor restarts.
- **Deleted-file rescue**: when an open file vanishes from disk, a
  banner offers Keep Buffer / Close Tab — Save writes the file back.
- Menu items display their keyboard shortcuts for the active layout;
  clipboard shortcuts work with focus anywhere in the ATE window.
- Console and Minimap moved to the View menu (alphabetical with Line
  Numbers and Word Wrap); all four default ON for fresh installs.
- Settings: "Open Markdown Rendered" is joined by "Recent Files Count".

### Fixes
- Undo/redo grouping is now humanly predictable (VS Code model): one
  undo removes one word; groups break on Enter, paste, caret moves,
  direction changes, pauses, save, and hard size/age caps. The status
  bar reports "Undid N char(s)". No GitHub issue; design defect.
- English-language editors showed the entire UI in Japanese after the
  localization change — Unity's catalog loader falls back to the first
  PO file alphabetically when the current language has no catalog; an
  English identity catalog fixes it (issue #4).
- Go to Definition (F12/Ctrl+B/Ctrl+Click) now works inside "from
  metadata" views and chains stub to stub. No GitHub issue.
- The "Unsaved Changes" dialogs no longer use Unity's modal system:
  closing a dirty tab uses the non-modal in-window banner, and closing
  the window silently preserves dirty buffers in the session instead
  of prompting. No GitHub issue; modality-policy completion.

## 0.8.0 — 2026-07-26

### Features
- **Markdown support** for `.md` files: full syntax coloring in source
  mode, and a rendered mode with block-level WYSIWYG editing — click
  any block to edit its source inline; undo works across both modes.
  A transient MD/source button by the settings gear shows the current
  mode and switches per tab.
- **Formatting toolbar** for any `.md` tab: headings, bold, italic,
  strikethrough, inline code, links, images, bullet/numbered/task
  lists, blockquotes, code blocks, tables, and horizontal rules — one
  click formats your selection or the block you're editing, or inserts
  a ready-made template, in either mode.
- Strikethrough, images, task lists (☐/☑), and tables render and
  color everywhere Markdown does.
- New setting **"Open Markdown Rendered"** chooses the default view
  for `.md` files (source by default). Release notes after an update
  always open rendered — like this document.

## 0.7.1 — 2026-07-26

### Features
- After an update, the new version's release notes open in a focused
  tab (you may be reading this in one right now).
- While an update installs, the ATE window shows an ATE-only "Updating…"
  overlay that blocks editing so nothing is lost in the reload — Unity
  itself stays fully responsive.

## 0.7.0 — 2026-07-26

### Features
- Go to Definition on referenced-assembly symbols (UnityEngine, BCL)
  opens a generated "from metadata" signature view of the type, caret
  on the invoked member (virtual, C#-highlighted, deduplicated tabs).
- Console text is selectable and copyable.

### Fixes
- The modal file-changed-on-disk dialog froze the Unity editor's main
  loop (and background tooling) whenever the window regained focus
  with a changed file — replaced by an in-window banner
  (Reload / Keep Mine). No GitHub issue; field-reported.

### Changes
- Non-modal dialog policy adopted: async update failures and
  informational notices are console/status messages. Decision dialogs
  directly after user actions remain modal.

## 0.6.1 — 2026-07-26

### Fixes
- Upgrading from 0.5.x with the old semantics module installed broke
  compilation (duplicate assembly name), leaving 0.5.x code running
  while About reported the new version. The built-in semantics assembly
  is renamed and the obsolete module package is removed automatically.
  If you hit this on 0.6.0: remove "ADKOM Text Editor — Semantics
  Module" in the Package Manager, or simply install this update.
- Update installs are no longer fire-and-forget: failures are logged to
  the ATE console and shown in a dialog with the manual install URL.

## 0.6.0 — 2026-07-26

### Changes
- Semantic Features now ship inside the main package: no separate
  semantics module, no extra install URL. First use (or the Settings
  toggle) offers one-click setup; the bundled MIT-licensed Roslyn
  assemblies are copied only when the project has no Roslyn of its own
  (THIRD-PARTY-NOTICES included; binaries inert until consented).
- The com.adkom.text-editor.semantics package and its upm-semantics
  branch are retired — remove old installs of the module if present.
- Package download grows ~14MB (bundled Roslyn).

## 0.5.1 — 2026-07-26

### Features
- Syntax-colorized minimap between the document and the scrollbar:
  whole-file overview with viewport indicator, click/drag to jump
  (Window → Minimap; on by default).
- Console pane at the bottom of the window collecting all ATE messages
  and status-bar output, timestamped (closable; Window → Console; on by
  default). Status messages also stay in the bar for 5s now.
- Double-click selects the word under the cursor; dragging extends the
  selection whole-word at a time.
- Selecting text highlights all other occurrences in a weaker color.

## 0.5.0 — 2026-07-26

### Features
- Semantic Features setting (OFF by default) with fully automatic
  dependency installation: enabling it installs the semantics module
  via UPM and, if the project has no Roslyn, the module's bundled
  MIT-licensed Roslyn 4.8 assemblies (THIRD-PARTY-NOTICES included);
  existing Roslyn copies are preferred.
- Go to Definition without the feature enabled asks via a dialog with
  one-click Enable and Install (replacing a transient status message).
- Ctrl+Alt+8 opens the ATE window. Say it out loud.
- Semantics module 0.2.0 (bundled Roslyn binaries + notices).

### Notes
- Includes everything listed under 0.4.1 below, which was version-
  bumped but never tagged; 0.5.0 is its release vehicle.

## 0.4.1 — 2026-07-25 (not tagged; shipped in 0.5.0)

### Features
- Identifier syntax highlighting: types, methods, variables, parameters
  in theme-authentic colors (built-in heuristics; compiler-accurate
  with the semantics module).
- Symbol navigation with the new optional Roslyn semantics module
  (`#upm-semantics`): Ctrl+Click / F12 / Ctrl+B jumps to definitions
  across files and assemblies; metadata symbols report their assembly.
- First run of a newly installed version checks for updates immediately.

### Notes
- The semantics module activates only when a Microsoft.CodeAnalysis.
  CSharp assembly exists in the project (detected automatically).
- Forensic console log when automatic updates are disabled.

## 0.4.0 — 2026-07-25

### Features
- Native menu bar (File, Edit, View, Tools, Window, Help) replacing the
  toolbar buttons; right-click context menu on file tabs (Save, Save
  As, Close, Close Other Tabs).
- Selectable as Unity's External Script Editor: scripts and console
  entries open in ATE at the exact line/column; configurable fallback
  editor for solutions, binaries, and project sync.
- Automatic update checks (daily at most, configurable in days,
  disable-able): console announcement plus an idle-time install dialog
  showing current/new versions with a settings-synced checkbox.
- Configurable font (any OS font or bundled monospace) and size, with
  Ctrl+MouseWheel / Ctrl+'+' / Ctrl+'-' zoom and Ctrl+0 reset.
- Optional smooth scrolling (same per-notch velocity, animated).
- Find and Replace toolbar buttons became Edit-menu entries.

### Fixes
- Monospace font by default (was inheriting Unity's UI font).
- Line-number gutter drifted out of alignment deeper into files.
- Opening the ATE window no longer creates an Untitled document or a
  duplicate window; an empty window shows a hint instead.
- Status bar hardening: "No file open" placeholder; Settings tab
  scrolls on short windows instead of spilling over the bar.
- New/switched documents receive keyboard focus immediately.

## 0.3.0 — 2026-07-24

### Features
- Find and Replace in a modeless dialog, with all-tabs scope ("find in
  tabs" / "replace in tabs"): match case, whole word, normal or regex
  search (with $1 group replacements), wrap around, backwards direction.
  Keyboard: Ctrl+F / Ctrl+Shift+F; Ctrl+H / Ctrl+Shift+H (VS, VS Code)
  or Ctrl+R / Ctrl+Shift+R (Rider); F3 / Shift+F3 repeat. Toolbar Find
  and Replace buttons.
- Word wrap returned, native to the virtualized view: self-computed wrap
  points render each visual row independently, syntax colors split
  correctly across rows, the gutter blanks continuation rows, and
  Up/Down move by visual row. Word Wrap setting restored.
- Repository root README distinguishing the dev project from the
  package.

### Fixes
- Tab-stop-aligned navigation and Backspace/Delete extended to any
  whitespace run (was leading indentation only).
- Package files deleted from the working tree caused a black window;
  recovered from git (issue #3, closed — user error, no tooling defect).

## 0.2.1 — 2026-07-24

### Changes
- License changed to MIT.
- Public-facing README rewrite (Editor-only guarantee front and center).

## 0.2.0 — 2026-07-24

### Features
- Multiple open files as tabs (dirty guards, middle-click close, survive
  domain reloads).
- Project-window context menu opens any text asset; already-open files
  switch to their tab.
- External change detection on tab activation and window focus.
- C# syntax highlighting with an extensible per-language formatter API.
- Color themes: Visual Studio, VS Code, Rider (dark + light palettes)
  with an Auto/Dark/Light mode selector.
- Line numbers in a toggleable gutter.
- Settings as a document tab (gear button toggles open/front/close):
  theme, mode, line numbers, tab size, keyboard layout.
- Tabs render as spaces (configurable tab size); files keep their
  original tab/space indentation on save.
- Tab key inserts spaces to the next tab stop; navigation, Backspace,
  and Delete honor tab stops across any whitespace run.
- Keyboard layouts: Visual Studio, VS Code, Rider (save/save all,
  open/new, close/next/prev tab, duplicate/delete/move line, toggle
  comment, indent/unindent, settings).
- Fully virtualized editor (CodeView): only visible lines render, so
  keystroke cost is independent of file size (measured 14.7ms on a
  5,000-line file). Includes caret/selection/mouse/clipboard and
  undo/redo with typing coalescing.
- Window title reads "ATE - filename".

### Fixes
- UPM git-URL installs failed on clean machines ("update_ref failed" /
  missing objects): the repo declared Git LFS attributes without LFS
  objects. LFS was retired from the repository entirely (issue #1).
- Status bar pushed off-window by tall documents.
- Editor hard-hang when opening .cs files (highlight overlay nested in a
  TextElement) (issue #2).
- Invisible indentation and off-by-a-bit caret placement (overlay
  whitespace collapse; caret hidden under overlay).
- Tab key inserted a literal tab character via a duplicate key event.
- Tab moved focus to the toolbar instead of indenting.
- Seconds-per-keystroke typing in large files (per-line gutter
  measuring, whole-document re-shaping) — resolved by virtualization.
- Initial render only filled part of the viewport until scrolled.

### Removed
- Word wrap (incompatible with the virtualized view; long lines scroll
  horizontally).

## 0.1.0 — 2026-07-24

### Features
- Dockable UIToolkit editor window (Tools → ADKOM → Text Editor).
- New / Open / Save / Save As with unsaved-changes prompts.
- Ctrl+S / Ctrl+Shift+S shortcuts.
- External file-change detection with reload prompt.
- Word-wrap toggle; status bar (line:col, encoding, line endings).
- Preserves each file's line endings (CRLF/LF/CR) and UTF-8 BOM.
- ITextFormatter extension point and reserved line-number gutter.

### Fixes
- Text invisible after opening a file (USS cleared the editor font).
