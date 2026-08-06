# ADKOM Text Editor — User Manual

**A real code editor, living right inside the Unity Editor.**

This manual covers every ATE feature and how to use it. Open it any time via **Help → Documentation → ATE Manual**. Keyboard shortcuts below are shown for the Visual Studio layout; the **Keyboard Layout** setting (VS / VS Code / Rider) changes them, and every menu item always shows the hint for your active layout.

---

## 1. Getting Started

- **Open the editor**: **Tools → ADKOM → Text Editor**, or **Ctrl+Alt+8**.
- **Open a file**: right-click any text asset in the Project window → *Open in ADKOM Text Editor*, drag-select in File → Open, or use **Quick Open** (Ctrl+,) to fuzzy-find a project file by name.
- **External Script Editor**: in Unity's *Preferences → External Tools* you can pick ATE as the External Script Editor. Double-clicking scripts and console entries then opens them in ATE at the right line; non-text requests (solutions, binaries) are forwarded to the configured **External Fallback** editor.
- **First run**: ATE opens its README, this Manual, and the Release Notes as welcome tabs (in that order).
- ATE is 100% Editor-only — nothing ships in player builds.

## 2. The Editor Window

From top to bottom:

1. **Menu bar** — File, Edit, View, Tools, Window, Section, Games (only when **Enable In-Editor Games** is on in Settings), Help. Menus are built the moment you click, so their contents (tab lists, symbol lists, checkmarks) are always current.
2. **Tab strip** — one tab per open document, with dirty markers (`*`), drag-to-reorder, middle-click close, scroll arrows, a jump-to-tab dropdown, and a right-click context menu. Tab colors derive from the **Tab Color** setting, uniformly.
3. **Document area** — the code view (see *Editing*), with gutter (line numbers, bookmarks, fold arrows, git markers), indentation guides, syntax-colorized **minimap**, and smooth scrolling. Its right-click context menu is truly context sensitive: only the features that apply to the current tab appear (a virtual tab like blame output shows a read-oriented menu without edit/file/git entries, the Settings tab offers tab navigation, and inapplicable items are hidden rather than disabled). The read-only views in the special windows — Time Lapse, Edit History, Diff — have context menus too, with git entries acting on the viewed file and anything self-recursive omitted (Time Lapse is absent from the Time Lapse window's own menu).
4. **Console area** — a resizable bottom pane hosting the Console, Search Results, Bookmarks, Scanner Results, and Map views (see *The Console Area*).
5. **Status bar** — messages on the left (also kept in the Console, except transient undo/redo feedback, which stays out of the Console by design), caret position and document info (indentation, line endings, encoding) on the right. Some commands (Goto Line) prompt inline here, emacs-style.

Every control in ATE has a tooltip — hover anything to learn what it does.

## 3. Files, Tabs & Sessions

- **New / Open / Save / Save As / Close** live in the File menu with the usual shortcuts. Ctrl+S saves, Ctrl+Shift+S saves all.
- **Sessions**: open tabs — including *unsaved buffer content* — survive closing the window, domain reloads, and editor restarts. A 30-second autosave protects the session against editor crashes too.
- **Recent Files**: File → Recent Files (per project; length is the **Recent Files Count** setting).
- **External changes**: when a file changes on disk and the buffer is clean, ATE reloads it automatically (configurable); with unsaved edits a non-modal banner asks. ATE never blocks the Unity main loop with modal dialogs.
- **Deleted-file rescue**: if a file vanishes from disk you may keep the buffer — one Save restores the file.
- **Respect for your files**: tab/space indentation, line endings (CRLF / LF / CR), and UTF-8 BOMs round-trip untouched. Optional save cleanups: **Trim Trailing Whitespace** and **Ensure Final Newline**.
- **Auto-Save on Focus Loss** (optional): every dirty file-backed document saves when the ATE window loses focus.

## 4. Editing

### Basics
- Word wrap, line numbers, indentation guides, browser-style zoom (Ctrl+wheel, Ctrl+'+'/'-', Ctrl+0 resets), configurable font and size. The View menu's top group holds every view toggle, sorted.
- **View → Hidden Characters** renders non-printing characters as faint glyphs — spaces as ·, tabs as →, no-break spaces as °, zero-width characters as □, C0 controls as ␀-style pictures, and a ¶ at every line end; display only, the file content is untouched.
- Double-click selects a word (drag extends by whole words); triple-click selects the line (drag extends by lines). Every other occurrence of the selection is highlighted automatically.
- Word-level, deterministic **undo/redo**: one undo removes one word, never minutes of typing.

### Multi-caret & selection
- **Alt+Click** adds a caret; **Add Next Occurrence** (Ctrl+D-style) and **Select All Occurrences** turn matches into carets; **Add Caret Above/Below** builds caret columns; column (box) selection is supported. Multi-caret edits are one undo step.
- **Expand / Shrink Selection** grows the selection by syntactic steps.
- **Drag-and-drop** the selection with the mouse (hold Ctrl to copy); an insertion caret follows the pointer.

### Line & text operations (Edit menu)
Insert line above/below, join lines, move/duplicate lines, whole-line cut/copy on empty selection, sort selected lines, UPPER/lower/Title case transforms, block (Ctrl+/) comments, word-wise delete.

### Brackets, pairs & folding
- Typing `(` `[` `{` `"` `'` inserts the closing pair; closers type over; Backspace removes an empty pair; a selection gets wrapped.
- **Go to Matching Bracket** (Ctrl+]) / **Match Previous Bracket** (Ctrl+[) — both also cycle `#if / #elif / #else / #endif` directives.
- **Code folding**: clickable gutter arrows, `{ ⋯ }` headers, double-click a brace to fold; `#region` folding with **Go to Region** navigation. Fold Region, Unfold Region, and Unfold All live under **Edit → Code → Region**.

### Autocomplete & IntelliSense
- **Word-based autocomplete** (Ctrl+Space): words from the current document, all open tabs, and the language's keywords.
- **IntelliSense** (with Semantic Features): compiler-accurate C# completions — after `.` the actual members of the expression, with overloads collapsed (+N) and signature details.

### Snippets
**Tools → Edit Snippets…** opens one plain-text, hot-reloaded file of `[trigger]` + body definitions with `$name$` tab stops and `$END$`. Expand with Tab or from the completion popup; Tab/Shift+Tab cycle the stops. 12 C# defaults ship. Example snippet:

```
[foreach]
foreach (var $item$ in $collection$)
{
 $END$
}
```

### Code generators (C#)
- **Generate Unity Method**: inserts any of Unity's 33 magic messages (`Awake`, `OnCollisionEnter`, …) with correct signatures; duplicates are refused when semantics can tell.
- **Override Method…**: lists overridable members up the base chain and inserts a correct stub with a `base.` call.

## 5. Find & Replace

**Edit → Find → Find ∕ Replace** (Ctrl+F) toggles the dialog — a fixed-size, tabbed parameter window in the Notepad++ style. It never shows results itself: every *Find All* lists its hits in the **Search Results** console tab, where clicking a row jumps to (and centers) the match. Search state persists for the session; **F3 / Shift+F3** repeat the last search without the dialog.

Common options: **Match case**, **Match whole word only**, and the **Search Mode**:

| Mode | Meaning | Example |
|------|---------|---------|
| Normal | Literal text | `Count(` finds exactly `Count(` |
| Extended | `\n \r \t \0 \xHH` escapes interpreted | `end\n` finds "end" at a line break |
| Regular expression | .NET regex; `$1`-style groups in replacements | find `(\w+)Service`, replace `$1Manager` |

*". matches newline"* makes regex `.` cross line breaks.

### Find tab (Ctrl+F)
Find Next (wrap around / backward options), **Count**, **Find All in Current Document**, **Find All in All Opened Documents**. **In selection** limits Count/Find All/Replace All/Bookmark All to the current selection.

### Replace tab (Ctrl+H)
Replace (current match, then find next), Replace All (current document, one undo step), **Replace All in All Opened Documents**. The **⇅** button swaps the Find and Replace texts.

### Find in Files tab (Ctrl+Shift+F)
Searches the project on disk — open buffers are searched *as you see them*, unsaved edits included.

- **Filters**: file-name wildcard (`*.cs`; `*` = all files).
- **Directory**: any folder; empty = Assets + Packages. **Follow current doc.** searches the active document's folder instead. Sub-folder and hidden-folder (dot-folder) descent are toggles.
- **Find All** → results into the Search Results tab.
- **Replace in Files** applies every match — disk files and open buffers — as ONE journaled operation. **Undo Replace / Redo Replace** restore or re-apply all touched files in one click, even files that were closed (or opened) in between. Binary files are skipped.

### Bookmark tab
Works on the active document: **Bookmark All** bookmarks every line with a match (**Purge for each search** clears old ones first), **Clear all bookmarks**, **Copy Matched Text** copies every match, one per line.

## 6. Bookmarks

- **Toggle Bookmark** (Ctrl+Alt+K), **Next / Previous Bookmark** (Ctrl+Alt+N / Ctrl+Alt+P), **Clear Bookmarks** — Edit → Bookmarks. Bookmarked lines get a gutter marker; bookmarks follow edits and are per document.
- **View Bookmarks** (Edit → Bookmarks) opens the **Bookmarks** console tab: every open document's bookmarks, grouped per file behind disclosure triangles (sorted by file name), with a filter box. Click a row to jump. The Bookmark tab of the Find/Replace dialog fills bookmarks en masse (see above).
- **Section → Bookmarks** lists the current document's bookmarks (sorted by line, with a text preview) for one-click jumps.
- **`#pragma bookmark <label>`** — a bookmark declared in the source itself: any line of that form appears in Section → Bookmarks under its label, merged with your toggled bookmarks.
- **Unknown-pragma warning (CS1633)**: in C# scripts the compiler flags unknown pragmas, `#pragma bookmark` included. Settings (Language & Tools) shows whether your project suppresses the warning and, when it doesn't, offers a one-click **Suppress in This Project** button that writes `-nowarn:1633` into `Assets/csc.rsp` (Unity's project-wide compiler response file) and recompiles. Alternatives: add `#pragma warning disable 1633` at the top of the file, or edit `Assets/csc.rsp` yourself. Assemblies compiled from their own asmdef may need the flag in their own response file.

## 7. Navigation

- **Goto Line** (Ctrl+G) — inline status-bar prompt.
- **Quick Open** (Ctrl+,) — type-to-filter project files.
- **Section menu** (menu bar) — Classes / Properties / Methods of the current tab (alphabetically sorted) plus its **Bookmarks** (sorted by line, with a text preview), rebuilt on every click so it always matches the active tab; selecting an item jumps there.
- **Navigate Back / Forward** — walk the caret-history (jumps from search results, bookmarks, Go to Definition, the Section menu, etc. all push history).
- Every jump **centers** the target line in the view.

## 8. The Console Area

A resizable pane (drag the splitter) at the bottom of the window hosting five views as tabs:

| Tab | Content |
|-----|---------|
| **Console** | Every ATE message, timestamped, with a **Filter** box (substring; the header counts "N of M shown"). Click a row to make it the active line; **Ctrl+C** or right-click → **Copy Line** copies that whole line — deliberately single-line: no multiline or sub-line selection. |
| **Search Results** | Hits from Find All, Find in Files, and Find All References, with a filter box. Click to jump. |
| **Bookmarks** | Bookmarked lines of all open documents, grouped per file (disclosure triangles). |
| **Scanner Results** | Addon security scanner findings (appears when a scan reports). |
| **Map tabs** | One tab per running Z-Machine game, labeled with the game's title, each holding that game's own auto-map. Activating a game's document brings its map tab to the front. |

**View menu**: *Console*, *Search Results*, and *Bookmarks* each toggle their own tab's visibility, independently — the checkmark shows whether the tab is offered. Every tab also carries an **×** that hides just that view (keeping the View-menu checkmark in sync); hiding the last visible tab hides the pane, and running a search (or View Bookmarks) reveals the relevant tab again. **Right-click** the Console or Search Results tab for a **Clear** command that empties that view. All views share the same look: framed subview, monospace font, alternating row tones.

## 9. Languages

- **C#** — syntax highlighting incl. types, methods, and variables out of the box; full semantic coloring with Semantic Features (below).
- **Markdown** — source-mode coloring plus a rendered **WYSIWYG mode** with click-to-edit blocks and a 16-button formatting toolbar (headings, emphasis, lists, task lists, tables, links, images, code, quotes, rules). The MD/source toggle sits in the toolbar; the **Rendered Markdown by Default** setting picks the initial mode. Ctrl+Click follows links in both modes.
- **Markdown lock (read-only)** — rendered Markdown opens **locked** by default (the lock button left of the MD toggle; **Open Markdown Locked** setting changes the default): clicks select text instead of opening block editors, and the formatting toolbar hides. Copying is always plain rendered text — no markers or tags; links keep their URL as "text (url)". The locked view renders as a CONTINUOUS document, so selecting works like any normal document: drag across headings, paragraphs, lists, quotes, and code freely, and Ctrl+A selects everything. **Known limitation**: Unity can only display a native text selection inside one text element at a time, so a selection that crosses a block boundary is drawn by ATE itself — it stays character-precise and highlights continuously (block margins fill as if they were selected empty lines), but does not always behave identically to a native selection: images inside the span show no highlight, and double-click/triple-click selections (which are native) do not extend across blocks. Dragging past the top or bottom edge scrolls the document — hold there and it keeps scrolling and selecting, faster the further out you go, so a file taller than the window selects in one drag. Select and Ctrl+C, or Ctrl+C with nothing selected to copy the whole document; right-click for **Copy Selection as Text** (when blocks are selected), **Copy Block as Text** (over a table or image), **Copy All as Text**, **Copy Link URL** (over a link), and **Unlock (Allow Editing)**. Cut and paste are disabled while locked; click to unlock and edit.
- **Search works in rendered view**: Find, F3/Shift+F3, and Search Results row clicks scroll the rendered view to the block containing the match and highlight it — no need to switch to source.
- **JSON** and **Unity shaders** — syntax coloring.
- **Spell checking** (optional) — unknown words are underlined in comments/strings (everything in Markdown/plain text); right-click a flagged word to add it to your user or project dictionary. Extra dictionaries (.txt / Hunspell .dic) can be dropped in the shared Dictionaries folder.

## 10. Semantic Features (Roslyn)

Enable **Semantic Features** in Settings. If the project has no Roslyn, ATE installs its bundled MIT-licensed Roslyn assemblies after a consent prompt. You get:

- Compiler-accurate **semantic colors**.
- **Go to Definition** — Ctrl+Click, F12, or Ctrl+B — across files and assemblies; engine/BCL symbols open generated *from metadata* views (with working F12 inside them).
- **Rename Symbol** (F2), **Find All References** (results in the Search Results tab), **Format Document**.
- **Error highlighting** — live compiler diagnostics as red (error) / amber (warning) squiggly underlines; hover for the message.
- **Read/write occurrence highlighting** — caret on a symbol highlights reads in the match tint and writes in amber.
- **IntelliSense** completions (see *Editing*).
- **Inspect Symbol…** — the reflection inspector window: live static and instance values of a type, editable where possible, and one-click running of static methods.

## 11. Git Integration

- **Gutter markers** show added/changed lines in file-backed documents of a git repository.
- **Tools → Git** opens the Git window: working-tree changes with per-file staging checkboxes, a commit-message box, **Commit** and **Push** buttons, commit inspection (click a commit in the graph to see its files and message; on HEAD, edit the message and press **Amend** to rewrite it in place), and a **branch history graph** (vertical or horizontal, toggleable; each branch drawn in its own color) with hash/date/author/subject tooltips. The window survives domain reloads — the file list, graph, and your typed commit-message draft all come back (the draft also survives a commit-inspection round-trip). **Double-click a file** in the Changes list to diff it against its previous version: the working-tree view diffs against HEAD, and an inspected commit diffs against its parent.
- The whole **Git submenu is also in the document right-click context menu**, so Blame, File History, Time Lapse and the Git Panel are reachable without leaving the text.
- **Time Lapse** (Tools → Git → Time Lapse Current File…) replays the current file's git history under a slider: the left end is the oldest commit that touched the file, the right end is the tab's contents right now, and dragging steps a read-only view (selection and Copy still work) through every revision in between. Each step tints the lines that revision added (green) or changed (amber) exactly like a diff, with the matching gutter bars — deletions leave the red gutter stub. A version x/y readout sits between the ◀ ▶ step buttons, and ▲ ▼ buttons hop between the change regions of the shown revision — wrapping at the ends when the **Wrap Searches** checkbox (bottom row) is on, stopping otherwise; the revision's date, hash and author, its commit title, and its full commit message sit in read-only fields below the text area (selectable and copyable); and the status line at the bottom left shows how many lines the current tab has added/removed against the shown revision. Whatever line you had centered stays centered while the file changes around it — scroll anywhere and the new spot is held instead. **Copy to Tab** replaces the file's tab contents with the shown revision (undoable while that tab is active). Every invocation opens its own window, so several files — or two points of one file — can be watched side by side.
- Time Lapse handles arbitrarily long histories with a **sliding window**: only the revisions around the slider position are kept fetched, filled in during pauses (nearest first) and dropped again as the window slides on. Scrub freely — landing beyond the window just fetches that revision on the spot, so the only cost of outrunning it is a brief wait. The **Window Size** field beside the slider adjusts the window for that window alone; the default for new Time Lapse windows is the **Time Lapse Window Size** setting in Options (5–500, default 50).

## 12. Diff / Merge

**Tools → Diff / Merge…** opens the comparison tool. Pick what to compare — **Files**, **Folders**, or open **Tabs** — choose both sides, and press Compare. Multiple diff windows can be open at once, and every window restores its comparison (including an in-progress merge) after a domain reload.

- **File / tab diffs** show an aligned side-by-side view in framed columns: added, removed and changed lines are tinted, changed line pairs highlight the exact changed span, and ▲ / ▼ step between change regions. ⇄ swaps the sides, and dragging the center gutter resizes the columns. The gutter also holds **per-change merge buttons**: ◀ copies that change into the left side, ▶ into the right (◀◀ / ▶▶ in the toolbar merge everything at once); edited sides are marked with * and saved with **Save Left** / **Save Right** — so a two-way diff doubles as a merge editor.
- **Folder diffs** compare two folder trees recursively and list each file as *differs*, *left only*, *right only* or *same* (identical files are hidden by default). Double-click a row to open that pair as a file diff.
- **Three-way merge** (Files mode, with the three-way toggle) takes a left, right and common base version. Changes from one side merge automatically; regions both sides changed differently become numbered conflict panels with **Take Left / Take Base / Take Right / Take Both** buttons (and All Left / All Right in the toolbar). The editable result pane updates live; **Save** writes it to the output file — unresolved conflicts are written as git-style `<<<<<<<` markers.
- **Unity integration**: in Settings, **Use ATE for Unity Diff/Merge** registers ATE as Unity's Revision Control Diff/Merge tool (Preferences → External Tools). Version-control diffs and merges then open in this project's ATE. The button flips to restore the previous tool. Note the preference is per-user: the most recently configured project receives the requests.

## 13. History (Visual Undo Timeline)

**Edit → History…** opens the History window: the undo/redo timeline as rows (undo history survives domain reloads with the session) with edit summaries, a per-document tab bar, and arrow-key navigation. Selecting a point previews the document exactly as it was (read-only, in the real editor view, changed line highlighted). Actions: **Restore to This Point** (itself undoable), **Open as New Tab**, **Copy to Clipboard**.

## 14. AI

- **GitHub Copilot** (optional; bring your own subscription + Node.js): ghost-text inline suggestions with an alternatives cycler; Tab or Enter accepts, Escape dismisses. Works in unsaved buffers. One-time device-flow sign-in from Settings persists; sign out any time.
- **Ask Unity AI** (when Unity's Assistant package is installed): right-click → *Ask Unity AI About Selection…/This File…* opens a prompt popup with your text attached — nothing is sent until you submit.

## 15. Add-ons & Games

- **Addons framework**: every subfolder of the shared addons folder (Tools → Addons → Open Addons Folder…) is ONE addon — all its C# files are Roslyn-compiled together, so it is always clear which files belong to which addon. Addons use the stable **AteApi** scripting facade (documented in the **Scripting Reference** and **AteApi Design** under Help → Documentation, with an importable every-member sample; **Game API Design** covers writing games). Addons report to the user with **`AteApi.DebugLog`**, which writes into ATE's own console pane — ATE leaves Unity's console to your project. Leftover single-file addons are migrated into folders automatically.
- **Security**: every addon is scanned for dangerous APIs before it runs; findings appear in the Scanner Results tab and a per-content consent gate asks before execution. Addons can be **signed**, and publishers endorsed; shipped samples are signed.
- **Games** (the **Games** menu — off by default; enable **Settings → Games → Enable In-Editor Games** to show it, which also reveals game addons under Tools → Addons): **How to Play** opens the player's guide, the **Z-Machine interpreter** plays the Zork trilogy — a game you do not have yet opens a confirmation window naming the source repository, the pinned commit, the licence, the size, the SHA-256 fingerprint and the destination folder before anything is downloaded (**Download** / **Cancel**; both GitHub links open in your browser, and **Copy Link to Clipboard** yields the exact download URL). What arrives must match the expected size *and* fingerprint or it is deleted rather than played — several games at once if you like ("Zork I", "Zork I (1)", …), each with its own map tab in the console area (SVG export included), and running games **survive domain reloads and editor restarts** (snapshotted and resumed automatically) — and every installed addon game (**Snake**, the full **Rogue** 5.4.4 port) is listed below, sorted. Game mode hides editor chrome and uses a block cursor.

## 16. Settings

Settings open as a document tab (gear icon, or Tools menu). Highlights — every control's tooltip explains the details:

- **View**: Color Theme (VS / VS Code / Rider palettes), Light/Dark mode (follow Unity or forced), Line Numbers, Word Wrap.
- **Editor**: Tab Size, Keyboard Layout (VS / VS Code / Rider), auto-closing pairs, smooth scrolling, indentation guides, autocomplete and IntelliSense toggles, spell checking.
- **Fonts**: font family (OS fonts; default bundled monospace) and size.
- **Files**: auto-reload, save cleanups, auto-save on focus loss, Recent Files count, default Markdown view, Open Markdown Locked (read-only rendered view by default).
- **Integrations**: External Fallback editor, Semantic Features, unknown-pragma (CS1633) suppression for `#pragma bookmark` (status + one-click `Assets/csc.rsp` fix), Copilot (enable + sign in/out), update checking and frequency.

Settings are stored per project where that makes sense (indicated in each tooltip).

## 17. Themes & Keyboard Layouts

Color themes and light/dark mode live in both Settings and the View menu. Keyboard layouts (VS / VS Code / Rider) change every shortcut; menu items and tooltips always display the binding for the active layout.

## 18. Updates

ATE checks GitHub for new releases (configurable frequency, or **Check for Updates Now** in Settings). When an update is available a green download icon appears next to the settings gear and **stays there — surviving script compiles and editor restarts — until the update is performed**. Clicking it opens the update dialog (Install Now / Later; embedded development copies see a manual-update hint instead of Install). During installation an ATE-only overlay blocks editor operations; afterwards the release notes open in a rendered tab.

**Asset Store installs work differently.** Unity's submission guidelines forbid a package from contacting the network unprompted or from changing the project's package set, so that build never checks on its own — the **Automatic Updates** toggle and its frequency field are not shown — and it never installs anything itself. **Check for Updates Now** still works whenever *you* press it, and reports what is available; you then update through **Window → Package Manager** (My Assets). The same rule removes two conveniences there: ATE does not appear in a dock's **Add Tab** menu (use **Tools → ADKOM → Text Editor**, the Window menu, **Ctrl+Alt+8**, or double-click a text asset), and with ATE selected as the External Script Editor, files it cannot open go to the OS default application rather than a fallback IDE — if you rely on `.csproj` generation for Rider or Visual Studio, keep that IDE as the External Script Editor.

## 19. Support

- **Help → Documentation** — every reference doc opens as a tab: the **Games** player guide on top, then (sorted) **Addon Signing**, **ATE Manual** (this manual), **AteApi Design** (the full scripting API with examples), **Game API Design** (writing games on AteApi 1.1), **Keyboard Shortcuts**, **Localization**, the **Scripting Reference** (AteApi quick start), **Snippets**, and **Troubleshooting**.
- **Help → Repository** — source code and documentation.
- **Help → Release Notes** — what changed in each version.
- **Help → Report an Issue** — bug reports and feature requests.

Ko-fi supporters keep the project going: <https://ko-fi.com/adkomgames>.
