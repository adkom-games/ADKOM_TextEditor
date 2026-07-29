# Release Notes — ADKOM Text Editor

**☕ Enjoying ATE? Support development on Ko-fi:**
[https://ko-fi.com/adkomgames](https://ko-fi.com/adkomgames)

## 0.12.2 — 2026-07-29

Fix release.

- **Automatic updates no longer fail with "HTTP 403 Forbidden."** The
  update check was hitting the rate-limited GitHub API; it now reads the
  releases feed on github.com instead, which needs no auth and isn't
  rate-limited (#33).

## 0.12.1 — 2026-07-29

The interactive-fiction release: ATE plays Zork, and maps it.

### Features
- **Z-Machine interpreter** (built in, Tools → Z-Machine): a clean-room
  version-3 virtual machine — written from the public Z-Machine
  Standards Document — that plays any `.z3` story file. One-click
  download of the MIT-licensed **Zork I/II/III** (on your action, to
  your machine; ATE ships no game file). Save, restore, and restart.
- **Auto-mapper**: turn it on and ATE draws the world as you explore —
  rooms, **directional arrow connections** (curved to the right corner,
  two-headed for two-way passages), the items you've found, and a
  side panel for details. It's **spoiler-free**: nothing shows up until
  you've actually seen it, and things inside closed containers stay
  hidden until you open them.
- **Interiors get their own map page**: walk `in`to a building and its
  rooms lay out on a fresh grid instead of tangling with the streets
  outside.
- **SVG export**: one click saves the whole map — every level and
  interior, cross-page links, and an alphabetical **object legend** with
  each item's location — as a standalone `.svg`.
- The map and your transcript are **saved and restored** along with the
  game.

### Fixes
- The game screen no longer shrinks a line at a time as you play; it's
  now a proper scrolling transcript you can scroll back through, with a
  pinned status line (#28, #29).
- Save/restore reliability (branch-polarity resume), no more ghost
  Enter, status line no longer clipped, faster typing.
- Map connections: separate passages between the same two rooms stay
  separate; odd-angle exits draw from the correct corner (#31).
- Map pane: scrollbars when the map is big, no clipped curves, the
  current room stays centred, and the map no longer blanks out (#30,
  #32).

## 0.12.0 — 2026-07-28

The games release: ATE plays Rogue.

### Features
- **Addons**: drop `.cs` files — or whole **folders** compiled as one
  addon — into the machine-shared addons folder and they load in every
  ATE instance (Tools → Addons; full load/unload/focus lifecycle).
- **Addon security**: every addon's source is scanned against
  known-dangerous API patterns (process execution, file deletion,
  network, native interop, dynamic code loading, prefs access, …). A
  risk report opens with a clickable **Scanner Results** console tab
  (each finding jumps to its file:line), and **nothing runs until you
  approve that addon once** — approval is keyed to the exact file
  content, so any change re-prompts.
- **Game API (AteApi 1.1)**: per-document game mode (chrome hidden,
  block cursor, undo-bypassing writes, input owned by the game),
  overwrite/insert `WriteAt`, per-cell fg/bg colors, consumable key
  events + key polling, text-coordinate mouse, ≤30 Hz tick, status-bar
  prompt, per-document font, tab titles, addon lifecycle.
- **Two games included**: **Snake**, and a faithful port of **Rogue
  5.4.4** — the 1980 BSD classic, with its real monster/item tables,
  combat formulas, dungeon generator, traps, identification game,
  hunger, tombstone, and total-winner screen. Tools → Addons →
  Install Sample Addons, then Tools → Addons → Games.

### Fixes
- Tab-list dropdown: selecting a tab now always scrolls it into view;
  the strip no longer lurches left and pushes the active tab off the
  right edge (issue #9).
- Addon consent no longer shows a stale risk report when the report
  file was already open in a tab.
- Security scanner now reports every occurrence of a dangerous API
  (was: first only) and covers EditorPrefs/PlayerPrefs access;
  approvals granted under weaker scans re-prompt once.
- Snake: playfield no longer bows on rows containing the snake
  (fallback-font glyph widths); board is pure ASCII drawn with colors.
- Rogue polish during the port: death-screen freeze, overlays being
  overdrawn, Shift+arrow running, double monster turns while running.

## 0.11.0 — 2026-07-27

The AI release.

### Features
- **GitHub Copilot inline suggestions** (Settings → GitHub Copilot,
  default off; requires Node.js and your own Copilot subscription):
  ghost-text completions as you type, in file-backed AND unsaved/
  virtual documents. Tab or Enter accepts (honoring Copilot's replace
  range — no duplicated text), Escape dismisses, and a ◂ 1/3 ▸ cycler
  above the ghost switches alternatives (buttons or Alt+[ / Alt+]).
  The official Copilot Language Server installs itself via npm on
  first enable (per project, never shipped); sign-in is GitHub's
  device flow with the code auto-copied to the clipboard, and the
  login persists across domain reloads, editor restarts, and reboots.
  The word-autocomplete popup yields whenever a Copilot suggestion
  arrives. Everything is non-modal.
- **Ask Unity AI** (when com.unity.ai.assistant is installed): "Ask
  Unity AI About Selection..." / "...About This File..." in the
  document right-click and Tools menus open Unity Assistant's prompt
  popup with your text attached; no AI call (no points) happens until
  you submit the prompt. Settings shows which Unity account Assistant
  uses (managed from Unity's own account menu).
- **Search Results tab** in the console pane (View menu): Find All
  References lists hits as clickable file:line rows that jump to the
  location, opening the file if needed — replacing the old console
  dump.
- **First-run welcome**: a fresh install opens the README and these
  release notes as tabs.
- **Ctrl+Click links everywhere**: bare http(s)/mailto URLs and
  markdown [label](url) spans open in the browser from source view —
  and from rendered Markdown mode — with a "Ctrl+Click to open …"
  hover tooltip.
- **Console copy**: select console text and Ctrl+C copies it.

### Fixes
- The notification banner (sign-in codes, file conflicts) is red,
  bold, and can no longer be compressed to half height by the window
  layout.
- Multi-line Copilot ghosts render at full height with continuation
  lines at column 0 (previously clipped and mis-indented).
- Copilot status changes log once instead of repeating.
- CI: actions/checkout bumped to v5 (Node 20 deprecation).

## 0.10.1 — 2026-07-27

- Added the Ko-fi support link (☕ above) to the READMEs and these
  release notes. No code changes.

## 0.10.0 — 2026-07-27

The IDE release: every "must-have" editing feature a coder reflexively
reaches for, in one version.

### Features
- **Multi-caret editing**: Alt+Click adds carets; add next occurrence
  (Shift+Alt+. / Ctrl+D / Alt+J) and select all occurrences turn every
  match into a caret; Ctrl+Alt+Up/Down adds carets on adjacent lines
  (column editing). Typing, paste, Backspace/Delete, and Enter apply at
  every caret as ONE undo step.
- **Word-based autocomplete**: prefix-matched popup while typing (2+
  chars) or Ctrl+Space — candidates harvested from the current
  document, every other open tab, and the language's keywords.
- **Code folding**: brace regions collapse/expand from clickable gutter
  arrows or Ctrl+Shift+[ / ]; folded headers read `{ ⋯ }`;
  double-click a `{`/`}` to fold its block (view centers the header),
  double-click the `⋯ }` indicator to reopen.
- **Indentation guides** (View menu, on by default).
- **Structural editing**: auto-closing brackets/quotes (type-over,
  selection wrap, pair Backspace), brace matching with jump, block
  comments, expand/shrink selection.
- **Semantic refactoring** (with Semantic Features): Rename Symbol
  (F2 / Shift+F6), Find All References (Shift+F12 / Alt+F7) listed in
  the console, Format Document (Shift+Alt+F / Ctrl+Alt+L).
- **Quick Open** (Ctrl+, / Ctrl+P / Ctrl+T): fuzzy-find open tabs,
  recent files, and any text file under Assets/ or Packages/.
- **Bookmarks**: Ctrl+Alt+K toggles an orange gutter mark; Ctrl+Alt+N/P
  jump next/previous with wrap; bookmarks follow edits.
- **Drag & drop selected text** to move it (Ctrl to copy) — one undo.
- **Editing primitives**: word-wise delete, whole-line cut/copy on
  empty selection, insert line above/below, join lines, UPPER/lower/
  Title transforms, sort selected lines, select line, navigate
  back/forward through caret history.
- **Save cleanups** (Settings, per project): trim trailing whitespace,
  ensure final newline.
- **Tab strip overhaul**: single line with overflow scroll arrows (the
  active tab keeps itself in view), jump-to-tab dropdown at the far
  right, a Tabs submenu atop the document context menu, and
  settings-tinted tab colors (active tab brighter with an accent).
- **Resizable console**: drag the divider above the console; height
  persists.
- **Markdown images**: standalone local images display in rendered
  mode (alt text as caption; placeholder when missing).
- **Auto-Reload Changed Files** (Settings, default off): clean buffers
  reload silently on external change; dirty buffers still ask.
- **Non-modal close-time Save All**: closing with unsaved documents
  shows a small floating notice (never blocks Unity) with one-click
  Save All; reopening with dirty session buffers shows a banner.
- **Add Tab integration**: ATE appears in every dock's Add Tab menu
  and in the Window menu (which shows the Ctrl+Alt+8 shortcut).

### Fixes
- Minimap graphics and viewport rectangle were vertically squished for
  files taller than the strip; clicks were unaffected (issue #7).
- Indentation guides were invisible: spaces measured 1px wide (the
  measurer trims trailing whitespace), crushing every guide against
  the gutter; guides now sit at true indent columns.
- Smooth scrolling had a 1px color-misregistration shimmer: the ease
  landed on fractional pixels which rasterized the input field and the
  color overlay differently; animation now snaps to whole pixels.
- Gutter fold arrows were unclickable (labels were created with
  PickingMode.Ignore, so the click handler never fired).
- Occurrence highlighting could throw ArgumentOutOfRangeException from
  every repaint when the selection state momentarily disagreed with
  the text; columns are now clamped (issue #8).
- Jumped-to tabs could remain scrolled offscreen; the active tab now
  always scrolls into view once layout exists.
- Pluralized UI strings ("N document(s)") were replaced with proper
  singular/plural forms in every language.

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
