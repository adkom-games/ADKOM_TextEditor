# Changelog

All notable changes to this package are documented here. Format follows [Keep a Changelog](https://keepachangelog.com); versions follow semver.

## [Unreleased]

### Added
- **Git window Changes-pane upgrades**: a **Select ▾ menu** beside the title bulk-sets the staging checkboxes (All / None / Modified / Untracked / Added / Deleted / Renamed / Invert, applied to the filtered rows); a right-aligned **Filter** field narrows both the working-tree and inspected-commit file lists; status letters are tinted like the gutter marks (green added/untracked, amber modified, red deleted, blue renamed); every file row gained a **context menu** (diff, open in editor, reveal, stage/unstage, and the Git submenu incl. Time Lapse — Git Panel pruned, it would recurse); inspecting a commit shows read-only **date / hash / author / title** fields above the message box, Time Lapse-style; and the commit-message box uses a smaller font.

- **Selection readout in the status bar**: with a selection, the caret position becomes "Ln a, Col b — Ln c, Col d" plus the selected character/word/line counts; disjoint multi-caret selections show "Multiple Selections" with the counts summed over every range.

### Changed
- **ATE draws its own tooltips everywhere.** All ATE windows (main editor, Git, Time Lapse, Edit History, Diff, Find/Replace, Inspector, Goto prompt) replace Unity's native tooltip with a shared cursor-following bordered tip: it appears just off the pointer, follows the mouse, and pulls back inside the window at the edges — matching the rendered-Markdown link tip, which now shares the exact style and placement. Controls still declare plain `tooltip` strings; nothing changed at declaration sites.
- **Author fields everywhere show the email too** — "Name \<email\>" in the Time Lapse author field, the Git window's inspected-commit author field, the branch graph's tooltips and selection status line, the History tab's author column, and Blame's author column.
- **Git window**: the horizontal branch-history layout now centers vertically in the view instead of hugging the top (recentering live on window resize; the vertical layout is unaffected).
- **Git window file rows: state-eligible git operations in the Git submenu.** Working-tree rows now offer exactly what the file's state allows — Stage (unstaged or untracked changes), Unstage (staged changes), and Discard Changes… (tracked working-tree modifications, confirmed before running) — inside the context menu's Git submenu; the old top-level Stage/Unstage items are gone.
- **Tab-strip context menu: Show in File Explorer** on every file-backed tab (hidden for generated buffers — blame, history, release notes, untitled, Settings).
- **File History opens as a focused tab.** Git → File History… (everywhere it appears) now opens a read-only "History \<file\>" tab — one hash/date/author/subject line per commit, searchable and copyable — instead of a transient dropdown popup.

### Fixed
- **Rendered Markdown tabs now carry the full document context menu.** Two defects stacked: the rendered view's own menu offered only clipboard/lock items (unlocked rendered mode had none at all), and on text it never appeared anyway — the selectable labels' native "Copy" menu displays after, and over, anything shown from a mouse handler. The menu is now built through UI Toolkit's contextual-menu event, replacing the native items, and carries the clipboard/lock group (locked mode) plus Tabs navigation, Switch to Markdown Source, Save As, Close Tab, and — for file-backed docs — Show in File Explorer and the Git submenu. Clicks inside an open block editor keep the editor's native menu; virtual rendered docs like release notes stay file/git-free. Source-mode tabs of any type (.md, .txt, …) already had the full menu.
- **Rendered Markdown: link tooltips describe the destination.** Hovering any link now shows its markdown title (`[text](url "title")`) or the link's text/alt above the "Ctrl+Click to open \<url\>" line — image links included. The tip follows the cursor (a native element tooltip would anchor to the tall segment label's bottom, far from the pointer) and pulls back inside the view at the edges.
- **Rendered Markdown: badge links render as links.** The README idiom `[![alt](badge)](target)` — an image nested inside a link — shredded into raw syntax because the inline parser matched the nested image's `]` as the link text's end. It now renders the alt text as a link to the target (the badge image can't render inline). Plain inline images (`![alt](url)` mid-paragraph) also became clickable: the `[img] alt` marker now opens the image URL. Ctrl+C plain-text copies render the nested form as "alt (target)".
- **Locked rendered Markdown: selection tint and hit zones are correct on headings.** The overlay assumed one uniform line height per label — but headings render as rich-text `<size>` spans inside 12px body labels, so lines within one label differ in height: heading tints were clipped and only a slice of a heading line responded to the pointer. Selection rects and pointer-to-character mapping now measure each line's real band (top = previous line's cursor bottom, bottom = its own), so tall heading lines tint fully and are selectable across their whole height.
- **Locked rendered Markdown: triple-click selects the paragraph.** The native label machinery never delivered a triple-click line select in the read-only rendered view; the document-selection layer now handles it, selecting the clicked block (copyable with Ctrl+C as plain rendered text). Double-click word selection is unchanged.
- **Branch history: the horizontal layout's branch labels are clickable.** The rotated ref labels under the lanes now select and inspect their commit — with the same context/double-click menu and hash/date/author/subject tooltip as the vertical layout's labels; previously they were inert text.
- **Git window: the title's second line now mirrors the selection.** It only updated when clicking a graph node's tiny dot — clicking the commit-label text (the usual target) left it stale, and any git operation overwrote it with working-tree text mid-inspection. Every selection path now routes through one updater: a selected commit shows its hash/date/author/subject, returning to the working tree (HEAD marker or the Working Tree button, which now also drops the gold selection) shows the changed-file count.
- **Git window: inspecting a merge commit now lists its files.** `git show` prints an empty combined diff for a clean merge, so clicking merge nodes on main showed a bare "Commit \<hash\>" with no files; the file list now falls back to the first-parent diff — what the merge actually landed on the branch. Separator rules were also added under the Changes header (title/Select/Filter) and the Branch History bar.
- **Branch history: unmerged branches no longer read as merged.** Graph colors were keyed by LANE, and lanes are reused for compactness — a stale unmerged branch inherited the lane (and color) of newer merged branches above it, so its chain read as one continuous line flowing into main. Colors are now keyed by contiguous chain SEGMENT: every branch bubble gets its own hue, an unmerged chain is visibly its own line, node positions are unchanged, and main's trunk keeps the first hue.
- **Git window: bulk staging no longer dies on big file sets (and no longer floods the console).** Select All / Select None built one giant `git add` / `git restore --staged` command; past the OS argument-length limit it failed, and the failure log echoed every path into the console, garbling its rows. Batched commands now run in chunks of 32 chained sequentially, embedded quotes in paths are escaped, the failure log truncates the command and message, and the ATE console itself caps any single entry at 2,000 characters as a backstop.
- **Branch history: the trunk lane belongs to main again.** Lane 0 — the graph's first color — used to go to whatever ref had the newest commit; mid-work that is the feature branch, whose first-parent chain runs straight down main's shared history, so the whole trunk line took the feature branch's color and identity. Lane 0 is now seeded with the local main/master tip (HEAD's branch when neither is in the window), so the trunk always reads as main.

## [1.1.0] - 2026-08-06

### Added
- **Git Time Lapse** (Tools → Git → Time Lapse Current File…, also in the document context menu): replays the current file's git history under a slider — oldest commit on the left, the live tab buffer on the right. A read-only editor view (copyable, framed, wrap/scrollbars as needed) steps through every revision; each step tints added/changed lines diff-style with matching gutter bars, a version x/y readout sits between the step buttons, the revision's date/hash/author, commit title and full message sit in read-only fields below the text area, a bottom status line shows +/− line counts of the current tab against it, and the centered line is held while content changes. **Copy to Tab** pushes the shown revision back into the file's tab (undoable while active). Multi-instance: every invocation opens its own window. Arbitrarily long histories are handled by a **sliding window** of fetched revision contents around the slider position — filled during pauses nearest-first, dropped as the window slides on, and fetched on the spot when the slider outruns it. The window size is a per-window override (field beside the slider) seeded from the new **Time Lapse Window Size** setting in Options (5–500, default 50).
- The **Git submenu now also appears in the document right-click context menu**, mirroring Tools → Git.
- **Time Lapse change navigation**: ▲/▼ buttons hop between the shown revision's change regions, wrapping at the ends per the new **Wrap Searches** checkbox in the bottom row.
- **Truly context-sensitive menus everywhere**: the document context menu now hides inapplicable items instead of disabling them, virtual tabs (blame, snapshots, release notes) get a read-oriented menu, the Settings tab gets tab navigation, and the read-only views in the Time Lapse, Edit History and Diff windows gained context menus of their own — git entries act on the viewed file, and self-recursive entries are pruned (no Time Lapse inside Time Lapse).

## [1.0.4] - 2026-08-05

### Changed
- **Asset Store validator compliance.** The shipped sample addons moved from `Samples~/Addons` to `Addons~`: they are loaded by ATE from disk (Tools → Addons → Install Sample Addons), not imported through the Package Manager, and the validator requires every `Samples~` folder to be a declared, importable sample — which addon source must never be, since it is deliberately unguarded for ATE’s Roslyn addon compiler. The Scripting sample now ships `.meta` files so importing it keeps stable GUIDs, and `package.json` declares the mandatory `unityRelease` field (minimum 6000.0.80f1, matching the verified minimum). No behavior change: addon install, signing, and the release gates all follow the new path.

## [1.0.3] - 2026-08-05

### Fixed
- "Localization must be run in Main Thread" when the Copilot Language Server installs: the npm-install worker thread resolved its two error strings with `L10n.Tr`, which is main-thread-only. The strings are now resolved before the thread starts and captured — the same fix issue #39 applied to Go to Definition, whose "repo-wide scan" missed this file. The scan is now a real script (`scratchpad` tooling graduated to a scope-aware pass over Task.Run/Thread bodies minus Post islands) and reports zero remaining offenders.

## [1.0.2] - 2026-08-05

### Fixed
- **The package no longer assumes its own name.** The Asset Store build ships under the portal-assigned technical name `com.adkomgames.text-editor` (the GitHub build stays `com.adkom.text-editor`), and eight code sites located package files through the hardcoded name — stylesheet loading, Roslyn installation, the spell-check dictionary, release notes, session restore of package docs, and sample-addon installation would all have silently failed in the store build. Every lookup now goes through `AtePackage`, which resolves the running package from the assembly. CI renames the package on the `upm-store` branch, and the store gate fails if the name and the portal ever disagree.
- **Installing both builds at once is detected.** The two names mean the GitHub and Asset Store builds can coexist in one project, where they define the same assemblies and break compilation with an unexplained "assembly already exists" error. ATE now detects the pairing and reports exactly which package to remove.

## [1.0.1] - 2026-08-05

