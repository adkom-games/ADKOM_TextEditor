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

### 🎨 Syntax highlighting & themes
C# highlighting out of the box, with an extensible formatter API ready
for more languages. Pick your palette: **Visual Studio**, **VS Code**,
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
| Toggle line comment | Ctrl+/ | Ctrl+/ | Ctrl+/ |
| Indent / unindent | Tab / Shift+Tab | Tab / Shift+Tab | Tab / Shift+Tab |
| Settings | — | Ctrl+, | Ctrl+Alt+S |

Plus undo/redo with typing coalescing, word-wise navigation, smart Home,
auto-indent on Enter, and full clipboard support.

### 🔧 Respects your files
Tabs render as spaces at your configured tab size — but on save, files
that indent with tabs get their tabs back, and space-indented files stay
spaces. Line endings (CRLF/LF/CR) and UTF-8 BOMs round-trip untouched.
Your teammates will never know you edited it in Unity (unless you tell
them). Navigation, Backspace, and Delete all honor tab stops across
whitespace, so space-indented files *feel* tab-indented.

### 🔍 Find & replace, everywhere
Search the current file or every open tab at once — match case, whole
word, plain text or full regular expressions (with $1 group
replacements), wrap-around, and backwards search, all from a compact
modeless dialog. F3 repeats your last search without reopening it.

### 🛡 Safe by design
Unsaved-changes prompts on close, external-change detection with a
reload offer when a file changes on disk, and line numbers when you want
them. A Settings tab (the gear button) keeps every preference —
theme, mode, line numbers, tab size, keyboard layout — one click away,
persisted across sessions.

## Get it

**Window → Package Manager → + → Add package from git URL…**

```
https://github.com/adkom-games/ADKOM_TextEditor.git#upm
```

Prefer a pinned version?

```
https://github.com/adkom-games/ADKOM_TextEditor.git#0.3.0
```

Requires Unity 6000.0+. The `upm` branch delivers only the package —
your download is lean. Then open **Tools → ADKOM → Text Editor**, or
just right-click a file.

## What's next

- More languages (JSON, Markdown, shaders) via the formatter API
- Custom user themes

See [RELEASE-NOTES.md](RELEASE-NOTES.md) for version history and
[CHANGELOG.md](CHANGELOG.md) for the details.

## License

See [LICENSE.md](LICENSE.md).

---

*Made by A Different Kind Of Mind Games (ADKOM).*
