# Changelog

All notable changes to this package are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com); versions follow semver.

## [Unreleased]

### Fixed
- Indentation guides were invisible: character-width measurement
  returned 1px for spaces (the measurer trims trailing whitespace), so
  every guide was crushed against the gutter. Guides now sit at true
  indent columns and are slightly more visible.
- Smooth scrolling shimmer: the ease landed on fractional pixel
  offsets, which rasterized the input field and the color overlay
  differently (a 1px color mis-registration). Every animation frame
  now snaps to a whole pixel.
- Folded regions now show the whole collapsed shape — the header line
  ends with a dimmed "⋯ }" instead of a bare "{".

### Changed
- Tabs stay on a SINGLE line: the strip clips at the edges, and scroll
  arrows appear on its left and right whenever tabs overflow (the
  active tab auto-scrolls into view; the tab-list dropdown stays at
  the far right).
- Tabs are colored uniformly with the color chosen in Settings (the
  per-tab random shade variation was retired as too busy); the active
  tab stands out as a brighter, fully opaque version with an accent
  top border.
- Menu-bar buttons get more side padding; hovering/pressing shows only
  the left and right edges of the selection so buttons look bounded on
  the sides.

### Added
- Closing the ATE window with unsaved documents now surfaces a single
  NON-MODAL floating notice (never blocks the editor or background
  tooling) listing the documents, with one-click "Save All Now" or
  "Keep in Session" — the buffers are already persisted, so ignoring
  it is always safe. Re-entrant closes reuse the one open notice. As
  a safety net, reopening the window with dirty session buffers shows
  a "N document(s) have unsaved changes from your last session" banner
  with Save All / Dismiss.
- Auto-Reload Changed Files (Settings, default off): files that change
  on disk reload automatically when the buffer has no unsaved edits;
  dirty buffers still get the banner so edits are never lost silently.
- ADKOM Text Editor appears in every dock's "Add Tab" menu (tab
  right-click and ⋮): picking it docks ATE as a sibling tab of that
  pane, or focuses the already-open ATE window. (The Add Tab list is a
  fixed set of built-in panes, so this hooks the editor's internal
  menu-population event; a Window menu entry was added as well.)
- Rendered Markdown now displays standalone images: local paths
  (relative to the document or absolute) load inline with the alt text
  as a caption; missing files show a placeholder. Remote URLs are not
  fetched.
