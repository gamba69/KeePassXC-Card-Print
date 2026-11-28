# Changelog

## [11.4.3] - 2026-11-28

- Keep diagnostic card rows complete at responsive widths, add a scan-time card, and align measurement groups on a shared compact grid. Retain hidden secondary metrics and provide an all-metrics view.

## [11.4.2] - 2026-11-28

- Broaden ONVIF discovery with typed and untyped WS-Discovery probes in both dialects, explicit reply addressing and actionable no-response feedback.

## [11.4.1] - 2026-11-28

- Construct the synthetic ONVIF credential-bearing URL at runtime in the rejection self-test so repository privacy validation remains enabled.

## [11.4.0] - 2026-11-28

- Add ONVIF discovery, device authentication and media profile selection, independent of the existing RTSP/HTTP frame readers.
- Add capability-driven ONVIF imaging, encoder settings, focus and PTZ controls; keep ONVIF credentials in the device-local DPAPI camera profile.
- Rename the scanner tab to Scan / Сканирование / Сканування.

## [11.3.9] - 2026-11-27

- Keep the scanner connection indicator and layout stable during automatic network camera retries; avoid transient error states and repeated diagnostic resets while preserving terminal errors and retry counts.

## [11.3.8] - 2026-11-27

- Align the decoder selector and diagnostics icon, expose recognition and local camera controls in compact wrapping rows, remove duplicate resolution from measurement statistics, center the fixed-initial-size decoder diagnostics window on the screen, and use consistent single-word search mode names.

## [11.3.7] - 2026-11-27

- Replace the scanner transport warning line with a compact amber Fluent warning icon beside the stream format; keep protection details in localized tooltips.

## [11.3.6] - 2026-11-27

- Compact the scanner connection summary and move decoder diagnostics beside the selector. Show only applicable measurements (including zero values), retain history, and use independent responsive table groups. Show camera video options directly in a screen-bounded scrolling form with a fixed footer.

## [11.3.5] - 2026-11-27

- Use the shared table palette for camera management and compact video decoder diagnostics, placing results before the summary in a scrolling body. Save decoder choices immediately and restart active connections only after a successful change. Display useful connection details inline instead of a redundant collapsible section. Use compact icon-only image and camera-management toolbars with localized tooltips and accessible labels.

## [11.3.4] - 2026-11-27

- Preserve device-local scanner profiles, selected camera, decoder probe cache and optional components during startup housekeeping. Keep explicit full reset destructive and add isolated persistence regressions, including DPAPI save/load on Windows.

## [11.3.3] - 2026-11-27

- Filter video decoder choices by probe results and stream codec; keep the saved choice unchanged until explicitly applied. Reflow the connection card and measurements into responsive columns and report whole-frame detail without a misleading focus grade. Preserve consent for unchanged network endpoints, theme the camera editor sections, and constrain secondary windows to the monitor working area with scrolling for simple forms.

## [11.3.2] - 2026-11-27

- Compact the scanner sidebar, use theme-consistent full-width collapsible sections, label connection abort explicitly, show seven live quality cards and measurements without opening an expander, and reflow the statistics into two tables only when the sidebar is wide enough.

## [11.3.1] - 2026-11-27

- Separate Camera, File and Clipboard input modes. Show only the actions for the selected mode, move camera management beside the device selector, and keep Clear with the static image tools. Successful drop/paste selects the corresponding input; cancelled or failed imports preserve the current source.

## [11.3.0] - 2026-11-27

- Rebuild the scanner workspace around one camera selector and explicit Start/Stop/Cancel/Retry actions. Separate camera management, profile editing and tests; provide source-specific settings, live decoder selection, compact status and optional diagnostics. Preserve the current source when imports are cancelled or unreadable, and test the unified workspace state model.

## [11.2.5] - 2026-11-27

- Remove intermediate PNG encoding and decoding from network video, live preview, burst analysis and freeze. Share owned BGRA pixels across scanner workers; use uncompressed framed BGRA for FFmpeg and reuse the preview framebuffer. Rank automatic decoder selection by measured output speed and stability, without GPU preference. Make named decoder selection strict by default with explicit fallback, refresh probe results, and expose separate decoding timing.

## [11.2.4] - 2026-11-27

