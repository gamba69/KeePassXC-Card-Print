# Changelog

## [9.1.7] - 2026-11-22

### Fixed
- Make update, recovery and post-restart result dialogs size to their actual content, matching Cloud Sync instead of reserving a fixed 230-DIP height.
- Collapse empty progress details and the hidden helper action button so the temporary updater stays compact.
- Use Fluent graphical icons with the localized Cancel/Close button labels and preserve the theme-aware Card Print branding.

## [9.1.6] - 2026-11-22

### Changed
- Compress the About performance panel: place all four metrics in a single row, with the diagnostics switch and Copy action on the heading row when space permits.
- Reserve 2x2 metrics and stacked actions for genuinely narrow windows, avoiding the premature 1020-DIP breakpoint and unnecessary vertical whitespace.
- Preserve the existing measurements, diagnostics behavior, update controls and technical information sections.

## [9.1.5] - 2026-11-22

### Changed
- Make the About page update panel the first full-width card, with status on the left and the check/install-or-repair actions aligned on the right.
- Place the full-width preview performance card second; arrange four metrics in a compact row and the diagnostics actions at the right.
- Keep Components & Versions, Graphics & Environment, and Printing & Export below in their original layout, with responsive behavior for narrow windows.
- Keep the safe diagnostic-copy hint as a tooltip and retain visible feedback after copying.

## [9.1.4] - 2026-11-21

### Fixed
- Pin patched .NET 10 SDK selection with a root global.json (latestFeature within .NET 10.0), preventing Windows GitHub runners with preinstalled .NET 11 from overriding the installed .NET 10 SDK.
- Verify the resolved SDK in both Windows CI and draft-release workflows immediately after setup-dotnet, before Native AOT compilation.
- Add an explicit SDK-selection regression guard while retaining the existing patched Native AOT compiler security checks.

## [9.1.3] - 2026-11-21

### Fixed
- Fix the Unicode showcase layout self-test: shape each printable line after the same hard-break/wrapping step used by card rendering, not a raw multiline string.
- Preserve missing-glyph checks for all printable Cyrillic, Latin and Greek characters, with separate title/notes/description diagnostics and actual print fonts.

## [9.1.2] - 2026-11-21

### Fixed
- Curate the showcase corpus: security extras belong only to relevant demo records, not to every third/fourth entry.
- Replace unsupported CJK/Arabic characters in the Unicode showcase with deliberate Cyrillic, Latin and Greek sample text; assert no missing glyphs in the printing font.
- Prevent two horizontal separators at a section boundary and extra dividers within wrapped values in the QR/recovery design.
- Give QR template titles the full header width rather than forcing multi-line titles next to a group badge.
- Start with ten intentionally composed example cards; keep all 40 records and specialized stress cases available for selection.
- Extend Native AOT layout self-tests for field boundaries, glyph coverage and well-scoped security samples.

## [9.1.1] - 2026-11-21

### Fixed
- Reduce overly large title, label and value fonts across the three built-in showcase templates, especially the QR/security template.
- Reduce maximum recovery-code font sizes and compact note typography without altering field data, QR geometry or the editable personal templates.
- Match the native layout smoke test to practical print-size typography bounds.

## [9.1.0] - 2026-11-21

### Changed
- Replace the five shallow built-in appearance styles with three complete, read-only showcase print templates (editorial archive, technical recovery, QR/security), each with distinct front, reverse, geometry, typography, and field mapping.
- Keep old classic/minimal style codes loadable by existing personal templates; do not introduce a configuration migration.
- Expand the offline demo vault to 40 synthetic records showing TOTP/HOTP, BitLocker, multi-column recovery codes, long multiline values, backup URLs, phone logins, Unicode, optional fields, and varied icons.
- Seed printed, unprinted, and partially printed demo records plus sample history strictly in the in-memory tracking ledger, without touching the real print-tracking folder.

