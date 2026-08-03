# Troubleshooting & FAQ

Known issues and their fixes, plus answers to the questions that come up most. If yours isn't here: **Help → Report an Issue**.

## Copilot

**Enabling Copilot fails with npm errors (e.g. `Cannot find module '…npm-prefix.js'`).** Update ATE — 0.13.2 fixed the installer for newer Node/npm (npm 10.9+, issue #40). Copilot always needs **Node.js** installed and **your own Copilot subscription**; the official Copilot Language Server installs itself per project on first enable.

**Copilot sign-in.** It's GitHub's device flow: the code is auto-copied to the clipboard, you paste it on the GitHub page, and the login persists across domain reloads, restarts, and reboots. Sign out any time from Settings.

## Addons & games

**The Addons menu says "Addons need Semantic Features".** Addons are compiled by ATE's bundled Roslyn — enable **Semantic Features** in ATE's Settings first.

**An addon shows ⚠ or says it awaits approval.** Every addon needs your one-time consent before anything in it executes. Run it from Tools → Addons: the security report opens, findings land in the Scanner Results tab, and the banner's Approve runs it. Approval is per exact content — any file change asks again.

**A sample addon shows "SIGNATURE INVALID".** Your installed copy is usually older than the ATE that shipped it, so its signature no longer matches its code. **Tools → Addons → Install Sample Addons** reinstalls fresh, signed copies.

**My single-file addon disappeared after an update.** Addons are folder-based now: each subfolder of the addons folder is one addon. ATE migrates stray top-level `.cs` files into folders of their own automatically (watch the console); approval and any signature must be renewed for the new folder identity, and old `<name>.cs.*.atesig` sidecars are left behind — delete them, they no longer apply.

**A game tab says "(unloaded)".** Z-Machine games normally *survive* domain reloads (script compiles, play mode, editor restarts) — ATE snapshots and resumes them automatically. The "(unloaded)" mark is the fallback for when that could not happen: the game had already ended, the snapshot failed, or the story file is no longer where it was. The tab keeps the transcript as plain text so nothing you read is lost; the live game is over. Start a new one from the Games menu, and if you used the game's `save` command, `restore` picks up from there (Z-Machine saves include the map and scrollback). Snake and Rogue also survive reloads (Snake resumes paused — press Space); if one didn't come back, its state predates the stateful samples — reinstall the samples.

## Updates

**The update check logs "HTTP 403 Forbidden".** Fixed in 0.12.2 — the check now reads the un-rate-limited releases feed. Update ATE manually once (Package Manager, git URL) and the automatic channel works again.

**Upgrading from 0.5.x broke compilation (duplicate assembly name).** The old separate semantics module conflicts with 0.6+. Remove "ADKOM Text Editor — Semantics Module" in the Package Manager, or install 0.6.1+ which removes it automatically.

**The green update icon by the gear — what is it?** A new ATE release is available. Click it for the update dialog; it stays (across compiles and restarts) until you actually update. Embedded development copies get a manual-update hint instead of Install Now.

## Editing

**Rendered Markdown won't let me edit.** Markdown opens **locked** (read-only) by default — clicks select text for copying instead of opening block editors. Click the lock button next to the MD toggle to unlock; **Settings → Open Markdown Locked** changes the default.

**Selecting text in a rendered Markdown file that contains emoji spams the console with `IndexOutOfRangeException` (`ATGTextJobSystem.ConvertMeshInfoToUIRVertex`).** This is a bug in Unity's own text generator (confirmed on 6000.3.19f1), not in ATE: a text element whose content includes an emoji — a glyph that comes from OS font fallback rather than a Unity font asset — throws every time its mesh is regenerated, and dragging a selection regenerates it on every mouse-move. The exception happens inside a Unity job, so nothing in ATE can catch or suppress it. Nothing is damaged: the text, the selection and the copy are all correct. Until Unity fixes it, viewing the file in **source mode** (the MD toggle) avoids the rendered path entirely. ATE's own documentation contains no emoji for this reason.

**Selecting across blocks in rendered Markdown looks different from normal selection.** Unity displays a native text selection in only one text element at a time, so ATE draws cross-block selections itself. They are character-precise, highlight continuously across block margins, and copy correctly — but images in the span show no highlight, and double-click/triple-click (native) selections stop at block boundaries. Tracked on GitHub.

**The Games menu is missing.** In-editor gaming is off by default. Enable **Settings → Games → Enable In-Editor Games** — the Games menu appears immediately in every open ATE window, and game addons show up under Tools → Addons. The player guides under Help → Documentation are available regardless.

**Double-clicking a script opens the wrong editor.** Pick ATE as the External Script Editor in Unity's **Preferences → External Tools**. Anything ATE doesn't handle (solutions, binaries) goes to the **External Fallback** editor you configure in ATE's Settings.

**`#pragma bookmark` makes the C# compiler warn (CS1633 "Unrecognized #pragma directive").** Expected — the pragma is ATE's, not the compiler's. Three fixes, pick one: (1) ATE Settings → Language & Tools shows the warning's suppression status and offers **Suppress in This Project**, which writes `-nowarn:1633` into `Assets/csc.rsp` and recompiles; (2) add that line to `Assets/csc.rsp` yourself; (3) put `#pragma warning disable 1633` at the top of the affected file. Note that assemblies compiled from their own asmdef may need the flag in their own response file.

**Git commands say "Not inside a git repository."** ATE uses your system git — the current file must live inside a git working tree, and git must be on the PATH.

**Are my unsaved changes safe?** Yes: open tabs — including unsaved buffer content — survive closing the window, domain reloads, and editor restarts, with a 30-second autosave guarding against crashes. Undo/redo history survives domain reloads too. If a file is deleted on disk you can keep the buffer, and one Save restores the file.

## Diff / Merge

**Unity doesn't open ATE for version-control diffs.** The External Tools preference is per-user, not per-project: the project that most recently pressed **Use ATE for Unity Diff/Merge** owns it, and its shim only reaches that project's editor. Re-press the button in the project you are working in, and check Preferences → External Tools shows "Custom Tool" pointing at `Library/ATE/ate-difftool`.

**A merge saved with `<<<<<<<` markers.** Save is allowed with unresolved conflicts on purpose (the markers are the standard hand-off format). Reopen the merge, resolve the remaining numbered conflicts, and Save again — or edit the markers away directly in the result pane.

## Where ATE keeps things

Machine-shared state lives under `%APPDATA%/ADKOM/TextEditor/`: `Addons/` (your addons, one folder each), `Snippets.txt`, `Keys/` (your signing identity), `AddonConsent.json`, and `TrustedKeys.json`. Sessions, recent files, and most settings are stored per project.