- Show incremental decoder-test progress, current GPU and codec, elapsed time, completed cells and cooperative cancellation. Distinguish missing runtime libraries from an unavailable GPU session. Add localized next-step guidance and official driver/HEVC links, plus a summary of verified working formats.

## [11.2.3] - 2026-11-27

- Restore diagnostic report clipboard copying by importing the Avalonia clipboard extensions. Replace deprecated network camera text-box Watermark properties with PlaceholderText.

## [11.2.2] - 2026-11-27

- Add independent NVIDIA NVDEC, Intel oneVPL / legacy Media SDK and AMD AMF hardware decoding adapters, dynamically loaded and tested per compatible DXGI GPU. Keep transport, TLS and software fallback independent.
- Package the pinned native bridge and oneVPL dispatcher with upstream notices; show direct SDK status separately from Media Foundation and retain raw driver diagnostics in report.

## [11.2.1] - 2026-11-27

- Query D3D11 driver decode profiles and 1080p configurations independently of MFT registration, and try D3D11-aware system transforms when adapter-specific hardware MFTs are absent. Require decoder-bound GPU surfaces for hardware selection.
- Distinguish unsupported profiles, missing codec components, initialization failures, unverified GPU output and decoding failures; compact diagnostic table with tooltips, selected-cell detail and report copying. Direct vendor SDKs remain out of scope.

## [11.2.0] - 2026-11-27

- Normalize SharpRTSP Annex B NALs exactly once, retain in-band parameter sets and restart on random-access pictures after damaged HEVC input. Feed complete NALs to libde265 with native error descriptions.
- Add per-DXGI-adapter Media Foundation hardware selection, separate software mode, verified decoder probes, local capability cache and profile-level manual/fallback policy. Direct vendor SDK adapters are not included.
- Add decoder diagnostics to network cameras and settings; keep the optional FFmpeg engine and all TLS restrictions unchanged.

## [11.1.5] - 2026-11-27

- Bundle a replaceable libde265 1.1.3 software HEVC decoder with pinned source, LGPL notices and deterministic build recipe. Fall back on a random-access frame without changing RTSP/TLS.
- Negotiate Main/Main10 and NV12/P010, try event-driven system transforms and D3D11 surface readback, report decoder inventory and stage-specific HRESULT.
- Add synthetic HEVC Main/Main10 decoder tests; FFmpeg remains optional.

## [11.1.4] - 2026-11-27

- Request low-latency Media Foundation decoding, mark initial discontinuity and random-access pictures, honor output alignment, and check streaming setup.
- Replace recycled synthetic H.264 pictures with a complete predictive test stream; verify live output dimensions, changing image content and report native HRESULT and frame counts on failure.

## [11.1.3] - 2026-11-27

- Align ProtectedData with the minimum required by SharpRTSP dependencies to resolve NuGet downgrade error NU1605 without suppressing dependency validation.

## [11.1.2] - 2026-11-27

- Isolate Media Foundation native decoder imports in the Windows scanning host layer; retain the platform boundary guard.

## [11.1.1] - 2026-11-27

- Construct the synthetic credential-bearing camera URL at runtime to preserve the repository privacy check.

## [11.1.0] - 2026-11-27

- Add network camera profiles and HTTP(S) JPEG/MJPEG and RTSP(S) TCP frame sources.
- Use Windows DPAPI for device-local profiles; require explicit plaintext permission and validate TLS.
- Add connection testing, reconnection, certificate controls and rolling network telemetry.
- Default to SharpRTSPClient and native Windows Media Foundation for RTSP video; choose a decoder per profile.
- Download, update, reinstall or remove the pinned optional FFmpeg component on demand; no bundled FFmpeg.

## [11.0.5] - 2026-11-27

- Reorganize scanner into a clean source viewport and resizable settings/diagnostics sidebar.
- Add live quality cards, fixed-scale trends, a striped current/P05/P95 measurement table and bounded numeric telemetry.
- Estimate QR geometry and region quality in original pixels; add interactive search regions and finer overlapping crops.
- Extend synthetic scanner tests with percentile expiry, manual ROI, geometry and clean/blurred density sweep.

## [11.0.4] - 2026-11-26

