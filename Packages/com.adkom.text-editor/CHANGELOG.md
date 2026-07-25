# Changelog

All notable changes to this package are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com); versions follow semver.

## [Unreleased]

### Fixed
- Status bar no longer gets pushed off the bottom of the window when the
  loaded document is taller than the visible editor area.

### Added
- Tabs are rendered as spaces at a configurable Tab Size (Settings); on
  save, files that originally indented with tabs are converted back so
  their formatting is preserved. The Tab key inserts spaces to the next
  tab stop (multi-line selections indent/unindent), and Left/Right arrows
  jump through space indentation in tab-size steps.
- Keyboard command layouts (Settings → Keyboard Layout): Visual Studio
  and Rider defaults for the commands the editor supports — save/save
  all, new/open, close tab, next/previous tab, duplicate line, delete
  line, move line up/down, toggle line comment, indent/unindent, and
  settings (Rider).
- Settings tab: a gear button in the toolbar opens Settings as a document
  tab (single instance; switches to it if already open). Color Theme,
  Light/Dark Mode, Line Numbers, and Word Wrap moved there from the
  toolbar.
- C# syntax highlighting (keywords, strings, chars, comments, numbers,
  preprocessor) via the `ITextFormatter` pipeline, chosen per tab by file
  extension; rendered by a rich-text overlay. Files over 200k chars fall
  back to plain rendering. Language coverage is extensible.
- Color themes with two built-in palettes — VS Code (Dark+/Light+) and
  JetBrains Rider (Rider Dark/IntelliJ Light) — selectable from the new
  Theme toolbar menu, applied to token colors, editor background, text,
  gutter, and caret. A light/dark mode selector in the same menu chooses
  Auto (follow the Editor skin, default), Dark, or Light; both choices
  persist via EditorPrefs.
- Line numbers in the gutter, toggled by the new "Lines" toolbar button;
  scroll-synced with the text. (Numbers are per logical line, so they can
  drift beside wrapped lines when Wrap is on.)
- Multiple open files as tabs: New/Open create tabs, opening an
  already-open file switches to its tab, per-tab dirty guard on close
  (middle-click or × to close). Open tabs survive domain reloads.
- Project window context menu item **Assets → Open in ADKOM Text Editor**
  for any text asset (scripts, TextAssets, shaders, USS/UXML, markdown,
  configs, …); reuses the existing editor window when one is open.

### Changed
- Context-menu/API opens always create a new tab (unless the file is
  already open, which switches to its tab) instead of replacing the
  current document.
- External file-change detection now also runs when a tab is activated,
  not just when the window regains focus.

## [0.1.0] - 2026-07-24

### Added
- Initial release: dockable UIToolkit text editor window (Tools → ADKOM → Text Editor).
- New / Open / Save / Save As with dirty-state guard dialogs.
- Ctrl+S / Ctrl+Shift+S shortcuts.
- External file-change detection with reload prompt.
- Word-wrap toggle; status bar (line:col, encoding, EOL).
- EOL and UTF-8 BOM preservation on save.
- `ITextFormatter` extension point (plain-text passthrough) and reserved
  line-number gutter for future releases.