### Tested
- Extend the published executable's layout smoke checks to validate the three preset maps, representative paginated card layouts, and QR rendering.

## [9.0.4] - 2026-11-21

### Fixed
- Remove the redundant mapping scope explanation from the field list above the table.
- Suppress false field-validation errors while viewing read-only built-in templates; keep numeric validation for editable personal templates.
- Add mapping self-tests for read-only and editable validation states.

## [9.0.3] - 2026-11-21

### Fixed
- Use the unified unsaved-template confirmation handler before switching configuration storage, fixing CS0103 from the incomplete 9.0.0 refactor.
- Detect stale references to the removed mapping-only confirmation handler in the pre-build confirmation policy regression check.

## [9.0.2] - 2026-11-21

### Fixed
- Update the typography regression checks for the unified template manager in Mapping, Appearance and Duplex.
- Require the shared `UiTemplate` heading to retain its semantic style in all three tabs, without weakening the 14-DIP heading contract.

## [9.0.1] - 2026-11-21

### Fixed
- Update the build-time confirmation policy check for the unified template unsaved-changes handler.
- Verify that unsaved template state and whole-template Save remain present without weakening file-output, virtual-printer or danger-action checks.

## [9.0.0] - 2026-11-21

### Changed
- One complete print template now owns both card faces and the entire field mapping; no independent mapping.json persistence.
- Mapping, Appearance and Duplex share the same template selection, Create and full-template Save toolbar.
- Built-in templates are completely read-only, including field mapping and per-field reset controls.
- Move mapping view options above the field table and the service subdomains button to the table card's top right.
- Cloud archives validate mapping embedded in templates; detached mapping.json is no longer portable.
- Explicit cloud upload can replace an older incompatible remote archive; automatic synchronization remains strict.
- A template's unsaved changes are tracked as one operation and must be saved together.

### Tests
- Exercise embedded mapping parsing, built-in mapping read-only state, cloud validation, and unified template UI contracts.

## [8.0.32] - 2026-11-20

### Fixed
- Use the common Avalonia Window type when choosing the application or updater window; correct CS0173 during managed/AOT compilation.
- Guard the Windows-only EventWaitHandle.OpenExisting call with OperatingSystem.IsWindows to satisfy CA1416 without disabling platform warnings.

## [8.0.31] - 2026-11-20

### Fixed
- Correct the single-instance startup regression check to match the actual main window assignment and activation attachment.
- Keep the mutex, activation signal and worker/self-test isolation checks unchanged.

## [8.0.30] - 2026-11-20

### Changed
- Increase spacing between the Theme/Language selectors and the following header labels without changing selector widths.
- Reduce only the right-side header padding from 24 to 12 DIP while preserving the 24-DIP left margin.

## [8.0.29] - 2026-11-20

### Added
- Enforce a single interactive window per Windows logon session using a named kernel mutex.
- A second launch signals the existing process, restores a minimized window and requests foreground activation instead of starting a second window.
- Keep update/recovery/reset workers and CLI self-tests independent of the UI instance gate.
- Add a build regression guard for singleton startup and existing-window activation.

## [8.0.28] - 2026-11-20

### Fixed
- Use the same 14-DIP text size for header labels, selected values and expanded items in Theme, Language and Storage selectors.
- Add a build regression check for header dropdown typography in all three selectors.

## [8.0.27] - 2026-11-20

### Changed
- Match the Cloud Sync header: 48-DIP DPI-aware icon, 23-DIP application title, 12-DIP subtitle, and 76-DIP header.
- Match the Cloud Sync theme/language selectors (132/142 DIP wide, 34 DIP high) and typography; keep the Card Print storage selector in the same visual family.
- Update the build-time header/icon regression checks for the new dimensions.

## [8.0.26] - 2026-11-20

### Fixed
- Give the service-subdomain list a theme-aware input background instead of the default Fluent graphite color.
- Theme the list items, hover states, and multi-selection using the Card Print light/dark palette without affecting other lists.

