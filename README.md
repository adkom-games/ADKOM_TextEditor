# ADKOM Text Editor

**A real code editor, living right inside the Unity Editor.**

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

- Dockable text/code editor window inside Unity (**Tools → ADKOM → Text Editor**), or right-click any text asset → *Open in ADKOM Text Editor*
- Fully virtualized rendering — fast typing even in 5,000+ line files
- Multiple tabs that survive domain reloads and editor restarts
- C# syntax highlighting with an extensible formatter API
- Themes and keyboard layouts matching Visual Studio, VS Code, and JetBrains Rider
- Respects your files: tab/space indentation, line endings, and UTF-8 BOMs round-trip untouched
- Native menu bar (File/Edit/View/Tools/Window/Help) and tab context menus
- Selectable as Unity's External Script Editor, with a configurable fallback for solutions and binaries
- Find/replace (regex, whole-word, case, backwards, wrap-around) across the current file or all open tabs
- Configurable font and size with browser-style zoom; smooth scrolling; automatic update checks
- Word wrap, line numbers, and configurable tab handling
- Unsaved-change guards and external file-change detection
- **100% Editor-only** — nothing ships in player builds

## Installation

Requires **Unity 6000.0+**.

**Window → Package Manager → + → Add package from git URL…**

Latest:

```
https://github.com/adkom-games/ADKOM_TextEditor.git#upm
```

Pinned release:

```
https://github.com/adkom-games/ADKOM_TextEditor.git#0.4.0
```

The `upm` branch contains only the package, so consumer downloads stay
lean.

## Repository layout

| Path | What it is |
|---|---|
| `Packages/com.adkom.text-editor/` | The package source (embedded package — this is the code that ships) |
| `Assets/` | Host Unity project used to develop and test the package |
| `.github/workflows/upm-branch.yml` | Regenerates the package-only `upm` branch via `git subtree split` on every package change |
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

Issues and pull requests are welcome. If you hit a bug or want a feature
(find/replace, more languages, custom themes are already on the
roadmap), please open an issue.

## License

MIT — see [LICENSE.md](Packages/com.adkom.text-editor/LICENSE.md).

---

*Made by [A Different Kind Of Mind Games](https://github.com/adkom-games) (ADKOM Games).*
