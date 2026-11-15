# Changelog

All notable changes to KeePassXC Card Print are documented **only in this file**.
Other documentation describes the current release and must not contain a version-by-version history.

## [7.0.3] - 2026-11-15

### Changed
- Arrange preview performance and update controls side by side as compact cards.
- Reflow both cards into stacked rows in narrow windows; present performance metrics in a two-by-two grid.
- Prevent cramped diagnostics controls and preserve vertical reachability without horizontal scrolling.

## [7.0.2] - 2026-11-15

### Fixed
- Refresh update and recovery controls immediately when the application language changes.
- Observe About view-model localization changes without requiring a tab switch; detach event handlers when the view leaves the visual tree or changes DataContext.

## [7.0.1] - 2026-11-15

### Fixed
- Wire automatic update preferences to Avalonia 12 `IsCheckedChanged` in code-behind,
  avoiding unsupported `Checked` / `Unchecked` XAML members during compilation.

## [7.0.0] - 2026-11-15

### Added
- Automatic GitHub Release checks (on startup and daily), optional downloads, and
  installation on exit, after confirmation, or manually in the About page.
- Explicit `--reinstall` recovery of the current immutable release, including
  user confirmation, verification, and a clean restart.
- GitHub Immutable Release attestation validation using pinned TUF trust roots,
  Sigstore/Fulcio identity, signed timestamps, tag identity and SHA-256.
- Recursive update of portable fonts, licenses and native runtime dependencies;
  per-installation ownership manifests and removal of unchanged obsolete files.
- Transactional installation with backups, rollback after failure, and recovery
  following interrupted installs. Private settings and databases remain untouched.
- Offline update policy, archive, nested-path, rollback and integrity self-tests.

## [6.2.5] - 2026-11-12

### Changed
- Use the selected cinematic hero collage on the README and GitHub Pages site.
- Synchronize the application and current-release documentation version with 6.2.5.

## [6.2.4] - 2026-11-12

### Changed
- Add a version-independent promotional collage and a demo-only screenshot gallery to the README.
- Add a GitHub Pages site with a dark desktop-inspired Cayman theme, overview, user guide, image gallery and privacy policy, served from `docs/`.
- Align current documentation examples and the application version with 6.2.4; application behavior is unchanged.

## [6.2.3] - 2026-11-12

### Fixed
- Localize context-menu and export-flyout commands as their popup visual roots open.
- Translate sheet-preview settings tooltips in their own flyout visual root.
- Translate each dynamic printer-driver diagnostic line, including duplex capability.
- Keep mapping-defined field captions, user content and card data unchanged.

## [6.2.2] - 2026-11-12

### Fixed
- Corrected a C# collection-initializer syntax error in the print dialog's
  localized calibration-axis labels. The pre-AOT managed build now receives a
  valid `string[]` initializer followed by LINQ translation.

## [6.2.1] - 2026-11-12

### Fixed
- Completed localization of print dialogs, print tracking,
  preview messages, workflow warnings, menus, tooltips and notifications.
- Localized appearance-editor parameters across Russian, Ukrainian and English.
- Updated language-dependent editor sections, choices and template labels without changing persisted configuration identifiers.
- Localized static XAML chrome, print-related dialogs and record status text without changing KDBX data.
- Kept the documentation version synchronized with the application version.

### Changed
- Introduced a single changelog as the authoritative place for release-to-release changes.
- Documentation examples and release procedures now describe version 6.2.1.
