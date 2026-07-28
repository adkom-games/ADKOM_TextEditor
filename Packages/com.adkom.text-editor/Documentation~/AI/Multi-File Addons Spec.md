# Multi-File (Folder) Addons — Spec

Agreed 2026-07-27 (Cary: "spec the multi-file addon support first" for the
Rogue 5.4.4 port). Extends the addon framework in [[Game API Design]].

## Problem

An addon is one Roslyn-compiled `.cs` file; files compile independently,
so a game cannot span files. Real games (Rogue) need modules.

## Design

- **Detection:** each FIRST-LEVEL subfolder of the shared addons folder
  (`%APPDATA%/ADKOM/TextEditor/Addons/`) whose name does not start with
  `.` is ONE addon. All `.cs` files under it (recursive) compile
  together into a single in-memory assembly. Top-level `.cs` files stay
  single-file addons, unchanged.
- **Entry class:** exactly one `[AteAddon]` class implementing
  `IAteAddon` across the folder; zero or multiple → entry error (menu
  shows it disabled with the reason). Helper classes/files need no
  attribute.
- **Compiler:** `IAddonCompiler` gains
  `TryCompileMany(string[] paths, out Assembly, out string[] errors)`;
  single-file `TryCompile` remains (now a wrapper). Errors keep
  file:line prefixes so they are actionable in the console.
- **Security (AddonSecurity):**
  - Identity hash = SHA-256 over the sorted sequence of
    (relative path + '\0' + content) — any file change, addition, or
    removal re-prompts consent.
  - Scan every file; `Finding` gains a `File`; the report groups
    findings by file; the Scanner Results tab rows jump to the exact
    file:line.
  - Consent store key = the folder path (same store, same semantics).
- **Menu / lifecycle / semver:** unchanged — Entry.Type drives Run and
  residents; the folder's ApiVersion comes from its [AteAddon].
- **Samples installer:** copies sample subfolders (recursively) as well
  as top-level files.
- **Non-goals:** no cross-addon references (each addon still compiles
  against ATE + loaded assemblies only, never against another addon's
  output); no nested addon folders (a subfolder inside an addon folder
  is part of that addon, not a new one).

## Status

- Implemented on feature/game-api (with the Rogue port that motivated it).
