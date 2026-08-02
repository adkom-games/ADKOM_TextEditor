# Release Notes — ADKOM Text Editor

**☕ Enjoying ATE? Support development on Ko-fi:** [https://ko-fi.com/adkomgames](https://ko-fi.com/adkomgames)

## 0.14.0 — 2026-08-02

A full diff & merge suite, read-only Markdown, visible invisibles, games and windows that survive compiles, a games menu, and a documentation shelf.

### Added
- **Diff / Merge** — a first-class comparison tool (**Tools → Diff / Merge…**): side-by-side diffs of **files, folders, or open tabs** in framed, splitter-resizable columns with exact changed-span highlighting and ▲/▼ change navigation. The center gutter holds **merge buttons** — copy any change (or everything) left or right, then **Save Left / Save Right** — so a diff doubles as a merge editor, including selective revert on git diffs. **Three-way merge** takes left/base/right, auto-merges one-sided changes, and gives every conflict Take Left/Base/Right/Both buttons with a live, editable, saveable result. **Double-click any file in the Git panel** to diff it against its previous version. And one Settings click makes ATE **Unity's Revision Control Diff/Merge tool** (Preferences → External Tools) — Unity's version-control diffs and merges then open right in ATE. Every diff window — merge-in-progress included — survives domain reloads.
- **Documentation menu** — **Help → Documentation** now holds the whole reference shelf as tabs: the **Games** player guide pinned on top, then (sorted) **Addon Signing**, the **ATE Manual**, **AteApi Design** (the complete scripting API with examples), **Game API Design** (writing games, complete skeleton included), **Keyboard Shortcuts**, **Localization**, the **Scripting Reference**, **Snippets**, and **Troubleshooting** — six of them brand new.
- **Section → Bookmarks** — the Section menu now also lists the current document's bookmarks (sorted by line, with a text preview); pick one to jump. Rebuilt on every open, so it is always current.
- **`#pragma bookmark <label>`** — declare a bookmark in the source itself: the line shows up in Section → Bookmarks under its label, merged with your toggled bookmarks.
- **CS1633, silenced in one click** — Settings shows whether the unknown-pragma warning is suppressed in your project; if not, a **Suppress in This Project** button writes `-nowarn:1633` into `Assets/csc.rsp` and recompiles, so `#pragma bookmark` stays warning-free in C#.
- **Play several Zorks at once** — every Z-Machine menu pick now starts a fresh game in its own tab ("Zork I", "Zork I (1)", ...). Typing goes to whichever game is active, each game gets its own map tab in the console area, and switching to a game pops its map tab to the front.
- **Zork survives compiles** — running Z-Machine games are snapshotted the instant before a domain reload (script compile, play mode) or editor quit and resumed automatically afterwards: same tab, same prompt, same map. When a resume is impossible (story file gone, game already over), the tab falls back to "Zork I (unloaded)" with a tooltip explaining why, and the transcript stays readable.
- **Snake and Rogue survive too** — the addon API grew a mobile-style stateful lifecycle (AteApi 1.2: SaveState/RestoreState + document re-binding), and both sample games use it: your dungeon or snake comes back after script compiles, play mode, and editor restarts. Snake also gained a **pause mode (Space)** and starts paused — and after a reload it resumes paused, so no reload ever costs you a life.
- **Games menu** — games get their own home in the menu bar: **How to Play** (the player guide), the **Z-Machine (Zork)** menu (moved there from Tools), and every installed addon game (**Snake**, **Rogue**), sorted.
- **Hidden Characters** (View menu) — see every invisible character: spaces as ·, tabs as →, no-break spaces as °, zero-width characters as □, control codes as ␀-style pictures, and ¶ at line ends. Pure display — your file doesn't change.
- **Search in rendered Markdown** — Find, F3, and Search Results clicks now work while the rendered view is up: the view scrolls to the matching block and highlights it.
- **Console filter & line copy** — the Console tab has a Filter box like Search Results, and copying is now precise: click a row, Ctrl+C (or right-click → Copy Line) copies exactly that line.
- **Markdown lock** — rendered Markdown now opens **read-only** by default: a 🔒 button sits just left of the MD toggle, clicks select text instead of popping block editors, and copying always gives you the plain rendered text — URLs included, no `**markers**` or tags. Ctrl+C copies the selection (or the whole document when nothing is selected); right-click for Copy Block as Text, Copy All as Text, and Copy Link URL. Click 🔒 to unlock and edit; Settings → **Open Markdown Locked** changes the default.

### Changed
- Region folding commands (Fold, Unfold, Unfold All) moved from the View menu into the new **Edit → Code → Region** submenu; the View menu top group now holds all view toggles, sorted.
- **Addons are folders now** — every addon is one subfolder of the shared addons folder, all its `.cs` files compiled together, so it is always obvious which files belong to which addon. Single-file addons are retired: any stray top-level `.cs` moves into a folder of its own automatically, with a console note; its one-time approval (and signature, if any) must be renewed for the new identity. The shipped samples are folders too.

### Fixed
- **Rendered Markdown selects like a real document** — the locked rendered view now lays out as one continuous document: drag a selection from mid-heading through paragraphs, lists, quotes, and code, character-precise, and Ctrl+C copies clean plain text; Ctrl+A selects everything. Only tables and images (real layout, not text) interrupt the flow — selections across them snap to whole blocks.
- **Colored branch graph** — the Git panel's branch history now colors every branch and its lines with its own hue, so parallel work reads at a glance; HEAD wears a green ring and the selected commit stays gold. The divider between the Changes list and the graph is now draggable and remembers its position — and clicking a commit shows its files and message; on the latest commit, edit the message and hit Amend to rewrite it in place. A HEAD node at the graph tip stands for your working tree — click it to get back to the staging checkboxes.
- **Windows survive compiles** — the Git panel, Find/Replace dialog, History window, and every other ATE window now come back alive and working after a script compile or play mode, instead of going blank; Find/Replace even keeps your query and options for the whole session — and your undo history survives the reload too: Ctrl+Z works right through a script compile. The Git panel's file list reliably repopulates after a reload, your typed commit-message draft is preserved (even through a commit inspection), and Find/Replace restores text you had typed but not yet searched.
- **Ctrl+S works mid-block-edit** — saving (and every other shortcut) while typing in a rendered-Markdown block editor now commits the edit first; previously the save wrote the pre-edit content and the tab stayed dirty.
- **Squiggly underlines** — errors, warnings, and spelling flags now use the wavy underline every IDE uses, instead of thick solid bars — same colors, far less visual weight.
- **"Approve Anyway" no longer loops** — approving an addon whose signature does not match (the type-its-name confirmation) could never finish: clicking anywhere cancelled the hidden status-bar prompt and the SIGNATURE INVALID banner instantly returned. The confirmation is now right in the banner: type the addon's name into the inline field and hit Approve and Run (or Enter) — one step, impossible to miss, immune to stray clicks, any capitalization.
- **Security-report links** — the file:line links in addon security reviews now actually Ctrl+Click-jump to the finding in rendered Markdown; they were styled as links but not clickable — and the destination line now lands centered in the view, even when the link opens the file fresh.
- **Console spam** — the "Undid/Redid N characters" undo feedback (most visible when stepping through History) now stays in the status bar and no longer floods the console.
- **Update icon disappearing** — the green update-available icon by the settings gear now survives script compiles, play mode, and editor restarts, staying visible until you actually update. Clicking it always opens the update dialog (embedded development copies get a manual-update hint instead of Install Now).
- **Narrow windows** — the Markdown toolbar no longer pushes the update icon and settings gear out of view; it clips its own buttons instead.
- **Status messages that catch the eye** — every new status-bar message flashes yellow briefly, so quick feedback (saves, git results, history loads) is no longer easy to miss.
- **File History works again** — Git → File History… silently did nothing (the commit picker never appeared); the revision list now drops down over the editor and opens any past version, slashes in commit subjects intact.

## 0.13.2 — 2026-08-01

Hotfix.

### Fixed
- **Copilot install failed on newer Node/npm** (npm 10.9+): enabling Copilot died with `Cannot find module '…npm-prefix.js'` repeated in the console and the Settings row. The npm.cmd shim resolves its own files via `%~dp0`, which degrades to the working directory when the batch file is launched by bare name — the installer now routes through `cmd.exe` on Windows (plain `npm` elsewhere). Issue #40.

## 0.13.1 — 2026-08-01

The search release: one dialog to find everything, results where they belong, and a user manual.

### Find & replace, reworked
- **One tabbed dialog** (Notepad++-style): Find, Replace, Find in Files, and Bookmark tabs sharing the query and options in a fixed-size window — toggled from Edit, View, or Ctrl+F.
- **Search Modes**: plain text, extended escapes (`\n`, `\t`, `\x41`), or regular expressions with $1 replacements and ". matches newline".
- **Count**, **In selection** scoping, Find All in Current / All Opened Documents, a Find/Replace swap button — and Find in Files filters, directory picker, follow-current-doc, sub-folder and hidden-folder switches.
- **Results live in the Search Results tab** — the dialog shows parameters only; every hit is a clickable row and every jump lands **centered** in the view. Replace in Files applies all matches as one journaled, undoable operation.

### Bookmarks
- **View Bookmarks** (Edit → Bookmarks): every open document's bookmarks in a console tab, grouped per file behind disclosure triangles, sorted and filterable.
- The dialog's **Bookmark tab** bulk-bookmarks every match — with purge, Clear all bookmarks, and Copy Matched Text.

### Around the editor
- **Section menu**: the current tab's classes, properties, and methods — sorted, always fresh, one click to jump.
- **Console, rebuilt**: per-line rows with alternating tones, row selection, Ctrl+C copies lines; Console / Search Results / Bookmarks toggle independently from the View menu; all bottom views share a framed, monospace, zebra-striped look.
- **Tooltips on every control**, fully localized.
- **Help → Open Manual**: a complete 18-section user manual, also part of the first-run welcome (README → Manual → Release Notes).
- Triple-click selects the line (drag extends by lines); bracket matching cycles `#if/#elif/#else/#endif` both directions; History window: real-editor preview, arrow-key navigation, per-document tabs; drag-and-drop insertion caret; green update icon by the gear; Window menu tabs sorted.

### Fixed
- Semantics fallback for files outside Unity assemblies (issue #36).
- Metadata stubs carry using directives so F12 works inside them (issue #37).
- Triple-click reliably fires (issue #38).
- Go to Definition resolves L10n strings before the background task (issue #39).
- "All Tabs" search scope did not persist on non-English editors (locale-dependent comparison; superseded by the tabbed dialog).

## 0.13.0 — 2026-07-30

The IDE release. Twenty-one features — ATE now edits like a full IDE.

### Code intelligence
- **IntelliSense**: type `.` and get the expression's real members — signatures, overloads, accessibility all correct. Scope symbols, words, keywords, and your snippets blend into one popup.
- **Live errors**: compiler errors and warnings underline as you type; hover for the message.
- **Read/write highlighting**: put the caret on a symbol and see every use — writes in amber.
- **Go to #region**, and regions now fold like braces.

### Editing power
- **Snippets**: your own, in one plain-text file — trigger + Tab, live tab stops, hot-reload. Twelve C# defaults included.
- **Generators**: any Unity magic method (33, duplicate-aware) or any overridable base member — full stubs, base calls included.
- **Visual History**: your undo timeline, with the document shown exactly as it was at any point — restore, open as tab, or copy.
- **Find/Replace in Files**: whole-project search with per-match checkboxes and previews — and one-click **undo across every file** a replace touched.
- **Auto-Save on Focus Loss** (optional, per project).

### The world around your code
- **Git built in**: change markers in the gutter, blame, per-file history, stage/commit/push — and an **interactive branch tree** you can flip vertical ⇄ horizontal.
- **Inspect running code**: live static and MonoBehaviour values while you play, editable primitives, one-click running of parameterless static methods.
- **Spell checking** (optional): 115k-word dictionary, other languages via Hunspell files, personal and per-project word lists.
- **JSON and shader coloring**, a filterable Find All References list, a tidier Edit menu, and organized Settings.

## 0.12.3 — 2026-07-29

Auto-map polish.

### Features
- **Colour-coded map**: every room gets its own colour (the same one every game), connection arrows take the colour of the room they leave, and the lines now curve around the boxes.
- **Zoom the map**: a slider (0.4×–2.5×) and Ctrl+scroll, kept in sync.
- **Revealed objects show up**: move the leaves and the grating appears; open the mailbox and the leaflet appears. Passages/doors are drawn as a ◇ diamond, and up/down as ▲/▼ triangles.
- **Tidier layout**: new rooms stay next to what they connect to and push others aside instead of overlapping, and the page re-settles so a link to a far room pulls the two together.
- Room #ids everywhere, and the game tab is named after the game ("Zork I").

### Fixes
- The map no longer blanks out after zooming (#34).
- Restoring a save no longer dumps every passage marker into your current room, and the transcript scrolls to the input line on restore (#35).

## 0.12.2 — 2026-07-29

Fix release.

- **Automatic updates no longer fail with "HTTP 403 Forbidden."** The update check was hitting the rate-limited GitHub API; it now reads the releases feed on github.com instead, which needs no auth and isn't rate-limited (#33).

## 0.12.1 — 2026-07-29

The interactive-fiction release: ATE plays Zork, and maps it.

### Features
- **Z-Machine interpreter** (built in, Tools → Z-Machine): a clean-room version-3 virtual machine — written from the public Z-Machine Standards Document — that plays any `.z3` story file. One-click download of the MIT-licensed **Zork I/II/III** (on your action, to your machine; ATE ships no game file). Save, restore, and restart.
- **Auto-mapper**: turn it on and ATE draws the world as you explore — rooms, **directional arrow connections** (curved to the right corner, two-headed for two-way passages), the items you've found, and a side panel for details. It's **spoiler-free**: nothing shows up until you've actually seen it, and things inside closed containers stay hidden until you open them.
- **Interiors get their own map page**: walk `in`to a building and its rooms lay out on a fresh grid instead of tangling with the streets outside.
- **SVG export**: one click saves the whole map — every level and interior, cross-page links, and an alphabetical **object legend** with each item's location — as a standalone `.svg`.
- The map and your transcript are **saved and restored** along with the game.

### Fixes
- The game screen no longer shrinks a line at a time as you play; it's now a proper scrolling transcript you can scroll back through, with a pinned status line (#28, #29).
- Save/restore reliability (branch-polarity resume), no more ghost Enter, status line no longer clipped, faster typing.
- Map connections: separate passages between the same two rooms stay separate; odd-angle exits draw from the correct corner (#31).
- Map pane: scrollbars when the map is big, no clipped curves, the current room stays centred, and the map no longer blanks out (#30, #32).

## 0.12.0 — 2026-07-28

The games release: ATE plays Rogue.

### Features
- **Addons**: drop `.cs` files — or whole **folders** compiled as one addon — into the machine-shared addons folder and they load in every ATE instance (Tools → Addons; full load/unload/focus lifecycle).
- **Addon security**: every addon's source is scanned against known-dangerous API patterns (process execution, file deletion, network, native interop, dynamic code loading, prefs access, …). A risk report opens with a clickable **Scanner Results** console tab (each finding jumps to its file:line), and **nothing runs until you approve that addon once** — approval is keyed to the exact file content, so any change re-prompts.
- **Game API (AteApi 1.1)**: per-document game mode (chrome hidden, block cursor, undo-bypassing writes, input owned by the game), overwrite/insert `WriteAt`, per-cell fg/bg colors, consumable key events + key polling, text-coordinate mouse, ≤30 Hz tick, status-bar prompt, per-document font, tab titles, addon lifecycle.
- **Two games included**: **Snake**, and a faithful port of **Rogue 5.4.4** — the 1980 BSD classic, with its real monster/item tables, combat formulas, dungeon generator, traps, identification game, hunger, tombstone, and total-winner screen. Tools → Addons → Install Sample Addons, then Tools → Addons → Games.

### Fixes
- Tab-list dropdown: selecting a tab now always scrolls it into view; the strip no longer lurches left and pushes the active tab off the right edge (issue #9).
- Addon consent no longer shows a stale risk report when the report file was already open in a tab.
- Security scanner now reports every occurrence of a dangerous API (was: first only) and covers EditorPrefs/PlayerPrefs access; approvals granted under weaker scans re-prompt once.
- Snake: playfield no longer bows on rows containing the snake (fallback-font glyph widths); board is pure ASCII drawn with colors.
- Rogue polish during the port: death-screen freeze, overlays being overdrawn, Shift+arrow running, double monster turns while running.

## 0.11.0 — 2026-07-27

The AI release.

### Features
- **GitHub Copilot inline suggestions** (Settings → GitHub Copilot, default off; requires Node.js and your own Copilot subscription): ghost-text completions as you type, in file-backed AND unsaved/virtual documents. Tab or Enter accepts (honoring Copilot's replace range — no duplicated text), Escape dismisses, and a ◂ 1/3 ▸ cycler above the ghost switches alternatives (buttons or Alt+[ / Alt+]). The official Copilot Language Server installs itself via npm on first enable (per project, never shipped); sign-in is GitHub's device flow with the code auto-copied to the clipboard, and the login persists across domain reloads, editor restarts, and reboots. The word-autocomplete popup yields whenever a Copilot suggestion arrives. Everything is non-modal.
- **Ask Unity AI** (when com.unity.ai.assistant is installed): "Ask Unity AI About Selection..." / "...About This File..." in the document right-click and Tools menus open Unity Assistant's prompt popup with your text attached; no AI call (no points) happens until you submit the prompt. Settings shows which Unity account Assistant uses (managed from Unity's own account menu).
- **Search Results tab** in the console pane (View menu): Find All References lists hits as clickable file:line rows that jump to the location, opening the file if needed — replacing the old console dump.
- **First-run welcome**: a fresh install opens the README and these release notes as tabs.
- **Ctrl+Click links everywhere**: bare http(s)/mailto URLs and markdown [label](url) spans open in the browser from source view — and from rendered Markdown mode — with a "Ctrl+Click to open …" hover tooltip.
- **Console copy**: select console text and Ctrl+C copies it.

### Fixes
- The notification banner (sign-in codes, file conflicts) is red, bold, and can no longer be compressed to half height by the window layout.
- Multi-line Copilot ghosts render at full height with continuation lines at column 0 (previously clipped and mis-indented).
- Copilot status changes log once instead of repeating.
- CI: actions/checkout bumped to v5 (Node 20 deprecation).

## 0.10.1 — 2026-07-27

- Added the Ko-fi support link (☕ above) to the READMEs and these release notes. No code changes.

## 0.10.0 — 2026-07-27

The IDE release: every "must-have" editing feature a coder reflexively reaches for, in one version.

### Features
- **Multi-caret editing**: Alt+Click adds carets; add next occurrence (Shift+Alt+. / Ctrl+D / Alt+J) and select all occurrences turn every match into a caret; Ctrl+Alt+Up/Down adds carets on adjacent lines (column editing). Typing, paste, Backspace/Delete, and Enter apply at every caret as ONE undo step.
- **Word-based autocomplete**: prefix-matched popup while typing (2+ chars) or Ctrl+Space — candidates harvested from the current document, every other open tab, and the language's keywords.
- **Code folding**: brace regions collapse/expand from clickable gutter arrows or Ctrl+Shift+[ / ]; folded headers read `{ ⋯ }`; double-click a `{`/`}` to fold its block (view centers the header), double-click the `⋯ }` indicator to reopen.
- **Indentation guides** (View menu, on by default).
- **Structural editing**: auto-closing brackets/quotes (type-over, selection wrap, pair Backspace), brace matching with jump, block comments, expand/shrink selection.
- **Semantic refactoring** (with Semantic Features): Rename Symbol (F2 / Shift+F6), Find All References (Shift+F12 / Alt+F7) listed in the console, Format Document (Shift+Alt+F / Ctrl+Alt+L).
- **Quick Open** (Ctrl+, / Ctrl+P / Ctrl+T): fuzzy-find open tabs, recent files, and any text file under Assets/ or Packages/.
- **Bookmarks**: Ctrl+Alt+K toggles an orange gutter mark; Ctrl+Alt+N/P jump next/previous with wrap; bookmarks follow edits.
- **Drag & drop selected text** to move it (Ctrl to copy) — one undo.
- **Editing primitives**: word-wise delete, whole-line cut/copy on empty selection, insert line above/below, join lines, UPPER/lower/Title transforms, sort selected lines, select line, navigate back/forward through caret history.
- **Save cleanups** (Settings, per project): trim trailing whitespace, ensure final newline.
- **Tab strip overhaul**: single line with overflow scroll arrows (the active tab keeps itself in view), jump-to-tab dropdown at the far right, a Tabs submenu atop the document context menu, and settings-tinted tab colors (active tab brighter with an accent).
- **Resizable console**: drag the divider above the console; height persists.
- **Markdown images**: standalone local images display in rendered mode (alt text as caption; placeholder when missing).
- **Auto-Reload Changed Files** (Settings, default off): clean buffers reload silently on external change; dirty buffers still ask.
- **Non-modal close-time Save All**: closing with unsaved documents shows a small floating notice (never blocks Unity) with one-click Save All; reopening with dirty session buffers shows a banner.
- **Add Tab integration**: ATE appears in every dock's Add Tab menu and in the Window menu (which shows the Ctrl+Alt+8 shortcut).

### Fixes
- Minimap graphics and viewport rectangle were vertically squished for files taller than the strip; clicks were unaffected (issue #7).
- Indentation guides were invisible: spaces measured 1px wide (the measurer trims trailing whitespace), crushing every guide against the gutter; guides now sit at true indent columns.
- Smooth scrolling had a 1px color-misregistration shimmer: the ease landed on fractional pixels which rasterized the input field and the color overlay differently; animation now snaps to whole pixels.
- Gutter fold arrows were unclickable (labels were created with PickingMode.Ignore, so the click handler never fired).
- Occurrence highlighting could throw ArgumentOutOfRangeException from every repaint when the selection state momentarily disagreed with the text; columns are now clamped (issue #8).
- Jumped-to tabs could remain scrolled offscreen; the active tab now always scrolls into view once layout exists.
- Pluralized UI strings ("N document(s)") were replaced with proper singular/plural forms in every language.

## 0.9.0 — 2026-07-26

### Features
- **Localized interface**: menus, settings, dialogs, prompts, and status messages follow Unity's Editor Language — Japanese, Korean, Simplified Chinese, and Traditional Chinese ship in the box.
- **Recent Files** (File menu): per project, most recent first, count configurable in Settings (default 5); missing files clean themselves up; "Clear Recent Files" empties the list.
- **Goto Line** (Ctrl+G): an emacs-style prompt in the status bar; numeric input, clamped to the file; line numbers not required.
- **Document context menu**: right-click in the editor for Go to Definition, Find Occurrences of the word under the cursor, clipboard, Save / Save As / Close Tab / Show in File Explorer, Find/Replace/Goto Line, and language-specific commands (C# comment toggle, Markdown mode switch).
- **Drag-and-drop tab reordering** — tabs move live as you drag.
- **Tabs survive closing the window**: the session (including each dirty tab's unsaved content) is restored when ATE reopens, even across editor restarts.
- **Deleted-file rescue**: when an open file vanishes from disk, a banner offers Keep Buffer / Close Tab — Save writes the file back.
- Menu items display their keyboard shortcuts for the active layout; clipboard shortcuts work with focus anywhere in the ATE window.
- Console and Minimap moved to the View menu (alphabetical with Line Numbers and Word Wrap); all four default ON for fresh installs.
- Settings: "Open Markdown Rendered" is joined by "Recent Files Count".

### Fixes
- Undo/redo grouping is now humanly predictable (VS Code model): one undo removes one word; groups break on Enter, paste, caret moves, direction changes, pauses, save, and hard size/age caps. The status bar reports "Undid N char(s)". No GitHub issue; design defect.
- English-language editors showed the entire UI in Japanese after the localization change — Unity's catalog loader falls back to the first PO file alphabetically when the current language has no catalog; an English identity catalog fixes it (issue #4).
- Go to Definition (F12/Ctrl+B/Ctrl+Click) now works inside "from metadata" views and chains stub to stub. No GitHub issue.
- The "Unsaved Changes" dialogs no longer use Unity's modal system: closing a dirty tab uses the non-modal in-window banner, and closing the window silently preserves dirty buffers in the session instead of prompting. No GitHub issue; modality-policy completion.

## 0.8.0 — 2026-07-26

### Features
- **Markdown support** for `.md` files: full syntax coloring in source mode, and a rendered mode with block-level WYSIWYG editing — click any block to edit its source inline; undo works across both modes. A transient MD/source button by the settings gear shows the current mode and switches per tab.
- **Formatting toolbar** for any `.md` tab: headings, bold, italic, strikethrough, inline code, links, images, bullet/numbered/task lists, blockquotes, code blocks, tables, and horizontal rules — one click formats your selection or the block you're editing, or inserts a ready-made template, in either mode.
- Strikethrough, images, task lists (☐/☑), and tables render and color everywhere Markdown does.
- New setting **"Open Markdown Rendered"** chooses the default view for `.md` files (source by default). Release notes after an update always open rendered — like this document.

## 0.7.1 — 2026-07-26

### Features
- After an update, the new version's release notes open in a focused tab (you may be reading this in one right now).
- While an update installs, the ATE window shows an ATE-only "Updating…" overlay that blocks editing so nothing is lost in the reload — Unity itself stays fully responsive.

## 0.7.0 — 2026-07-26

### Features
- Go to Definition on referenced-assembly symbols (UnityEngine, BCL) opens a generated "from metadata" signature view of the type, caret on the invoked member (virtual, C#-highlighted, deduplicated tabs).
- Console text is selectable and copyable.

### Fixes
- The modal file-changed-on-disk dialog froze the Unity editor's main loop (and background tooling) whenever the window regained focus with a changed file — replaced by an in-window banner (Reload / Keep Mine). No GitHub issue; field-reported.

### Changes
- Non-modal dialog policy adopted: async update failures and informational notices are console/status messages. Decision dialogs directly after user actions remain modal.

## 0.6.1 — 2026-07-26

### Fixes
- Upgrading from 0.5.x with the old semantics module installed broke compilation (duplicate assembly name), leaving 0.5.x code running while About reported the new version. The built-in semantics assembly is renamed and the obsolete module package is removed automatically. If you hit this on 0.6.0: remove "ADKOM Text Editor — Semantics Module" in the Package Manager, or simply install this update.
- Update installs are no longer fire-and-forget: failures are logged to the ATE console and shown in a dialog with the manual install URL.

## 0.6.0 — 2026-07-26

### Changes
- Semantic Features now ship inside the main package: no separate semantics module, no extra install URL. First use (or the Settings toggle) offers one-click setup; the bundled MIT-licensed Roslyn assemblies are copied only when the project has no Roslyn of its own (THIRD-PARTY-NOTICES included; binaries inert until consented).
- The com.adkom.text-editor.semantics package and its upm-semantics branch are retired — remove old installs of the module if present.
- Package download grows ~14MB (bundled Roslyn).

## 0.5.1 — 2026-07-26

### Features
- Syntax-colorized minimap between the document and the scrollbar: whole-file overview with viewport indicator, click/drag to jump (Window → Minimap; on by default).
- Console pane at the bottom of the window collecting all ATE messages and status-bar output, timestamped (closable; Window → Console; on by default). Status messages also stay in the bar for 5s now.
- Double-click selects the word under the cursor; dragging extends the selection whole-word at a time.
- Selecting text highlights all other occurrences in a weaker color.

## 0.5.0 — 2026-07-26

### Features
- Semantic Features setting (OFF by default) with fully automatic dependency installation: enabling it installs the semantics module via UPM and, if the project has no Roslyn, the module's bundled MIT-licensed Roslyn 4.8 assemblies (THIRD-PARTY-NOTICES included); existing Roslyn copies are preferred.
- Go to Definition without the feature enabled asks via a dialog with one-click Enable and Install (replacing a transient status message).
- Ctrl+Alt+8 opens the ATE window. Say it out loud.
- Semantics module 0.2.0 (bundled Roslyn binaries + notices).

### Notes
- Includes everything listed under 0.4.1 below, which was version-bumped but never tagged; 0.5.0 is its release vehicle.

## 0.4.1 — 2026-07-25 (not tagged; shipped in 0.5.0)

### Features
- Identifier syntax highlighting: types, methods, variables, parameters in theme-authentic colors (built-in heuristics; compiler-accurate with the semantics module).
- Symbol navigation with the new optional Roslyn semantics module (`#upm-semantics`): Ctrl+Click / F12 / Ctrl+B jumps to definitions across files and assemblies; metadata symbols report their assembly.
- First run of a newly installed version checks for updates immediately.

### Notes
- The semantics module activates only when a Microsoft.CodeAnalysis.CSharp assembly exists in the project (detected automatically).
- Forensic console log when automatic updates are disabled.

## 0.4.0 — 2026-07-25

### Features
- Native menu bar (File, Edit, View, Tools, Window, Help) replacing the toolbar buttons; right-click context menu on file tabs (Save, Save As, Close, Close Other Tabs).
- Selectable as Unity's External Script Editor: scripts and console entries open in ATE at the exact line/column; configurable fallback editor for solutions, binaries, and project sync.
- Automatic update checks (daily at most, configurable in days, disable-able): console announcement plus an idle-time install dialog showing current/new versions with a settings-synced checkbox.
- Configurable font (any OS font or bundled monospace) and size, with Ctrl+MouseWheel / Ctrl+'+' / Ctrl+'-' zoom and Ctrl+0 reset.
- Optional smooth scrolling (same per-notch velocity, animated).
- Find and Replace toolbar buttons became Edit-menu entries.

### Fixes
- Monospace font by default (was inheriting Unity's UI font).
- Line-number gutter drifted out of alignment deeper into files.
- Opening the ATE window no longer creates an Untitled document or a duplicate window; an empty window shows a hint instead.
- Status bar hardening: "No file open" placeholder; Settings tab scrolls on short windows instead of spilling over the bar.
- New/switched documents receive keyboard focus immediately.

## 0.3.0 — 2026-07-24

### Features
- Find and Replace in a modeless dialog, with all-tabs scope ("find in tabs" / "replace in tabs"): match case, whole word, normal or regex search (with $1 group replacements), wrap around, backwards direction. Keyboard: Ctrl+F / Ctrl+Shift+F; Ctrl+H / Ctrl+Shift+H (VS, VS Code) or Ctrl+R / Ctrl+Shift+R (Rider); F3 / Shift+F3 repeat. Toolbar Find and Replace buttons.
- Word wrap returned, native to the virtualized view: self-computed wrap points render each visual row independently, syntax colors split correctly across rows, the gutter blanks continuation rows, and Up/Down move by visual row. Word Wrap setting restored.
- Repository root README distinguishing the dev project from the package.

### Fixes
- Tab-stop-aligned navigation and Backspace/Delete extended to any whitespace run (was leading indentation only).
- Package files deleted from the working tree caused a black window; recovered from git (issue #3, closed — user error, no tooling defect).

## 0.2.1 — 2026-07-24

### Changes
- License changed to MIT.
- Public-facing README rewrite (Editor-only guarantee front and center).

## 0.2.0 — 2026-07-24

### Features
- Multiple open files as tabs (dirty guards, middle-click close, survive domain reloads).
- Project-window context menu opens any text asset; already-open files switch to their tab.
- External change detection on tab activation and window focus.
- C# syntax highlighting with an extensible per-language formatter API.
- Color themes: Visual Studio, VS Code, Rider (dark + light palettes) with an Auto/Dark/Light mode selector.
- Line numbers in a toggleable gutter.
- Settings as a document tab (gear button toggles open/front/close): theme, mode, line numbers, tab size, keyboard layout.
- Tabs render as spaces (configurable tab size); files keep their original tab/space indentation on save.
- Tab key inserts spaces to the next tab stop; navigation, Backspace, and Delete honor tab stops across any whitespace run.
- Keyboard layouts: Visual Studio, VS Code, Rider (save/save all, open/new, close/next/prev tab, duplicate/delete/move line, toggle comment, indent/unindent, settings).
- Fully virtualized editor (CodeView): only visible lines render, so keystroke cost is independent of file size (measured 14.7ms on a 5,000-line file). Includes caret/selection/mouse/clipboard and undo/redo with typing coalescing.
- Window title reads "ATE - filename".

### Fixes
- UPM git-URL installs failed on clean machines ("update_ref failed" / missing objects): the repo declared Git LFS attributes without LFS objects. LFS was retired from the repository entirely (issue #1).
- Status bar pushed off-window by tall documents.
- Editor hard-hang when opening .cs files (highlight overlay nested in a TextElement) (issue #2).
- Invisible indentation and off-by-a-bit caret placement (overlay whitespace collapse; caret hidden under overlay).
- Tab key inserted a literal tab character via a duplicate key event.
- Tab moved focus to the toolbar instead of indenting.
- Seconds-per-keystroke typing in large files (per-line gutter measuring, whole-document re-shaping) — resolved by virtualization.
- Initial render only filled part of the viewport until scrolled.

### Removed
- Word wrap (incompatible with the virtualized view; long lines scroll horizontally).

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