### Fixed
- The package now declares the engine modules it actually uses: `com.unity.modules.unitywebrequest` (the update check), `com.unity.modules.uielements` (the entire UI), and `com.unity.modules.jsonserialize`. Only `newtonsoft-json` was declared before, so installing into a minimal project — one whose manifest lacks those modules, as opposed to a template-created project that has them — failed to compile. Found while building a clean project for the Asset Store upload.

## [1.0.0] - 2026-08-04

The 0.14.5 feature set, declared stable. No code changes from 0.14.5.

- First stable release. Clean-install verified on Unity 6000.0.80f1 (minimum), 6000.3.21f1 (LTS), and 6000.5.6f1 (newest) — zero console warnings or errors, Semantic Features exercised, and a real player build audited to contain nothing of ATE.
- The public scripting surface (`ADKOM.TextEditor.Scripting`, AteApi 1.3.0) keeps its existing semver contract: additions on minor releases, breaking changes only on a major.
- From here on, versioning follows semver at the package level too: 1.x releases will not break projects, settings, sessions, or addons that 1.0.0 works with.

## [0.14.5] - 2026-08-03

### Changed
- **In-editor gaming is now opt-in.** A new machine-wide setting (Settings → Games → **Enable In-Editor Games**, off by default) gates the entire Games feature: the Games menu itself, and game addons under Tools → Addons (which would otherwise be a bypass; they are removed from that menu outright). With the feature off, snapshotted Z-Machine games are not resumed after a reload (their transcript tabs restore as plain documents); games already running when the setting is turned off keep running. The documentation shelf under Help is NOT gated — the player guides stay readable. Toggling takes effect immediately in every open ATE window. **Existing users: the Games menu disappears after this update until the setting is enabled once.**

## [0.14.4] - 2026-08-03

### Fixed
- Unity 6000.5 spammed "Trying to access the DPI setting of a visual element that is not on a panel" on window creation: CreateGUI sets word wrap before the window attaches, and every `MeasureTextSize` call in that state now warns (two per character, across the whole document). `CharWidth` returns its font-derived estimate without measuring while off-panel — the geometry-change handler already re-measures with real widths once the panel exists, so wrapping is unchanged.

## [0.14.3] - 2026-08-03

### Fixed
- Compile errors on Unity 6000.0–6000.2 (clean install of 0.14.2): `ITextSelection.GetCursorPositionFromStringIndex` and `DropdownMenuSizeMode` are both 6000.3+ APIs. The cursor-position mapping now goes through a version-gated shim — on older editors the same answer comes from a public set/read/restore round-trip through `cursorIndex`/`cursorPosition`, verified pixel-identical to the real API where both exist — and the Git file-history dropdown uses the anchored `DropDown` overload. The fallback bodies are compile-verified with no version defines set.

## [0.14.2] - 2026-08-03

### Added
- Downloading a Zork story file now asks first. Picking a game you do not have opens a confirmation window — source repository and story file (both links to GitHub, at the pinned commit), the commit itself, the licence, the exact size, the SHA-256 fingerprint and the destination folder — with Download / Cancel; nothing reaches the network until Download is pressed. It is a utility window rather than a modal dialog, so it never freezes the editor, and it appears every time rather than remembering consent.
- **Copy Link to Clipboard** on the story-file row copies the exact raw URL the download uses, so it can be checked, fetched or archived independently.
- Downloads are verified twice: against the byte count at the pinned commit, and against its SHA-256. Either check failing deletes the file rather than playing it, so neither a captive-portal page nor a same-sized substitution can reach the interpreter. Files already in the story folder are size-checked when the menu is built, and a mismatch simply offers the download again.
- Asset Store distribution: a `upm-store` branch built by CI from the same source with the guideline-sensitive files swapped, plus a `Tools/check-store-compliance.ps1` release gate (run for both the development tree and the generated store tree) that fails the build if package-set mutation or Editor-internal reflection reappears outside `Editor/Distribution/`, if a consumer-visible path reaches 150 characters, or if required files/`package.json` fields go missing.
- `package.json` now declares `documentationUrl`, `changelogUrl` and `licensesUrl`, so the Package Manager's Documentation / Changelog / Licenses links resolve.

### Changed
- **AteApi 1.2.0 → 1.3.0**: adds `AteApi.DebugLog(object)` — `UnityEngine.Debug.Log`'s counterpart for addons, with the same signatures (including the `(object, UnityEngine.Object)` overload) and the same formatting, writing to ATE's console pane instead of the project's. Addons had no way to report to the user except `UnityEngine.Debug.Log`, which now means writing into the project's own console. Existing addons are unaffected — this is an additive minor version — and the shipped samples (Hello Addon, Word Count, Insert Timestamp) plus the new-addon template now use it and declare `ApiVersion = "1.3"`.
- ATE stays out of Unity's console. `AteConsole.Info` and `AteConsole.Warn` — 75 call sites covering update checks, semantic setup, addon loading, Copilot status, session restores and the rest — now write only to ATE's own console pane; previously every one of them was mirrored to Unity's console, burying the messages a user's own project is trying to show them. Only `AteConsole.Error` still reaches Unity's console, so a failure is visible with no ATE window open.
- The two obsolete-semantics-module messages were promoted from Warn to Error: they report a condition that blocks compilation project-wide, which has to be visible outside ATE.
- The Z-Machine map's SVG-export failure goes to ATE's console instead of calling `Debug.LogWarning` directly. now lives in one file (`Editor/Distribution/AtePackageInstaller.cs`), and everything that reflects into Editor internals lives beside it (`AteAddTabIntegration`, `AteCodeEditorRegistry`). Behaviour of the GitHub build is unchanged; the Asset Store build gets refusing variants of those files, because submissions may neither add/remove packages in a user's project nor use Editor-internal APIs.
- The obsolete-semantics-module cleanup only removes the package where that is allowed; otherwise it says what to remove in the Package Manager instead of doing it silently.
- Registering ATE as Unity's diff/merge tool writes the External Tools preference keys directly instead of calling the internal `InternalEditorUtility.SetCustomDiffToolPrefs` — an already-present fallback path. An open Preferences window now needs reopening to show the new values.

### Fixed
- **The bundled Roslyn assemblies are marked editor-only when Semantic Features installs them.** They were copied into `Assets/Plugins/ADKOM.TextEditor/Roslyn` with Unity's default plugin settings — "Any Platform" (verified on a fresh copy) — which would have included all ~14 MB of Roslyn in the user's player builds, contradicting the package's core promise that nothing it does reaches a build. The importer is now forced to editor-only after every install, and the same pass runs on every semantics bootstrap, so installs made by earlier versions are repaired automatically the next time the editor loads. Verified end-to-end: a simulated default-settings install reads ANY-PLATFORM, the repair flips both DLLs to editor-only, and a second run is a no-op.
- Selection geometry in a rendered (locked) Markdown document survives a window resize. Labels re-wrap without a re-render, which left the memoized cursor positions (and line heights) pointing at the old layout — measured two lines of drift after narrowing the view 250 px, putting both the highlight and the pointer-to-character mapping on the wrong characters until the next render. The caches now invalidate on a content geometry change and any active selection redraws at the new positions (the character offsets themselves survive a re-wrap).
- The editor-guard release gate takes a `-PackageRoot`, and CI now runs it a second time against the assembled `upm-store` tree — whose `Editor/Distribution` files come from `Tools/store-overrides` and were never seen by the dev-tree run.
- `NullReferenceException` from `ReloadActiveFromDisk` when the editor regained focus during a domain reload or editor start. Unity raises `OnFocus` from `HostView.RegisterSelectedPane` while the dock is enabling — before `CreateGUI` has built the window — so the external-change check ran against a banner that did not exist yet. `OnFocus` now waits for the UI (`_uiReady`, `[NonSerialized]` so a hot-serialized `true` cannot survive the reload and defeat the guard), and nothing is skipped: `CreateGUI` ends in `SwitchTo`, which runs the same check. The four banner actions that dereferenced the bar directly now go through the null-safe `HideBanner` as well.
- Dragging a selection through a rendered (locked) Markdown document no longer floods Unity's console with `IndexOutOfRangeException` from `ATGTextJobSystem.ConvertMeshInfoToUIRVertex`. The fault is in Unity's Advanced Text Generator (6000.3.19f1): a `TextElement` whose text contains an emoji — a glyph supplied by OS font fallback rather than a Unity `FontAsset` — throws every time its mesh is regenerated, and a selection drag regenerates the label under the pointer on every mouse-move. Nothing in the package can catch it (it happens inside a job) or opt out of it (`UITKTextHandle.useAdvancedText` is internal Unity API), so ATE's own documentation no longer uses decorative emoji: README, Manual, CHANGELOG, RELEASE-NOTES and Troubleshooting keep their typographic characters (— → … ▲ ◀ ⇄ □) and lost only the section-marker pictographs. A document of *yours* containing emoji can still trigger it — see Troubleshooting. Reported to Unity; the write-up is in `docs/unity-bug-atg-emoji.md` in the repository.
- Selection in a rendered (locked) Markdown document stays character-precise across block boundaries. It used to degrade to whole-block highlighting the moment a drag left the block it started in — the large filled rectangles over each covered region. Unity renders a native selection in at most ONE `TextElement` (setting a range on a second silently clears the first at the next repaint), so a spanning selection is now painted by the view itself: the usual three-part shape — from the anchor character to the end of its line, full width through the middle, and up to the pointer's character on the last line — drawn into an overlay behind the text. A single-block selection still uses Unity's own, so double-click word select and triple-click line select are untouched. Finding the character under the pointer needs the inverse of UI Toolkit's index→position mapping, which is monotonic and therefore binary-searchable: about 12 probes per pointer-move, verified to round-trip with zero drift.
- A selection in a rendered (locked) Markdown document survives clicking a tab, the window title bar, or switching to another application. Unity wipes a native text selection on any focus change (measured: even moving focus to a toolbar button in the same window zeroes it), so the moment a drag ends, a single-block selection is converted into ATE's own drawn highlight, which has no focus dependency — and copy keeps working after the focus loss.
- Ctrl+C in a rendered Markdown document no longer writes an empty string to the clipboard after a backward drag (right-to-left or bottom-to-top within one block): the character ends arrived in reverse order and the slice came out empty. The ends are now ordered, and as a belt, an empty copy result never overwrites whatever was on the clipboard.
- **Copy honours the selection exactly.** Copying a span used to emit whole blocks at both ends, taking more than was highlighted; it now slices the first and last blocks at the selected characters. Images and tables inside a span still contribute their whole plain form (alt text, tab-separated cells), since neither can hold a text selection.
- Dragging a selection past the top or bottom of a rendered (locked) Markdown document now scrolls it. Holding the pointer beyond an edge keeps scrolling and extending the selection — ramping from 4 to 28 px per tick with distance — so a document taller than the window can be selected in one drag; previously the selection simply stopped at whatever was on screen when the drag began. The scroll runs on a scheduled tick rather than off pointer-move events, because those stop arriving the moment the pointer stops moving, and it releases on mouse-up.
- A selection spanning blocks in a rendered (locked) Markdown document highlights continuously: the margin between covered blocks is filled as if it were a selected empty line (a full-width bridge rect per gap in the drawn overlay), so the highlight reads as one range instead of stripes with seams. Table cells on the same row produce no bridge, and verified: a multi-block span's rects tile with zero vertical holes.
- Drag-selection in a rendered (locked) Markdown document is now fast as well as character-precise. Three per-mouse-move costs were removed: the label under the pointer no longer regenerates its whole text mesh (the view takes the pointer capture and draws the highlight itself — Unity's native selection had been re-shaping the label on every move), `GetCursorPositionFromStringIndex` results are memoized per label (measured 1.5–1.7 ms per call — it re-shapes the text every time, and the pointer-to-character search makes ~12 calls per move), and the highlight is emitted as raw quads instead of painter2D paths (the path tessellator cost ~9 ms per repaint for a long selection). A warm full-document drag now averages 2.3 ms per move (worst 7.5), a short in-paragraph drag 1.1 ms; the first drag through not-yet-visited text pays a one-time probe cost (avg 11.7 ms during that sweep). Clicking a scrollbar no longer clears the selection, and Ctrl+Click links plus double/triple-click word/line selection still reach the labels natively.
- Selecting text with the mouse in a rendered (locked) Markdown document was heavily laggy in long files. A run of consecutive text blocks rendered as one `TextElement`, and Unity regenerates an element's entire text mesh whenever its selection changes — so a document of nothing but text (this package's RELEASE-NOTES.md, 40,768 characters) became a single label costing **124.7 ms per pointer-move**. Segments are now capped at 2,000 characters, splitting only between blocks: the same document renders as 23 labels and a mouse-move costs **6.7 ms on average, 14 ms worst case**. Character-precise selection inside any single block is unchanged; a drag spanning the new seams falls through to the existing block-span selection, exactly as it already did across images and tables.
- Dragging a block selection in a rendered Markdown document restyled every label on every mouse-move — 27,556 style writes for a full-document drag in this package's README, now ~166. Only labels whose selected state actually flips are touched.
- Nothing in the package contacts the network unprompted in the Asset Store build: automatic update checks are compiled out (the Settings toggle and frequency field are hidden with them), leaving **Check for Updates Now** as the only path, and its result points at the Package Manager rather than offering to install.