- Minimap: for documents taller than the minimap strip, the code
  graphics and viewport rectangle were vertically compressed into the
  top of the strip (proportionally worse the bigger the file), while
  click navigation used the full strip (issue #7). Sampled rows now
  spread across the whole strip.

### Added
- Quick Open (Ctrl+, / Ctrl+P VSCode / Ctrl+T Rider): a centered
  overlay that fuzzy-lists open tabs and recent files instantly and
  every text file under Assets/ and Packages/ once you type a filter;
  Up/Down navigate, Enter or click opens (recorded in navigation
  history), Escape dismisses. Also in the File menu.
- Bookmarks (Ctrl+Alt+K toggle, Ctrl+Alt+N / Ctrl+Alt+P next and
  previous with wrap-around, Clear Bookmarks in the Edit menu):
  per-document line bookmarks shown as an orange line number in the
  gutter; they shift with edits above them, and jumps are recorded in
  navigation history.
- Drag and drop of selected text: press inside the selection and drag
  to move it to the drop point — one undo step; hold Ctrl to copy
  instead. The dropped text stays selected; a simple click inside the
  selection still just places the caret.
- Semantic refactoring commands (Semantic Features required for the
  first two): Rename Symbol (F2 / Shift+F6 Rider) renames every
  in-document occurrence via the status-bar prompt as one undo step;
  Find All References (Shift+F12 / Alt+F7 Rider) lists every use
  across the symbol's assembly in the console with path:line
  previews; Format Document (Shift+Alt+F / Ctrl+Alt+L Rider)
  re-indents from brace depth — string/comment-aware, preserving
  content and blank lines, one undo step.
- Code folding: brace-delimited regions collapse and expand from
  clickable gutter arrows or Ctrl+Shift+[ / Ctrl+Shift+] (Unfold All
  in the View menu). Folds ride the virtualized row model, shift with
  edits made above them, survive typing on their header line, reveal
  when an edit touches the hidden body, and unfold automatically when
  the caret lands inside (search, Goto Line, Go to Definition).
- Indentation guides: a faint vertical bar per indent level, spanning
  blank lines; toggle in the View menu (on by default).
- Word-based autocomplete: a popup of prefix-matched words harvested
  from the current document AND every other open tab. Appears while
  typing (2+ word chars) or on demand with Ctrl+Space; Up/Down
  navigate, Enter/Tab accept (one undo step), Escape dismisses,
  further typing refines. Case-matching candidates rank first, and
  the active language's keywords (all of C# today) are offered as
  first-class candidates via the classifier's ICompletionKeywords
  capability — future languages join automatically.
- Tab strip upgrades: a dropdown button at the right end of the strip
  lists every open tab (numbered, dirty-starred, active checked) and
  jumps on pick; the document context menu opens with a "Tabs" submenu
  doing the same; and tabs are colorized — each tab renders a stable
  per-document shade of a base color chosen with the new "Tab Color"
  RGB selector in Settings.
- Multi-caret editing: Alt+Click adds/removes carets; Add Next
  Occurrence (Ctrl+D VS Code, Alt+J Rider, Shift+Alt+. VS) grows a
  selection per press; Select All Occurrences (Ctrl+Shift+L VS Code,
  Ctrl+Alt+Shift+J Rider, Shift+Alt+; VS); Add Caret Above/Below
  (Ctrl+Alt+Up/Down) for column-style editing. With multiple carets:
  typing, Backspace/Delete, Enter, and paste apply at every caret as
  ONE undo step (paste distributes line-per-caret when counts match);
  Escape or a plain click collapses to the primary caret. Extra
  carets and their selections render live.
- Editing must-haves, batch 2: auto-closing brackets/quotes (openers
  insert the pair, closers type over, Backspace removes empty pairs,
  selections get wrapped; "Auto-Close Brackets" setting, on by
  default); brace matching (caret-adjacent bracket and its match
  highlighted; Go to Matching Bracket — Ctrl+] VS, Ctrl+Shift+  VS Code, Ctrl+Shift+M Rider); Toggle Block Comment /* */
  (Ctrl+Shift+/ or Shift+Alt+A); Expand/Shrink Selection
  (caret → word → line → bracket block → document; Shift+Alt+Right/
  Left, Rider Ctrl+W / Ctrl+Shift+W); and Navigate Back/Forward
  through caret history across tabs (Ctrl+- / Ctrl+Shift+- VS,
  Alt+Left/Right VS Code, Ctrl+Alt+Left/Right Rider), recorded on
  Goto Line, Go to Definition, and external opens.
- Editing must-haves, batch 1: word-wise delete (Ctrl+Backspace /
  Ctrl+Delete); Cut/Copy with no selection act on the whole current
  line; Insert Line Above/Below without splitting (per-keymap
  bindings: VS Ctrl+Enter above / Ctrl+Shift+Enter below, VS Code the
  reverse, Rider Shift+Enter below / Ctrl+Alt+Enter above); Join
  Lines (Ctrl+J); Select Line (Ctrl+L in the VS Code layout);
  Transform UPPERCASE/lowercase and Sort Selected Lines (Edit menu);
  and per-project save cleanups — "Trim Trailing Whitespace on Save"
  and "Ensure Final Newline on Save" (Settings, off by default).
- Scripting API: `ADKOM.TextEditor.Scripting.AteApi` is a stable,
  semver-governed surface for editor scripts — open window/files,
  `NewDocument`, `Documents`/`ActiveDocument` handles (`GetText`,
  `SetText`/`ReplaceRange`, `GoTo`, `Activate`, `Save`, `Close`), and
  events: `documentOpened/Closed/Saved`, `activeDocumentChanged`,
  `textChanged` (debounced). Edits to the active document are one undo
  step; edits to background documents are documented as not undoable.
  See `Documentation~/Scripting.md`. Accidental public members on
  internals (CodeView edit/hit-test helpers) are now `internal` — the
  facade is the only supported scripting surface.
