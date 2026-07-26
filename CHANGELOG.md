# Changelog

All notable changes to this package are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com); versions follow semver.

## [0.5.0] - 2026-07-26

### Added
- Ctrl+Alt+8 (Cmd+Alt+8 on macOS) opens the ATE window.
- Semantic Features setting (OFF by default): enabling it installs
  everything automatically — the semantics module via UPM and, when the
  project has no Roslyn, the module's bundled MIT-licensed Roslyn
  assemblies (© .NET Foundation; see the module's THIRD-PARTY-NOTICES)
  copied into Assets/Plugins. Existing Roslyn copies are preferred and
  nothing is duplicated.

### Changed
- Go to Definition without semantic features now asks via a dialog
  (offering one-click Enable and Install) instead of a transient
  status-bar message; a dialog also explains when the module is still
  installing or compiling.

## [0.4.1] - 2026-07-25

### Added
- Syntax highlighting now colors identifiers — types, methods,
  variables, and parameters — with theme-authentic colors in all six
  palettes. A built-in heuristic classifier works everywhere; with the
  new optional semantics module installed the colors are
  compiler-accurate (Roslyn).
- Symbol navigation (requires the semantics module): Ctrl+Click any
  identifier, or press F12 (Visual Studio / VS Code layouts) or Ctrl+B
  (Rider), to jump to its definition — locals, parameters, members,
  and types across files and assemblies; symbols defined in referenced
  binaries report their assembly in the status bar.
- New companion package `com.adkom.text-editor.semantics` (install from
  the `upm-semantics` branch): builds real Roslyn compilations from
  Unity's CompilationPipeline (sources, defines, references), cached
  and incrementally updated, off the main thread. It activates only
  when a Microsoft.CodeAnalysis.CSharp assembly exists in the project
  (the main package detects Roslyn and enables the module's compile
  gate automatically).
- The highlighting engine is span-based internally (per-line classified
  spans instead of markup strings) — groundwork for more languages.
- The first run of any newly installed version checks for updates
  immediately (once), bypassing the daily schedule, so fresh installs
  are brought current right away. Automatic updates remain ON by
  default on clean installs.

## [0.4.0] - 2026-07-25

### Changed
- All source files are additionally wrapped in #if UNITY_EDITOR guards
  (belt-and-braces on top of the Editor-only assembly), so copies of
  package files pasted into a project's Assets folder can never break
  player builds.

### Fixed
- Status bar hardening: the empty window state shows "No file open"
  instead of a blank bar, and the Settings tab scrolls on short windows
  instead of its controls spilling over the status bar.
- Line numbers drifted out of alignment with code lines, worsening
  toward the bottom of the file: the gutter was one multi-line label
  whose natural line spacing differed subtly from the row height. Gutter
  numbers are now pooled per-row labels positioned with the same row
  math as the code lines.
- The editor now uses a monospace font (the editor's bundled RobotoMono,
  with OS monospace fallbacks) instead of inheriting Unity's UI font.

### Added
- Optional smooth scrolling (Settings, default on): wheel input animates
  the text view with an exponential ease toward the same per-notch
  distance as stepped scrolling, instead of jumping line by line.
- File tabs have a right-click context menu: Save, Save As…, Close, and
  Close Other Tabs (per-document dirty prompts; Cancel aborts the rest).
- Configurable editor font and font size (Settings): any installed OS
  font or the bundled monospace default; size 8–40. Zoom with
  Ctrl+MouseWheel or Ctrl+'+' / Ctrl+'-' (Cmd on macOS), Ctrl+0 resets —
  the same gestures as browsers and terminals. All layout metrics
  (wrap points, caret, gutter) recompute on change.
- ATE can be selected as Unity's External Script Editor (Preferences →
  External Tools): double-clicked scripts and console log entries open
  in the ATE window at the exact line and column. A configurable
  Fallback Editor (Settings and the External Tools pane) receives
  everything ATE doesn't handle — solutions, Open C# Project requests,
  binaries, and project-file sync — defaulting to the OS default
  application. Note: deep IDE integrations (debugger attach, solution
  sync extras) belong to the editor actually selected in Unity; the
  fallback receives plain open/sync calls.
- Automatic update checks: polls the GitHub latest release (via UPM git
  URL install) on a configurable schedule — daily at most, or every N
  days — announcing new versions in the console and, when the editor is
  idle, offering an install dialog showing current and new version
  numbers with an "automatic updates" checkbox synced to Settings.
  Settings additions: Automatic Updates toggle, Check Every (days), a
  Check for Updates Now button, and the installed version. Embedded
  (development) copies log availability but never auto-install.