## [0.14.1] - 2026-08-02

### Fixed
- "Copilot is ready." appeared twice in the console: the sign-in check and the server's own status notification both report Ready (with different detail texts), and each fired the announcement. The console line now prints only on a real state change; detail-only updates still refresh the Settings row silently.
- The macOS/Linux diff-tool shim's executable bit is set via File.SetUnixFileMode when the runtime provides it (chmod as fallback), and a failure is now reported in the console instead of silently leaving Unity's diff/merge invocations dead.

## [0.14.0] - 2026-08-02

### Added
- Diff / Merge tool (Tools → Diff / Merge…): first-class comparisons of files, folders (recursive, with per-file status and drill-down), and open tabs — aligned side-by-side view with intra-line change highlights, change-region navigation, and side swapping. Multiple diff windows can be open at once, and every window restores its full state — comparison, merge choices, and edited result — after a domain reload.
- Two-way diffs are merge editors too: framed columns with a draggable center splitter, and per-change ◀ / ▶ gutter buttons (plus whole-side ◀◀ / ▶▶) copy changes across; edited sides are marked and saved with Save Left / Save Right. Works for git diffs as well — merge left on a working-tree diff and save to revert chosen regions.
- Three-way merge: left / base / right columns, automatic merging of one-sided changes, numbered conflict panels with Take Left/Base/Right/Both (plus All Left / All Right), a live editable result pane, and Save with git-style conflict markers for anything left unresolved.
- ATE can be Unity's Revision Control Diff/Merge tool: a Settings button registers it in Preferences → External Tools via a generated per-project shim, so version-control diffs and merges open in ATE; the previous tool can be restored with one click.
- Git window: double-clicking a file now opens a diff against its previous version — working-tree files diff against HEAD, files of an inspected commit diff against the commit's parent.
- View → Hidden Characters: renders non-printing characters as faint glyphs (spaces ·, tabs →, NBSP °, zero-width □, C0 control pictures, ¶ line ends). Display-only; per-window view state.
- Find works in rendered Markdown: Find/F3 and Search Results jumps scroll the rendered view to the matching block and highlight it.
- Console tab gained a Filter box (substring, with an "N of M shown" header), mirroring Search Results.
- Console line copy: single-selection rows; Ctrl+C or right-click → Copy Line copies the active line whole (multiline selection removed by design).
- Manual, README, and release notes reformatted to natural line flow (no hard column wraps in prose); the Scripting Reference, the Documentation~ docs, and this changelog reformatted likewise.
- The View menu top group holds every view toggle (Bookmarks, Console, Find ∕ Replace, Hidden Characters, Indentation Guides, Line Numbers, Minimap, Search Results, Word Wrap), sorted by localized label.
- Help → Documentation submenu (at the end of the Help menu, after a separator): the Games player guide pinned on top, then every reference doc sorted by localized label — Addon Signing, ATE Manual, AteApi Design, Game API Design, Keyboard Shortcuts, Localization, Scripting Reference, Snippets, Troubleshooting. Strings localized in all five languages.
- Multiple simultaneous Z-Machine games: every menu pick starts a new instance in its own tab ("Zork I", "Zork I (1)", ...), the key hook routes input to the active game, and each game gets its OWN map tab in the console area, labeled with the game title — activating a game brings its map tab to the front.
- Z-Machine games SURVIVE domain reloads and editor restarts: on beforeAssemblyReload/quitting every running game is silently snapshotted (VM state via a new .azx context format that captures the parked sread, transcript via the .log sidecar, map via .map) into a session folder, its doc stamped with the snapshot id; after the reload the launcher rebuilds each game around its surviving transcript tab — same input prompt, same map — with a console note. RNG state is not carried. The "(unloaded)" title + tooltip remain as the fallback when a snapshot cannot be taken or restored; snapshot files are deleted when a game ends and unreferenced ones are garbage-collected.
- AteApi 1.2: stateful addon lifecycle (mobile-app model) — IAteAddonStateful adds SaveState/RestoreState called by the host before every teardown and after the next load; AteDocument.StateTag (session-persisted) re-binds an addon's documents; the host owns state storage (addons never touch disk). ApiVersion bumped to 1.2.0.
- Snake and Rogue are stateful: both survive domain reloads, addon reloads, and editor restarts. Snake gained a pause mode (Space, context-sensitive with restart), starts every game paused, and resumes from a reload PAUSED; Rogue serializes its entire dungeon (reflection graph serializer, RogueSave.cs) and resumes on the same turn, dropping any open prompt. Both declare ApiVersion 1.2; reinstall + re-sign the samples.

