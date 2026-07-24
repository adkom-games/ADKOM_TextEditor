# Changelog

All notable changes to this package are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com); versions follow semver.

## [Unreleased]

### Fixed
- Status bar no longer gets pushed off the bottom of the window when the
  loaded document is taller than the visible editor area.

### Added
- Multiple open files as tabs: New/Open create tabs, opening an
  already-open file switches to its tab, per-tab dirty guard on close
  (middle-click or × to close). Open tabs survive domain reloads.
- Project window context menu item **Assets → Open in ADKOM Text Editor**
  for `.cs` files; reuses the existing editor window when one is open.

## [0.1.0] - 2026-07-24

### Added
- Initial release: dockable UIToolkit text editor window (Tools → ADKOM → Text Editor).
- New / Open / Save / Save As with dirty-state guard dialogs.
- Ctrl+S / Ctrl+Shift+S shortcuts.
- External file-change detection with reload prompt.
- Word-wrap toggle; status bar (line:col, encoding, EOL).
- EOL and UTF-8 BOM preservation on save.
- `ITextFormatter` extension point (plain-text passthrough) and reserved
  line-number gutter for future releases.