### Changed
- The toolbar buttons were replaced by a standard menu bar — File, Edit,
  View, Tools, Window, Help — rendered with the platform's native menus
  on Windows, macOS, and Linux. Menus connect to existing features
  (file ops, undo/redo, clipboard, line ops, find/replace, view
  toggles, theme, tab list); Tools → Options… opens the Settings tab;
  Recent Files is a stub for a future release.

### Fixed
- Opening the ATE window no longer creates an Untitled document (and no
  longer creates a duplicate window): an empty window shows a hint and
  documents are created only by the user. Closing the last tab leaves
  the window empty instead of spawning a new Untitled.

## [0.3.0] - 2026-07-24

### Added
- Find and Replace toolbar buttons (after Save As).
- Find and Replace, including across all open tabs, in a modeless
  dialog: match case, whole word, normal or regular-expression search,
  wrap around, and backwards direction. Ctrl+F find / Ctrl+Shift+F find
  in tabs (all layouts); Ctrl+H / Ctrl+Shift+H replace (Visual Studio,
  VS Code) or Ctrl+R / Ctrl+Shift+R (Rider); F3 / Shift+F3 repeat the
  last search. Replace All reports counts; regex replacements support
  $1-style groups; replacements in the active tab are undoable.
- Word wrap is back, now native to the virtualized view: the editor
  computes its own wrap points (greedy word wrap, per-character width
  table) and renders each visual row independently — rendering, caret,
  clicks, and selection always agree. Syntax coloring splits correctly
  across wrapped rows; the gutter blanks continuation rows; Up/Down and
  PageUp/Down move by visual row; the horizontal scrollbar hides while
  wrap is on. The Word Wrap setting has returned to the Settings tab.

## [0.2.1] - 2026-07-24

### Changed
- License changed to MIT (was all-rights-reserved).
- README rewritten as public-facing copy, leading with the Editor-only /
  zero-shipping-impact guarantee.

## [0.2.0] - 2026-07-24

### Changed
- The editor is now fully virtualized: the document renders as pooled
  per-line elements and only visible lines are laid out, so keystroke
  cost no longer depends on file size. Measured: 14.7ms keystroke-to-
  frame on a 5,000-line file (was ~930ms). Caret, selection, mouse,
  keyboard, clipboard, and undo/redo (Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z,
  with typing coalescing) are implemented by the new CodeView; syntax
  colors update live per line (no more plain-text flash while typing).
- Word wrap is no longer available: the virtualized view scrolls long
  lines horizontally instead. The Wrap setting has been removed.

### Changed (virtualized view)
- Tab-stop behavior now applies to ANY whitespace run, not just leading
  indentation: Left/Right arrows jump to tab-stop-aligned columns
  (bounded by the run), and Backspace/Delete remove whitespace back/
  forward to the nearest tab stop.

### Fixed
- Colorization is now asynchronous: while typing, the field's own glyphs
  show in plain theme color and the syntax-colored overlay re-renders
  ~150ms after typing pauses, removing the whole-document rich-text
  re-shape from the keystroke path.
- Typing was extremely slow (seconds per keystroke) in large files with
  line numbers and wrap enabled: the gutter re-measured every logical
  line on every keystroke (twice). The wrap-aware measure is now
  debounced until typing pauses (~200ms); keystrokes cost <1ms.
- Indentation was invisible in highlighted files and clicks placed the
  caret slightly off: the highlight overlay used white-space: normal,
  which collapses space runs in Unity 6, shifting every rendered glyph
  off the real text layout. The overlay now preserves whitespace
  (pre / pre-wrap).
- Tab key inserted a literal tab character instead of spaces (and skewed
  click-to-caret mapping afterwards): Unity delivers a second,
  character-only key event for Tab which bypassed the handler; it is now
  swallowed. Tab correctly advances to the next tab stop (1..TabSize
  spaces depending on column).
- Indentation (Tab key / typed spaces) appeared to do nothing in highlighted
  files: the highlight overlay covered the caret, and programmatic caret
  placement was clamped by the text engine. The overlay now draws under the
  transparent-glyph field (caret and selection render above the colors) and
  caret placement is re-asserted a frame later.
- Status bar no longer gets pushed off the bottom of the window when the
  loaded document is taller than the visible editor area.

### Added
- Visual Studio color theme (VS Dark/Light) and VS Code keyboard layout
  (Ctrl+W close, Ctrl+PageUp/Down tabs, Shift+Alt+Up/Down copy line,
  Ctrl+Shift+K delete line, Ctrl+, settings). Themes now define selection
  colors.
- The settings gear now toggles: opens the Settings tab, brings it to the
  front if backgrounded, closes it if already frontmost.
- Project Log added to Documentation~ (chronological history of features,
  defects, and decisions); Project State refreshed.
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
