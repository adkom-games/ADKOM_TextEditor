# ADKOM Text Editor

**A real code editor, living right inside the Unity Editor.**

[![Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20ADKOM%20Games-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/adkomgames)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](Packages/com.adkom.text-editor/LICENSE.md)
[![Unity](https://img.shields.io/badge/Unity-6000.0%2B-black.svg)](https://unity.com)

This repository is the **full Unity development project** for the ADKOM
Text Editor UPM package. If you just want to *use* the editor in your own
project, you don't need to clone this repo — install the package via a
git URL (see [Installation](#installation)).

The package itself lives at
[`Packages/com.adkom.text-editor`](Packages/com.adkom.text-editor) — see
its [README](Packages/com.adkom.text-editor/README.md) for the full
feature pitch.

## Features

**Editor core**
- Dockable text/code editor window inside Unity (**Tools → ADKOM → Text Editor**, **Ctrl+Alt+8**), or right-click any text asset → *Open in ADKOM Text Editor*
- Fully virtualized rendering — typing lands in under 15 ms even in 5,000+ line files
- Word-level, deterministic **undo/redo** (one undo removes one word, never minutes of typing), with status-bar feedback
- Word wrap, line numbers, syntax-colorized **minimap**, smooth scrolling, browser-style zoom (Ctrl+wheel / Ctrl+±/0), configurable font and size
- Double-click word selection with whole-word drag; automatic highlighting of every other occurrence of the selection
- **Goto Line** (Ctrl+G) via an emacs-style status-bar prompt

**Tabs & files**
- Multiple tabs: single-line strip with overflow scroll arrows and a jump-to-tab dropdown, drag-to-reorder, middle-click close, context menus, dirty markers, settings-tinted colors
- **Sessions**: open tabs — including *unsaved buffer content* — survive closing the window, domain reloads, editor restarts, and (via 30-second autosave) even editor crashes
- **Recent Files** menu (per project, configurable length)
- External change detection with a non-modal reload banner; **deleted-file rescue** (keep the buffer, one Save restores the file)
- Respects your files: tab/space indentation, line endings (CRLF/LF/CR), and UTF-8 BOMs round-trip untouched; tab-stop-aware navigation and editing

**Languages**
- **C# syntax highlighting** incl. types/methods/variables out of the box; opt-in **Semantic Features** add compiler-accurate colors, **Go to Definition** (Ctrl+Click / F12 / Ctrl+B) across files and assemblies, and generated "from metadata" views for engine/BCL symbols — dependencies (bundled MIT Roslyn) install themselves on consent
- **Markdown**: full source-mode coloring plus a rendered **WYSIWYG mode** with click-to-edit blocks, a 16-button formatting toolbar (headings, emphasis, lists, task lists, tables, links, images, code, quotes, rules), and a per-file mode toggle

**IDE editing** (new in 0.10.0)
- **Multi-caret editing** (Alt+Click, add-next/select-all occurrences, caret columns) with single-undo multi-edits; **column selection**
- **Word-based autocomplete** (current doc + open tabs + language keywords, Ctrl+Space)
- Auto-closing brackets/quotes, brace matching with jump, **code folding** (clickable gutter arrows, `{ ⋯ }` headers, double-click a brace to fold), indentation guides
- **Rename Symbol** (F2), **Find All References**, **Format Document** — on the same Roslyn semantics as Go to Definition
- **Quick Open** (Ctrl+, / Ctrl+P / Ctrl+T), per-document **bookmarks** with gutter markers, **drag-and-drop of selected text** (Ctrl to copy)
- Expand/shrink selection, insert line above/below, join lines, case transforms, sort lines, block comments, word-wise delete, whole-line cut/copy, navigate back/forward through caret history
- Save cleanups: trim trailing whitespace, ensure final newline

**IDE comfort**
- Native menu bar (File/Edit/View/Tools/Window/Help) with **per-layout shortcut hints** on every item; right-click context menu inside the document (Go to Definition, Find Occurrences, clipboard, file ops, Show in File Explorer, language commands)
- **Keyboard layouts** and **color themes** (dark + light) matching Visual Studio, VS Code, and JetBrains Rider; follows the Unity Editor skin or forced Dark/Light
- Find/replace (regex with $1 groups, whole-word, case, backwards, wrap-around) across the current file or **all open tabs**, in a modeless dialog
- Bottom **console pane** collecting all ATE messages, timestamped, selectable and copyable
- Non-modal by design: ATE's prompts never freeze the Unity editor or background tooling

**AI**
- **GitHub Copilot inline suggestions** (bring your own subscription + Node.js): ghost text with an alternatives cycler, Tab/Enter accept, works in unsaved buffers, one-time device-flow sign-in that persists
- **Ask Unity AI** about a selection or document (when Unity's Assistant package is installed) — prompt popup with your text attached, no points spent until you submit

**Integration**
- Selectable as Unity's **External Script Editor** — scripts and console entries open at the exact line/column, with a configurable fallback editor for solutions and binaries
- **Localized UI**: Japanese, Korean, Simplified Chinese, Traditional Chinese — follows Unity's Editor Language
- **Automatic updates** from GitHub Releases (configurable cadence, one-click install, release notes shown after updating)
- **Stable scripting API** (`AteApi`): open/edit/save/close documents and subscribe to editor events from your own editor scripts, with an importable every-member sample ([docs](Packages/com.adkom.text-editor/Documentation~/Scripting.md))
- **100% Editor-only** — nothing ships in player builds

**Addons & games** (new in 0.12.0)
- **Addons**: single-file or **multi-file folder** `.cs` addons in a machine-shared folder, Roslyn-compiled in-memory, with a full load/unload/focus lifecycle and a Tools → Addons menu
- **Addon security**: source scanned against known-dangerous API patterns; a risk report + clickable Scanner Results console tab; **nothing runs until one-time approval** keyed to the exact file content
- **Game API** (AteApi 1.1): per-document game mode (chrome hidden, block cursor, undo-bypassing overwrite writes), per-cell fg/bg colors, consumable key events + key polling, text-coordinate mouse, 30 Hz tick, per-document font and tab title, status-bar prompt
- **Two shipped games**: Snake, and a faithful port of **Rogue 5.4.4** (the 1980 BSD classic) as the first folder addon

## Installation

Requires **Unity 6000.0+**.

**Window → Package Manager → + → Add package from git URL…**

Latest:

```
https://github.com/adkom-games/ADKOM_TextEditor.git#upm
```

Pinned release:

```
https://github.com/adkom-games/ADKOM_TextEditor.git#0.12.0
```

The `upm` branch contains only the package, so consumer downloads stay
lean.

## Repository layout

| Path | What it is |
|---|---|
| `Packages/com.adkom.text-editor/` | The package source (embedded package — this is the code that ships) |
| `Assets/` | Host Unity project used to develop and test the package |
| `Tools/` | Release-gate scripts (editor-only guard audit, localization completeness) run locally and in CI |
| `.github/workflows/upm-branch.yml` | Runs the gates, then regenerates the package-only `upm` branch via `git subtree split` on every package change |
| `upm` branch | Distribution branch consumed by Unity Package Manager |

## Development

Open the repository root as a Unity 6 project. The package is
**embedded** under `Packages/`, so it is fully editable in place —
changes compile immediately in the host project. Releases are tagged,
and the `upm` branch is regenerated automatically by the GitHub Actions
workflow.

Version history: [RELEASE-NOTES.md](Packages/com.adkom.text-editor/RELEASE-NOTES.md)
· [CHANGELOG.md](Packages/com.adkom.text-editor/CHANGELOG.md)

## Contributing

Issues and pull requests are welcome. If you hit a bug or want a
feature, please open an issue.

## License

MIT — see [LICENSE.md](Packages/com.adkom.text-editor/LICENSE.md).

---

*Made by [A Different Kind Of Mind Games](https://github.com/adkom-games) (ADKOM Games).*
