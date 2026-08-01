# ADKOM Text Editor — User Manual

**A real code editor, living right inside the Unity Editor.**

This manual covers every ATE feature and how to use it. Open it any time
via **Help → Open Manual**. Keyboard shortcuts below are shown for the
Visual Studio layout; the **Keyboard Layout** setting (VS / VS Code /
Rider) changes them, and every menu item always shows the hint for your
active layout.

---

## 1. Getting Started

- **Open the editor**: **Tools → ADKOM → Text Editor**, or **Ctrl+Alt+8**.
- **Open a file**: right-click any text asset in the Project window →
  *Open in ADKOM Text Editor*, drag-select in File → Open, or use
  **Quick Open** (Ctrl+,) to fuzzy-find a project file by name.
- **External Script Editor**: in Unity's *Preferences → External Tools*
  you can pick ATE as the External Script Editor. Double-clicking scripts
  and console entries then opens them in ATE at the right line; non-text
  requests (solutions, binaries) are forwarded to the configured
  **External Fallback** editor.
- **First run**: ATE opens its README, this Manual, and the Release
  Notes as welcome tabs (in that order).
- ATE is 100% Editor-only — nothing ships in player builds.

## 2. The Editor Window

From top to bottom:

1. **Menu bar** — File, Edit, View, Tools, Window, Section, Help. Menus
   are built the moment you click, so their contents (tab lists, symbol
   lists, checkmarks) are always current.
2. **Tab strip** — one tab per open document, with dirty markers (`*`),
   drag-to-reorder, middle-click close, scroll arrows, a jump-to-tab
   dropdown, and a right-click context menu. Tab colors derive from the
   **Tab Color** setting, uniformly.
3. **Document area** — the code view (see *Editing*), with gutter (line
   numbers, bookmarks, fold arrows, git markers), indentation guides,
   syntax-colorized **minimap**, and smooth scrolling.
4. **Console area** — a resizable bottom pane hosting the Console,
   Search Results, Bookmarks, Scanner Results, and Map views (see
   *The Console Area*).
5. **Status bar** — messages on the left (also kept in the Console),
   caret position and document info (indentation, line endings,
   encoding) on the right. Some commands (Goto Line) prompt inline here,
   emacs-style.

Every control in ATE has a tooltip — hover anything to learn what it does.

## 3. Files, Tabs & Sessions

- **New / Open / Save / Save As / Close** live in the File menu with the
  usual shortcuts. Ctrl+S saves, Ctrl+Shift+S saves all.
- **Sessions**: open tabs — including *unsaved buffer content* — survive
  closing the window, domain reloads, and editor restarts. A 30-second
  autosave protects the session against editor crashes too.
- **Recent Files**: File → Recent Files (per project; length is the
  **Recent Files Count** setting).
- **External changes**: when a file changes on disk and the buffer is
  clean, ATE reloads it automatically (configurable); with unsaved edits
  a non-modal banner asks. ATE never blocks the Unity main loop with
  modal dialogs.
- **Deleted-file rescue**: if a file vanishes from disk you may keep the
  buffer — one Save restores the file.
- **Respect for your files**: tab/space indentation, line endings
  (CRLF / LF / CR), and UTF-8 BOMs round-trip untouched. Optional save
  cleanups: **Trim Trailing Whitespace** and **Ensure Final Newline**.
- **Auto-Save on Focus Loss** (optional): every dirty file-backed
  document saves when the ATE window loses focus.

## 4. Editing

### Basics
- Word wrap, line numbers, indentation guides, browser-style zoom
  (Ctrl+wheel, Ctrl+'+'/'-', Ctrl+0 resets), configurable font and size.
- Double-click selects a word (drag extends by whole words);
  triple-click selects the line (drag extends by lines). Every other
  occurrence of the selection is highlighted automatically.
- Word-level, deterministic **undo/redo**: one undo removes one word,
  never minutes of typing.