- Section → Bookmarks submenu: the current document's bookmarks sorted by line ("line: text preview"), rebuilt every time the menu opens so it tracks tab switches and bookmark changes; selecting one jumps to the line.
- #pragma bookmark <label>: source-declared bookmarks — matching lines appear in Section → Bookmarks under their label, merged (and winning) when a line is also toggled.
- Settings (Language & Tools): unknown-pragma (CS1633) status row with a one-click Suppress in This Project button that writes -nowarn:1633 into Assets/csc.rsp and requests a recompile, so #pragma bookmark compiles clean.
- Git branch graph: every branch (lane) gets its own stable color — golden-angle hues on both the commit dots and their connecting edges; forks/merges take the outer lane's color. Selection stays gold; HEAD keeps its lane color under a green ring.
- Git panel: the divider between Changes and Branch History is a real splitter (TwoPaneSplitView) — drag to resize; the width persists across sessions.
- Git panel: clicking a commit in the branch graph inspects it — its file list replaces the Changes pane (Working Tree button returns) and its message fills the message box. On HEAD the Commit button becomes Amend: edit the message and press it for a message-only amend (git commit --amend --only); older commits are read-only with the reason in the tooltip. A HEAD pseudo-node (hollow ring on HEAD's lane, one slot ahead of the newest commit; filled faintly while the tree is dirty, labeled "HEAD *") represents the working tree — it is the ACTIVE node by default (selection gold) and clicking it re-activates it, returning to the staging view; opening the panel always starts there.

- Six new reference docs in Documentation~: Games (the player guide), Addon Signing, Keyboard Shortcuts, Snippets, Localization, and Troubleshooting.
- Games menu in the menu bar (before Help): How to Play (a second link to the player guide, intentionally), the Z-Machine (Zork) menu, then every installed addon game sorted by name (Snake, Rogue). Localized in all five languages.
- AteApi Design (Documentation~/ATEAPI Design.md): full documentation of the scripting API — design principles, every member, and working examples.
- Game API Design rewritten from the implementation-era design sketch into a real game-programming reference with a complete skeleton addon and per-area examples; moved from Documentation~/AI to Documentation~.
- Markdown lock (read-only rendered view): a lock button left of the MD toggle makes the tab read-only — clicks select text for copying instead of opening block editors; the formatting bar hides while locked. On by default for .md files (Settings → Open Markdown Locked); lock state persists per tab across sessions.
- Copying from locked rendered Markdown always yields plain rendered text (no markers or rich-text tags): select + Ctrl+C, Ctrl+C with no selection copies the whole document, and a right-click menu offers Copy Block as Text, Copy All as Text, and Copy Link URL. Links copy as "text (url)" so URLs survive. Cut and paste are disabled.

### Changed
- Open Manual moved from the top of the Help menu into the Help → Documentation submenu and renamed ATE Manual.
- The Z-Machine (Zork) menu moved from Tools into the new Games menu.
- Fold Region, Unfold Region, and Unfold All moved from the View menu into the new Edit → Code → Region submenu.
- Addons are FOLDER-based only: each subfolder of the shared addons folder is one addon (all its .cs files compile together); single-file addons are retired. Stray top-level .cs files migrate into folders of their own automatically — approval and any signature must be renewed for the new folder identity, and old sidecar .atesig files are left behind.
- Sample addons restructured accordingly: HelloAddon, InsertTimestamp, SnakeGame, and WordCount each ship as a folder now. Their author signatures must be re-created (Tools → Addons → Signing → Sign Shipped Samples) because the folder identity hash differs from the single-file one; RogueGame was already a folder and keeps its signature.
- IAddonCompiler: the single-file TryCompile member is gone — TryCompileMany is the only compile path.

### Fixed
- The Git panel came back EMPTY after a domain reload (file list never repopulated, stuck on “Working…”): two independent causes. Unity’s reload hot-serialization keeps even private fields — a null inspected-commit hash came back as “” (suppressing the working-tree rebuild forever) and a mid-flight busy flag came back stuck true (blocking every future refresh); those fields are now [NonSerialized]. And the synchronization context captured in early post-reload callbacks swallows Posts, so async git results never landed — the refresh now waits for the functional Unity context (AteMainCtx.WhenReady; delayCall is no escape: one registered inside a UIToolkit scheduler callback never fires).
- The Git panel commit-message draft is preserved across domain reloads (serialized with the window) — and across commit-inspection round-trips: leaving an inspected commit restores the draft instead of blanking the box.
- Find/Replace lost typed-but-unsaved field contents (and checkbox/option states) on a domain reload: state was only flushed on close and tab switch. It now also flushes on beforeAssemblyReload, so everything typed is restored after the reload.
- Latent post-reload stalls fixed across the editor: the same hot-serialization quirk could revive in-flight guard flags as true, permanently blocking git gutter marks, occurrence highlighting, and Find/Replace searches. All such guards are now [NonSerialized].
### Fixed
- Git > File History... appeared to do nothing: GenericMenu.ShowAsContext needs Event.current, which the async history-loaded continuation never has, so Unity silently dropped the picker. It now uses UIToolkit's GenericDropdownMenu, anchored over the code view — context-independent.
- Status-bar messages flash yellow briefly when they change, so easy-to-miss feedback (history loading, save results, git output) actually draws the eye.
- Auxiliary windows went blank/unresponsive after a domain reload. Every window now recovers: Git panel rebuilds from its serialized repo root and re-reads state (returning at the HEAD/working-tree view); Find/Replace re-adopts its singleton, re-acquires its owner, and keeps ALL search state via SessionState (also fixing F3 forgetting the last search across reloads); History rebinds to the active document; the unsaved-documents notice closes itself when orphaned (the session already protects those buffers). Owner references ride Unity object serialization ([SerializeField] EditorWindow).
- Undo histories SURVIVE domain reloads: UndoWorld/UndoOp/UndoSeg are Unity-serializable and ride each document's serialization (the active document's live world is parked on it via beforeAssemblyReload); the null-Segments "simple op" marker is restored on attach (Unity revives null lists as empty). Ctrl+Z and the History timeline work across compiles.
- Git panel polish: both panes and the commit-message box are framed (the console-area subview look), and the Changes/Commit header truncates long titles with an ellipsis instead of overflowing into the file list.
- The Git panel commit-message box clipped long messages; it now shows a scrollbar when the text exceeds the box (multi-paragraph bodies from inspected commits especially).
- Keyboard shortcuts (Ctrl+S and every other global binding) ran against stale content while an inline Markdown block editor was open: the editor only commits on focus loss, and shortcuts do not move focus. Command dispatch now commits the open block editor first, exactly like a menu click does.
- The Markdown lock button clipped its emoji glyph to half width: the toolbar font measures the fallback-font emoji narrower than it renders. The button now has an explicit width.
- Locked rendered Markdown re-architected as a CONTINUOUS document: runs of text blocks render as one selectable rich-text segment (headings, quotes, code boxes, lists, and rules reproduced with rich-text tags), so native character-precise selection and copy span the document like a normal editor. Images and tables keep their real elements (they cannot live inside a rich text) and interrupt segments; drags across them fall back to whole-block span selection with Copy Selection as Text. Ctrl+A selects the whole document; search highlight marks the match block inside the segment.
- Diagnostics and spell-check marks were thick solid bars under the text; both now render as proper squiggly underlines (a smooth Painter2D sine wave, pooled SquiggleElement) in their existing colors — red/amber for errors/warnings, blue for spelling.
- The typed approve-anyway consent (tampered/impersonation signatures) was un-completable: the type-the-name prompt lived in the status bar and cancelled on ANY focus loss, and its cancel re-showed the consent banner — so clicking a menu bounced the user in an approve/banner loop forever. The confirmation now lives IN the banner itself as an inline name field (one step: type the name, Approve and Run), survives stray clicks, and matches the name case-insensitively.
- Jumps into a just-opened file (ate:// security-report links especially) landed at the top instead of centered: CenterOnLine clamped to the scroller's stale range before layout settled, and the single deferred re-center often ran too early. GoToLine now re-centers until the scroll range holds still (capped), so every jump truly lands centered.
- file:line links in the addon security reports (*.security.md) were not clickable in rendered Markdown: the renderer only link-tagged http(s)/mailto targets, so ate:// spans got the link color but no click region. The ate:// scheme is now link-tagged too; Ctrl+Click jumps to the finding (source view already worked).
- Undo/redo "Undid/Redid N character(s)" feedback no longer floods the console (visible from History stepping especially); it stays in the status bar only.
- The green update-available icon vanished after any domain reload (compile, play mode) because the discovered version was static-only state; it is now persisted per project and the icon stays next to the settings gear until the update is actually performed.
- Clicking the update icon on an embedded (development) copy only logged to the console; it now always opens the update dialog, with Install Now disabled and a manual-update hint for embedded copies.
- On narrow windows the Markdown toolbar could squeeze the pinned update icon and settings gear out of the toolbar; the format bar now yields its own space (clipping whole buttons) instead.

## [0.13.2] - 2026-08-01

### Fixed
- Copilot language-server install failed with MODULE_NOT_FOUND (`…copilot\node_modules\npm\bin\npm-prefix.js`) on npm 10.9+ — the npm.cmd shim's `%~dp0` degrades to the working directory when the batch file is started by bare name; the install now routes through `cmd.exe /d /s /c` on Windows and plain `npm` elsewhere (issue #40).

## [0.13.1] - 2026-08-01

### Added
- Tabbed Find/Replace dialog (Notepad++-style): Find, Replace, Find in Files, and Bookmark tabs in one fixed-size parameter window. New search powers: Search Modes (Normal / extended escapes `\n \r \t \0 \xHH` / regex with ". matches newline"), Count, In-selection scoping, Find All in Current / All Opened Documents, swap button, and Find-in-Files filters, directory picker, follow-current-doc, sub-folder and hidden-folder switches.
- All Find All results land in the Search Results console tab as clickable rows (untitled tabs included); the dialog itself never shows results.
- Bookmarks view: Edit → Bookmarks → View Bookmarks lists every open document's bookmarks in a console tab — grouped per file behind disclosure triangles, sorted by file, filterable. The dialog's Bookmark tab bulk-bookmarks matches (Bookmark All / Clear all bookmarks / Copy Matched Text).
- Section menu (after Window): the current tab's Classes, Properties, and Methods — sorted, rebuilt on every click, jump-to-declaration.
- Help → Open Manual + a full 18-section user manual (Manual.md); the first-run welcome now opens README, Manual, and Release Notes.
- Tooltips on every control across the entire UI, localized in all five catalogs.
- Triple-click selects the line; dragging extends by whole lines.
- Go to Matching Bracket handles preprocessor directives; Match Previous Bracket (Ctrl+[) cycles them in reverse.
- History window: read-only real-editor preview, arrow-key timeline navigation, per-document tab bar.
- Text drag-and-drop shows an insertion-point caret; green update-available icon pinned left of the settings gear.

### Changed
- Console: per-line rows with alternating tones, row selection, and Ctrl+C line copying (was one selectable text block); Console, Search Results, and Bookmarks views toggle independently from the View menu.
- Console-area views share one look: framed subview, monospace font, zebra rows; every jump from results/bookmarks/menus lands centered in the view.
- Replace in Files applies every found match as one journaled, undoable operation (per-match checkboxes removed).
- Edit and View menus offer a single "Find ∕ Replace" toggle; the Window menu lists tabs alphabetically; Ctrl+Shift+F/H open the Find in Files tab.

### Fixed
- Semantics fallback for files outside Unity assemblies (issue #36).
- Metadata stubs carry using directives so F12 works inside them (issue #37).
- Triple-click reliably fires (click chain counted internally, issue #38).
- Go to Definition resolves L10n strings before the background task (issue #39).
- "All Tabs" search scope did not persist on non-English editors (locale-dependent comparison; superseded by the tabbed dialog).

## [0.13.0] - 2026-07-30

The IDE release — every item of the new-features campaign.

### Added
- IntelliSense: compiler-accurate C# completion. After `.` the actual members of the expression (instance vs static vs namespace contexts, accessibility respected, base chain walked); elsewhere every symbol in scope. Overload groups collapse with a (+N) count and a signature detail; semantic candidates blend with word/keyword completion, one background query per word filtered locally as the prefix grows.
- Error highlighting: live compiler diagnostics as red (error) / amber (warning) underlines, version-gated so stale results never land on edited text; hover shows the full message.
- Read/write reference highlighting: a bare caret on a symbol highlights every use in the file — reads in the match tint, writes (declaration, assignment incl. compound, ++/--, ref/out) in amber.
- Customizable code snippets: one plain-text file (Tools → Edit Snippets…, hot-reloaded), [trigger] + body with $name$ tab stops and $END$; expand via Tab or the completion popup; live sessions cycle stops with Tab/Shift+Tab; 12 C# defaults ship.
- Code generators: Generate Unity Method (33 messages with correct signatures, duplicates refused when semantics can tell) and Override Method… (overridable methods/properties up the base chain, stubs with correct accessibility, parameter modifiers, and base calls).
- Visual edit history (Edit → History…): the undo/redo timeline, one row per step with edit summaries; any point previews the document exactly as it was (changed line highlighted); restore (still undoable), open as a new tab, or copy.
- Find/Replace in Files: project-wide search (Assets + Packages or any root; glob, regex with group substitutions, case/word; binary sniffing; open buffers searched as seen); per-match checkboxes with a before → after preview; Replace Selected applies as ONE journaled operation with global Undo/Redo across all touched files (stale matches verified and skipped, disk writes BOM-aware).
- Reflection inspector (right-click → Inspect Symbol…): live static fields/properties (play-mode values update; writable primitives editable), Run buttons for parameterless static methods, and a scene-instance picker for Component types with live instance members.
- Git integration (system git CLI): gutter diff markers vs HEAD (green added / amber modified / red deletion stubs), Blame as an annotated read-only tab, File History opening any past revision, and a Git Panel with stage/unstage checkboxes, commit, push, and an INTERACTIVE branch-history tree — vertical ⇄ horizontal toggle, checkout branch / create branch here / checkout detached (guarded while dirty).
- Optional spell checking (Settings, off by default): comments/strings in code, everything in markdown/plain; camelCase judged per hump; bundled 114,926-word SCOWL-derived dictionary (US + UK spellings + contractions, attributed in THIRD-PARTY-NOTICES); Hunspell .dic / plain .txt imports from a shared Dictionaries folder; right-click → add to the user or per-project dictionary.
- Syntax coloring for JSON/JSONC (.json, .asmdef, .asmref — keys vs values distinguished) and Unity shaders (.shader, .hlsl, .cginc, .compute — ShaderLab + HLSL keywords, types, calls).
- #region support: regions fold like brace blocks everywhere, and Edit → Go To → Go to #region… jumps between them.
- Find All References results gained a live filter box ("N of M shown").
- Auto-Save on Focus Loss (Settings, per project): dirty file-backed documents save when the window loses focus; untitled buffers never prompt.

### Changed
- Edit menu reorganized: common items flat, the rest grouped into Find, Selection, Line, Code, Go To, and Bookmarks submenus.
- Settings organized under section headers (Appearance, Editor, Fonts, Language & Tools, Display, Editing, AI, Files & Saving, Updates).

## [0.12.3] - 2026-07-29

### Added
- Auto-map polish: procedurally colour-coded rooms (a deterministic colour per room, same every game), connection lines drawn in the FROM room's colour with arrowheads, and splines routed around the boxes.
- Dynamic map zoom: a Zoom slider (0.4×–2.5×) with a live percentage and Ctrl+mouse-wheel, kept in sync.
- Revealed hidden objects: an item unhidden by the game — a grating under moved leaves, a leaflet in an opened mailbox — now appears in the room. Connector/door objects (the grating) show as a ◇ diamond, and up/down transitions render as ▲/▼ triangles.
- Room #ids shown after every room/object name in the map panel, tooltips, and the SVG legend.
- Grid push/cascade placement: a new room stays adjacent to the room it connects to; occupants are pushed aside instead of overlapping. After a room or edge appears, a bounded relaxation re-settles the page so a connection to a distant room pulls its endpoints together.

### Fixed
- Map pane went blank after adding zoom (a scale transform on the scroll content, and content sizing gated behind viewport layout) — the canvas is now scaled inside an unscaled sizer and sized unconditionally (#34).
- Restored games no longer pile every connector diamond into the current room; connectors are placed once and never relocated, and the map's parent/attribute baselines reset on load (#35).
- The transcript scrolls to the input line after a restore.
- Z-Machine game tab is titled with the proper game name ("Zork I", not "zork1").

## [0.12.2] - 2026-07-29

### Fixed
- Automatic update check no longer fails with HTTP 403 Forbidden. It polled the rate-limited GitHub REST API (`api.github.com`, 60 requests/hour/IP → 403 when exceeded on shared/NAT'd networks; some proxies block the host outright). It now reads the releases Atom feed on github.com (`/releases.atom`) — unauthenticated and not API-rate-limited — parsing the latest tag from the newest entry, with the previous `tag_name` JSON parse kept as a fallback (#33).

## [0.12.1] - 2026-07-29

### Added
- Z-Machine interpreter (built into the editor core, Tools → Z-Machine): a clean-room version-3 virtual machine written from the public Z-Machine Standards Document (no Infocom code, no GPL interpreter source) that plays any `.z3` story file — memory/header, ZSCII, object table, dictionary tokeniser, full v3 opcode set, save/restore/restart, output streams. One-click download of the MIT-licensed Zork I/II/III (pinned commits, on your action, to your machine; ATE ships no game file). The screen is a growing, scrollable transcript with a pinned status line, on the Game API.
- Auto-mapper (Tools → Z-Machine → Auto-map): builds a map purely by observing engine state as you explore — rooms on a per-level grid, directional connections drawn as arrowed splines (both ends for a two-way corridor), items you have found, and a clickable side panel. Spoiler-free: an object appears only once directly visible (in a room or carried), never while nested in an unopened container, and the player avatar is never shown.
- Interiors as separate areas: entering a container via `in` opens a new area on its own coordinate grid, so an interior no longer collides with the exterior it is nested inside; the pane and export page by (area, level).
- Map SVG export ("SVG" button): the whole map to a standalone `.svg` — all pages stacked, dashed cross-page connectors for level/area changes, and an alphabetical multi-column object legend (name + location) at the bottom.
- Map/transcript persistence: the explored map and the on-screen transcript are saved beside the game save and restored with it.

### Fixed
- Z-Machine terminal shrank a row per command and eventually collapsed (a viewport-measurement feedback loop); replaced the viewport-fitted grid with a growing transcript that scrolls back (#28, #29).
- Save/restore resume PC accounted for branch polarity (Zork's save is branch-on-failure); ghost `<ENTER>`; status line clipped at the right edge; typing sluggishness (per-keystroke input rendering).
- Map: distinct corridors between the same two rooms were merged into one two-way spline — now keyed by attach endpoints so they stay separate; non-Euclidean exits are drawn from the correct corner (#31).
- Map pane: horizontal/vertical scrollbars appear when the map exceeds the pane; splines no longer clip at the edges; the current room is centred; a viewport-derived canvas padding fed back and blanked the map — now a fixed margin (#32).
- Horizontal scrollbar showed with nothing clipped (content width was over-padded by 60px); now padded by the caret width only (#30).

## [0.12.0] - 2026-07-28

### Added
- Game API (AteApi 1.0.0 → 1.1.0): per-document GameMode (word wrap and syntax highlighting off, programmatic writes bypass undo, keys the game doesn't consume can't edit the buffer, context menu suppressed, editor chrome — gutter, indent guides, minimap — hidden, terminal-style block cursor, clicks don't move the caret); WriteAt(line, col, text, Overwrite|Insert), ReadAt/GetLine/LineCount, TryGetCursor; per-cell foreground/background color overlay (SetColor/ClearColors — render attributes, never buffer content); consumable keyDown/keyUp events + IsKeyDown polling (reset on focus loss); mouseMoved/mouseButtonDown(consumable)/mouseButtonUp in text coordinates; AteApi.Prompt (status-bar mini-buffer with cancel); StartTick clamped to 30 Hz (pauses unfocused); SetFont/ClearFont per-document font override (zoom adjusts the override); SetTitle tab titles; IAteAddonLifecycle (OnLoad/OnUnload/OnFocusGained/OnFocusLost) with single resident instances.
- Multi-file (folder) addons: a subfolder of the addons folder is one addon — all its .cs files compile into a single assembly (exactly one [AteAddon] class); consent hash covers the whole file set.
- Addon security: source scanned against known-dangerous API patterns (process execution, file deletion/writes/reads, network, native interop, dynamic code loading, registry, secrets, prefs access); markdown risk report + clickable "<Script> Scanner Results" console tab (file:line rows jump to the location); NOTHING runs — resident OnLoad included — until one-time approval keyed to the file content's SHA-256 and the scanner version.
- Games (installable samples): Snake (colors, polling, prompt, ticks) and Rogue — a faithful C# port of BSD Rogue 5.4.4 as the first folder addon (original monster/item tables, combat formulas, dungeon generator with dark/maze/treasure rooms, all traps, potions/scrolls/wands/rings, hunger, identification, tombstone and winner screens).

### Fixed
- Tab-list dropdown selection not always scrolled into view; the strip could scroll fully left and push the active tab off-edge (issue #9 — un-laid-out tab rects report x=0/width=NaN).
- Addon security consent showed a STALE risk report when the report file was already open in a tab (the reload prompt was stomped by the consent banner).
- Scanner reported only the first occurrence per dangerous API, and missed ordinary EditorPrefs/PlayerPrefs access entirely.
- Snake: playfield rows bowed right — non-monospace fallback glyphs; cells are now ASCII colored fg==bg.
- Rogue (pre-release polish): freeze on death (message pump clobbered the tombstone screen), help/inventory overlays instantly overdrawn, Shift+arrows not running, monsters acting twice per running step.

### Added (addons framework, pre-branch)
- Tools > Addons > Install Sample Addons: copies three ready-to-run sample addons (Hello Addon — resident event subscriber; Insert Timestamp — document editing; Word Count — document reading) from the package into the shared addons folder and loads them.
- Addons framework: single-file C# addons in the machine-shared %APPDATA%/ADKOM/TextEditor/Addons folder load into EVERY ATE instance (compiled in-memory by the bundled Roslyn — no project changes). [AteAddon(Name, Category, ApiVersion)] + IAteAddon (menu-invoked) or IAteAddonResident (OnLoad at startup for AteApi event subscriptions); Tools > Addons > Category > Name with case-insensitive category merging; semver gate against AteApi.ApiVersion (1.0.0) with incompatible addons shown disabled and the reason; Reload Addons / Open Addons Folder items; compile errors and addon exceptions isolated to the ATE console.

## [0.11.0] - 2026-07-27

### Added
- The notification banner (sign-in codes, file conflicts) is now RED, bold, white-on-red and roomier — impossible to miss, no more squished text.
- Copilot works in unsaved and virtual documents too (synced under a pseudo path — nothing is written to disk).
- Multi-line Copilot suggestions render correctly: the first line continues at the caret and the remaining lines start at column 0 with the suggestion's own indentation (they were all shifted right by the caret offset). Copilot status changes also log once instead of repeating 'Copilot is ready.'.
- When a Copilot suggestion arrives, the word-autocomplete popup hides automatically — the two no longer fight for the same screen space (Copilot wins; the popup returns as you keep typing whenever no suggestion is showing).
- Copilot suggestions show a small ◂ 1/3 ▸ cycler above the ghost text when alternatives exist — click the arrows or press Alt+[ / Alt+] to cycle; Tab OR Enter accepts, Escape dismisses.
- Copilot ghost text now honors the suggestion's REPLACE RANGE: accepting a suggestion replaces the text Copilot rewrote (e.g. the auto-closed paren after the caret) instead of inserting a duplicate; the ghost shows only the not-yet-typed remainder.
- Console text selections copy with Ctrl+C (explicit handler; shows 'Copied.' in the status bar).
- New Search Results tab in the console pane (View menu toggle): multi-result commands (Find All References today) list their hits there as clickable rows — file:line + preview, hover highlight — and clicking jumps to the location, opening the file if needed. (Replaces both the old console dump and a short-lived popup.)
- AI account controls in Settings: the Copilot row's button flips to Sign Out once signed in, and a Unity AI row shows the Editor's Unity account with a banner-confirmed 'Sign Out of Unity Account…' button (Unity AI has no narrower sign-out — it rides the Editor login, so the confirmation spells out that it signs out the whole Editor).
- Ask Unity AI (when com.unity.ai.assistant is installed): 'Ask Unity AI About Selection...' and '...About This File...' in the document right-click menu and the Tools menu open Unity Assistant's prompt popup with the text attached — you type the question there, and no AI call (no points) happens until you submit it. The menu items simply don't appear when the package is absent.
- GitHub Copilot inline suggestions (Settings, default off; requires Node.js and your own Copilot subscription): ghost-text completions appear as you type in file-backed documents — Tab accepts, Escape dismisses. The official Copilot Language Server installs itself (npm, per project, never shipped) on first enable; sign-in is GitHub's device flow (the code is auto-copied to the clipboard). Non-modal throughout.
- First-run welcome: the first time ATE opens in a project (nothing to restore), the package README and RELEASE-NOTES open as tabs.
- Clickable links in ANY document: Ctrl+Click a bare http(s)/mailto URL — or anywhere on a markdown [label](url) — to open it in the browser/mail client; hovering shows a "Ctrl+Click to open …" tooltip. Go to Definition still handles everything that isn't a link. Works in rendered Markdown mode too: link labels and bare URLs are underlined link spans — Ctrl+Click opens them (a plain click still edits the block), with the same tooltip on hover.

## [0.10.1] - 2026-07-27

### Added
- Ko-fi support link: badge at the top of both READMEs and a text link at the top of RELEASE-NOTES (visible in the in-editor release-notes tab after every update).

## [0.10.0] - 2026-07-27

### Fixed
- Indentation guides were invisible: character-width measurement returned 1px for spaces (the measurer trims trailing whitespace), so every guide was crushed against the gutter. Guides now sit at true indent columns and are slightly more visible.
- Smooth scrolling shimmer: the ease landed on fractional pixel offsets, which rasterized the input field and the color overlay differently (a 1px color mis-registration). Every animation frame now snaps to a whole pixel.
- Folded regions now show the whole collapsed shape — the header line ends with a dimmed "⋯ }" instead of a bare "{". Double-clicking the "⋯ }" indicator reopens the region, and double-clicking any '{' or '}' character folds (or unfolds) the region that brace bounds; the gutter arrows are clickable as well.
- Occurrence highlighting no longer throws when the selection state is momentarily out of sync with the text (clamped; issue #8).

### Changed
- Tabs stay on a SINGLE line: the strip clips at the edges, and scroll arrows appear on its left and right whenever tabs overflow (the active tab auto-scrolls into view; the tab-list dropdown stays at the far right).
- Tabs are colored uniformly with the color chosen in Settings (the per-tab random shade variation was retired as too busy); the active tab stands out as a brighter, fully opaque version with an accent top border.
- Menu-bar buttons get more side padding; hovering/pressing shows only the left and right edges of the selection so buttons look bounded on the sides.

### Added
- Closing the ATE window with unsaved documents now surfaces a single NON-MODAL floating notice (never blocks the editor or background tooling) listing the documents, with one-click "Save All Now" or "Keep in Session" — the buffers are already persisted, so ignoring it is always safe. Re-entrant closes reuse the one open notice. As a safety net, reopening the window with dirty session buffers shows a "N document(s) have unsaved changes from your last session" banner with Save All / Dismiss.
- The console pane is resizable: drag the divider between the editor and the console (highlights on hover); the height persists across sessions.
- Auto-Reload Changed Files (Settings, default off): files that change on disk reload automatically when the buffer has no unsaved edits; dirty buffers still get the banner so edits are never lost silently.
- ADKOM Text Editor appears in every dock's "Add Tab" menu (tab right-click and ⋮): picking it docks ATE as a sibling tab of that pane, or focuses the already-open ATE window. (The Add Tab list is a fixed set of built-in panes, so this hooks the editor's internal menu-population event; a Window menu entry was added as well.)
- Rendered Markdown now displays standalone images: local paths (relative to the document or absolute) load inline with the alt text as a caption; missing files show a placeholder. Remote URLs are not fetched.
- Minimap: for documents taller than the minimap strip, the code graphics and viewport rectangle were vertically compressed into the top of the strip (proportionally worse the bigger the file), while click navigation used the full strip (issue #7). Sampled rows now spread across the whole strip.

### Added
- Quick Open (Ctrl+, / Ctrl+P VSCode / Ctrl+T Rider): a centered overlay that fuzzy-lists open tabs and recent files instantly and every text file under Assets/ and Packages/ once you type a filter; Up/Down navigate, Enter or click opens (recorded in navigation history), Escape dismisses. Also in the File menu.
- Bookmarks (Ctrl+Alt+K toggle, Ctrl+Alt+N / Ctrl+Alt+P next and previous with wrap-around, Clear Bookmarks in the Edit menu): per-document line bookmarks shown as an orange line number in the gutter; they shift with edits above them, and jumps are recorded in navigation history.
- Drag and drop of selected text: press inside the selection and drag to move it to the drop point — one undo step; hold Ctrl to copy instead. The dropped text stays selected; a simple click inside the selection still just places the caret.
- Semantic refactoring commands (Semantic Features required for the first two): Rename Symbol (F2 / Shift+F6 Rider) renames every in-document occurrence via the status-bar prompt as one undo step; Find All References (Shift+F12 / Alt+F7 Rider) lists every use across the symbol's assembly in the console with path:line previews; Format Document (Shift+Alt+F / Ctrl+Alt+L Rider) re-indents from brace depth — string/comment-aware, preserving content and blank lines, one undo step.
- Code folding: brace-delimited regions collapse and expand from clickable gutter arrows or Ctrl+Shift+[ / Ctrl+Shift+] (Unfold All in the View menu). Folds ride the virtualized row model, shift with edits made above them, survive typing on their header line, reveal when an edit touches the hidden body, and unfold automatically when the caret lands inside (search, Goto Line, Go to Definition).
- Indentation guides: a faint vertical bar per indent level, spanning blank lines; toggle in the View menu (on by default).
- Word-based autocomplete: a popup of prefix-matched words harvested from the current document AND every other open tab. Appears while typing (2+ word chars) or on demand with Ctrl+Space; Up/Down navigate, Enter/Tab accept (one undo step), Escape dismisses, further typing refines. Case-matching candidates rank first, and the active language's keywords (all of C# today) are offered as first-class candidates via the classifier's ICompletionKeywords capability — future languages join automatically.
- Tab strip upgrades: a dropdown button at the right end of the strip lists every open tab (numbered, dirty-starred, active checked) and jumps on pick; the document context menu opens with a "Tabs" submenu doing the same; and tabs are colorized — each tab renders a stable per-document shade of a base color chosen with the new "Tab Color" RGB selector in Settings.
- Multi-caret editing: Alt+Click adds/removes carets; Add Next Occurrence (Ctrl+D VS Code, Alt+J Rider, Shift+Alt+. VS) grows a selection per press; Select All Occurrences (Ctrl+Shift+L VS Code, Ctrl+Alt+Shift+J Rider, Shift+Alt+; VS); Add Caret Above/Below (Ctrl+Alt+Up/Down) for column-style editing. With multiple carets: typing, Backspace/Delete, Enter, and paste apply at every caret as ONE undo step (paste distributes line-per-caret when counts match); Escape or a plain click collapses to the primary caret. Extra carets and their selections render live.
- Editing must-haves, batch 2: auto-closing brackets/quotes (openers insert the pair, closers type over, Backspace removes empty pairs, selections get wrapped; "Auto-Close Brackets" setting, on by default); brace matching (caret-adjacent bracket and its match highlighted; Go to Matching Bracket — Ctrl+] VS, Ctrl+Shift+ VS Code, Ctrl+Shift+M Rider); Toggle Block Comment /* */ (Ctrl+Shift+/ or Shift+Alt+A); Expand/Shrink Selection (caret → word → line → bracket block → document; Shift+Alt+Right/Left, Rider Ctrl+W / Ctrl+Shift+W); and Navigate Back/Forward through caret history across tabs (Ctrl+- / Ctrl+Shift+- VS, Alt+Left/Right VS Code, Ctrl+Alt+Left/Right Rider), recorded on Goto Line, Go to Definition, and external opens.
- Editing must-haves, batch 1: word-wise delete (Ctrl+Backspace / Ctrl+Delete); Cut/Copy with no selection act on the whole current line; Insert Line Above/Below without splitting (per-keymap bindings: VS Ctrl+Enter above / Ctrl+Shift+Enter below, VS Code the reverse, Rider Shift+Enter below / Ctrl+Alt+Enter above); Join Lines (Ctrl+J); Select Line (Ctrl+L in the VS Code layout); Transform UPPERCASE/lowercase and Sort Selected Lines (Edit menu); and per-project save cleanups — "Trim Trailing Whitespace on Save" and "Ensure Final Newline on Save" (Settings, off by default).
- Scripting API: `ADKOM.TextEditor.Scripting.AteApi` is a stable, semver-governed surface for editor scripts — open window/files, `NewDocument`, `Documents`/`ActiveDocument` handles (`GetText`, `SetText`/`ReplaceRange`, `GoTo`, `Activate`, `Save`, `Close`), and events: `documentOpened/Closed/Saved`, `activeDocumentChanged`, `textChanged` (debounced). Edits to the active document are one undo step; edits to background documents are documented as not undoable. See `Documentation~/Scripting.md`. Accidental public members on internals (CodeView edit/hit-test helpers) are now `internal` — the facade is the only supported scripting surface.
- Scripting docs: a prominent "Things you must know" section (domain reloads erase event subscriptions — subscribe from [InitializeOnLoad]; handles expire; background edits not undoable; async dirty-close; modal Save As on untitled; debounce; no nesting; main thread; virtual tabs), plus an importable Package Manager sample ("Scripting (AteApi)") demonstrating every API member as working menu commands.

### Changed
- Internal: the main window class is decomposed into partial classes by concern (Commands, Menus, Tabs, Session, Banners, ContextMenus, Semantics, Api) — 2,300 lines down to 1,300 in the core file, pure code motion verified behavior-identical by the full regression battery. The recent-files list is parsed once and cached (was re-read from EditorPrefs on every File-menu open), and the tab strip skips rebuilding when nothing visible changed.
- Keyboard commands are now defined in a single command table (bindings, handlers, and menu shortcut hints in one place per keymap), removing the triple definition that let labels drift from behavior. Visible fix: the VS Code and Rider layouts now display Redo's canonical Ctrl+Shift+Z in menus (Ctrl+Y still works in VS Code). Behavior is otherwise unchanged — verified by a 54-assertion binding matrix across all three keymaps.
- Settings scoping audit: settings that describe the project are now stored per project instead of machine-wide — Tab Size (indentation convention), Semantic Features (consent to install Roslyn into that project), Automatic Updates and Check Every (days) (the package install is per project), and the file dialog's remembered directory. Existing values migrate automatically. User-preference settings (keymap, font, theme, smooth scrolling, Markdown default view, recent-files count, fallback editor) remain machine-wide, as does the update-check timestamp (a per-machine GitHub rate limiter).

### Fixed
- Large-file editing: undo/redo now stores range-based deltas (only the text each edit group inserted and replaced) instead of full document snapshots — undo memory scales with edit size, never file size. Also fixes a latent defect the snapshot model hid: undo history is now scoped per document (swapped on tab switch), so Ctrl+Z in one tab can no longer restore another tab's text. Undo grouping behavior is unchanged.
- Defect sweep (from the 2026-07-26 code review): dirty buffers now autosave to the session every 30s, so an editor crash loses at most half a minute of unsaved work (previously only saved on window close); F3 with the Find dialog closed no longer creates and destroys a throwaway window per keypress; background Go to Definition / semantic results are dropped if the window was closed mid-resolve; silent failures (unreadable session, unrestorable tab, Roslyn source/reference load) now leave console breadcrumbs; path comparisons unified on one normalizer; the active-document accessor is clamped defensively; metadata-stub staleness documented.
- The release-notes tab (and first-run update check) could be silently skipped after an update: the "last seen version" was stored machine-wide, so whichever project ran a new version first suppressed it for every other project. Now tracked per project, with migration from the old key (issue #5).

## [0.9.0] - 2026-07-26

### Added
- Localization: all user interface text (menus, settings, dialogs, banners, prompts, status messages, Find/Replace, the update dialog) goes through Unity's editor localization (L10n.Tr) and follows the Editor Language selected in Preferences. Ships with Japanese, Korean, Simplified Chinese, and Traditional Chinese catalogs; English is the source language. Console/diagnostic log text intentionally stays English for supportability.
- Drag-and-drop tab reordering: left-drag a file tab along the tab bar and it moves live as you cross neighboring tabs' midpoints (the dragged tab dims while in flight). A plain click still switches; the reordered layout persists through the tab session.
- Right-click context menu inside the document area: Go to Definition and "Find Occurrences of '<word>'" / "Find in Tabs" for the symbol or selection under the cursor (pre-filling the Find dialog), the clipboard set, Undo/Redo, Save / Save As / Close Tab / Show in File Explorer, Find/Replace/Goto Line — plus language-specific entries (C#: Toggle Comment, Go to Definition; Markdown: switch rendered/source mode). Right-clicking outside the selection moves the caret there first, like other editors.
- Goto Line (Edit menu, Ctrl+G): an emacs-style prompt appears in the status bar ("Goto Line:" plus an inline numeric field). Enter jumps, Escape or clicking away cancels; the destination is clamped to the file's line range; visible line numbers are not required. The status-bar mini-buffer is generic and will host future commands.
- File → Recent Files: the most recently opened files (per project, newest first, deduplicated), each entry reopening its file; missing files are dropped from the list with a console note. "Clear Recent Files" empties it. The list length is configurable in Settings ("Recent Files Count", default 5, 1-30).

- When an open tab's file is deleted from disk, a non-modal banner asks whether to keep the buffer or close the tab. Keeping marks the buffer dirty so the unsaved-changes guards protect it, and Save writes the file back to disk — sometimes that saves the day.
- Open tabs survive closing the ATE window: the session (file tabs + active tab) is saved on close and restored when the window reopens, including across editor restarts. Files missing by then are skipped.

### Fixed
- English-language editors showed the entire UI in Japanese after the localization change: Unity's per-assembly catalog loader falls back to the first PO file alphabetically when the current language has no catalog. An en.po identity catalog makes English resolve explicitly (issue #4).
- Undo/redo grouping is now humanly predictable (VS Code model). Typing coalesces per word — one undo removes one word, not minutes of typing. Groups also break on: Enter, selection replacement, paste, backspace/delete direction changes, moving the caret between edits, a 0.75s typing pause, save, window focus loss, and hard caps (100 chars / 5s) so a group can never grow unbounded. Backspace and forward-delete runs each chain as their own group. The status bar reports "Undid N char(s)" so the step size is visible.
- Go to Definition (F12 / Ctrl+B / Ctrl+Click) now works inside "from metadata" views: the stub remembers which real file it was opened from and resolves symbols against that compilation — chaining from stub to stub works, and stubs get semantic coloring too.

### Changed
- The "Unsaved Changes" dialog no longer uses Unity's modal system. Closing a dirty tab shows the non-modal in-window banner (Save / Discard / Cancel; navigating away cancels); Close Other Tabs raises one banner for the whole batch (Save All / Discard All / Cancel). Closing the ATE window shows no dialog at all: dirty tabs persist their unsaved content in the session and come back dirty when the window reopens — nothing is lost, nothing blocks Unity. (Remaining Unity dialogs — Help→About and the semantics enable-and-install prompt — are direct responses to a click and stay modal per the modality policy.)
- Menu items now display their keyboard shortcuts (matching the active keyboard layout) — Cut Ctrl+X, Copy Ctrl+C, and friends across the File, Edit, Tools, and Window menus.
- Clipboard and Select All shortcuts work with focus anywhere in the ATE window (menu bar, tab bar, gutter), not just inside the code view. Text inputs (settings fields, Markdown block editor) and the selectable console keep their own handling.
- Console and Minimap moved from the Window menu to the View menu, where all four view toggles now sit alphabetically: Console, Line Numbers, Minimap, Word Wrap.
- All four view toggles default ON for fresh installations. Existing windows keep whatever was already configured (settings are preserved by Unity's layout serialization).

## [0.8.0] - 2026-07-26

### Changed
- The MD/source toggle now shows the CURRENT mode ("MD" while rendered, "</>" while in source) instead of the mode it would switch to — the action label read as the wrong state. The tooltip names the action.

### Added
- Markdown support (.md): syntax coloring in source mode (headers, emphasis, code spans/fences, links, lists, quotes, rules mapped onto the theme palette), and a rendered mode with block-level WYSIWYG editing — headers, paragraphs, lists, quotes, code blocks, and rules render styled; click any block to edit its source inline (Ctrl+Enter or focus-out commits, Escape cancels), with edits applied through the code view so undo/redo and dirty tracking work. A transient toggle left of the settings gear (MD ⇄ source) appears only while a .md tab is active; the mode is remembered per document.
- Markdown formatting toolbar: while a .md tab is active (either mode), a button strip appears left of the MD/source toggle — one button per element type (H1–H3, bold, italic, strikethrough, inline code, link, image, bullet/numbered/task lists, blockquote, code block, table, horizontal rule). In rendered mode buttons act on the block being edited (wrapping the selection or transforming its lines) or append a new template block when no block editor is open; in source mode they act on the code view directly — wrapping the selection, transforming the selected lines, or inserting after the current line. Always through the undo-tracked path.
- Settings: "Open Markdown Rendered" — the default view for .md files when opened (rendered/WYSIWYG when on, source when off; off by default). The per-tab MD/source toggle still switches freely. The release-notes tab shown after an update always opens rendered (with full Markdown treatment: coloring, toolbar, block editing) regardless of the setting.
- Markdown feature parity across source coloring, rendering, and the toolbar: strikethrough (~~text~~), images (![alt](url)), task lists (- [ ] / - [x] render as /), and tables (| cells |, header row bold with a separator-aware grid).

## [0.7.1] - 2026-07-26

### Added
- While an update is installing, the ATE window shows an ATE-only modal overlay ("Updating…") that blocks editing and commands — edits during the package swap would be lost in the reload. Unity itself is never blocked (per the modality policy); the overlay clears on failure or is replaced by the reload on success.
- After an update, the new version's release notes open in a focused tab (raw markdown text from the packaged RELEASE-NOTES.md). Fresh installs are not interrupted.

## [0.7.0] - 2026-07-26

### Added
- Go to Definition on symbols defined in referenced assemblies (e.g. UnityEngine types) now opens a "from metadata" view: a generated C# signature stub of the containing type, with the caret on the invoked member. Virtual documents are C#-highlighted and deduplicated by title.
- Console text is selectable and copyable (Ctrl+C).

### Changed
- Dialogs that can appear without a user decision no longer block the editor: the file-changed-on-disk prompt is a non-modal banner with Reload / Keep Mine buttons (the modal froze Unity's main loop — and background tooling — whenever the window regained focus with a changed file); the async update-failure dialog and the "semantics still compiling" notice are console/status messages. Decision dialogs that immediately follow a user action (unsaved changes, Enable & Install consent, About) remain modal by design.

## [0.6.1] - 2026-07-26

### Fixed
- Upgrading from 0.5.x with the old semantics module installed broke compilation (duplicate assembly name), leaving old code running while About reported the new version — no minimap or console. The built-in semantics assembly is renamed (ADKOM.TextEditor.Editor.Semantics) so it never collides, and the obsolete module package is removed automatically on load if present.
- Update installs were fire-and-forget: a failed Client.Add was silent and the project stayed on the old version with no indication. The install request is now monitored — success and failure are logged to the ATE console, and failures show a dialog with the manual install URL.

## [0.6.0] - 2026-07-26

### Changed
- The semantics module is now part of the main package — no separate install, no `upm-semantics` branch. Semantic Features work out of the box: the first use (or the Settings toggle) offers one-click setup, copying the bundled MIT-licensed Roslyn assemblies only when the project has none. The package download grows ~14MB; the bundled binaries stay inert until consented. Existing installs of com.adkom.text-editor.semantics can be removed.

## [0.5.1] - 2026-07-26

### Added
- Minimap along the right edge of the document area (between content and scrollbar): a syntax-colorized code-shape overview of the whole document with a viewport indicator; click or drag to jump. Toggled via Window → Minimap; on by default.
- Selecting text highlights every other occurrence of the selection in the file in a weaker color, so matches stand out while the active selection stays dominant (single-line selections up to 200 chars; whitespace-only selections excluded).
- Double-click selects the word under the cursor; dragging from a double-click extends the selection a whole word at a time in either direction (identifier runs, whitespace runs, or single symbols).
- Console pane attached to the bottom of the window (horizontal tab strip; Console is the only tab for now, visible by default). It collects every ATE message — tool output, update checks, semantic setup, find/replace results — and every status-bar message, which are also now held in the status bar for a few seconds instead of being immediately overwritten. Closing the tab hides the pane; Window → Console shows it again.

## [0.5.0] - 2026-07-26

### Added
- Ctrl+Alt+8 (Cmd+Alt+8 on macOS) opens the ATE window.
- Semantic Features setting (OFF by default): enabling it installs everything automatically — the semantics module via UPM and, when the project has no Roslyn, the module's bundled MIT-licensed Roslyn assemblies (© .NET Foundation; see the module's THIRD-PARTY-NOTICES) copied into Assets/Plugins. Existing Roslyn copies are preferred and nothing is duplicated.

### Changed
- Go to Definition without semantic features now asks via a dialog (offering one-click Enable and Install) instead of a transient status-bar message; a dialog also explains when the module is still installing or compiling.

## [0.4.1] - 2026-07-25

### Added
- Syntax highlighting now colors identifiers — types, methods, variables, and parameters — with theme-authentic colors in all six palettes. A built-in heuristic classifier works everywhere; with the new optional semantics module installed the colors are compiler-accurate (Roslyn).
- Symbol navigation (requires the semantics module): Ctrl+Click any identifier, or press F12 (Visual Studio / VS Code layouts) or Ctrl+B (Rider), to jump to its definition — locals, parameters, members, and types across files and assemblies; symbols defined in referenced binaries report their assembly in the status bar.
- New companion package `com.adkom.text-editor.semantics` (install from the `upm-semantics` branch): builds real Roslyn compilations from Unity's CompilationPipeline (sources, defines, references), cached and incrementally updated, off the main thread. It activates only when a Microsoft.CodeAnalysis.CSharp assembly exists in the project (the main package detects Roslyn and enables the module's compile gate automatically).
- The highlighting engine is span-based internally (per-line classified spans instead of markup strings) — groundwork for more languages.
- The first run of any newly installed version checks for updates immediately (once), bypassing the daily schedule, so fresh installs are brought current right away. Automatic updates remain ON by default on clean installs.

## [0.4.0] - 2026-07-25

### Changed
- All source files are additionally wrapped in #if UNITY_EDITOR guards (belt-and-braces on top of the Editor-only assembly), so copies of package files pasted into a project's Assets folder can never break player builds.

### Fixed
- Status bar hardening: the empty window state shows "No file open" instead of a blank bar, and the Settings tab scrolls on short windows instead of its controls spilling over the status bar.
- Line numbers drifted out of alignment with code lines, worsening toward the bottom of the file: the gutter was one multi-line label whose natural line spacing differed subtly from the row height. Gutter numbers are now pooled per-row labels positioned with the same row math as the code lines.
- The editor now uses a monospace font (the editor's bundled RobotoMono, with OS monospace fallbacks) instead of inheriting Unity's UI font.

### Added
- Optional smooth scrolling (Settings, default on): wheel input animates the text view with an exponential ease toward the same per-notch distance as stepped scrolling, instead of jumping line by line.
- File tabs have a right-click context menu: Save, Save As…, Close, and Close Other Tabs (per-document dirty prompts; Cancel aborts the rest).
- Configurable editor font and font size (Settings): any installed OS font or the bundled monospace default; size 8–40. Zoom with Ctrl+MouseWheel or Ctrl+'+' / Ctrl+'-' (Cmd on macOS), Ctrl+0 resets — the same gestures as browsers and terminals. All layout metrics (wrap points, caret, gutter) recompute on change.
- ATE can be selected as Unity's External Script Editor (Preferences → External Tools): double-clicked scripts and console log entries open in the ATE window at the exact line and column. A configurable Fallback Editor (Settings and the External Tools pane) receives everything ATE doesn't handle — solutions, Open C# Project requests, binaries, and project-file sync — defaulting to the OS default application. Note: deep IDE integrations (debugger attach, solution sync extras) belong to the editor actually selected in Unity; the fallback receives plain open/sync calls.
- Automatic update checks: polls the GitHub latest release (via UPM git URL install) on a configurable schedule — daily at most, or every N days — announcing new versions in the console and, when the editor is idle, offering an install dialog showing current and new version numbers with an "automatic updates" checkbox synced to Settings. Settings additions: Automatic Updates toggle, Check Every (days), a Check for Updates Now button, and the installed version. Embedded (development) copies log availability but never auto-install.

### Changed
- The toolbar buttons were replaced by a standard menu bar — File, Edit, View, Tools, Window, Help — rendered with the platform's native menus on Windows, macOS, and Linux. Menus connect to existing features (file ops, undo/redo, clipboard, line ops, find/replace, view toggles, theme, tab list); Tools → Options… opens the Settings tab; Recent Files is a stub for a future release.

### Fixed
- Opening the ATE window no longer creates an Untitled document (and no longer creates a duplicate window): an empty window shows a hint and documents are created only by the user. Closing the last tab leaves the window empty instead of spawning a new Untitled.

## [0.3.0] - 2026-07-24

### Added
- Find and Replace toolbar buttons (after Save As).
- Find and Replace, including across all open tabs, in a modeless dialog: match case, whole word, normal or regular-expression search, wrap around, and backwards direction. Ctrl+F find / Ctrl+Shift+F find in tabs (all layouts); Ctrl+H / Ctrl+Shift+H replace (Visual Studio, VS Code) or Ctrl+R / Ctrl+Shift+R (Rider); F3 / Shift+F3 repeat the last search. Replace All reports counts; regex replacements support $1-style groups; replacements in the active tab are undoable.
- Word wrap is back, now native to the virtualized view: the editor computes its own wrap points (greedy word wrap, per-character width table) and renders each visual row independently — rendering, caret, clicks, and selection always agree. Syntax coloring splits correctly across wrapped rows; the gutter blanks continuation rows; Up/Down and PageUp/Down move by visual row; the horizontal scrollbar hides while wrap is on. The Word Wrap setting has returned to the Settings tab.

## [0.2.1] - 2026-07-24

### Changed
- License changed to MIT (was all-rights-reserved).
- README rewritten as public-facing copy, leading with the Editor-only / zero-shipping-impact guarantee.

## [0.2.0] - 2026-07-24

### Changed
- The editor is now fully virtualized: the document renders as pooled per-line elements and only visible lines are laid out, so keystroke cost no longer depends on file size. Measured: 14.7ms keystroke-to-frame on a 5,000-line file (was ~930ms). Caret, selection, mouse, keyboard, clipboard, and undo/redo (Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z, with typing coalescing) are implemented by the new CodeView; syntax colors update live per line (no more plain-text flash while typing).
- Word wrap is no longer available: the virtualized view scrolls long lines horizontally instead. The Wrap setting has been removed.

### Changed (virtualized view)
- Tab-stop behavior now applies to ANY whitespace run, not just leading indentation: Left/Right arrows jump to tab-stop-aligned columns (bounded by the run), and Backspace/Delete remove whitespace back/forward to the nearest tab stop.

### Fixed
- Colorization is now asynchronous: while typing, the field's own glyphs show in plain theme color and the syntax-colored overlay re-renders ~150ms after typing pauses, removing the whole-document rich-text re-shape from the keystroke path.
- Typing was extremely slow (seconds per keystroke) in large files with line numbers and wrap enabled: the gutter re-measured every logical line on every keystroke (twice). The wrap-aware measure is now debounced until typing pauses (~200ms); keystrokes cost <1ms.
- Indentation was invisible in highlighted files and clicks placed the caret slightly off: the highlight overlay used white-space: normal, which collapses space runs in Unity 6, shifting every rendered glyph off the real text layout. The overlay now preserves whitespace (pre / pre-wrap).
- Tab key inserted a literal tab character instead of spaces (and skewed click-to-caret mapping afterwards): Unity delivers a second, character-only key event for Tab which bypassed the handler; it is now swallowed. Tab correctly advances to the next tab stop (1..TabSize spaces depending on column).
- Indentation (Tab key / typed spaces) appeared to do nothing in highlighted files: the highlight overlay covered the caret, and programmatic caret placement was clamped by the text engine. The overlay now draws under the transparent-glyph field (caret and selection render above the colors) and caret placement is re-asserted a frame later.
- Status bar no longer gets pushed off the bottom of the window when the loaded document is taller than the visible editor area.

### Added
- Visual Studio color theme (VS Dark/Light) and VS Code keyboard layout (Ctrl+W close, Ctrl+PageUp/Down tabs, Shift+Alt+Up/Down copy line, Ctrl+Shift+K delete line, Ctrl+, settings). Themes now define selection colors.
- The settings gear now toggles: opens the Settings tab, brings it to the front if backgrounded, closes it if already frontmost.
- Project Log added to Documentation~ (chronological history of features, defects, and decisions); Project State refreshed.
- Tabs are rendered as spaces at a configurable Tab Size (Settings); on save, files that originally indented with tabs are converted back so their formatting is preserved. The Tab key inserts spaces to the next tab stop (multi-line selections indent/unindent), and Left/Right arrows jump through space indentation in tab-size steps.
- Keyboard command layouts (Settings → Keyboard Layout): Visual Studio and Rider defaults for the commands the editor supports — save/save all, new/open, close tab, next/previous tab, duplicate line, delete line, move line up/down, toggle line comment, indent/unindent, and settings (Rider).
- Settings tab: a gear button in the toolbar opens Settings as a document tab (single instance; switches to it if already open). Color Theme, Light/Dark Mode, Line Numbers, and Word Wrap moved there from the toolbar.
- C# syntax highlighting (keywords, strings, chars, comments, numbers, preprocessor) via the `ITextFormatter` pipeline, chosen per tab by file extension; rendered by a rich-text overlay. Files over 200k chars fall back to plain rendering. Language coverage is extensible.
- Color themes with two built-in palettes — VS Code (Dark+/Light+) and JetBrains Rider (Rider Dark/IntelliJ Light) — selectable from the new Theme toolbar menu, applied to token colors, editor background, text, gutter, and caret. A light/dark mode selector in the same menu chooses Auto (follow the Editor skin, default), Dark, or Light; both choices persist via EditorPrefs.
- Line numbers in the gutter, toggled by the new "Lines" toolbar button; scroll-synced with the text. (Numbers are per logical line, so they can drift beside wrapped lines when Wrap is on.)
- Multiple open files as tabs: New/Open create tabs, opening an already-open file switches to its tab, per-tab dirty guard on close (middle-click or × to close). Open tabs survive domain reloads.
- Project window context menu item **Assets → Open in ADKOM Text Editor** for any text asset (scripts, TextAssets, shaders, USS/UXML, markdown, configs, …); reuses the existing editor window when one is open.

### Changed
- Context-menu/API opens always create a new tab (unless the file is already open, which switches to its tab) instead of replacing the current document.
- External file-change detection now also runs when a tab is activated, not just when the window regains focus.

## [0.1.0] - 2026-07-24

### Added
- Initial release: dockable UIToolkit text editor window (Tools → ADKOM → Text Editor).
- New / Open / Save / Save As with dirty-state guard dialogs.
- Ctrl+S / Ctrl+Shift+S shortcuts.
- External file-change detection with reload prompt.
- Word-wrap toggle; status bar (line:col, encoding, EOL).
- EOL and UTF-8 BOM preservation on save.
- `ITextFormatter` extension point (plain-text passthrough) and reserved line-number gutter for future releases.
