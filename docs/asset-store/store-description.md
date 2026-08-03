# Store listing draft

A starting point for the publisher portal, not final copy. **Rewrite it in your own voice before submitting** — the guidelines say purely AI-generated descriptions can be rejected, and the description has to accurately cover features, dependencies and requirements.

Fields below map to the portal's own fields.

## Title

ADKOM Text Editor

## Summary (short description)

A real code editor — C# semantics, multi-caret, find in files, diff/merge — living inside the Unity Editor.

## Description

**A real code editor, living right inside the Unity Editor.** Open a script, edit it, jump to a definition, rename a symbol and diff it against HEAD without ever leaving Unity. Editor-only: it contributes nothing to your player builds.

**Editing that keeps up.** Fully virtualized rendering keeps typing under 15 ms in 5,000+ line files. Word-level undo/redo removes one word per undo instead of minutes of work. Word wrap, line numbers, a syntax-colored minimap, smooth scrolling, browser-style zoom, configurable font and size.

**Tabs and sessions.** Multiple tabs with drag-to-reorder, overflow scrolling and dirty markers. Open tabs — including unsaved buffer content — survive closing the window, domain reloads, editor restarts and, via 30-second autosave, even editor crashes. External change detection with a non-modal reload banner, and deleted-file rescue: keep the buffer, one Save restores the file. Tabs, spaces, line endings and UTF-8 BOMs round-trip untouched.

**C# that understands your project.** Syntax highlighting out of the box; opt-in Semantic Features add compiler-accurate colors, Go to Definition across files and assemblies (Ctrl+Click / F12), Find All References, Rename Symbol (F2) and Format Document, plus generated "from metadata" views for engine and BCL symbols. Markdown gets full source coloring and a rendered WYSIWYG mode with a 16-button formatting toolbar.

**IDE editing.** Multi-caret and column selection, word-based autocomplete, auto-closing brackets, brace matching, code folding, indentation guides, Quick Open, per-document bookmarks with a Bookmarks view, drag-and-drop of selected text, expand/shrink selection, join lines, case transforms, sort lines, block comments, and caret history navigation.

**Find and compare.** A tabbed Find/Replace dialog (Normal, extended-escape and regex) with Find in Files, results listed in a Search Results view, every jump landing centered. A full diff/merge tool: files, folders and open tabs, side-by-side with intra-line highlights, two-way merging with per-change copy buttons, and three-way merge with conflict panels — optionally registered as Unity's own Revision Control diff/merge tool.

**Comfort.** Native menu bar with per-layout shortcut hints, a Section menu that jumps to any class, property or method in the current tab, color themes and keyboard layouts matching Visual Studio, VS Code and Rider, a bottom console area (Console / Search Results / Bookmarks), tooltips on every control, and a full user manual. Localized in English, Japanese, Korean, Simplified and Traditional Chinese. Non-modal by design: ATE's prompts never freeze the Unity Editor.

**Optional AI.** GitHub Copilot inline suggestions with an alternatives cycler, and Ask Unity AI about a selection or document. Both are opt-in and need their own prerequisites — see Requirements.

## Requirements (must appear in the listing)

- Unity 6000.0 or newer.
- Editor-only. Nothing in this package compiles into a player build.
- Dependency: `com.unity.nuget.newtonsoft-json` (3.2.1), installed automatically by the Package Manager.
- **Semantic Features (optional)**: enabling them copies the bundled MIT-licensed Roslyn assemblies into `Assets/Plugins/ADKOM.TextEditor/Roslyn` in your project. This happens only after you turn the feature on, and Roslyn adds ~14 MB to the download whether or not you use it.
- **GitHub Copilot (optional)**: requires your own active GitHub Copilot subscription and a local Node.js installation. ATE ships no credentials and no subscription.
- **Ask Unity AI (optional)**: requires Unity's Assistant package; ATE only forwards your selected text to it and spends no points until you submit.
- **Games menu (optional)**: the bundled Z-Machine interpreter can download three MIT-licensed Infocom story files on request. Nothing is downloaded unless you choose a game; no story files ship in the package.
- Updates are delivered through the Package Manager. This build performs no automatic update checks and never changes your project's packages.

## Keywords

editor, text editor, code editor, script editor, C#, IDE, syntax highlighting, diff, merge, find in files, multi-caret, refactoring, markdown, productivity, tools

## Category

Tools → Utilities (an Editor Extension)