- Scripting docs: a prominent "Things you must know" section (domain
  reloads erase event subscriptions — subscribe from
  [InitializeOnLoad]; handles expire; background edits not undoable;
  async dirty-close; modal Save As on untitled; debounce; no nesting;
  main thread; virtual tabs), plus an importable Package Manager
  sample ("Scripting (AteApi)") demonstrating every API member as
  working menu commands.

### Changed
- Internal: the main window class is decomposed into partial classes by
  concern (Commands, Menus, Tabs, Session, Banners, ContextMenus,
  Semantics, Api) — 2,300 lines down to 1,300 in the core file, pure
  code motion verified behavior-identical by the full regression
  battery. The recent-files list is parsed once and cached (was
  re-read from EditorPrefs on every File-menu open), and the tab strip
  skips rebuilding when nothing visible changed.
- Keyboard commands are now defined in a single command table
  (bindings, handlers, and menu shortcut hints in one place per
  keymap), removing the triple definition that let labels drift from
  behavior. Visible fix: the VS Code and Rider layouts now display
  Redo's canonical Ctrl+Shift+Z in menus (Ctrl+Y still works in
  VS Code). Behavior is otherwise unchanged — verified by a
  54-assertion binding matrix across all three keymaps.
- Settings scoping audit: settings that describe the project are now
  stored per project instead of machine-wide — Tab Size (indentation
  convention), Semantic Features (consent to install Roslyn into that
  project), Automatic Updates and Check Every (days) (the package
  install is per project), and the file dialog's remembered directory.
  Existing values migrate automatically. User-preference settings
  (keymap, font, theme, smooth scrolling, Markdown default view,
  recent-files count, fallback editor) remain machine-wide, as does
  the update-check timestamp (a per-machine GitHub rate limiter).

### Fixed
- Large-file editing: undo/redo now stores range-based deltas (only
  the text each edit group inserted and replaced) instead of full
  document snapshots — undo memory scales with edit size, never file
  size. Also fixes a latent defect the snapshot model hid: undo
  history is now scoped per document (swapped on tab switch), so
  Ctrl+Z in one tab can no longer restore another tab's text. Undo
  grouping behavior is unchanged.
- Defect sweep (from the 2026-07-26 code review): dirty buffers now
  autosave to the session every 30s, so an editor crash loses at most
  half a minute of unsaved work (previously only saved on window
  close); F3 with the Find dialog closed no longer creates and
  destroys a throwaway window per keypress; background Go to
  Definition / semantic results are dropped if the window was closed
  mid-resolve; silent failures (unreadable session, unrestorable tab,
  Roslyn source/reference load) now leave console breadcrumbs; path
  comparisons unified on one normalizer; the active-document accessor
  is clamped defensively; metadata-stub staleness documented.
- The release-notes tab (and first-run update check) could be silently
  skipped after an update: the "last seen version" was stored
  machine-wide, so whichever project ran a new version first suppressed
  it for every other project. Now tracked per project, with migration
  from the old key (issue #5).

## [0.9.0] - 2026-07-26

### Added
- Localization: all user interface text (menus, settings, dialogs,
  banners, prompts, status messages, Find/Replace, the update dialog)
  goes through Unity's editor localization (L10n.Tr) and follows the
  Editor Language selected in Preferences. Ships with Japanese,
  Korean, Simplified Chinese, and Traditional Chinese catalogs;
  English is the source language. Console/diagnostic log text
  intentionally stays English for supportability.
- Drag-and-drop tab reordering: left-drag a file tab along the tab bar
  and it moves live as you cross neighboring tabs' midpoints (the
  dragged tab dims while in flight). A plain click still switches; the
  reordered layout persists through the tab session.
- Right-click context menu inside the document area: Go to Definition
  and "Find Occurrences of '<word>'" / "Find in Tabs" for the symbol
  or selection under the cursor (pre-filling the Find dialog), the
  clipboard set, Undo/Redo, Save / Save As / Close Tab /
  Show in File Explorer, Find/Replace/Goto Line — plus
  language-specific entries (C#: Toggle Comment, Go to Definition;
  Markdown: switch rendered/source mode). Right-clicking outside the
  selection moves the caret there first, like other editors.
- Goto Line (Edit menu, Ctrl+G): an emacs-style prompt appears in the
  status bar ("Goto Line:" plus an inline numeric field). Enter jumps,
  Escape or clicking away cancels; the destination is clamped to the
  file's line range; visible line numbers are not required. The
  status-bar mini-buffer is generic and will host future commands.
