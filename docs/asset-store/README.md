# Asset Store submission

How the ADKOM Text Editor is prepared for the Unity Asset Store, what the store build differs in, and what still needs a human before the first submission.

Submit as a **UPM product** (publisher portal → UPM packages), not a classic asset package. A `.unitypackage` built from `Assets/` would silently drop every `~` folder — `RoslynBinaries~`, `Editor/SpellCheckData~`, `Samples~`, `Documentation~` — i.e. Roslyn, the spell dictionary and the samples. The 17 MB package is far inside the 700 MB UPM limit.

## The store build

CI produces `upm-store` from the same source as `upm` (`.github/workflows/upm-branch.yml`). Three things change, all driven by `Tools/store-overrides/`:

| Swapped file | Development build | Store build |
|---|---|---|
| `Editor/Distribution/AteBuildFlavor.cs` | `AssetStore = false` | `AssetStore = true` — guards every call site below |
| `Editor/Distribution/AtePackageInstaller.cs` | `Client.Add` / `Client.Remove` | refuses; never references `UnityEditor.PackageManager` |
| `Editor/Distribution/AteAddTabIntegration.cs` | reflects into `UnityEditor.HostView` for the dock "Add Tab" entry | empty class |
| `Editor/Distribution/AteCodeEditorRegistry.cs` | reads Unity's private external-editor registry | returns nothing |

CI additionally deletes `LICENSE.md` (the store copy is governed by the Asset Store EULA, not the MIT grant) and rewrites `license` / `licensesUrl` in `package.json`.

Behaviour the store build therefore has:

- No unprompted network access. Automatic update checks are compiled out and the **Automatic Updates** toggle and frequency field are hidden; **Check for Updates Now** still works on demand and points the user at the Package Manager.
- No package-set changes. The obsolete-semantics-module cleanup warns instead of removing.
- No Editor-internal reflection. No dock "Add Tab" entry; external-editor fallback degrades to the OS default application and project sync is left to Unity's own IDE packages.

## Rules this repository enforces

`Tools/check-store-compliance.ps1` runs in CI for the development tree and again (with `-Store`) for the generated store tree. It fails on:

1. `Client.Add` / `Client.Remove` / `Client.Embed` outside `Editor/Distribution/` — and anywhere at all in the store tree.
2. Editor-internal reflection (`GetType("UnityEditor…`, `UnityEditorInternal.`, `BindingFlags.NonPublic`) outside `Editor/Distribution/` — and anywhere at all in the store tree. `Editor/AteInspectorWindow.cs` is allowlisted: it reflects over the types a user inspects, not over Unity's internals.
3. Any consumer-visible path of 150 characters or more.
4. Missing `THIRD-PARTY-NOTICES.md`, `Manual.md`, `README.md`, `CHANGELOG.md`, `package.json`, or a missing `name` / `version` / `displayName` / `description` / `unity` / `documentationUrl` field.
5. Store tree only: `AteBuildFlavor.AssetStore` not `true`, or a surviving `LICENSE.md`.

Run it locally the same way CI does: `pwsh -File Tools/check-store-compliance.ps1`.

## Before the first submission

These need a person, not a script:

- [ ] **Compile against the newest supported Editor AND the declared minimum (6000.0) with zero warnings.** The guidelines reject deprecated/obsolete API warnings and any warning originating from package content — and the minimum matters just as much: 0.14.2 shipped compile errors on 6000.0 because it was developed against 6000.3 APIs (`ITextSelection.GetCursorPositionFromStringIndex`, `DropdownMenuSizeMode`, both 6000.3+; fixed with version defines in 0.14.3). Also enable Semantic Features during the minimum-version test — the Semantics assembly only compiles once Roslyn is installed, so a clean-install compile alone does not cover it. Verified so far: development Editor 6000.3.19f1; newest released line is 6000.5.
- [ ] **Baseline build-bleed test: build a player and inspect it.** Build a minimal player for any target, then read the build report at the end of `Editor.log`: no `ADKOM.*` assembly, no `Microsoft.CodeAnalysis.*`, no package asset paths under "Used Assets". Run it twice — once plain, once with Semantic Features enabled first, so the `Assets/Plugins/ADKOM.TextEditor/Roslyn` copy exists and the editor-only importer settings are exercised. This is the ground truth the static gates approximate; afterwards it only needs repeating when the bleed surface changes (Roslyn bundle updated or its install path moved, an asmdef added/restructured, non-code assets added, or the supported Unity version raised) — routine editor-code releases are covered by the gates.
- [ ] **Fast Enter Playmode (Unity 6.6+)** — required for packages that claim 6.6 support. ATE is editor-only but keeps live state across reloads; exercise tabs, sessions and Copilot with it enabled. Unity 6.6 is in beta at the time of writing; this blocks a 6.6 support claim, not the initial submission.
- [ ] **Decide the price and whether the GitHub MIT build stays public.** Selling a package whose source is MIT elsewhere is allowed — you hold the copyright — but buyers can and will find it; that is a business decision, not a compliance one.
- [ ] **Fill in the store listing** from [`store-description.md`](store-description.md) and [`disclosures.md`](disclosures.md).
- [ ] **Keep every link live.** Inactive publisher links (email, website, docs) are grounds for deprecation without warning.

## Review loop

Submissions take at least five business days. If nothing arrives within two weeks, open a Unity support ticket. Rejections list a reason in the publisher portal — fix, then resubmit the same draft.

## Sources

- [Submission guidelines](https://assetstore.unity.com/publishing/submission-guidelines)
- [Submitting a package](https://docs.unity.com/en-us/asset-store/publishing/asset-packages/submit)
- [Publishing a UPM product](https://docs.unity.com/en-us/asset-store/publishing/upm-packages)
