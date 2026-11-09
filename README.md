# KeePassXC Card Print

**Version 6.0.8 · Windows x64 · .NET 10 Native AOT**

KeePassXC Card Print is a portable desktop application for preparing and printing
compact account-information cards from a KeePassXC-compatible KDBX database.
It provides a highly configurable layout, direct printing, vector PDF export,
and a live Skia-based preview. It is an independent application and is **not
an official KeePassXC project**.

> [!WARNING]
> The cards and exported documents may contain passwords, TOTP secrets,
> recovery codes, and other sensitive account data. Use a trusted local
> computer and printer. Keep printed cards, exported PDFs, and backups secure.

## Features

- Open local KDBX databases with a master password, optional key file and
  supported YubiKey HMAC-SHA1 challenge-response flow.
- Select individual entries for printing, use quick filters and track printed
  card status. No entry is shown in the preview when no entries are selected.
- Map data fields and configure labels, spacing, grouping, typography,
  checkboxes, URL handling, phone-number formatting, icons, QR codes and notes.
- Design fronts and backs with built-in presets or saved personal templates.
- Preview the same document model used for printing, with zoom, pan, caching
  and optional rendering diagnostics.
- Prepare A3/A4/A5/A6/Letter/Legal sheets; print only selected pockets and
  control front/back order, calibration offsets and manual duplex handling.
- Print directly through the Windows XPS vector backend or GDI raster fallback;
  export vector PDF or raster images where available in the UI.
- Switch between system and portable settings, and use Russian, Ukrainian or
  English UI translations with light/dark/system appearance.
- View compiled dependency versions, selected rendering details, and safe
  preview performance statistics on the **About** tab.

## Requirements

**Run:** Windows x64, a supported printer/driver if printing, and access to
local KDBX/key files. The published Native AOT application does not require an
installed .NET runtime or Python. Supported YubiKey use depends on the device
and its Windows access permissions.

**Build from source:** Windows x64; a patched .NET 10 SDK; Visual Studio 2022
or newer Build Tools with MSVC x64 and the Windows SDK; PowerShell 5.1; and
internet access for NuGet packages. All eight specialist variable TTF fonts
and their OFL licenses are included in this repository; no font downloads occur. Refer to [Building](docs/building.md) for precise steps.

A local `build.bat` publishes the runnable application to `publish/` and the
verified portable ZIP plus SHA-256 to `dist/` (both excluded from Git).
GitHub Actions automatically builds and tests pushes and pull requests on
Windows; releases are created and published only through the separate manual
[release procedure](docs/releasing.md).

Linux and macOS implementations **are not included**. The shared code is
structured around platform services for possible future hosts, but those hosts
have not been implemented or validated.

## Getting started

1. Obtain the Windows portable binary package from a trusted distribution,
   extract it to a folder you control, and run `KeePassXCCardPrint.exe`.
2. Choose the KDBX database and, if applicable, its key file and YubiKey mode.
   Enter the master password to unlock the database.
3. On **Database and entries**, select the entries you intend to print.
4. Configure **Mapping**, **Design**, and **Two sides** as needed. Use built-in
   presets as read-only starting points; save an editable personal copy.
5. Verify both sides and the physical card positions in the preview.
6. Choose the target printer and its capabilities. Review duplex, calibration,
   the selected print method, and any confirmation for file-producing printers.
7. After the actual job completes, confirm the printed entries as appropriate;
   submitting a spool job alone does not prove that paper was printed.

The **About** tab is located at the right edge of the editor's tab strip. The
preview remains separate on the right of the main window.

See [Repository privacy](docs/repository-privacy.md) for source-publication
safeguards and the automated pre-build secret check.

## Portable settings

A writable `data/` directory next to the application is the portable settings
location. When there is no explicit storage-mode selection, an existing
portable `data/` directory takes precedence. In system mode, Windows stores
settings under `%LOCALAPPDATA%\keepassxccardprint`. The storage-mode selection
itself is stored in `storage-mode.txt` inside that system location, not in the
registry.

Passwords, TOTP values and KDBX database contents are not intentionally saved
in application configuration. However, if explicitly enabled, saved settings
can include **paths** to the KDBX database and key file, hashed selection IDs,
printer names, entry UUIDs and print history. Do not treat a settings backup as
anonymous. See [Configuration](docs/configuration.md) and
[Security](SECURITY.md).

**First-release configuration:** This source tree uses its own 6.0.8 settings
files and does not import configurations from earlier implementations. If you
have settings in another installation, retain your backup and configure this
installation separately.

## Guides

| Topic | Documentation |
| --- | --- |
| Preparing the application | [Building and portable distribution](docs/building.md) |
| Daily use | [Card design and workflow](docs/usage.md) |
| Front/back and printing | [Printing, duplex, calibration and exports](docs/printing.md) |
| Settings and data locations | [Configuration and state files](docs/configuration.md) |
| Architecture and portability | [Architecture and platform boundaries](docs/architecture.md) |
| Test commands and results | [Testing](docs/testing.md) |
| Tag, draft, archive and manual publication | [Release procedure](docs/releasing.md) |
| Common problems | [Troubleshooting](docs/troubleshooting.md) |
| Responsible disclosure | [Security](SECURITY.md) |
| Building on the code | [Contributing](CONTRIBUTING.md) |

## Privacy and security

KDBX reading is local. Preview caches are held in process memory. Printing
through a system spooler may cause the operating system or printer to retain
job data; exported PDF/raster files are sensitive documents. Never include
secrets or database screenshots in bug reports. The About tab's copied
technical summary excludes KDBX paths and entry data by design.

## Licensing

The application's original source code and documentation are available under
the [MIT License](LICENSE). Bundled third-party artwork, fonts
and libraries retain their own licenses; MIT does not replace these terms.
See [Third-party notices](THIRD_PARTY_NOTICES.md) before redistribution.