- Add live relative sharpness, exposure/lighting, contrast and inter-frame change diagnostics with actionable hints.
- Select the best recent camera frame from bounded bursts, freeze and run maximum-sensitivity recognition, and resume without stale results.
- Add optional Windows KS focus/exposure capability probing and explicit auto/manual controls when supported; retain digital preview zoom.
- Extend scanner self-tests with synthetic quality measurements.

## [11.0.3] - 2026-11-26

- Add bounded, adaptive multi-pass QR recognition with contrast, sharpening, local/global thresholds, inversion, overlapping regions and resampling.
- Prefer 1080p camera capture, separate preview and recognition workers, and report real recognition stages and measured image diagnostics below the viewport.
- Add three sensitivity levels, static-source retry, higher-resolution PDF retry and synthetic recognition regression tests.

## [11.0.2] - 2026-11-26

- Resolve the conflict between System.FormatException and ZXing.FormatException in scanner validation.
- Use the current Avalonia PNG encoder and SkiaSharp bitmap sampling APIs.

## [11.0.1] - 2026-11-26

- Add a full-width QR Scanner workspace with camera, image, PDF, clipboard and file drop sources.
- Detect eligible TOTP and Passkey payloads and optionally other QR types; show dedicated result dialogs.
- Retain clickable QR annotations on static images and PDF pages, including during zoom and rotation.
- Pause live recognition during result dialogs and suppress duplicate automatic openings.
- Keep scanned data in memory only; provide confirmed file export and timed clipboard clearing.
- Add scanner dependency version reporting and a Native AOT scanner/PDF self-test.

## [10.6.1] - 2026-11-26

- Use an identifier symbol for UUID and a single-document icon for the front background, paired with the existing double-document back icon.

## [10.6.0] - 2026-11-26

- Add semantic Fluent Regular icons to all main-tab card headings and all template parameter groups.
- Use shared compact heading layout, consistent 16-DIP glyphs and theme text color. Preserve existing navigation, actions, and card geometry.
- Extend preflight and group coverage tests for heading icon consistency.

## [10.5.9] - 2026-11-26

- Remove the redundant Output group column from both field mapping tables; retain Section and align the remaining columns.
- Reduce mapping table minimum widths and verify header/row column contracts during preflight.

## [10.5.8] - 2026-11-26

- Unify Settings and About page backgrounds with the other main tabs, using the shared card surface only for individual sections.
- Place About identity, wrapped metadata, repository link and description inside a standard card; normalize outer spacing.

## [10.5.7] - 2026-11-26

- Update the template preflight to require the dictionary-aware restriction help control introduced in 10.5.6.

## [10.5.6] - 2026-11-26

- Unify compact content margins across all main tabs, retaining only necessary scrollbar clearance.
- Wrap About metadata as complete segments, suppressing separators at line breaks.
- Reuse bundled language patterns and the existing prose line breaker for About and template help text.

## [10.5.5] - 2026-11-26

- Restore semantic section-heading typography for both collapsible headers without changing their shared interaction style.

## [10.5.4] - 2026-11-26

- Unify the source connection header with the mapping restrictions toggle; preserve the Fluent heading icons.

## [10.5.3] - 2026-11-25

- Restore the source card heading without a full-width selected-state background; use a compact chevron on the right.
- Add matching Fluent icons to the source and print-records headings.

## [10.5.2] - 2026-11-25

- Match the source connection header and content to the application palette; remove the nested Fluent expander frame.
- Reserve right-side content clearance and allow the YubiKey options to wrap in narrow panes.

## [10.5.1] - 2026-11-25

- Slim down existing scrollbars in both themes, including the preview.
- Draw existing panel splitters as 2-DIP lines while retaining their full drag hit area.

## [10.5.0] - 2026-11-25

- Rework source and mapping layout for small viewports: independent bounded scroll areas, collapsible connection and print restrictions, and a persistent mapping splitter.
- Keep the preview toolbar accessible with automatic narrow-pane docking without overwriting the saved preference.
- Bound print and confirmation dialogs to the monitor work area; retain fixed action footers.
- Update layout preflight and add adaptive viewport regression checks.

## [10.4.6] - 2026-11-25

### Fixed
- Group preview commands with compact 2-DIP spacing and 8-DIP spacing between groups in both toolbar docking positions. Hide the entire face selection group when duplex is unavailable.

## [10.4.5] - 2026-11-25

