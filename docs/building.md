# Building KeePassXC Card Print on Windows

## Toolchain

The current release targets **Windows x64** and uses **.NET 10 Native AOT**,
Avalonia, SkiaSharp 4, HarfBuzzSharp and an MSVC-built Windows XPS Object Model
bridge. The project uses a pinned NuGet graph for reproducible dependency
selection. The build performs security/version checks on the .NET SDK and
Native AOT compiler; follow any build error requesting an updated patched SDK.

Install:

1. A supported, patched .NET 10 SDK.
2. Visual Studio 2022/2025 Build Tools with **Desktop development with C++**,
   the x64 MSVC toolset, and a Windows SDK.
3. Windows PowerShell 5.1 (included with supported Windows releases).
4. Internet connectivity for NuGet package restore, unless already cached.
   All specialist fonts and their licenses are included in the repository.

No Python runtime, system-wide font installation or installer authoring tools
are needed.

## Standard build

Extract the repository into a clean, writable directory and run:

```bat
build.bat
```

When launched by double-clicking, the console **pauses at the end** whether
successful or failed. For CI or unattended scripts use:

```bat
build.bat --no-pause
```

This returns a normal exit code without waiting for a keypress. Aliases
`/no-pause` and `-nopause` are also accepted.

This invokes `tools/check_build_syntax.ps1` before the main build script. The
main script performs source and platform-contract checks, verifies bundled fonts
locally without network access, builds the Windows native XPS bridge, compiles
and tests a managed executable, publishes Native AOT, and then exercises the published application's tests.
Tests run automatically in the standard build; do not bypass them when
validating a change.

The build uses two fixed, ignored directories at the repository root:

```text
publish/    # Complete runnable Windows application (EXE, DLLs, fonts, notices)
dist/       # KeePassXCCardPrint-6.0.7-win-x64.zip and its .zip.sha256
```

Each new build clears the previous `publish/` and `dist/` contents. It does
**not** create timestamped build folders or a latest-build index. Intermediate
MSBuild/MSVC files stay under `source/keepassxccardprint/bin/` and
`source/keepassxccardprint/obj/`, which are ignored by Git. If `publish/data/` exists, the build stops instead of
silently deleting personal settings. For local development, portable settings
are stored in the repository's `data/` rather than in `publish/`.

Only the finished and tested `publish/` files are packaged into `dist/`.
The ZIP and checksum are checked for integrity. The .NET runtime is compiled
into the Native AOT executable, while the native XPS DLL, verified fonts and
licenses are shipped beside the EXE.

`run.bat` starts `publish/KeePassXCCardPrint.exe` directly.

## Font resources

The eight original variable TTF fonts are **included in `fonts/`** together
with their eight SIL OFL 1.1 licenses in `fonts/licenses/`. The canonical lists
are in `PORTABLE_FONT_FILES.txt` and `PORTABLE_FONT_LICENSES.txt`.
`FONT_SHA256.txt` pins all eight original TTF files by hash.

`tools/verify_fonts.ps1` runs on every build and checks names, count, actual
variable-font tables, file hashes, and accompanying license texts. The build
**never downloads fonts**. A missing or modified file fails the build early,
rather than producing a release with incomplete typography. Only approved
fonts and their matching license files are copied to `publish/fonts/`.
GitHub Actions uses the same verification from the checked-out tag; it has no
separate font download or caching stage.

## Reproducibility and version

The **single application version** comes from `<Version>` in
`source/keepassxccardprint/KeePassXCCardPrint.csproj`. MSBuild generates
`AppBuildInfo.g.cs` so the UI, About tab and executable use the same value.
A separate compile-time dependency manifest derives package versions from
`PackageReference` declarations. Windows-specific package declarations are
kept in `platforms/windows/windows-packages.props`.

The current version is **6.0.7**. Do not add version numbers to application
source files or XAML titles. Platform folders and all other repository folders
use lowercase names; source filenames and class names retain their natural
C# casing.

## CI

`.github/workflows/draft-release.yml` is a **manual-only** GitHub Actions
workflow. It checks out an existing tag, builds/tests the Windows package,
verifies ZIP and SHA-256, and attaches them to an existing **draft** release.
It never creates or publishes releases. See [Releasing](releasing.md) for the
complete operator procedure. No Linux/macOS binaries or installers are produced.

## Build failures

Every source assertion and runtime self-test uses `[PASS]` or `[FAIL]` prefixes.
The build returns a non-zero exit code when a required condition fails. Retain
the full text log and the first `[FAIL]` line when diagnosing a problem.
See [Testing](testing.md) and [Troubleshooting](troubleshooting.md).
