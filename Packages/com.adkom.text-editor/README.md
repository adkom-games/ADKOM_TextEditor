# ADKOM Text Editor

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20ADKOM%20Games-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/adkomgames)

**A real code editor, living right inside the Unity Editor.**

Stop alt-tabbing. Whether it's a quick tweak to a config file, a README edit, or browsing a script while your game runs, the ADKOM Text Editor gives you a fast, IDE-grade editing experience without ever leaving Unity — dockable, themeable, and tuned to feel like the editors you already know.

## 100% Editor-only. Zero shipping impact.

This is a **Unity Editor asset** — a tool for you, not your players. Every line of code lives in an Editor-only assembly: nothing is compiled into your builds, nothing ships with your game, and nothing touches your runtime. Install it, use it every day, and your shipping product stays byte-for-byte identical.

## Why you'll love it

### Fast at any size
The editor is **fully virtualized** — only the lines on screen are ever rendered. Open a 5,000-line file and typing still lands in under 15ms. No lag, no stutter, no matter how big the file.

### Tabs, like a real editor
Open as many files as you want. Tabs remember themselves across domain reloads and editor restarts, warn you about unsaved changes, close with a middle-click, **reorder with drag-and-drop**, and switch instead of duplicating when you re-open a file. The strip stays on a single line with **scroll arrows** when tabs overflow (the active tab keeps itself in view), a **tab-list dropdown** at the far right jumps to any tab, tabs are tinted with a color you pick in Settings (the active tab pops), and a **Tabs submenu** tops the document right-click menu. Right-click any text asset in the Project window — scripts, shaders, JSON, YAML, markdown, USS/UXML, configs — and it opens in ATE. **File → Recent Files** remembers what you've had open (per project, count configurable) so yesterday's file is two clicks away.

### Syntax highlighting & themes
C# highlighting out of the box — keywords, strings, comments, and **identifiers too**: types, methods, variables, and parameters, in each theme's authentic colors — plus **JSON** (keys, values, comments; .json and .asmdef) and **Unity shaders** (ShaderLab + HLSL; .shader, .hlsl, .cginc, .compute), and Markdown. Flip on **Semantic Features** in Settings and the C# colors become compiler-accurate (powered by Roslyn) — every dependency installs itself automatically. Pick your palette: **Visual Studio**, **VS Code**, or **JetBrains Rider** — each with authentic dark and light variants, following your Unity Editor skin automatically (or forced Dark/Light, your call).

### Your muscle memory works here
Choose your keyboard layout — **Visual Studio**, **VS Code**, or **Rider** — and the shortcuts you already know just work:

| Command | Visual Studio | VS Code | Rider |
|---|---|---|---|
| Save | Ctrl+S | Ctrl+S | — |
| Save All | Ctrl+Shift+S | — | Ctrl+S |
| New file | Ctrl+N | Ctrl+N | — |
| Open file | Ctrl+O | Ctrl+O | — |
| Close tab | Ctrl+F4 | Ctrl+W / Ctrl+F4 | Ctrl+F4 |
| Next / previous tab | Ctrl+Tab / Ctrl+Shift+Tab | Ctrl+PgDn / Ctrl+PgUp (or Ctrl+Tab) | Alt+Right / Alt+Left |
| Duplicate line | Ctrl+D | Shift+Alt+Up/Down | Ctrl+D |
| Delete line | Ctrl+L | Ctrl+Shift+K | Ctrl+Y |
| Move line up / down | Alt+Up / Alt+Down | Alt+Up / Alt+Down | Alt+Shift+Up / Alt+Shift+Down |
| Find / Find in Files | Ctrl+F / Ctrl+Shift+F | Ctrl+F / Ctrl+Shift+F | Ctrl+F / Ctrl+Shift+F |
| Replace / Find in Files | Ctrl+H / Ctrl+Shift+H | Ctrl+H / Ctrl+Shift+H | Ctrl+R / Ctrl+Shift+R |
| Find next / previous | F3 / Shift+F3 | F3 / Shift+F3 | F3 / Shift+F3 |
| Zoom in / out / reset | Ctrl+'+' / Ctrl+'-' / Ctrl+0 | Ctrl+'+' / Ctrl+'-' / Ctrl+0 | Ctrl+'+' / Ctrl+'-' / Ctrl+0 |
| Toggle line comment | Ctrl+/ | Ctrl+/ | Ctrl+/ |
| Indent / unindent | Tab / Shift+Tab | Tab / Shift+Tab | Tab / Shift+Tab |
| Go to Definition | F12 / Ctrl+Click | F12 / Ctrl+Click | Ctrl+B / Ctrl+Click |
| Settings | — | Ctrl+, | Ctrl+Alt+S |
| Word-wise delete | Ctrl+Backspace / Ctrl+Delete | Ctrl+Backspace / Ctrl+Delete | Ctrl+Backspace / Ctrl+Delete |
| Insert line above / below | Ctrl+Enter / Ctrl+Shift+Enter | Ctrl+Shift+Enter / Ctrl+Enter | Ctrl+Alt+Enter / Shift+Enter |
| Join lines | Ctrl+J | Ctrl+J | Ctrl+J |
| Select line | — | Ctrl+L | — |
| Go to matching bracket | Ctrl+] | Ctrl+Shift+\ | Ctrl+Shift+M |
| Toggle block comment | Ctrl+Shift+/ | Shift+Alt+A | Ctrl+Shift+/ |
| Expand / shrink selection | Shift+Alt+Right / Left | Shift+Alt+Right / Left | Ctrl+W / Ctrl+Shift+W |
| Navigate back / forward | Ctrl+- / Ctrl+Shift+- | Alt+Left / Alt+Right | Ctrl+Alt+Left / Right |
| Add next occurrence | Shift+Alt+. | Ctrl+D | Alt+J |
| Select all occurrences | Shift+Alt+; | Ctrl+Shift+L | Ctrl+Alt+Shift+J |
| Add caret above / below | Ctrl+Alt+Up / Down | Ctrl+Alt+Up / Down | Ctrl+Alt+Up / Down |
| Autocomplete | Ctrl+Space | Ctrl+Space | Ctrl+Space |
| Fold / unfold region | Ctrl+Shift+[ / ] | Ctrl+Shift+[ / ] | Ctrl+Shift+[ / ] |
| Rename symbol | F2 | F2 | Shift+F6 |
| Find all references | Shift+F12 | Shift+F12 | Alt+F7 |
| Format document | Shift+Alt+F | Shift+Alt+F | Ctrl+Alt+L |
| Quick Open | Ctrl+, | Ctrl+P | Ctrl+T |
| Toggle bookmark | Ctrl+Alt+K | Ctrl+Alt+K | Ctrl+Alt+K |
| Next / previous bookmark | Ctrl+Alt+N / P | Ctrl+Alt+N / P | Ctrl+Alt+N / P |
| Goto Line | Ctrl+G | Ctrl+G | Ctrl+G |
| Accept Copilot suggestion | Tab / Enter | Tab / Enter | Tab / Enter |
| Cycle Copilot suggestions | Alt+[ / ] | Alt+[ / ] | Alt+[ / ] |
| Open the ATE window | Ctrl+Alt+8 | Ctrl+Alt+8 | Ctrl+Alt+8 |

Plus **word-level undo/redo** (one undo removes one word, never minutes of typing — with "Undid N characters" feedback in the status bar), word-wise navigation, smart Home, auto-indent on Enter, full clipboard support anywhere in the window, double-click word selection with whole-word drag, and automatic highlighting of every other occurrence of whatever you select. Every menu item shows its shortcut for your chosen layout, **Ctrl+G** opens an emacs-style Goto Line prompt in the status bar, and a **right-click menu in the document** puts Go to Definition, Find Occurrences, clipboard, file, and language commands under the cursor.