### Fixed
- Fit legacy OTP QR preferences against the complete measured composition on an empty card side, retaining the safe module pitch and configured minimum. Explicit mapped QR sizes remain strict.
- Preserve full OTP captions, grouped secret text and metadata when the preferred inline layout cannot fit. Add regression coverage for the large-QR fallback and constrained geometry; report safe worker failure categories in layout self-tests.

## [10.4.4] - 2026-11-25

### Fixed
- Add contextual help for every new header information setting in Russian, Ukrainian and English; keep the existing localization contract checks intact.

## [10.4.3] - 2026-11-25

### Fixed
- Synchronize the appearance registry and Native AOT contract tests with all 193 appearance properties after adding the header authentication settings.

## [10.4.2] - 2026-11-25

### Added
- Independent header page-number and authentication indicators (TOTP/HOTP/PASSKEY), with single-line deduplicated authentication labels.
- Shared information-column geometry, vertical and horizontal alignment, spacing, separate typography and optional backgrounds; preview hit regions follow actual visible rows.
- Header geometry regression checks for hidden indicators, repeated credential types, alignment and narrow layouts.

## [10.4.1] - 2026-11-25

### Fixed
- Rename the database closing action from Clear to Close database in Russian, Ukrainian and English.

## [10.4.0] - 2026-11-25

### Added
- Content-aware pagination with indivisible QR/OTP compositions and recovery-code rows, and optional whole-field placement.
- Per-field directional links to the next printed field or a named mapping field; overlapping links are considered together without reordering content.
- Per-field forced side breaks and localized mapping controls. Existing custom mappings default to automatic pagination and no links.
- Built-in mappings link login to password and explicitly enabled passkey attributes to the passkey export QR.
- Synthetic pagination and mapping regression tests.

### Changed
- The paginator prefers intact linked blocks, relaxes oversized links at legal boundaries, and accounts for repeated captions before selecting a break.
- QR blocks are no longer shrunk to fill leftover space; impossible indivisible blocks report the affected field without printing its value.

## [10.3.3] - 2026-11-25

### Fixed
- Recognize the expected oversized-OTP-QR rejection when parallel card layout wraps the error in AggregateException; the self-test continues to reject unrelated errors. QR layout and output are unchanged.

## [10.3.2] - 2026-11-25

### Changed
- TOTP/HOTP QR encoding now accepts per-field mapping settings: correction L/M/Q/H, module pitch, physical size and position. Left/right OTP text layout, grouping and labels remain intact.
- Legacy template QR behavior is unchanged until that OTP field QR setting is edited; mapping inherits its visible values.
- Legacy QR geometry controls remain stored for compatibility but are hidden from the Appearance editor.

### Tested
- Synthetic OTP regression cases cover legacy layout, per-field size and error correction, QR-only and text-only modes, and minimum-size rejection.

## [10.3.1] - 2026-11-25

### Fixed
- Avoid a false positive in the repository privacy guard from PKCS#8 PEM delimiter literals used by the passkey validator. Runtime validation remains unchanged.

## [10.3.0] - 2026-11-25

### Added
- QR output per mapped field: text, QR, or both; ECC L/M/Q/H, left/center/right alignment, 0.35/0.40/0.50 mm minimum module, and optional manual size.
- In-memory KeePassXC passkey JSON export field for paper recovery using native KPEX_PASSKEY_* attributes.
- Strict QR physical-size validation: no silent shrinking; block layouts that cannot fit the required module pitch.

### Compatibility
- Existing OTP QR/text positioning, grouping, typography, ECC M, and pagination are unchanged for old templates.
- Existing templates default to text for ordinary fields. Raw passkey secrets are hidden unless explicitly enabled in mapping.

## [10.2.7] - 2026-11-25

### Added
- Independent per-field word hyphenation and language overrides for notes and multiline prose. Inherit, enable or disable hyphenation; inherit, automatically detect or explicitly select an installed language. Preserve overrides in drafts, template JSON, snapshots and cloud sync; refresh language choices after package changes without discarding edits. Technical fields retain their original rendering.
- Regression checks for override precedence, old and new template JSON, mapping drafts, future language codes and localized choices.

## [10.2.6] - 2026-11-25

### Fixed
- Start both mapping views and the installed hyphenation package table with the normal row background; apply zebra striping to the second and every subsequent even data row, independently of the header. Preserve hover and selection styling.

