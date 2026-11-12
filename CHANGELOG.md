# Changelog

All notable changes to KeePassXC Card Print are documented **only in this file**.
Other documentation describes the current release and must not contain a version-by-version history.

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