### Edits like an IDE
**Multi-caret editing**: Alt+Click adds carets, add-next-occurrence (Ctrl+D in VS Code layout) and select-all-occurrences turn every match into a caret, Ctrl+Alt+Up/Down grows a caret column — then type, paste, or delete everywhere at once (one undo step). **Word-based autocomplete** pops as you type (plus the language's keywords and words from your other open tabs; Ctrl+Space on demand). Brackets and quotes **auto-close** (type-over, wrap the selection, Backspace removes the pair), the **matching brace** highlights with jump (and double-clicking a brace folds its block), and **code folding** collapses brace regions from clickable gutter arrows — folded headers read `{ ⋯ }`, and double-clicking the indicator reopens them. **Indentation guides**, **expand/shrink selection**, insert-line-above/below, join lines, case transforms, sort lines, block comments, word-wise delete, whole-line cut/copy on an empty selection, and **navigate back/forward** through your caret history round it out. **Rename Symbol** (F2), **Find All References**, and **Format Document** ride the same Roslyn semantics as Go to Definition. **Quick Open** (Ctrl+P in VS Code layout) fuzzy-finds any project file; per-document **bookmarks** (Ctrl+Alt+K) mark lines in the gutter with next/previous jumps, a **View Bookmarks** console tab listing every open document's bookmarks (grouped per file), and bulk bookmarking from the Find/Replace dialog; and selected text **drags and drops** to a new location (Ctrl to copy). Optional save cleanups trim trailing whitespace and ensure a final newline, and **Auto-Save on Focus Loss** can save every dirty file the moment you click away.

### IntelliSense & live errors
With Semantic Features on, completion becomes **compiler-accurate**: type `.` and get the actual members of the expression — instance, static, or namespace — with signatures, overload counts, and accessibility respected; elsewhere every symbol in scope blends with words and keywords. **Errors and warnings underline live** as you type (red/amber, hover for the full compiler message), and parking the caret on a symbol highlights every use in the file — **reads in grey, writes in amber**.

### Snippets & code generators
**Snippets** live in one plain-text file you edit in ATE itself (Tools → Edit Snippets…): type a trigger and press Tab — the body expands re-indented, placeholders become live tab stops you cycle with Tab, and `$END$` marks where you land. A default C# set ships; add your own and they hot-reload on save. **Generate Unity Method** inserts any of 33 Unity messages with correct signatures (and refuses duplicates the class already declares); **Override Method…** lists everything overridable up the base chain and generates the stub, base call included.

### Edit history, visually
**Edit → History…** shows your undo/redo timeline — one row per edit with a summary and line number, future (redo) steps dimmed on top, down to the original. Click any point to see the document **exactly as it looked then** (changed line highlighted), restore to it (still undoable), open the snapshot as a new tab, or copy it.

### Inspect running code
Right-click a symbol → **Inspect Symbol…** and a reflection inspector opens on its type: static fields and properties with **live values** (play-mode state updates as it changes), writable primitives editable in place, **Run buttons for parameterless static methods**, and — for MonoBehaviours — a scene-instance picker with the instance's fields live too.

### Git, built in
Gutter markers show **added/modified/deleted lines** against HEAD as you work. Tools → Git brings **Blame** (a read-only annotated view), **File History** (open any past revision of the file), and the **Git Panel**: stage and unstage with checkboxes, commit, push, inspect any commit (files + message; amend HEAD's message in place), and a **branch-history tree** — an interactive commit graph, switchable vertical ⇄ horizontal, where you can check out branches or create one at any commit (guarded while your tree is dirty). Uses your system git; no bundled VCS.

### Diff / Merge
A first-class comparison tool (Tools → Diff / Merge…): side-by-side diffs of **files, folders, or open tabs** with intra-line change highlights and change-region navigation, plus a **three-way merge** with per-conflict Take Left/Base/Right/Both resolution and an editable, saveable result. Double-click any file in the Git Panel to diff it against its previous version. One Settings click makes ATE **Unity's Revision Control Diff/Merge tool** (Preferences → External Tools) — and every diff window survives domain reloads, merge-in-progress included.

### Spell checking, optional
Flip it on in Settings and unknown words get a soft blue underline — comments and strings in code, everything in markdown and plain text, camelCase split and judged per hump. Ships a 115k-word English dictionary (US + UK, SCOWL-derived, fully attributed); drop in Hunspell `.dic` files for other languages, and right-click any flagged word to add it to your **user or per-project dictionary**.

### AI, both flavors
**GitHub Copilot** (Settings, off by default): real ghost-text inline suggestions as you type — in files AND unsaved buffers — with a ◂ 1/3 ▸ cycler for alternatives (Alt+[ / Alt+]), Tab or Enter to accept, Escape to dismiss. Bring your own Copilot subscription and Node.js; the official Copilot Language Server installs itself on first enable, you sign in once with GitHub's device flow, and the login persists across restarts. **Unity AI**: with Unity's Assistant package installed, right-click any selection or document and *Ask Unity AI* — Assistant's prompt popup opens with your text attached (no AI points spent until you submit). Non-modal throughout, like everything else.

### Respects your files
Tabs render as spaces at your configured tab size — but on save, files that indent with tabs get their tabs back, and space-indented files stay spaces. Line endings (CRLF/LF/CR) and UTF-8 BOMs round-trip untouched. Your teammates will never know you edited it in Unity (unless you tell them). Navigation, Backspace, and Delete all honor tab stops across whitespace, so space-indented files *feel* tab-indented.

### Go to Definition
**Ctrl+Click** any symbol — or press F12 (Visual Studio / VS Code layouts) or Ctrl+B (Rider) — and jump straight to its definition: locals, parameters, members, and types, across files and assemblies. Symbols from referenced binaries (UnityEngine, the BCL) open a generated **"from metadata" signature view** of their type, caret on the member. Part of the opt-in Semantic Features: everything ships in the box, and the first use offers one-click setup (bundled MIT-licensed Roslyn, notices included; an existing project Roslyn is used as-is).

### Find & replace, everywhere
One fixed-size, tabbed dialog (Notepad++-style) drives every search: **Find**, **Replace**, **Find in Files**, and **Bookmark** tabs share the query and options — match case, whole word, and a **Search Mode** of plain text, extended escapes (`\n`, `\t`, `\x41`), or full regular expressions with $1 group replacements. The dialog holds parameters only: every **Find All** (current document, all open documents, or the whole project) lists its hits in the console's **Search Results** tab as clickable rows, and every jump lands **centered** in the view. F3 repeats the last search without the dialog. **Find in Files** searches Assets + Packages or any folder (file-name filters, sub-folder and hidden-folder switches, follow-current-doc), with open buffers searched as you see them; **Replace in Files** applies every match as ONE journaled operation with global Undo/Redo across all touched files, open or closed. The **Bookmark** tab bookmarks every matching line — Count, In-selection scoping, and Copy Matched Text included.

### Markdown, both ways
Open a `.md` file and a toggle appears next to the settings gear: **source mode** with full markdown syntax coloring, or **rendered mode** — headers, lists, quotes, and code blocks styled properly, with block-level WYSIWYG editing: click any block, edit its source inline, and it re-renders on commit. Undo works across both modes. A **formatting toolbar** appears for any `.md` tab — one button per element (headings, bold, italic, strikethrough, code, links, images, lists, task lists, quotes, code blocks, tables, rules) that formats your selection or the block you're editing, in either mode. Tables, task lists (checkboxes), and strikethrough render and color in both modes — and **local images actually display** in rendered mode (alt text as caption, placeholder when missing), and a settings option picks which view `.md` files open in.

### See the whole file
A **syntax-colorized minimap** runs along the right edge — the shape of your whole document at a glance, with a viewport indicator; click or drag it to jump anywhere. A **console area** at the bottom — resizable by dragging the divider (the height sticks) — hosts framed, monospace, zebra-striped views as tabs: the **Console** (every ATE message, timestamped, row-selectable, Ctrl+C copies), **Search Results** (hits from Find All, Find in Files, and Find All References — filterable, click to jump), **Bookmarks** (every open document's bookmarks, grouped per file behind disclosure triangles), plus the addon scanner and game map when active. The View menu toggles each tab **independently**, and line numbers and word wrap live there too.

### Feels like an application
A real **menu bar** — File, Edit, View, Tools, Window, Section, Games, Help — rendered with your platform's native menus, plus right-click context menus on file tabs (Save, Save As, Close, Close Other Tabs), and ATE appears in every dock's **Add Tab** menu and in the Window menu (with its shortcut). The **Section menu** lists the current tab's classes, properties, and methods — sorted, rebuilt on every click — and jumps to the declaration. The Window menu lists open tabs alphabetically. Every control carries a **tooltip**, and **Help → Documentation** opens a whole reference shelf as tabs: the full user manual, scripting and game API references, addon signing, keyboard shortcuts, snippets, localization, troubleshooting, and a player's guide to the games. Pick any installed **font** and size, zoom with Ctrl+MouseWheel or Ctrl+'+'/'-' (Ctrl+0 resets), and enjoy **smooth scrolling** (toggleable).

### Speaks your language
The entire interface follows Unity's **Editor Language** setting: Japanese, Korean, Simplified Chinese, and Traditional Chinese ship in the box (English is the source). Menus, settings, dialogs, prompts, and status messages all translate — shortcuts stay universal.

### Script it
A **stable scripting API** (`ADKOM.TextEditor.Scripting.AteApi`) lets your editor scripts open files, read and edit documents, save, close, and subscribe to events (opened/closed/saved/changed) — VS Code-shaped and semver-stable. **Addons** — one folder per addon in a machine-shared location, all its `.cs` files compiled together in-memory — appear under Tools → Addons, with a full lifecycle (load/unload/focus). And addons are **security-gated**: their source is scanned against known-dangerous API patterns, a risk report opens (with a clickable findings tab in the console pane), and nothing runs until you approve that exact file content once. See `Documentation~/Scripting.md`.

### Plays games
API 1.1 turns a document into a **game screen**: game mode (no wrap, no undo churn, editor chrome hidden, block cursor), overwrite-style `WriteAt`, per-cell **foreground/background colors**, consumable keyboard events plus key-state polling, mouse in text coordinates, a 30 Hz tick, per-document fonts and tab titles, and the status-bar prompt. Two games ship as installable **sample addons** — **Snake**, and **Rogue**, a faithful port of the 1980 BSD classic (26 monsters, the real dungeon generator, potions/scrolls/wands/rings, the tombstone) — via Tools → Addons → Install Sample Addons, then the **Games** menu, which lists every installed addon game (plus a **How to Play** guide). A third, the **Z-Machine interpreter**, is built into ATE (Games → Z-Machine): a clean-room version-3 virtual machine (written from the public Z-Machine Standards Document) that plays any `.z3` story file, with one-click download of the MIT-licensed Zork trilogy (fetched to your machine, on your action; ATE ships no game file). It saves and restores games, and an optional **auto-mapper** draws the world as you explore — colour-coded rooms, directional connections, items you've found (passages as ◇, up/down as ▲/▼), interiors on their own page — zoomable, with a spoiler-free side panel and one-click **SVG export** (map + object legend).

### Unity's External Script Editor
Select ATE in **Preferences → External Tools** and double-clicked scripts and console log entries open in ATE at the exact line and column. A configurable fallback editor receives anything ATE doesn't handle — solutions, C# project requests, binaries — so your IDE is still one click away.

### Stays current
Automatic update checks (daily to every-N-days, or off) announce new releases in the console and offer a one-click UPM install when the editor is idle — showing you exactly which version you'd get. While an update installs, ATE locks itself (never Unity) so no edits get lost — and afterwards, the new version's release notes open in a tab.

### Safe by design
Nothing here ever blocks Unity: close the window with unsaved documents and a small floating notice offers one-click **Save All** (the buffers are already safe in your session either way — a banner reminds you when they come back). External changes on disk show a non-modal reload banner — or reload silently if you enable **Auto-Reload Changed Files** and the buffer is clean — and if a file is **deleted** out from under you, ATE offers to keep the buffer so one Save brings the file back. Close the whole window and your tabs come back when you reopen it, even across editor restarts. A Settings tab (the gear button, or Tools → Options…) keeps every preference — theme, mode, line numbers, word wrap, font and size, smooth scrolling, tab size, keyboard layout, Markdown default view, save cleanups (trim trailing whitespace, final newline), recent-files count, external fallback editor, automatic updates — one click away, persisted across sessions.

## Get it

**Window → Package Manager → + → Add package from git URL…**

```
https://github.com/adkom-games/ADKOM_TextEditor.git#upm
```

Prefer a pinned version?

```
https://github.com/adkom-games/ADKOM_TextEditor.git#0.14.2
```

Semantic Features (compiler-accurate colors + Go to Definition) are built in — the first use, or the Settings toggle, sets everything up automatically.

Requires Unity 6000.0+. The `upm` branch delivers only the package — your download is lean. Then open **Tools → ADKOM → Text Editor**, or just right-click a file — or press **Ctrl+Alt+8** (say it out loud).

See [RELEASE-NOTES.md](RELEASE-NOTES.md) for version history and [CHANGELOG.md](CHANGELOG.md) for the details.

## License

See [LICENSE.md](LICENSE.md).

---

*Made by A Different Kind Of Mind Games (ADKOM).*
