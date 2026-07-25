# Release Notes — ADKOM Text Editor

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
- Status bar pushed off-window by tall documents.
- Editor hard-hang when opening .cs files (highlight overlay nested in a
  TextElement).
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
