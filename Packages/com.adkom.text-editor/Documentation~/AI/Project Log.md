---
tags: [project-log]
---

# Project Log — ADKOM Text Editor

Chronological history of features, defects, and decisions. Newest entries at the bottom. See [[Project State]] for the current snapshot.

## Pre-history (before 2026-07-24 session)

- Package skeleton authored (`com.adkom.text-editor`, Editor-only asmdef) with the v1 editor: dockable UIToolkit window, New/Open/Save/Save As, dirty guards, external-change detection, EOL/BOM preservation, word wrap, status bar, `ITextFormatter` passthrough, and a reserved gutter.
- At that time the package was its own git repo with the dev Unity project outside it, linked by a directory junction. **This architecture was later replaced** (see 2026-07-24: repo creation).

## 2026-07-24 — v1 verification and first bug

- **Bug: text invisible after opening a file.** Root cause: the USS rule `-unity-font-definition: initial` cleared the inherited editor font, leaving both font and font-definition NULL — glyphs had nothing to render with. Fixed by removing the rule. Lesson: `initial` in UITK clears rather than resets to default.

## 2026-07-24 — Repo creation and UPM distribution

- Public repo created: `adkom-games/ADKOM_TextEditor`. **Decision:** one repo holding the whole dev project with the package embedded at `Packages/com.adkom.text-editor`; the earlier package-repo-plus-junction scheme was absorbed (the nested `.git` was removed; its local history is recorded here).
- **Defect (issue #1): UPM install failed on clean machines.** The Unity `.gitattributes` template marked binaries as Git LFS, but the repo never used LFS; git-lfs machines aborted UPM's checkout. Fixed by neutering the LFS macro to `-text`. Also moved `.gitattributes` to repo root (attribute macros are only legal there).
- **Decision: package-only `upm` branch** (via `git subtree split`, kept in sync by a GitHub Action on every push to main) so consumers don't clone the full Unity project. Install: `...git#upm`, pinned: `...git#0.1.0` (tag points at the split commit). Tagged **0.1.0**.

## 2026-07-24 — Feature wave 1

- **Assets context menu** ("Open in ADKOM Text Editor"), initially `.cs` only; later widened to any text asset (TextAsset/MonoScript types + an extension list) and changed to always open a new tab (switch if already open).
- **Multi-file tabs** with per-tab dirty guards, middle-click close, and serialization across domain reloads. External-change checks run on tab activation as well as window focus.
- **Fix:** status bar was pushed off-window by tall documents (`flex-grow` with auto basis); pinned with `flex-basis: 0; min-height: 0` and `flex-shrink: 0` on the status bar.
- **Line numbers** (Lines toggle) in the gutter, scroll-synced. Later fixed to stay aligned under word wrap by measuring each logical line's height and padding blank gutter rows for wrapped continuations.
- **Window title** prefixed "ATE - filename" so the dock tab is identifiable.

## 2026-07-24 — Syntax highlighting (and the editor-hang saga)

- **Architecture decision:** an editable TextField cannot render rich text, so highlighting is a rich-text overlay Label showing `CSharpFormatter` output while the field's glyphs go transparent (caret/selection stay).
- **Defect (issue #2): editor hard-hang on opening .cs.** The overlay was parented inside the editable TextElement; in Unity 6 nested TextElements join the parent's text generation, which loops. First fix (reparent to TextInput) failed because TextInput is *also* a TextElement. Final fix: the overlay lives entirely outside the TextField in a rect-synced clipping container. The tokenizer was exonerated by running it standalone via the dotnet CLI. Highlighting is capped at 200k chars as engine-load insurance.
- **Lesson:** never nest any TextElement inside another in Unity 6.

## 2026-07-24 — Themes, settings, and editing ergonomics

- **Color themes:** `HighlightTheme` with dark/light palettes per theme. Built-ins: VS Code (Dark+/Light+), JetBrains Rider (Darcula/IntelliJ Light), later Visual Studio (VS Dark/Light). Applied to tokens, background, gutter, caret. **Light/Dark mode** selector: Auto (follow editor skin, default) / Dark / Light.
- **Settings tab:** a gear toolbar button opens Settings as a single-instance document tab (replacing the toolbar's Theme/Lines/Wrap controls). Gear behavior: open → bring to front → close when already frontmost.
- **Tabs as spaces:** files load with tabs expanded (column-aware) at the configurable Tab Size; documents remember whether the file indents with tabs and convert leading indentation back on save so formatting is preserved. Tab key inserts spaces to the next tab stop; arrows jump leading indentation in tab-size steps.
- **Keyboard layouts:** Visual Studio, Rider, and later VS Code defaults for supported commands (save/save all, new/open, close/next/prev tab, duplicate /delete/move line, toggle comment, indent/unindent, settings).
- **Defect: indentation looked like it did nothing.** Two causes: the overlay drew *over* the field, hiding the caret (fixed by drawing the overlay *under* the transparent-glyph field — background moved to the row, input made transparent); and programmatic caret placement was clamped by the text engine's pre-edit state (fixed by re-asserting the caret a frame later).

## 2026-07-24 — Performance saga and virtualization

- **Defect: seconds-per-keystroke typing.** Profiled in stages: first the wrap-aware gutter (measuring every logical line per keystroke, twice) — debounced; then the overlay's whole-document rich-text re-shape — made asynchronous (plain text while typing, colors on idle); finally the editable TextField's own whole-document re-shape proved to be the floor (~176ms @ 1k lines, ~930ms @ 5k). A gap buffer was considered and rejected: string splices are microseconds; TEXT SHAPING was the cost.
- **Decision: full virtualization (CodeView).** The document renders as pooled per-line Labels (only visible lines laid out); caret, selection, mouse, keyboard, clipboard, and undo/redo implemented in-house; the formatter emits per-line tags so lines colorize independently. Result: 14.7ms keystroke-to-frame on 5,000 lines (63x). Trade-off: word wrap removed (long lines scroll horizontally); the TextField-era overlay, gutter, and wrap machinery were deleted.

## 2026-07-25 — Application polish wave (0.4.0)

- **Native menu bar** (File/Edit/View/Tools/Window/Help via GenericMenu, built per click for live state) replaced the toolbar buttons; tab right-click context menu (Save/Save As/Close/Close Other Tabs).
- **No auto-Untitled:** empty window is a valid state (hint + "No file open" status); Tools menu focuses the existing window instead of creating duplicates.
- **Automatic updates:** GitHub latest-release polling (min once/day, configurable days, disable-able), console announcement, idle-time install dialog with a settings-synced checkbox; embedded dev copies never auto-install. GitHub Releases are now the update channel.
- **External Script Editor:** ATE registers via IExternalCodeEditor (installation path = Unity itself); text files open at line/column; configurable fallback editor receives solutions/binaries/sync.
- **Fonts:** monospace by default (bundled RobotoMono; fixed the non-mono inheritance), any OS font + size in Settings, browser-style zoom (Ctrl+wheel, Ctrl+±, Ctrl+0).
- **Fixes:** cumulative gutter drift (per-row gutter labels — one spacing authority); status bar hardening (empty-state text, scrollable settings pane); focus on new/switched documents.
- **Smooth scrolling** (optional, same per-notch velocity, exponential ease).

## 2026-07-25 — Semantic highlighting and navigation (0.4.1)

- **Span-based highlighting engine**: classifiers emit per-line (start,len,TokenClass) spans; markup built lazily per visible row; wrap slicing clips spans. Replaced CSharpFormatter/ITextFormatter.
- **Heuristic identifier classification** (types/methods/variables/params) + identifier colors in all six palettes.
- **Companion package com.adkom.text-editor.semantics** (Roslyn): asmdef gated on ADKOM_TE_ROSLYN (set by main-package bootstrap when Roslyn is detected — avoids duplicate-DLL conflicts, e.g. with the MCP's copies). Builds cached CSharpCompilations from CompilationPipeline data; bg-thread classification (~80ms warm, 515 spans) replaces heuristics version-checked.
- **Go to Definition**: Ctrl+Click / F12 / Ctrl+B; cross-file, locals, metadata provenance. Distributed via new upm-semantics split branch.
- **Known gap**: no bundled Roslyn for projects without one (module stays dormant); planned: bundled DLLs + installer.

## 2026-07-26 — Opt-in semantics with auto-install (0.5.0)

- **Semantic Features setting (OFF by default)**: enabling drives SemanticSetup across reloads — module install (Client.Add #upm-semantics), bundled Roslyn copy (only when the project has none), compile-gate define. Roslyn 4.8 netstandard2.0 binaries (10 DLLs, ~14MB) ship inert in the module's RoslynBinaries~ with THIRD-PARTY-NOTICES.md (MIT, .NET Foundation) satisfying redistribution. Decision: bundle rather than NuGetForUnity (Cary: no NuGet); prefer any Roslyn already present.
- Go to Definition without the feature now opens a dialog with one-click Enable and Install (was a transient status message).
- Ctrl+Alt+8 opens the ATE window ("Ctrl-Alt-ATE").
- Field-test pending: Client.Add and DLL-copy paths can't run in the dev project (module embedded, Roslyn present via MCP).

## 2026-07-26 — Overview & selection wave (0.5.1)

- **Console pane** (bottom, tab strip, closable, Window menu restores, on by default): AteConsole thread-safe sink; all ATE tool logs and status-bar messages route through it; status messages now pinned 5s in the bar (fixes the too-short-to-read complaint).
- **Minimap** between content and scrollbar: Painter2D single-mesh code-shape overview, syntax-colorized from the span classifier, batched per color; viewport indicator; click/drag jump; Window menu toggle, on by default (verified stale serialized layouts keep the initializer default — update-safe).
- **Double-click word selection** with whole-word snap drag.
- **Selection occurrence highlighting** on visible rows in a weaker color than the active selection.

## 2026-07-26 — Semantics in the box (0.6.0)

- **Merged the semantics module into the main package**: provider under Editor/Semantics (nested asmdef, same ADKOM_TE_ROSLYN gate), RoslynBinaries~ + THIRD-PARTY-NOTICES at package root. Consent flow (first-use dialog / Settings) now only copies bundled Roslyn when the project has none — the never-field-tested module Client.Add path was eliminated rather than tested. upm-semantics branch retired (semantics-0.2.0 tag kept for history). Download +~14MB, binaries inert until consented.

## 2026-07-26 — Upgrade-path fixes (0.6.1)

- **Field-tested the update path at last** — and found two defects. (1) Duplicate-assembly collision: projects upgrading to 0.6.0 with the old semantics module installed failed compilation and silently kept running 0.5.x while About said 0.6.0. Fixed: built-in semantics asmdef renamed ADKOM.TextEditor.Editor.Semantics + auto-removal of the obsolete module on load. (2) Client.Add was fire-and-forget; failures now log to the ATE console and show a dialog with the manual URL. Lesson: never ship a merged assembly under a name a previous optional package used.

## 2026-07-26 — Metadata view and modality policy (0.7.0)

- **"From metadata" view**: F12 on referenced-assembly symbols opens a Roslyn-generated signature stub as a virtual document (TextDocument.VirtualName/VirtualCSharp; deduped by title; caret on the invoked member).
- **Non-modal policy** (field-driven: modal file-changed dialog blocked Unity's main loop and the MCP): file-changed prompt is a banner with Reload/Keep Mine; async update-failure and semantics-compiling notices are console/status. Decision dialogs immediately after user actions stay modal by design.
- Console text selectable/copyable.

## 2026-07-26 — Update experience (0.7.1)

- **Release notes after update**: first run of a new version (updates only, not fresh installs) opens the packaged RELEASE-NOTES.md as a focused virtual tab. OpenVirtualDoc extracted from the metadata view.
- **ATE-only updating overlay**: InstallInProgress state + event; the window dims and blocks pointer/keyboard/commands during Client.Add — Unity itself stays responsive (modality policy). Clears on failure; success ends in the reload.

## 2026-07-26 — Markdown (0.8.0)

- **Markdown support**: `MarkdownClassifier` (source coloring onto the TokenClass palette) + `MarkdownView` (block parse/render with char offsets; block-level WYSIWYG editing routed through `CodeView.ReplaceRangeInternal` for undo). Transient MD/source toggle left of the gear; per-document mode.
- **Formatting toolbar**: 16 element buttons shown for any `.md` tab. Rendered mode targets the open block editor or appends a template block; source mode wraps the selection / transforms lines / inserts after the current line. Shared statics on `MarkdownView` (`TemplateFor`, `TryGetInlineWrap`, `TransformLines`).
- **Parity additions**: strikethrough, images (placeholder render), task lists (☐/☑), pipe tables (bold header grid) across classifier, renderer, and toolbar.
- **Default view setting** (`MdOpenRendered`, off by default) + `TextDocument.VirtualMarkdown` so the post-update release-notes tab always opens rendered with full Markdown treatment.
- **Toggle-status fix** (field report): button label showed the switch target, reading as the wrong state; now shows the current mode.

## 2026-07-26 — Quality-of-life wave (0.9.0)

- **View menu** owns Console/Line Numbers/Minimap/Word Wrap (alphabetical, all default ON; existing configs preserved via layout serialization).
- **Recent Files** (per project, configurable count 1-30) and **Goto Line** (Ctrl+G) via a generic status-bar mini-buffer.
- **Deleted-file rescue** (Keep Buffer/Close Tab banner; Save restores) and **tab session persistence** — the session (Library/ADKOMTextEditor/session.json) now stores dirty tabs' unsaved CONTENT, eliminating the window-close Unsaved Changes dialog entirely.
- **Non-modal Unsaved Changes**: generic ShowBanner replaces DisplayDialogComplex for tab close and Close Other Tabs.
- **Menu shortcut hints** per keymap; **window-wide clipboard**; **document context menu** (symbol/clipboard/file/language groups, Show in File Explorer); **drag-to-reorder tabs** (capture-transfer across mid-drag rebuilds; fix: switch-before-capture, the compat MouseDown's rebuild was killing the captured element).
- **Deterministic word-level undo** (EditKind grouper: same-kind + contiguous + gap/caps + word boundaries; status feedback).
- **F12 in metadata views** via VirtualContextPath.
- **Localization** (L10n.Tr + po catalogs ja/ko/zh-hans/zh-hant); defect #4: Unity's loader falls back to the first po alphabetically when the language has no catalog — en.po identity catalog fixes English UIs showing Japanese.
- **Release gate**: Tools/check-editor-guards.ps1 in CI and the release procedure (step 0). Release-notes policy: all fixed defects listed, issue numbers cited.

- **0.10.0 feature campaign (2026-07-27)**: all 19 must-have editor features in 7 batches — editing primitives, structural editing (auto-close/brace match/block comment/expand selection), multi-caret
  + column selection, word autocomplete (+ language keywords), code folding + indent guides, Rename/Find All References/Format Document, Quick Open + bookmarks + drag-drop text; plus Tab UX (single-line strip w/ scroll arrows, tab-list dropdown, Tabs context submenu, settings-tinted tabs). New partials: Navigation, QuickOpen, CodeView.Completion, CodeView.Folding.
- **0.10.0 polish round (2026-07-27, Cary's Defects.md)**: minimap squish on big files (issue #7), indent guides invisible (CharWidth measured spaces as 1px), smooth-scroll pixel-snap shimmer fix, folded-header "{ ⋯ }" + double-click fold/unfold with centered reveal, clickable gutter fold arrows (PickingMode bug), Add Tab menu integration (populateDefaultMenuItems hook), non-modal close-time Save All notice + reopen banner (modal reverted — MCP freeze), singular/plural l10n strings (no "(s)"), Window-menu shortcut display, resizable console splitter, Markdown local-image rendering
  + MarkdownTest.md, Auto-Reload Changed Files setting, SelectionNeedle clamp (issue #8).

- **0.11.0 — the AI release (2026-07-27)**: GitHub Copilot integration (official copilot-language-server via Node/LSP-stdio; npm auto-install to Library/; device-flow sign-in persisting in the server's own token store; ghost text with replace-range semantics, alternatives cycler, Tab/Enter accept, unsaved-buffer support via pseudo paths; autocomplete popup yields to ghost). Ask Unity AI via AssistantApi (reflection, optional package). Search Results console tab replaces console dumps AND a short-lived popup. First-run welcome tabs, Ctrl+Click links (source + rendered md), console Ctrl+C copy, red non-compressible banner. New dependency: com.unity.nuget.newtonsoft-json.

- **0.12.0 — the games release (2026-07-28)**: Addons framework matured — single-file AND multi-file folder addons (one assembly per folder), full IAteAddonLifecycle with single resident instances, addon SECURITY (dangerous-API scan, markdown risk report, Scanner Results console tab, one-time consent keyed to content SHA-256 + scanner version). Game API 1.1: GameMode (chrome hidden, block cursor, click guard, undo bypass), WriteAt Overwrite/Insert, fg/bg color overlay, key events + polling, text-coordinate mouse, 30 Hz tick, Prompt, SetFont, SetTitle. Games shipped: Snake and a faithful Rogue 5.4.4 port (13 files, ~4.5k lines, specs extracted from the BSD source by agents). Fixes: tab-dropdown scroll (issue #9), stale consent report, scanner first-hit-only + prefs coverage, Snake glyph bow, Rogue death-freeze/overlay/shift-run/double-turn. Editor-guard gate now excludes Samples~ (not compiled by Unity; guards would break the addon compiler).

- **Z-Machine interpreter game (2026-07-29)**: clean-room version-3 Z-machine written from The Z-Machine Standards Document (no Infocom code, no GPL interpreter source). Lives in the EDITOR CORE (Editor/ZMachine, Tools -> Z-Machine) rather than as an addon, so its story-file download is a native ATE feature trusted by installation and never hits the addon consent gate (Cary's call 2026-07-29): memory/header, ZSCII decode/encode, object table + properties, dictionary tokeniser, full v3 opcode set, call/branch/store, save/restore/restart, output streams. ZScreen is a scrolling transcript + status line on the game API; input via inline key echo through a core window key hook (not the addon input events, which an addon reload resets). ZStory downloads the MIT-licensed Zork I/II/III (pinned commit SHAs, user action, to the user's machine; ATE ships no game). Verified live: interpreter runs downloaded Zork I end to end (room 'West of House', status line, parser responds, multi-turn loop). NOTE: an orphaned truncated fragment from an interrupted earlier draft (Samples~/Addons/ZMachine/ZmCore.cs, class Zm, never committed) had leaked into %APPDATA% via InstallSamples; investigated and removed.

## 2026-07-29 — Z-Machine auto-mapper, SVG export, interiors (0.12.1)

- **Auto-mapper** (Editor/ZMachine/ZMap*, ZMapView, ZMapLayout): builds a map purely by OBSERVING engine state each turn — current room (global 0) and the object-containment tree — plus the direction word parsed from the player's command. Nothing drives execution. Rooms are laid out on a per-level grid; objects tracked through the containment tree (room, carried, origin). Interactive pane in the console area (bidirectional scroll, click a room/item for details); toggle via Tools → Z-Machine → Auto-map.
- **Spoiler-free**: a new object shows only when directly visible (in a room or carried); items nested in an unopened container stay hidden until taken/opened. The player avatar is never mapped (childless global-referenced room-child heuristic + movement confirmation).
- **Persistence**: the map and the on-screen transcript ride alongside the game save as `.map`/`.log` sidecars (ZMachine AfterSave/AfterRestore hooks), so restore brings the explored map and scrollback back.
- **Terminal rework**: ZScreen is now a growing, scrollable transcript with a PINNED status overlay (new CodeView game status bar + AteApi.SetStatusBar) instead of a viewport-fitted grid — fixes the terminal that shrank a row per command (measurement feedback) and gives real scroll-back. Caret sits at the cursor glyph; output reliably scrolls into view (issues #28, #29).
- **Connections**: directional splines with arrowheads (both ends when a genuine two-way corridor) that attach at the exit's side/corner, so a non-Euclidean link (e.g. a SOUTHWEST exit back to a room due south) is a visible curve. Keyed by attach endpoints, not room pair, so distinct corridors between the same rooms stay separate (issue #31).
- **SVG export** (ZMapSvg, "SVG" button): the whole map to a standalone `.svg` — all pages stacked, dashed cross-page connectors for level/area changes, and an alphabetical multi-column **object legend** (name + location) at the bottom.
- **Interiors as areas**: entering a container via `in` opens a NEW area at its own origin, so an interior lays out on its own grid instead of colliding with the exterior. Rooms carry an Area id; the pane/SVG page by (area, level).
- **Map-pane fixes**: h/v scrollbars (canvas no longer shrinks to viewport), canvas sized to spline extents (no clipping), current room centred with a FIXED canvas margin — the viewport-derived padding had fed back and exploded the canvas, blanking the map (issue #32).

## 2026-07-29 — 0.12.2 (update-check 403 fix)

- **Auto-update HTTP 403.** UpdateChecker polled the GitHub REST API (`api.github.com/.../releases/latest`), rate-limited to 60 req/hr/IP; GitHub returns 403 (not 429) when exceeded — hit on a shared/NAT'd network, and some corporate proxies block api.github.com outright (reported updating from 0.12.0). Switched to the releases Atom feed on github.com (`/releases.atom`): unauthenticated, not API-rate-limited, newest entry first. ParseTagName now reads the first `/releases/tag/` link, keeping the `tag_name` JSON parse as a fallback (issue #33).

## 2026-07-29 — 0.12.3 (auto-map polish)

- **Map rendering**: procedural per-room colours (golden-ratio hue hash of the room id — deterministic, same every game); connection splines stroked in the FROM room's colour with arrowheads; obstacle-aware routing (ZMapLayout.RouteControls samples the cubic and bows around boxes). Zoom (slider 0.4–2.5× + Ctrl-wheel, kept in sync) via a scale transform on the canvas inside an unscaled sizer sized to base*zoom (#34: the first attempt scaled the scroll content directly and gated sizing on the viewport → blank map).
- **Revealed objects** (ScanObjects): an object nested in a container in a known room now surfaces when the game reveals it — its own or its container's attribute bits change since last turn (grating under moved leaves; leaflet in an opened mailbox). New MapAttrBits reads the v3 32-attribute bitmask. A revealed connector/door whose home room is unvisited (the grating lives in the room below) is shown in the CURRENT room as a ◇ diamond (IsConnector); up/down exits render as ▲/▼. Connectors are placed once and never relocated, and LoadFrom resets the parent/attr baselines, so a restore no longer piles every diamond into the current room (#35).
- **Placement**: grid push/cascade (PushChain shoves a contiguous run aside — no overlaps, new room stays adjacent to its connector), then a bounded stress-minimising relaxation (Resolve) that re-settles the page after a room/edge appears so a far-flung connection pulls its endpoints together (squared-distance stress, accept-if-improving → converges; 30-pass cap).
- **Misc**: transcript scrolls to the input line after a restore (AteApi.ScrollToEnd, re-applied for 300 ms past the layout settle); game tab titled with the proper name via ZStory.TitleForFile ("Zork I"); #ids after every room/object name.

## 2026-07-30 — 0.13.0: the IDE release (newfeatures.md campaign, all 21 items)

Full campaign plan/statuses in [[New Features Plan]]. One-line map (details in the per-item commits):

- **Semantic capability pattern**: each Roslyn feature is an optional interface on the provider (`ISemanticCompletion`, `ISemanticDiagnostics`, `ISemanticOccurrences`, `ISemanticGeneration`, `ISemanticSymbolInfo`), discovered by cast, background-threaded, version-gated on apply.
- IntelliSense (member/scope, one query per word, local prefix filtering); error underlines (model.GetDiagnostics, hover tip); read/write occurrence highlighting (declaration/assignment/++/--/ref-out = write).
- Snippets (SnippetStore plain-text format, live tab-stop sessions in CodeView.Snippets); generators (UnityMessages catalog + override stubs).
- History window (undo-delta reconstruction — never snapshots); #region folding + navigator; Search Results filter box; Edit menu submenus; Settings sections; auto-save on focus loss; JSON + Shader classifiers.
- Find/Replace in Files (FindInFiles engine: buffer-aware, binary-sniffed, glob/regex; ReplaceJournal global undo of full before/after per file; stale matches verified and skipped; BOM-aware disk writes).
- Reflection inspector (ISemanticSymbolInfo CLR names incl. nested Outer+ Inner; live-polled statics/instances; run parameterless statics).
- Git (GitService CLI backend: unified-0 diff → gutter marks; line-porcelain blame → virtual tab; log --follow history → revision tabs; status/stage/commit/push panel; --all --topo-order graph with lane assignment → interactive V⇄H branch tree, dirty-tree guarded checkouts).
- Spell check (SCOWL 2020.12.07 sizes 10–60 US+UK+contractions = 114,926 words in Editor/SpellCheckData~, attributed in THIRD-PARTY-NOTICES; Hunspell .dic imports; global + ProjectSettings/AteDictionary.txt; comments/strings in code, camelCase per-hump).
- Every engine was verified headlessly via MCP script-execute against the live compilation/repository before commit (results quoted per commit).

## Conventions

- Branch per feature/fix from main; merge with `--no-ff`; branches are kept.
- Defects get GitHub issues (#1 LFS install, #2 editor hang) even when fixed immediately.
- Every feature is verified live in the editor via the ai-game-developer MCP before merging to main.

- **Paper-trail sweep (2026-07-28)**: all historical bugs from git history and this log filed as GitHub issues (#10-#25, closed with root cause + fix commit + shipped version; two umbrellas for the 0.10.0 defects round and the 14 code-review defects). Issue #4 closed retroactively (en.po fix shipped 0.9.0). Only #6 (wrap freeze) remains open.

## 2026-07-31 / 08-01 — Find/Replace rework, console views, Section menu, Manual (0.13.1)

- Docker dev container for running Claude Code against the project (`Tools/claude-docker.sh` + `Dockerfile.claude`, install-anywhere from `~/bin`; per-project `.mcp.docker.json` shadows `.mcp.json`; named volumes `claude-home`/`claude-gh` carry Claude + gh auth across projects).
- **Find/Replace rebuilt** as one fixed-size tabbed parameter dialog (Notepad++ layout: Find / Replace / Find in Files / Bookmark). Search Modes: Normal, extended escapes (`\n \r \t \0 \xHH`), regex with ". matches newline"; In-selection scoping; Count; per-tab button stacks; swap button. The dialog shows NO results: every Find All lands in the Search Results console tab (PickLocation rows; untitled tabs jump via DocId=index+1). FindInFiles engine gained NoRecurse/Hidden/DotNL; Replace in Files applies all matches as one journaled operation (per-match checkboxes removed). F3 core preserved.
- **Console area**: console converted to a per-line ListView (row selection, Ctrl+C copies selected lines); shared AteViewStyle (frame + monospace + zebra) across Console, Search Results, Scanner, and the new Bookmarks view; View menu toggles each tab INDEPENDENTLY (serialized per-tab visibility; pane hides when no tab is offered).
- **Bookmarks**: Edit → Bookmarks → View Bookmarks fills a Bookmarks console tab — per-file disclosure groups sorted by file, filter box, jump rows; the dialog's Bookmark tab does bulk Bookmark All / Clear / Copy Matched Text (MarkBookmarkLines API).
- **Section menu** (after Window): Classes/Properties/Methods of the current tab via a regex outline scan, sorted, rebuilt per click, jumps to declarations. ALL GoToLine-based jumps now CENTER the target (CenterOnLine reuse; game mode untouched).
- **Menus**: GenericMenu treats '/' in item labels as submenu separator — bit us with "Find / Replace" nesting a bogus submenu; fixed via U+2215 lookalike substitution (MenuSafe). Edit + View gained a single "Find ∕ Replace" toggle; Window menu tab list sorted; Help → Open Manual.
- **Tooltips on every control** (dialog, console tabs, tab strip, status bar, menu bar, Settings gaps, History, Git, Quick Open, inspector) — ~90 new localized strings this cycle across all five catalogs.
- **Manual.md** — 18-section user manual at the package root; first-run welcome order is now README → Manual → RELEASE-NOTES. Standing rule: the manual is updated in the same session as any feature change.
- Every stage verified headlessly via MCP script-execute against the live editor (window structure, searches, menus, jumps quoted per step).

## 2026-08-01 — 0.13.2 hotfix: Copilot npm install (issue #40)

- Field report from a second project: enabling Copilot spammed `Cannot find module '…copilot\node_modules\npm\bin\npm-prefix.js'` (console + Settings row). Root cause: npm 10.9+ ships a cmd shim that runs `node %~dp0\node_modules\npm\bin\npm-prefix.js`, and a batch file started by BARE NAME via CreateProcess resolves %~dp0 to the WORKING DIRECTORY (Library/.../copilot). Older shims had no npm-prefix.js — the bug lay dormant until a Node upgrade.
- Fix: route the installer through `cmd.exe /d /s /c "npm …"` on Windows (cmd's PATH lookup keeps %~dp0 correct); exec plain `npm` elsewhere (the old unconditional npm.cmd could never work off Windows). Verified on the affected machine via MCP: bare npm.cmd reproduces; the routed real install command dry-runs clean (npm 11.13.0). Lesson: never launch .cmd shims by bare name from CreateProcess.