## [10.2.5] - 2026-11-25

### Changed
- Match the installed hyphenation package table to the mapping table: one continuous header, compact rows, and alternating theme-aware row colors matching Cloud Sync. Apply the same striping to both mapping views.
- Show only relevant per-package actions: restore or update, and remove or reset to bundled when applicable. Hide unavailable bulk actions and retain sorting and tooltips. Display author-provided rule versions verbatim and identify upstream revision separately.

## [10.2.4] - 2026-11-25

### Fixed
- Use Avalonia 12 TextBox.PlaceholderText for the hyphenation language search, eliminating compiler warning CS0618.

## [10.2.3] - 2026-11-25

### Added
- Sortable installed hyphenation package table with source, version, update state, size, metadata and per-language actions; searchable installation picker and optional daily update checks. Compare actual upstream blobs, not unrelated repository revisions.
- Per-field alignment override for notes and multiline text, with inherited defaults, draft persistence, localized editor and regression checks.

## [10.2.2] - 2026-11-25

### Fixed
- Assign explicit unique manifest resource names to all eight bundled hyphenation files, preventing CS1508 and excluding unexpected files from the build. Verify the complete resource-name contract in the published executable self-tests.

## [10.2.1] - 2026-11-25

### Fixed
- Discover language packages from the upstream TeX file tree instead of restricting installation to a fixed list. Cache the catalog for offline use and refresh it on demand. Accept valid future and retired language codes in portable configuration; use per-language typesetting minima from the verified source.

## [10.2.0] - 2026-11-25

### Added
- Left, centered, right and justified alignment for notes and multiline prose, with paragraph-aware word hyphenation and shared preview/print geometry. Technical values keep their existing rendering.
- Meziantou Unicode hyphenation engine; Russian, Ukrainian and American English rules embedded from an immutable TeX Hyphenation revision. All discovered upstream language packages are available on demand; the offline catalog snapshot is refreshed dynamically.
- Local package install, update, restore and reset; portable configuration pins exact revisions and SHA-256 file hashes. Cloud configuration restores missing packages from the fixed upstream repository.
- Offline layout and bundled-package regression checks.

## [10.1.3] - 2026-11-24

### Added
- Independent entry icon processing and coloring: shared scheme policy and per-template override with original, grayscale, palette role or individual RGB. Monochrome schemes now recolor raster entry icons; warm and technical schemes use the accent. Preserve alpha and tonal detail, existing templates, explicit overrides and QR contrast.
- Regression tests for icon coloring, inheritance, persistence, localization and actual bitmap pixels.

## [10.1.2] - 2026-11-24

### Changed
- Enable Ctrl-hover and Ctrl-click preview navigation from every main tab.
- Clear fixed preview highlights with Escape, a plain preview click, a repeated Ctrl-click, an empty-area Ctrl-click on the page, a preview mode change or a main tab change. Clearing does not modify template data or reset the chosen section.

## [10.1.1] - 2026-11-24

### Fixed
- Resolve a C# local-variable name collision in the palette color-source dialog (CS0136). Preserve the color-source selector and RGB channel captions.

## [10.1.0] - 2026-11-24

### Added
- Seven shared template palette roles, four ready-to-use color schemes, and individual element colors. Choose a palette role, the original default or an individual RGB/HEX value directly in the element editor. Palette edits preserve literal overrides; old template colors and the original QR contrast are retained.
- Ctrl-hover outlines a single preview element, Ctrl-click fixes its selection and opens the corresponding editor group. Ordinary dragging still pans the preview. A compact, non-interactive viewport label identifies the hovered or fixed element without adding a toolbar row.
- Regression checks for color references, legacy JSON, source validation, ownership, palette and preview gestures.

## [10.0.15] - 2026-11-24

### Fixed
- Reduce decorative emblem picker previews from 24 to 16 DIP to fit the compact 26-DIP selection control without clipping. Keep the printed emblem size, vector artwork and template settings unchanged. Guard the picker glyph dimensions in the build checks.

## [10.0.14] - 2026-11-24

### Fixed
- Restore the template selector and all four template actions to one horizontal row, with Save anchored on the right and the status notice below. Allow horizontal scrolling only when the editor is too narrow to fit the toolbar.

## [10.0.13] - 2026-11-24