## [8.0.25] - 2026-11-20

### Fixed
- Remove the 152-DIP minimum width from the mapping Save button; it now sizes to its icon and short label.

## [8.0.24] - 2026-11-20

### Fixed
- Correct the mapping toolbar build preflight XPath: `Grid.Column` is a single XML attribute name, not an XML namespace-qualified `Column` attribute.
- Verify that the scope description occupies column 2 and is right-aligned next to the single Save button.

## [8.0.23] - 2026-11-20

### Changed
- Selecting a template in either design editor now loads it immediately; remove the separate Load button.
- Keep Create next to the picker and Save at the right edge; the Create button now has a short caption.
- Prompt before discarding unsaved template edits, restoring the active selection on cancellation or load failure.
- Right-align the mapping scope description beside Save; hide the redundant Saved status and shorten Save in all three languages.
- Update UI build preflight rules to validate the new template selector behavior and compact mapping toolbar.

## [8.0.22] - 2026-11-20

### Fixed
- Update the mapping toolbar UI preflight for the new four-column layout, preserving single Save action, mapping reset and no-Apply checks.

## [8.0.21] - 2026-11-20

### Changed
- Align the YubiKey panel background with the surrounding storage card.
- Compress template management in both design editors to two lines: template selection, Create…, Load and Save, followed by a single status line.
- Create personal templates in a named modal with Create and Cancel; successfully created templates become active immediately.
- Put mapping scope information on the title row and shorten Save labels in Russian, Ukrainian and English.

## [8.0.20] - 2026-11-20

### Fixed
- Prevent unhandled dispatcher exceptions during storage switching, even when callbacks, status text, or UI restoration throw.
- Isolate storage operation subscribers so one faulty listener cannot terminate the process or suppress other listeners.
- Guard the Settings tab async selection handler and its final UI refresh independently.
- Add Native AOT self-test coverage for throwing storage observers and UI recovery operations.

## [8.0.19] - 2026-11-20

### Fixed
- Permanent backups are complete verified snapshots of all application configuration roots, never per-operation fragments.
- Backups use chronological date/time names, retain only the three newest verified snapshots, and discard legacy partial or corrupt backup folders at startup.
- Storage switching, cloud application and mapping recovery now use full backups; transient rollback data is always removed after the operation.
- Full reset seals a verified multi-root backup before remote replacement or local deletion and preserves print-tracking as an indivisible directory unless explicitly cleared.

## [8.0.18] - 2026-11-20

### Changed
- Full settings reset now covers system, portable, and cloud-cache configuration, replaces the remote cloud archive with a verified clean snapshot, and restores default settings.
- Treat print-tracking as one atomic folder: keep all history and statuses unless explicitly requested to delete the entire folder everywhere.
- Clean obsolete and unknown entries from application-owned configuration roots on startup; preserve only current-format configuration, templates, and print-tracking.
- Use dated backup folders (YYYY-MM-DD_HH-mm-ss-fff), retaining at most three on startup and after successful reset.
- Remove obsolete cloud verification and destination-journal compatibility fallbacks; missing storage mode now defaults to system storage.
- Clear Card Print's Windows Credential Manager entries after successful reset, without modifying KeePassXC databases or key files.

All notable changes to KeePassXC Card Print are documented **only in this file**.
Other documentation describes the current release and must not contain a version-by-version history.

## [8.0.17] - 2026-11-20

### Fixed
- Initialize the reset-settings dialog using generated Avalonia XAML initialization so named controls are bound before the constructor accesses them, preventing NullReferenceException and allowing the confirmation dialog to open.

## [8.0.16] - 2026-11-20

### Fixed
- Replace reflection-based ComboBox item bindings with typed compiled bindings compatible with Native AOT.
- Preserve disabled cloud-storage choices in both storage selectors without using reflection.

## [8.0.15] - 2026-11-20

