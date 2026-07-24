# ADKOM Text Editor

An advanced text editor that lives inside the Unity Editor. Open it from
**Tools → ADKOM → Text Editor**.

- New / Open / Save / Save As with unsaved-changes protection
- Ctrl+S to save (Ctrl+Shift+S for Save As)
- External-change detection (prompts to reload if the file changes on disk)
- Word-wrap toggle, status bar with line/column, encoding, and line-ending info
- Preserves the file's original line endings (CRLF/LF/CR) and UTF-8 BOM on save

**Editor-only by construction**: all code is in an Editor-platform assembly.
The package contributes nothing to player builds and never interferes with the
host project.

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

- Line numbers (gutter is already reserved in the layout)
- Syntax highlighting via the `ITextFormatter` extension point (C#, JSON, Markdown)

## License

See [LICENSE.md](LICENSE.md).