### Fixed
- Restore 6 DIP of space between the automatic-size checkbox glyph and its caption in all three automatic numeric editors, without changing row height or spacing for other fields. Guard the caption padding in the build checks.

## [10.0.12] - 2026-11-24

### Fixed
- Use natural caption heights and bottom-aligned 26-DIP editor rows in the template editor. Single-line captions no longer reserve an empty second line; mixed-height captions keep their input controls aligned. Reduce unused horizontal space for short numeric and checkbox labels.

## [10.0.11] - 2026-11-24

### Fixed
- Align all template editor captions and controls using uniform two-line label and editor bands. Place automatic size toggles alongside numeric editors and preserve compact field widths. Add a build-time layout regression check.

## [10.0.10] - 2026-11-24

### Fixed
- Give every template navigation section a distinct Fluent Regular icon. Use dedicated header, footer, field list, key and QR-code glyphs. Add navigation glyph regression tests.

## [10.0.9] - 2026-11-24

### Fixed
- Update the forced-preview build contract for combined duplex cards. Require selection of both records, snapshot isolation, and disabled print/export in forced preview modes.

## [10.0.8] - 2026-11-24

### Changed
- Compact wrapping field editors, themed Fluent navigation glyphs and automatic/manual numeric modes.
- Place global print restrictions with field mapping; preserve the original independent restriction and per-field rule semantics.
- Hide inactive dependent settings while retaining values, record precise footer and frame hit regions, and extend editor regression tests.
- Update the user guide and workflow documentation.

## [10.0.7] - 2026-11-24

### Fixed
- Restore the standard label and paragraph separator in the detailed notes-mode tooltip in all three interface languages. Extend the layout self-test to verify the tooltip format.

## [10.0.6] - 2026-11-24

### Fixed
- Localize all fixed appearance choices in Russian, Ukrainian and English, with explicit notes, cut-guide, reverse-background, icon and font-weight labels. Preserve stored codes and font family names. Add clear notes-mode help and an exhaustive choice-localization self-test.

## [10.0.5] - 2026-11-24

### Fixed
- Use the application palette for the template navigation background, hover and selected items in both themes.

## [10.0.4] - 2026-11-24

### Fixed
- Use the Avalonia 12 FocusChangedEventArgs type for template-field focus events.

## [10.0.3] - 2026-11-24

### Fixed
- Validate the twelve numeric stepper controls in the unified editor by role, direction and repeat timing.
- Align the single-preview crossfade build guard with the editor highlight bindings and render invalidation properties.

## [10.0.2] - 2026-11-24

### Fixed
- Update the build guard for the unified preview while retaining both empty-selection checks and verifying isolation of forced-side samples.

## [10.0.1] - 2026-11-24

### Fixed
- Use an approved synthetic credential prefix in the template preview sample without weakening the repository privacy guard.

## [10.0.0] - 2026-11-24

### Changed
- Consolidate mapping and both card faces into one Template tab with one shared template toolbar and a vertical, element-oriented editor.
- Assign each persisted appearance descriptor to exactly one section and group; hide inactive dependent controls without discarding values.
- Keep legacy template JSON, mapping, cloud storage, and print layout semantics unchanged.
- Add element navigation and UI-only highlights in the existing preview, including isolated front/data-back/empty-back samples and a color palette picker.

## [9.2.5] - 2026-11-24

### Fixed
- Standardize desktop action icons on Fluent System Icons Regular, including database actions, filters, print and confirmation dialogs, cloud actions, settings and updater controls.
- Use explicit glyph font sizes matching the icon bounds; enlarge About update, repair and diagnostics buttons to 18 DIP without changing the cloud footer size.
- Use original Microsoft Fluent Regular SVG paths on the offline OAuth result page; retain app branding, database record images and the existing Fluent vector card emblems.
- Replace obsolete build guards that required hand-authored icons with a project-wide Fluent icon policy check.

## [9.2.4] - 2026-11-24

### Fixed
- Use the existing FluentIcons.Avalonia Regular cloud glyphs for every footer synchronization state and for the upload/download conflict actions. Preserve the 15-DIP footer size and its text-matched theme foreground.

## [9.2.3] - 2026-11-24

### Fixed
- Match the cloud footer glyph foreground to the adjacent status text, including after live theme changes.

## [9.2.2] - 2026-11-24

