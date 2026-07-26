# ADKOM Text Editor

**A real code editor, living right inside the Unity Editor.**

Stop alt-tabbing. Whether it's a quick tweak to a config file, a README
edit, or browsing a script while your game runs, the ADKOM Text Editor
gives you a fast, IDE-grade editing experience without ever leaving
Unity — dockable, themeable, and tuned to feel like the editors you
already know.

## 100% Editor-only. Zero shipping impact.

This is a **Unity Editor asset** — a tool for you, not your players.
Every line of code lives in an Editor-only assembly: nothing is compiled
into your builds, nothing ships with your game, and nothing touches your
runtime. Install it, use it every day, and your shipping product stays
byte-for-byte identical.

## Why you'll love it

### ⚡ Fast at any size
The editor is **fully virtualized** — only the lines on screen are ever
rendered. Open a 5,000-line file and typing still lands in under 15ms.
No lag, no stutter, no matter how big the file.

### 🗂 Tabs, like a real editor
Open as many files as you want. Tabs remember themselves across domain
reloads and editor restarts, warn you about unsaved changes, close with
a middle-click, and switch instead of duplicating when you re-open a
file. Right-click any text asset in the Project window — scripts,
shaders, JSON, YAML, markdown, USS/UXML, configs — and it opens in ATE.
**File → Recent Files** remembers what you've had open (per project,
count configurable) so yesterday's file is two clicks away.

### 🎨 Syntax highlighting & themes
C# highlighting out of the box — keywords, strings, comments, and
**identifiers too**: types, methods, variables, and parameters, in each
theme's authentic colors. Flip on **Semantic
Features** in Settings and the colors become compiler-accurate (powered
by Roslyn) — every dependency installs itself automatically. Pick your palette: **Visual Studio**, **VS Code**,
or **JetBrains Rider** — each with authentic dark and light variants,
following your Unity Editor skin automatically (or forced Dark/Light,
your call).

### ⌨️ Your muscle memory works here
Choose your keyboard layout — **Visual Studio**, **VS Code**, or
**Rider** — and the shortcuts you already know just work:

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
| Find / Find in tabs | Ctrl+F / Ctrl+Shift+F | Ctrl+F / Ctrl+Shift+F | Ctrl+F / Ctrl+Shift+F |
| Replace / Replace in tabs | Ctrl+H / Ctrl+Shift+H | Ctrl+H / Ctrl+Shift+H | Ctrl+R / Ctrl+Shift+R |
| Find next / previous | F3 / Shift+F3 | F3 / Shift+F3 | F3 / Shift+F3 |
| Zoom in / out / reset | Ctrl+'+' / Ctrl+'-' / Ctrl+0 | Ctrl+'+' / Ctrl+'-' / Ctrl+0 | Ctrl+'+' / Ctrl+'-' / Ctrl+0 |
| Toggle line comment | Ctrl+/ | Ctrl+/ | Ctrl+/ |
| Indent / unindent | Tab / Shift+Tab | Tab / Shift+Tab | Tab / Shift+Tab |
| Go to Definition | F12 / Ctrl+Click | F12 / Ctrl+Click | Ctrl+B / Ctrl+Click |
| Settings | — | Ctrl+, | Ctrl+Alt+S |
| Open the ATE window | Ctrl+Alt+8 | Ctrl+Alt+8 | Ctrl+Alt+8 |

Plus undo/redo with typing coalescing, word-wise navigation, smart Home,
auto-indent on Enter, full clipboard support, double-click word
selection with whole-word drag, and automatic highlighting of every
other occurrence of whatever you select.

### 🔧 Respects your files
Tabs render as spaces at your configured tab size — but on save, files
that indent with tabs get their tabs back, and space-indented files stay
spaces. Line endings (CRLF/LF/CR) and UTF-8 BOMs round-trip untouched.
Your teammates will never know you edited it in Unity (unless you tell
them). Navigation, Backspace, and Delete all honor tab stops across
whitespace, so space-indented files *feel* tab-indented.

