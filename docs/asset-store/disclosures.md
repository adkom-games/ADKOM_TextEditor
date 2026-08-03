# Required disclosures

What the submission form and the listing must state, and why the guidelines ask for it. Copy these into the portal fields verbatim where a field exists; where none does, they belong in the description.

## Third-party content

The package redistributes MIT-licensed Roslyn assemblies (`Microsoft.CodeAnalysis*`, plus supporting `System.*` libraries from the .NET Foundation) in `RoslynBinaries~`, and an English spell-check dictionary derived from SCOWL. Both are listed with their full license text in `THIRD-PARTY-NOTICES.md`, which ships in the package. No GPL, LGPL, Creative Commons or Apache-2.0-with-attribution dependency is present — those are the licenses the guidelines reject.

The Games menu can download Zork I, II and III story files from three MIT-licensed `historicalsource` repositories, pinned per commit. No story file ships in the package and nothing downloads unless the user picks a game. This is disclosed in `THIRD-PARTY-NOTICES.md` and must also appear in the store description.

## Network access

The Asset Store build makes **no** network request on import or on any schedule. Every outbound request follows an explicit user action:

| Action | Endpoint | Why |
|---|---|---|
| **Check for Updates Now** (Settings) | `github.com/adkom-games/ADKOM_TextEditor/releases.atom` | reports whether a newer version exists; installs nothing |
| Choosing a game in the Games menu (the whole feature is opt-in: Settings → Enable In-Editor Games, off by default), then confirming | `raw.githubusercontent.com/historicalsource/zork{1,2,3}` | downloads that story file to a per-user folder outside the project. A confirmation window naming the repository, the pinned commit, the licence, the size, the SHA-256 and the destination path appears first, every time; nothing is fetched unless the user presses Download, and the result is verified against both the expected size and the expected SHA-256 before it is used |
| Signing in to GitHub Copilot | GitHub device-flow and Copilot endpoints | the user's own subscription; ATE ships no credentials |
| Opening Help → Repository / Release Notes / Issues | `github.com` | ordinary user-initiated link |

No telemetry, no analytics, no crash reporting, no phone-home of any kind. Nothing to opt out of because nothing is collected.

## AI functionality

Two optional integrations, both off until the user turns them on:

- **GitHub Copilot inline suggestions.** Requires the user's own Copilot subscription and a local Node.js. Document text around the caret is sent to GitHub's Copilot service to produce suggestions, under the user's own agreement with GitHub. ADKOM neither receives nor stores that content, and no developer, project or customer data is used to train any model.
- **Ask Unity AI.** Forwards the user's selected text or document to Unity's own Assistant package when it is installed. No points are spent until the user submits the prompt.

Credentials: ATE stores no Copilot token at all. Sign-in state is owned by GitHub's own Copilot language server, which ATE installs under `<project>/Library/ADKOMTextEditor/copilot` — outside `Assets/`, never imported as an asset and never included in a build. Nothing ATE writes can carry an API key into a player build.

No part of the product itself — code, icon, documentation — is AI-generated content requiring the portal's AI-generation disclosure. If that changes, say so in the AI description field in plain terms.

## Behaviour a reviewer will look for

- **Menus** live under `Tools → ADKOM → Text Editor` and `Window → ADKOM Text Editor`, per the placement rule.
- **No package manipulation.** The store build cannot add, update or remove packages; the only file that ever could is replaced with a refusing stub.
- **No Editor-internal reflection.** The two places that used it are replaced; ATE loses its dock "Add Tab" entry and its external-editor fallback in this build.
- **No forced registration, DRM, watermarks or time limits.** All core editing works with no account. Copilot is an optional integration behind the user's own subscription, which the guidelines permit when disclosed.
- **Documentation ships locally**: `Manual.md` plus `Documentation~/`, opened from Help → Documentation. No video files in the package.
- **Roslyn is copied into the project only after the user enables Semantic Features**, into `Assets/Plugins/ADKOM.TextEditor/Roslyn`.