### Fixed
- Keep all cloud footer status icons monochrome and theme-inherited; distinguish synchronization states by glyph and text rather than hard-coded colors.

## [9.2.1] - 2026-11-24

### Fixed
- Update the build-time footer layout guard for the dedicated cloud activity column while preserving contiguous database metadata and right-aligned version text.

## [9.2.0] - 2026-11-24

### Added
- Show pending cloud saves, configuration transfers, verification, conflicts and errors in the main-window footer without flashing on unchanged background checks.
- Wait for the latest portable configuration to be confirmed before normal exit, update or repair; offer retry, cancel exit or explicitly exit with unconfirmed local changes.
- Track the last confirmed cloud snapshot separately from pending local edits, and require an explicit choice when both copies changed.

### Fixed
- Prevent an older completed upload from clearing newer local changes and avoid re-saving portable settings after the shutdown synchronization barrier.
- Preserve the existing SHA-256 verification and cloud destination safeguards.

## [9.1.16] - 2026-11-23

### Fixed
- Synchronize the Native AOT JSON serialization tests with the 146 appearance settings introduced in 9.1.15 (previously 140).
- Restore the appearance preflight check without weakening the registry and persisted-key consistency guards.

## [9.1.15] - 2026-11-23

### Added
- Expose independent font family, point size and font weight settings for both lines of the decorative empty reverse caption.
- Include installed system and bundled monospaced fonts in the reverse-side caption selectors, with Russian, Ukrainian and English labels and help.
- Preserve legacy typography for existing templates through inherited weights/fonts and automatic language-specific sizes.

### Fixed
- Fit oversized decorative reverse captions to the printable width and adjust line spacing to avoid overlaps or printing failures.
- Add AOT-safe descriptor, JSON round-trip and raster-output regression checks for reverse caption typography.

## [9.1.14] - 2026-11-23

### Changed
- Update GitHub Actions to their Node.js 24-based major versions: checkout v7, setup-dotnet v6, upload-artifact v7 and download-artifact v8.
- Retain the .NET 10 Native AOT build, security checks, SHA-256 validation and manual draft-release publishing workflow.

## [9.1.13] - 2026-11-23

### Fixed
- Display clearly labeled Rename and Delete buttons next to Create in all three print-template toolbars.
- Increase the Rename/Delete Fluent icons to a legible 16-DIP size, consistent with other application actions.
- Localize action captions in Russian, Ukrainian and English, while retaining full descriptive tooltips and personal-template-only availability.

## [9.1.12] - 2026-11-23

### Fixed
- Update the build-time UI regression checks for the seven-column template toolbar shared by Mapping, Appearance, and Duplex.
- Verify Rename/Delete actions and their personal-template-only enabled state while retaining immediate template loading, Create next to the picker, and Save at the far right.

## [9.1.11] - 2026-11-23

### Added
- Rename and delete actions for personal print templates in Mapping, Appearance, and Duplex editors.
- Confirmation before deleting a template, with explicit warning about losing unsaved changes.
- Keep complete front/reverse appearance and mapping when renaming; update active template selection.
- Protect built-in templates, reject duplicate or invalid names, and return to the built-in Archive template after deletion.
- Add native self-test assertions for safe personal template naming.

## [9.1.10] - 2026-11-23

### Changed
- Replace the bright blue accent in the dark theme with a subdued slate-blue tint for checked controls, progress indicators and selection accents.
- Soften selected and hovered row backgrounds, preserving clear selection and accessible contrast with light text.
- Leave the light theme, neutral dark-theme surfaces and behavior unchanged.

## [9.1.9] - 2026-11-22

### Fixed
- Preserve the Test connection and Sync now Fluent icons during cloud progress refreshes by updating only their localized text labels, not the buttons' contents.
- Keep status changes (Checking / Synchronizing) and all three UI languages working without losing the action icons.

## [9.1.8] - 2026-11-22

### Changed
- Align the complete application light/dark palette with Cloud Sync's rendered shell: backgrounds, cards, borders, primary/secondary text, header and footer, plus Fluent region colors.
- Preserve Cloud Sync's reference navigation-tab shades separately from the new main-window surfaces, and retain the dedicated printing canvas colors.
- Add real Fluent vector icons to cloud and update actions without replacing localized button content at runtime.

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