### 🧭 Go to Definition
**Ctrl+Click** any symbol — or press F12 (Visual Studio / VS Code
layouts) or Ctrl+B (Rider) — and jump straight to its definition:
locals, parameters, members, and types, across files and assemblies.
Symbols from referenced binaries (UnityEngine, the BCL) open a
generated **"from metadata" signature view** of their type, caret on
the member. Part of the opt-in Semantic Features: everything ships in the box,
and the first use offers one-click setup (bundled MIT-licensed Roslyn,
notices included; an existing project Roslyn is used as-is).

### 🔍 Find & replace, everywhere
Search the current file or every open tab at once — match case, whole
word, plain text or full regular expressions (with $1 group
replacements), wrap-around, and backwards search, all from a compact
modeless dialog. F3 repeats your last search without reopening it.

### 📝 Markdown, both ways
Open a `.md` file and a toggle appears next to the settings gear:
**source mode** with full markdown syntax coloring, or **rendered
mode** — headers, lists, quotes, and code blocks styled properly, with
block-level WYSIWYG editing: click any block, edit its source inline,
and it re-renders on commit. Undo works across both modes. A
**formatting toolbar** appears for any `.md` tab — one button per
element (headings, bold, italic, strikethrough, code, links, images,
lists, task lists, quotes, code blocks, tables, rules) that formats
your selection or the block you're editing, in either mode. Tables,
task lists (☐/☑), images, and strikethrough render and color in both
modes, and a settings option picks which view `.md` files open in.

### 🗺 See the whole file
A **syntax-colorized minimap** runs along the right edge — the shape of
your whole document at a glance, with a viewport indicator; click or
drag it to jump anywhere. A **console pane** at the bottom collects
every ATE message (tool output, update checks, find/replace results,
status messages) with timestamps — selectable and copyable. Both close away cleanly and come back
from the View menu — and both are on by default, along with line
numbers and word wrap.

### 🖱 Feels like an application
A real **menu bar** — File, Edit, View, Tools, Window, Help — rendered
with your platform's native menus, plus right-click context menus on
file tabs (Save, Save As, Close, Close Other Tabs). Pick any installed
**font** and size, zoom with Ctrl+MouseWheel or Ctrl+'+'/'-' (Ctrl+0
resets), and enjoy **smooth scrolling** (toggleable).

### 🔌 Unity's External Script Editor
Select ATE in **Preferences → External Tools** and double-clicked
scripts and console log entries open in ATE at the exact line and
column. A configurable fallback editor receives anything ATE doesn't
handle — solutions, C# project requests, binaries — so your IDE is
still one click away.

### 🔄 Stays current
Automatic update checks (daily to every-N-days, or off) announce new
releases in the console and offer a one-click UPM install when the
editor is idle — showing you exactly which version you'd get. While an
update installs, ATE locks itself (never Unity) so no edits get lost —
and afterwards, the new version's release notes open in a tab.

### 🛡 Safe by design
Unsaved-changes prompts on close, external-change detection with a
reload offer when a file changes on disk — and if a file is **deleted**
out from under you, ATE offers to keep the buffer so one Save brings
the file back. Close the whole window and your tabs come back when you
reopen it, even across editor restarts. A Settings tab (the gear button, or Tools → Options…) keeps every
preference — theme, mode, line numbers, word wrap, font and size,
smooth scrolling, tab size, keyboard layout, Markdown default view,
recent-files count, external fallback editor, automatic updates — one
click away, persisted across sessions.

## Get it

**Window → Package Manager → + → Add package from git URL…**

```
https://github.com/adkom-games/ADKOM_TextEditor.git#upm
```

Prefer a pinned version?

```
https://github.com/adkom-games/ADKOM_TextEditor.git#0.8.0
```

Semantic Features (compiler-accurate colors + Go to Definition) are
built in — the first use, or the Settings toggle, sets everything up
automatically.

Requires Unity 6000.0+. The `upm` branch delivers only the package —
your download is lean. Then open **Tools → ADKOM → Text Editor**, or
just right-click a file — or press **Ctrl+Alt+8** (say it out loud).

## What's next

- More languages (JSON, shaders) via the formatter API
- Custom user themes

See [RELEASE-NOTES.md](RELEASE-NOTES.md) for version history and
[CHANGELOG.md](CHANGELOG.md) for the details.

## License

See [LICENSE.md](LICENSE.md).

---

*Made by A Different Kind Of Mind Games (ADKOM).*