### Fixed
- Route storage choices from both selectors through the same transition method; the header now reflects the persisted active storage instead of a pending selection.
- Disable the cloud option until the saved connection is verified and the configuration mapping is compatible; show the reason in Settings.
- Separate active storage, connection verification, synchronization and the latest operation message.
- Clear stale operation errors after a successful attempt; retain full diagnostics in the local log.
- Show reset-button feedback beside the button, including launch failures, instead of silently recording an error in cloud status.

## [8.0.14] - 2026-11-20

### Fixed
- Guard calls to the Windows-only settings reset worker and launcher so platform compatibility analysis (CA1416) succeeds without suppressing warnings.

## [8.0.13] - 2026-11-20

### Added
- Add an explicit Settings > Maintenance reset dialog with current/all-local storage scope and a separate, unchecked-by-default option to delete print history and printed-card tracking.
- Create a checksum-verified local backup of each affected file before deletion. Keep KeePassXC databases, key files, the remote cloud archive, stored Windows credentials and unrelated application data untouched.
- Run the reset after the original application exits, then restart in the appropriate storage mode; report the result and backup directory on first startup.
- Add isolated reset tests for print-history preservation, explicit history clearing, backup verification and unrelated-file protection.

### Improved
- Fix the unhandled managed NullReferenceException reported from SettingsView.OnStorageChanged by removing async-void event continuations and containing failures in the storage-selection/modal UI path; also capture switch and remote-apply exceptions in local diagnostics.

## [8.0.12] - 2026-11-19

### Fixed
- Detect incompatible field mappings at the active configuration source, not just in the cloud cache; show the affected path and recovery action even before cloud storage is enabled.
- Before switching to cloud storage, reject invalid source mappings with an actionable Settings dialog rather than copying them into the cache and failing the upload.
- Serialize and back up recovery of both the active source and a damaged cloud cache, retaining every original mapping separately and requiring manual review before cloud synchronization resumes.
- Disable manual cloud synchronization while another storage mode is selected; present an inactive status instead of a stale cloud-sync error, and clear the obsolete status after repair.
- Add mapping source selection and dual-root recovery regression tests. No automatic mapping reset or cloud overwrite.

## [8.0.11] - 2026-11-19

### Fixed
- Add explicit, loss-preserving mapping recovery for incompatible cloud-cache mapping.json files; restore the most recent valid saved rules from local/system/backup storage when available.
- Always save the original invalid mapping outside the synchronized cloud cache before replacing it. A reset to empty rules requires explicit warning and confirmation.
- Pause automatic synchronization across restarts until the recovered mapping is reviewed and manually synchronized. Serialize recovery with active cloud operations.
- Localize the recovery UI and add native cloud self-tests for backup, restoration, reset and review marker.

## [8.0.10] - 2026-11-19

### Improved
- Replace ambiguous Yes/No storage-selection dialogue with two explicit transfer directions, showing source and destination paths, overwrite consequences and action-specific buttons.
- Show the configured cloud provider and remote `configuration.zip` path, distinguish the local cloud cache from the remote copy, and explain when nothing is copied.
- Use the same decision layout when selecting a new cloud destination; keep original data-movement semantics and RU/UK/EN localization.
- Add non-UI cloud self-tests for dialog choices across cloud, system, local and destination-change flows.

## [8.0.9] - 2026-11-19

### Fixed
- Show a single S3 provider choice for Amazon S3, Cloudflare R2 and compatible endpoints, without modifying existing R2 credentials or remote destination fingerprints.
- Preserve cloud connection verification across restarts, harmless UI refreshes and Proton CLI update-check preference changes; accept and migrate 8.0.8 verification markers.
- Stop invalidating verified access when a user merely edits a draft connection field; invalidate only after saving changed connection parameters or credentials.
- Resume cloud synchronization if an unsaved connection edit is reverted, and show a distinct warning for unverified draft changes.
- Extend Native AOT cloud self-tests for R2 compatibility and verification fingerprint stability.

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