### Multi-caret & selection
- **Alt+Click** adds a caret; **Add Next Occurrence** (Ctrl+D-style) and
  **Select All Occurrences** turn matches into carets; **Add Caret
  Above/Below** builds caret columns; column (box) selection is
  supported. Multi-caret edits are one undo step.
- **Expand / Shrink Selection** grows the selection by syntactic steps.
- **Drag-and-drop** the selection with the mouse (hold Ctrl to copy); an
  insertion caret follows the pointer.

### Line & text operations (Edit menu)
Insert line above/below, join lines, move/duplicate lines, whole-line
cut/copy on empty selection, sort selected lines, UPPER/lower/Title case
transforms, block (Ctrl+/) comments, word-wise delete.

### Brackets, pairs & folding
- Typing `(` `[` `{` `"` `'` inserts the closing pair; closers type
  over; Backspace removes an empty pair; a selection gets wrapped.
- **Go to Matching Bracket** (Ctrl+]) / **Match Previous Bracket**
  (Ctrl+[) — both also cycle `#if / #elif / #else / #endif` directives.
- **Code folding**: clickable gutter arrows, `{ ⋯ }` headers,
  double-click a brace to fold; `#region` folding with **Go to Region**
  navigation.

### Autocomplete & IntelliSense
- **Word-based autocomplete** (Ctrl+Space): words from the current
  document, all open tabs, and the language's keywords.
- **IntelliSense** (with Semantic Features): compiler-accurate C#
  completions — after `.` the actual members of the expression, with
  overloads collapsed (+N) and signature details.

### Snippets
**Tools → Edit Snippets…** opens one plain-text, hot-reloaded file of
`[trigger]` + body definitions with `$name$` tab stops and `$END$`.
Expand with Tab or from the completion popup; Tab/Shift+Tab cycle the
stops. 12 C# defaults ship. Example snippet:

```
[foreach]
foreach (var $item$ in $collection$)
{
    $END$
}
```

### Code generators (C#)
- **Generate Unity Method**: inserts any of Unity's 33 magic messages
  (`Awake`, `OnCollisionEnter`, …) with correct signatures; duplicates
  are refused when semantics can tell.
- **Override Method…**: lists overridable members up the base chain and
  inserts a correct stub with a `base.` call.

## 5. Find & Replace

**Edit → Find → Find ∕ Replace** (Ctrl+F) toggles the dialog — a fixed-size,
tabbed parameter window in the Notepad++ style. It never shows results
itself: every *Find All* lists its hits in the **Search Results**
console tab, where clicking a row jumps to (and centers) the match.
Search state persists for the session; **F3 / Shift+F3** repeat the last
search without the dialog.

Common options: **Match case**, **Match whole word only**, and the
**Search Mode**:

| Mode | Meaning | Example |
|------|---------|---------|
| Normal | Literal text | `Count(` finds exactly `Count(` |
| Extended | `\n \r \t \0 \xHH` escapes interpreted | `end\n` finds "end" at a line break |
| Regular expression | .NET regex; `$1`-style groups in replacements | find `(\w+)Service`, replace `$1Manager` |

*". matches newline"* makes regex `.` cross line breaks.

### Find tab (Ctrl+F)
Find Next (wrap around / backward options), **Count**, **Find All in
Current Document**, **Find All in All Opened Documents**. **In
selection** limits Count/Find All/Replace All/Bookmark All to the
current selection.

### Replace tab (Ctrl+H)
Replace (current match, then find next), Replace All (current document,
one undo step), **Replace All in All Opened Documents**. The **⇅**
button swaps the Find and Replace texts.

### Find in Files tab (Ctrl+Shift+F)
Searches the project on disk — open buffers are searched *as you see
them*, unsaved edits included.

- **Filters**: file-name wildcard (`*.cs`; `*` = all files).
- **Directory**: any folder; empty = Assets + Packages. **Follow current
  doc.** searches the active document's folder instead. Sub-folder and
  hidden-folder (dot-folder) descent are toggles.
- **Find All** → results into the Search Results tab.
- **Replace in Files** applies every match — disk files and open
  buffers — as ONE journaled operation. **Undo Replace / Redo Replace**
  restore or re-apply all touched files in one click, even files that
  were closed (or opened) in between. Binary files are skipped.

### Bookmark tab
Works on the active document: **Bookmark All** bookmarks every line with
a match (**Purge for each search** clears old ones first), **Clear all
bookmarks**, **Copy Matched Text** copies every match, one per line.

## 6. Bookmarks

- **Toggle Bookmark** (Ctrl+Alt+K), **Next / Previous Bookmark**
  (Ctrl+Alt+N / Ctrl+Alt+P), **Clear Bookmarks** — Edit → Bookmarks.
  Bookmarked lines get a gutter marker; bookmarks follow edits and are
  per document.
- **View Bookmarks** (Edit → Bookmarks) opens the **Bookmarks** console
  tab: every open document's bookmarks, grouped per file behind
  disclosure triangles (sorted by file name), with a filter box. Click a
  row to jump. The Bookmark tab of the Find/Replace dialog fills
  bookmarks en masse (see above).

## 7. Navigation

- **Goto Line** (Ctrl+G) — inline status-bar prompt.
- **Quick Open** (Ctrl+,) — type-to-filter project files.
- **Section menu** (menu bar) — Classes / Properties / Methods of the
  current tab, alphabetically sorted, rebuilt on every click; selecting
  one jumps to the declaration.
- **Navigate Back / Forward** — walk the caret-history (jumps from
  search results, bookmarks, Go to Definition, the Section menu, etc.
  all push history).
- Every jump **centers** the target line in the view.

## 8. The Console Area

A resizable pane (drag the splitter) at the bottom of the window hosting
five views as tabs:

| Tab | Content |
|-----|---------|
| **Console** | Every ATE message, timestamped. Rows are selectable (click / Shift / Ctrl); Ctrl+C copies the selected lines. |
| **Search Results** | Hits from Find All, Find in Files, and Find All References, with a filter box. Click to jump. |
| **Bookmarks** | Bookmarked lines of all open documents, grouped per file (disclosure triangles). |
| **Scanner Results** | Addon security scanner findings (appears when a scan reports). |
| **Map** | The Z-Machine game's auto-map (appears while playing). |

**View menu**: *Console*, *Search Results*, and *Bookmarks* each toggle
their own tab's visibility, independently — the checkmark shows whether
the tab is offered. Hiding the last visible tab hides the pane; running
a search (or View Bookmarks) reveals the relevant tab again. All views
share the same look: framed subview, monospace font, alternating row
tones.

## 9. Languages

- **C#** — syntax highlighting incl. types, methods, and variables out
  of the box; full semantic coloring with Semantic Features (below).
- **Markdown** — source-mode coloring plus a rendered **WYSIWYG mode**
  with click-to-edit blocks and a 16-button formatting toolbar
  (headings, emphasis, lists, task lists, tables, links, images, code,
  quotes, rules). The MD/source toggle sits in the toolbar; the
  **Rendered Markdown by Default** setting picks the initial mode.
  Ctrl+Click follows links in both modes.
- **JSON** and **Unity shaders** — syntax coloring.
- **Spell checking** (optional) — unknown words are underlined in
  comments/strings (everything in Markdown/plain text); right-click a
  flagged word to add it to your user or project dictionary. Extra
  dictionaries (.txt / Hunspell .dic) can be dropped in the shared
  Dictionaries folder.

## 10. Semantic Features (Roslyn)

Enable **Semantic Features** in Settings. If the project has no Roslyn,
ATE installs its bundled MIT-licensed Roslyn assemblies after a consent
prompt. You get:

- Compiler-accurate **semantic colors**.
- **Go to Definition** — Ctrl+Click, F12, or Ctrl+B — across files and
  assemblies; engine/BCL symbols open generated *from metadata* views
  (with working F12 inside them).
- **Rename Symbol** (F2), **Find All References** (results in the Search
  Results tab), **Format Document**.
- **Error highlighting** — live compiler diagnostics as red (error) /
  amber (warning) underlines; hover for the message.
- **Read/write occurrence highlighting** — caret on a symbol highlights
  reads in the match tint and writes in amber.
- **IntelliSense** completions (see *Editing*).
- **Inspect Symbol…** — the reflection inspector window: live static and
  instance values of a type, editable where possible, and one-click
  running of static methods.

## 11. Git Integration

- **Gutter markers** show added/changed lines in file-backed documents
  of a git repository.
- **Tools → Git** opens the Git window: working-tree changes with
  per-file staging checkboxes, a commit-message box, **Commit** and
  **Push** buttons, and a **branch history graph** (vertical or
  horizontal, toggleable) with hash/date/author/subject tooltips.

## 12. History (Visual Undo Timeline)

**Edit → History…** opens the History window: the undo/redo timeline as
rows with edit summaries, a per-document tab bar, and arrow-key
navigation. Selecting a point previews the document exactly as it was
(read-only, in the real editor view, changed line highlighted). Actions:
**Restore to This Point** (itself undoable), **Open as New Tab**,
**Copy to Clipboard**.

## 13. AI

- **GitHub Copilot** (optional; bring your own subscription + Node.js):
  ghost-text inline suggestions with an alternatives cycler; Tab or
  Enter accepts, Escape dismisses. Works in unsaved buffers. One-time
  device-flow sign-in from Settings persists; sign out any time.
- **Ask Unity AI** (when Unity's Assistant package is installed):
  right-click → *Ask Unity AI About Selection…/This File…* opens a
  prompt popup with your text attached — nothing is sent until you
  submit.

## 14. Add-ons & Games

- **Addons framework**: drop C# addon scripts (or multi-file folder
  addons) into the shared addons folder (Tools → Open Addons Folder…);
  they are Roslyn-compiled and use the stable **AteApi** scripting
  facade (documented in Scripting.md, with an importable every-member
  sample).
- **Security**: every addon is scanned for dangerous APIs before it
  runs; findings appear in the Scanner Results tab and a per-content
  consent gate asks before execution. Addons can be **signed**, and
  publishers endorsed; shipped samples are signed.
- **Games** (Tools menu): **Snake**, a full **Rogue** (5.4.4) port, and
  a **Z-Machine interpreter** that plays the Zork trilogy — complete
  with an automatic map in the Map tab (SVG export included). Game mode
  hides editor chrome and uses a block cursor.

## 15. Settings

Settings open as a document tab (gear icon, or Tools menu). Highlights —
every control's tooltip explains the details:

- **View**: Color Theme (VS / VS Code / Rider palettes), Light/Dark mode
  (follow Unity or forced), Line Numbers, Word Wrap.
- **Editor**: Tab Size, Keyboard Layout (VS / VS Code / Rider),
  auto-closing pairs, smooth scrolling, indentation guides, autocomplete
  and IntelliSense toggles, spell checking.
- **Fonts**: font family (OS fonts; default bundled monospace) and size.
- **Files**: auto-reload, save cleanups, auto-save on focus loss,
  Recent Files count, default Markdown view.
- **Integrations**: External Fallback editor, Semantic Features,
  Copilot (enable + sign in/out), update checking and frequency.

Settings are stored per project where that makes sense (indicated in
each tooltip).

## 16. Themes & Keyboard Layouts

Color themes and light/dark mode live in both Settings and the View
menu. Keyboard layouts (VS / VS Code / Rider) change every shortcut;
menu items and tooltips always display the binding for the active
layout.

## 17. Updates

ATE checks GitHub for new releases (configurable frequency, or **Check
for Updates Now** in Settings). When an update is available a green
download icon appears next to the settings gear — click it to install.
During installation an ATE-only overlay blocks editor operations;
afterwards the release notes open in a rendered tab.

## 18. Support

- **Help → Repository** — source code and documentation.
- **Help → Release Notes** — what changed in each version.
- **Help → Report an Issue** — bug reports and feature requests.

Ko-fi supporters keep the project going: <https://ko-fi.com/adkomgames>.
