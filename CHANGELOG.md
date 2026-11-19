# Changelog

All notable changes to KeePassXC Card Print are documented **only in this file**.
Other documentation describes the current release and must not contain a version-by-version history.

## [8.0.8] - 2026-11-19

### Improved
- Upgrade KeePassXC.CloudTransport to 1.0.4 to display real byte-transfer progress for HTTP cloud providers during manual and background synchronization, including connection tests.
- Keep the thin, subdued progress bar. Show transferred/total payload bytes and percentages only when the transport supplies a known total; use non-animated stage text for Proton CLI and show actual final transferred bytes.
- Avoid stale progress from preceding uploads or downloads using per-operation transfer identities and phase/direction filtering. Retain RU/UK/EN labels and Native AOT self-test coverage.

## [8.0.7] - 2026-11-19

### Improved
- Replace the fast indeterminate cloud progress animation with a subtle,
  non-animated four-stage indicator for explicit tests and manual sync.
- Show localized stage numbers without inventing byte-transfer percentages.
- Suppress the progress bar when the operation has no measurable stage
  (background synchronization and interactive authorization).

## [8.0.6] - 2026-11-19

### Fixed
- Separate connection verification from configuration transfer: testing a cloud
  connection never opens a migration dialog or reads/writes configuration.zip.
- Serialize authentication, connection probes and archive synchronization with
  the same engine gate, guard against repeated UI commands and handle shutdown.
- Bind the sync journal to a cloud destination: rechecking the existing cloud
  resumes LWW without a migration prompt; changing the destination requires an
  explicit upload/download choice before any automatic write.
- Recognize 8.0.5 journals by their matching remote snapshot identifier, without
  silently adopting unrelated or missing cloud archives.
- Protect file-watcher, background sync and UI progress callbacks from uncaught
  exceptions. Log diagnostics locally without tokens or archive contents.

### Improved
- Show a visible, localized progress indicator with actual test phases and
  manual/background synchronization stages.
- Add Native AOT self-tests for destination binding and a mock connection probe
  that proves configuration.zip is not read or modified.

## [8.0.5] - 2026-11-18

### Fixed
- Diagnose incompatible `mapping.json` structures during cloud snapshot validation,
  including a format-tagged JSON envelope at `$.format`, instead of surfacing
  an unhelpful JSON-to-FieldRule conversion exception.
- Keep malformed mapping files untouched and refuse an unsafe upload or
  replacement while their format remains incompatible.
- Distinguish successful cloud connection verification from synchronization
  errors; localize the actionable mapping-format diagnostic in RU/UK/EN.
- Preflight portable configuration before a connection test on an active cloud
  profile can trigger further archive operations.
- Add Native AOT cloud self-test coverage for unsupported mapping envelopes.

## [8.0.4] - 2026-11-18

### Added
- Host-owned, branded Google OAuth callback pages matching Card Print theme and
  RU/UK/EN localization for successful and failed authorizations.
- Upgrade the pinned KeePassXC.CloudTransport dependency to 1.0.3 for external
  OAuth callback HTML support, without duplicating the OAuth loopback listener.

## [8.0.3] - 2026-11-18

### Added
- Preconfigured KeePassXC Card Print Google OAuth Desktop client with optional
  advanced override; OAuth tokens remain in Windows Credential Manager.
- Configurable cloud folder (default KeePassXC-Card-Print) across all providers,
  with path validation and separate read/write/delete connection preflight.
- Proton Drive CLI auto-discovery, official SHA-512 validated download/update,
  idle lease, transactional binary replacement, local rollback and daily checks.

### Fixed
- Reject unconfigured and unverified cloud storage transitions without crashing.
- Invalidate the verified connection after edits or authentication changes;
  suspend cloud sync until another successful verification.
- For an active cloud profile reconfiguration, require an explicit user choice
  before importing or replacing any pre-existing configuration.zip.

## [8.0.2] - 2026-11-18

### Fixed
- Replace obsolete Avalonia TextBox.Watermark with PlaceholderText on the
  Settings cloud endpoint field (AVLN5001).
- Make the existing About update-control refresh guard effective, removing
  the unused-field compiler warning (CS0414).
- Expose a public default XAML constructor for UpdateOperationWindow while
  retaining its explicit operation constructor (AVLN3001).

## [8.0.1] - 2026-11-18

### Fixed
- Move Windows Credential Manager P/Invoke, browser integration and Proton CLI
  process launching into platforms/windows; shared synchronization now obtains
  cloud host services through the platform abstraction.
- Restore the strict platform-boundary build check without weakening build.ps1.

## [8.0.0] - 2026-11-18

### Added
- Cloud configuration cache and one-archive Last Write Wins synchronization through
  pinned KeePassXC.CloudTransport 1.0.2 (Google Drive, Proton Drive, WebDAV, S3/R2).
- SHA-256 manifest, bounded extraction, strict allow-list, upload verification,
  transactional remote apply and rollback; local pending journal and offline retries.
- Synchronization of appearance, mappings, domains, templates, and print-tracking
  history without exporting machine-bound UI settings, local vault paths or secrets.
- Cloud-first storage selector, new right-anchored Settings tab and the application
  auto-update preferences moved out of About; RU/UK/EN captions.
- Native AOT executable self-test entry point `--self-test-cloud`.

## [7.1.1] - 2026-11-15

### Fixed
- Localized, branded title-bar captions for all update, repair and result windows instead of Avalonia's default "Window".
- Foreground activation for the relaunched application after updates, repairs and rollbacks, including handoff from a 7.1.0 helper; no permanent always-on-top state.

## [7.1.0] - 2026-11-15

### Added
- Unified Avalonia progress dialogs for manual release checks, update preparation and recovery.
- A standalone Avalonia progress window in the temporary updater, with live file-level progress and rollback status.
- Per-installation, one-time results displayed after the updated application successfully starts.
- Compatibility notice on the first successful upgrade from a 7.0.x updater.
- Visual feedback for automatic on-exit installations without interrupting background downloads.

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
