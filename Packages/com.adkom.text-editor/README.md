# ADKOM Text Editor

An advanced text editor that lives inside the Unity Editor — a dockable
window with tabs, syntax highlighting, and IDE-style keyboard commands.
Open it from **Tools → ADKOM → Text Editor**, or right-click any text
asset in the Project window and choose **Open in ADKOM Text Editor**.

## Features

### Documents & tabs
- Multiple open files as **tabs** — close with the × button or middle-click,
  with unsaved-changes protection. Open tabs survive domain reloads and
  editor restarts.
- Open **any text asset** from the Project window context menu: scripts,
  TextAssets, shaders, USS/UXML, markdown, JSON, YAML, configs, and more.
  Already-open files switch to their tab instead of duplicating.
- **External change detection**: if a file changes on disk, you're offered a
  reload when its tab is activated or the window regains focus.
- Preserves each file's original **line endings** (CRLF/LF/CR) and **UTF-8
  BOM** on save.

### Code editing
- **C# syntax highlighting** (keywords, strings, comments, numbers,
  preprocessor) with an extensible formatter pipeline for future languages.
- **Color themes** with three built-in palettes — **Visual Studio** (VS
  Dark/Light), **VS Code** (Dark+/Light+), and **JetBrains Rider** (Rider
  Dark/IntelliJ Light) — plus a light/dark mode selector (Auto follows the
  Unity Editor skin).
- **Line numbers** (toggleable) that stay aligned even with word wrap on:
  wrapped continuation rows leave a gap in the gutter.
- **Word wrap** toggle.
- **Tabs rendered as spaces** at a configurable tab size. Files keep their
  original indentation style: tab-indented files are saved back with tabs,
  space-indented files stay spaces — your formatting is never destroyed.
- The **Tab key inserts spaces** to the next tab stop; arrow keys jump
  through space indentation in tab-size steps, as if they were real tabs.

### Keyboard commands
Choose your layout in Settings — **Visual Studio**, **VS Code**, or
**Rider** defaults for everything the editor supports:

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
| Toggle line comment | Ctrl+/ | Ctrl+/ | Ctrl+/ |
| Indent / unindent | Tab / Shift+Tab | Tab / Shift+Tab | Tab / Shift+Tab |
| Settings | — | Ctrl+, | Ctrl+Alt+S |

The settings gear also toggles: it opens Settings, brings the tab to the
front if it's in the background, and closes it if it's already frontmost.

### Settings
The gear button opens **Settings as a document tab**: Color Theme,
Light/Dark Mode, Line Numbers, Word Wrap, Tab Size, and Keyboard Layout.
All settings persist across sessions.

### Quality of life
- Status bar with line/column, language, encoding, and line-ending info.
- Window title reads "ATE - filename" so the dock tab is always
  identifiable.
- **Editor-only by construction**: all code is in an Editor-platform
  assembly. The package contributes nothing to player builds and never
  interferes with the host project.

## Installation

In Unity: **Window → Package Manager → + → Add package from git URL…**

```
https://github.com/adkom-games/ADKOM_TextEditor.git#upm
```

To pin a version, use a release tag instead of `upm`:

```
https://github.com/adkom-games/ADKOM_TextEditor.git#0.1.0
```

The `upm` branch contains only the package (kept in sync automatically);
the default branch holds the full development project.

Requires Unity 6000.0 or newer.

## Roadmap

- Syntax highlighting for more languages (JSON, Markdown, shaders) via the
  `ITextFormatter` extension point
- Find / replace
- Custom user themes

## License

See [LICENSE.md](LICENSE.md).