- File → Recent Files: the most recently opened files (per project,
  newest first, deduplicated), each entry reopening its file; missing
  files are dropped from the list with a console note. "Clear Recent
  Files" empties it. The list length is configurable in Settings
  ("Recent Files Count", default 5, 1-30).

- When an open tab's file is deleted from disk, a non-modal banner asks
  whether to keep the buffer or close the tab. Keeping marks the buffer
  dirty so the unsaved-changes guards protect it, and Save writes the
  file back to disk — sometimes that saves the day.
- Open tabs survive closing the ATE window: the session (file tabs +
  active tab) is saved on close and restored when the window reopens,
  including across editor restarts. Files missing by then are skipped.

### Fixed
- English-language editors showed the entire UI in Japanese after the
  localization change: Unity's per-assembly catalog loader falls back
  to the first PO file alphabetically when the current language has no
  catalog. An en.po identity catalog makes English resolve explicitly
  (issue #4).
- Undo/redo grouping is now humanly predictable (VS Code model). Typing
  coalesces per word — one undo removes one word, not minutes of
  typing. Groups also break on: Enter, selection replacement, paste,
  backspace/delete direction changes, moving the caret between edits,
  a 0.75s typing pause, save, window focus loss, and hard caps
  (100 chars / 5s) so a group can never grow unbounded. Backspace and
  forward-delete runs each chain as their own group. The status bar
  reports "Undid N char(s)" so the step size is visible.
- Go to Definition (F12 / Ctrl+B / Ctrl+Click) now works inside "from
  metadata" views: the stub remembers which real file it was opened
  from and resolves symbols against that compilation — chaining from
  stub to stub works, and stubs get semantic coloring too.

### Changed
- The "Unsaved Changes" dialog no longer uses Unity's modal system.
  Closing a dirty tab shows the non-modal in-window banner
  (Save / Discard / Cancel; navigating away cancels); Close Other Tabs
  raises one banner for the whole batch (Save All / Discard All /
  Cancel). Closing the ATE window shows no dialog at all: dirty tabs
  persist their unsaved content in the session and come back dirty
  when the window reopens — nothing is lost, nothing blocks Unity.
  (Remaining Unity dialogs — Help→About and the semantics
  enable-and-install prompt — are direct responses to a click and stay
  modal per the modality policy.)
- Menu items now display their keyboard shortcuts (matching the active
  keyboard layout) — Cut Ctrl+X, Copy Ctrl+C, and friends across the
  File, Edit, Tools, and Window menus.
- Clipboard and Select All shortcuts work with focus anywhere in the
  ATE window (menu bar, tab bar, gutter), not just inside the code
  view. Text inputs (settings fields, Markdown block editor) and the
  selectable console keep their own handling.
- Console and Minimap moved from the Window menu to the View menu,
  where all four view toggles now sit alphabetically: Console, Line
  Numbers, Minimap, Word Wrap.
- All four view toggles default ON for fresh installations. Existing
  windows keep whatever was already configured (settings are preserved
  by Unity's layout serialization).

## [0.8.0] - 2026-07-26

### Changed
- The MD/source toggle now shows the CURRENT mode ("MD" while rendered,
  "</>" while in source) instead of the mode it would switch to — the
  action label read as the wrong state. The tooltip names the action.

### Added
- Markdown support (.md): syntax coloring in source mode (headers,
  emphasis, code spans/fences, links, lists, quotes, rules mapped onto
  the theme palette), and a rendered mode with block-level WYSIWYG
  editing — headers, paragraphs, lists, quotes, code blocks, and rules
  render styled; click any block to edit its source inline (Ctrl+Enter
  or focus-out commits, Escape cancels), with edits applied through the
  code view so undo/redo and dirty tracking work. A transient toggle
  left of the settings gear (MD ⇄ source) appears only while a .md tab
  is active; the mode is remembered per document.
- Markdown formatting toolbar: while a .md tab is active (either mode),
  a button strip appears left of the MD/source toggle — one button per
  element type (H1–H3, bold, italic, strikethrough, inline code, link,
  image, bullet/numbered/task lists, blockquote, code block, table,
  horizontal rule). In rendered mode buttons act on the block being
  edited (wrapping the selection or transforming its lines) or append a
  new template block when no block editor is open; in source mode they
  act on the code view directly — wrapping the selection, transforming
  the selected lines, or inserting after the current line. Always
  through the undo-tracked path.
- Settings: "Open Markdown Rendered" — the default view for .md files
  when opened (rendered/WYSIWYG when on, source when off; off by
  default). The per-tab MD/source toggle still switches freely. The
  release-notes tab shown after an update always opens rendered (with
  full Markdown treatment: coloring, toolbar, block editing) regardless
  of the setting.
- Markdown feature parity across source coloring, rendering, and the
  toolbar: strikethrough (~~text~~), images (![alt](url)), task lists
  (- [ ] / - [x] render as ☐/☑), and tables (| cells |, header row
  bold with a separator-aware grid).

## [0.7.1] - 2026-07-26

### Added
- While an update is installing, the ATE window shows an ATE-only modal
  overlay ("Updating…") that blocks editing and commands — edits during
  the package swap would be lost in the reload. Unity itself is never
  blocked (per the modality policy); the overlay clears on failure or
  is replaced by the reload on success.
- After an update, the new version's release notes open in a focused
  tab (raw markdown text from the packaged RELEASE-NOTES.md). Fresh
  installs are not interrupted.

## [0.7.0] - 2026-07-26

### Added
- Go to Definition on symbols defined in referenced assemblies (e.g.
  UnityEngine types) now opens a "from metadata" view: a generated C#
  signature stub of the containing type, with the caret on the invoked
  member. Virtual documents are C#-highlighted and deduplicated by
  title.
- Console text is selectable and copyable (Ctrl+C).

### Changed
- Dialogs that can appear without a user decision no longer block the
  editor: the file-changed-on-disk prompt is a non-modal banner with
  Reload / Keep Mine buttons (the modal froze Unity's main loop — and
  background tooling — whenever the window regained focus with a
  changed file); the async update-failure dialog and the
  "semantics still compiling" notice are console/status messages.
  Decision dialogs that immediately follow a user action (unsaved
  changes, Enable & Install consent, About) remain modal by design.

## [0.6.1] - 2026-07-26

### Fixed
- Upgrading from 0.5.x with the old semantics module installed broke
  compilation (duplicate assembly name), leaving old code running while
  About reported the new version — no minimap or console. The built-in
  semantics assembly is renamed (ADKOM.TextEditor.Editor.Semantics) so
  it never collides, and the obsolete module package is removed
  automatically on load if present.
- Update installs were fire-and-forget: a failed Client.Add was silent
  and the project stayed on the old version with no indication. The
  install request is now monitored — success and failure are logged to
  the ATE console, and failures show a dialog with the manual install
  URL.

## [0.6.0] - 2026-07-26

### Changed
- The semantics module is now part of the main package — no separate
  install, no `upm-semantics` branch. Semantic Features work out of the
  box: the first use (or the Settings toggle) offers one-click setup,
  copying the bundled MIT-licensed Roslyn assemblies only when the
  project has none. The package download grows ~14MB; the bundled
  binaries stay inert until consented. Existing installs of
  com.adkom.text-editor.semantics can be removed.

## [0.5.1] - 2026-07-26

### Added
- Minimap along the right edge of the document area (between content
  and scrollbar): a syntax-colorized code-shape overview of the whole
  document with a viewport indicator; click or drag to jump. Toggled
  via Window → Minimap; on by default.
- Selecting text highlights every other occurrence of the selection in
  the file in a weaker color, so matches stand out while the active
  selection stays dominant (single-line selections up to 200 chars;
  whitespace-only selections excluded).
- Double-click selects the word under the cursor; dragging from a
  double-click extends the selection a whole word at a time in either
  direction (identifier runs, whitespace runs, or single symbols).
- Console pane attached to the bottom of the window (horizontal tab
  strip; Console is the only tab for now, visible by default). It
  collects every ATE message — tool output, update checks, semantic
  setup, find/replace results — and every status-bar message, which
  are also now held in the status bar for a few seconds instead of
  being immediately overwritten. Closing the tab hides the pane;
  Window → Console shows it again.

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
