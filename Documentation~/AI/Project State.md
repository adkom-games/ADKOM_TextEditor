---
tags: [project-state]
---

# Project State — ADKOM Text Editor

**Vision:** An advanced text editor living entirely inside the Unity Editor,
shipped as a UPM package installable from a pure GitHub URL (optionally
version-pinned with `#vX.Y.Z`). Strictly Editor-only — it must never build
into a player or interfere with the host project.

- **Engine/stack:** Unity 6.3 LTS (6000.3.19f1), UIToolkit, C#. UPM package `com.adkom.text-editor` at the **repo root**.
- **Last updated:** 2026-07-24
- **Active branch:** `feature/text-editor`

## Milestones

| Milestone | Status |
|---|---|
| Package skeleton (package.json, asmdef, docs) | Done |
| v1 editor (window, file ops, status bar, wrap, dirty guard, external-change detect) | Done (code written, NOT yet compiled/tested) |
| Repo restructure: dev project → `D:\ADKOM\source\ADKOM_TextEditor` (outside repo) | Done |
| Verify compile + manual test in ADKOM_TextEditor | **Next: open it in Unity (MCP still down)** |
| Tag v0.1.0, verify git-URL install in scratch project | Not started |
| Line numbers | Future |
| Syntax highlighting (ITextFormatter impls) | Future |

## Current work area

Package authored at repo root: `package.json`, `Editor/` (asmdef Editor-only,
`TextEditorWindow.cs`, `TextDocument.cs`, `FileService.cs`, `ITextFormatter.cs`,
`UI/TextEditor.uss`), `README/CHANGELOG/LICENSE`, this `Documentation~/`.

## Key decisions

- **Repo root = package** so consumers install with a pure git URL, no `?path=`.
  Version pinning via annotated tags (`#v0.1.0`).
- **Dev project lives OUTSIDE the repo** at `D:\ADKOM\source\ADKOM_TextEditor`
  (moved 2026-07-24; not version-controlled in this repo; productName
  `ADKOM_TextEditor`). The repo contains ONLY the package.
- **Embed via junction:** to get Unity to generate/commit `.meta` files, a
  directory junction `D:\ADKOM\source\ADKOM_TextEditor\Packages\com.adkom.text-editor`
  → repo root makes the package embedded/writable (`file:` refs are immutable).
- **UIToolkit over IMGUI** — needed for future line-number gutter and rich-text
  syntax highlighting; IMGUI TextArea would dead-end.
- **Editor-only guarantee** = asmdef `includePlatforms: ["Editor"]` + all code in `Editor/`.
- Content normalized to `\n` internally; original EOL style + UTF-8 BOM restored on save.
- `ITextFormatter` passthrough in place now so highlighters plug in without refactor.

## Immediate next steps

1. Open `D:\ADKOM\source\ADKOM_TextEditor` in Unity Hub (the project moved —
   old Hub entries are stale). The junction in its Packages folder → repo root
   is in place, so the package appears embedded and writable.
2. Wait for compile; fix any errors/warnings; commit the `.meta` files Unity
   generates for the package (they land at repo root through the junction).
3. Manual test pass (open/save/dirty guard/external change, Tools → ADKOM →
   Text Editor), then merge to main and tag `v0.1.0`.
4. Verify install from `https://github.com/...git#v0.1.0` in a scratch project.

## Standing constraints

- ai-game-developer MCP was not connected this session — compile verification
  pending; notify Cary if still down when Unity work resumes.
- Never place package code outside `Editor/`; keep asmdef Editor-only.

## Research Efforts Log

*(none yet — first effort is the v1 editor itself)*
