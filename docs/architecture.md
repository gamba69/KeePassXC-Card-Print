# Architecture and portability boundary

## Intent and current status

KeePassXC Card Print 6.0.8 is **Windows x64 only**. Its shared rendering,
models, business rules and Avalonia UI are organized to minimize assumptions
about the operating system. Linux and macOS hosts do not exist yet. This is a
source-level architectural boundary, **not a claim of functional portability
or verified cross-platform compilation**.

The current distribution uses one .NET project and a Windows composition root.
Platform code is physically isolated beneath `platforms/windows`; Windows-only
NuGet declarations are imported from that folder. A future independently
compilable cross-platform host/project would still require separate build,
resource and runtime validation.

## Source layout

```text
.github/workflows/               Windows CI
source/keepassxccardprint/
  assets/                        Embedded images and icon frames
  controls/                      Avalonia controls
  models/                        KDBX record view models
  rendering/                     Layout, text shaping, Skia, PDF, sheet planning
  services/                      State, mapping, card design and preferences
  viewmodels/                    App presentation logic
  platforms/
    IPlatformServices.cs         Shared platform contracts
    windows/
      Program.cs                 Windows process entry point
      WindowsHost.cs             Guarded host initialization
      WindowsPlatformServices.cs Host implementation
      windows-packages.props     Windows-only NuGet packages
      input/                     Password-input language scope
      native/                    Windows XPS COM C++ bridge
      printing/                  Windows XPS / GDI drivers
      security/                  Windows security-key integration
      storage/                   Windows configuration location and selector
      tests/                     Windows integration and card-engine smoke tests
  KeePassXCCardPrint.csproj      Current Windows application packaging
fonts/                           Bundled original TTFs, OFL licenses, manifests and SHA-256
tools/                           Build scripts and static checks
docs/                            User and developer documentation
```

All **directories** in the source tree use lowercase names. Public type names,
resource identifiers and filenames preserve their intentionally defined case.

## Shared contracts

`IPlatformServices` exposes:

- `IPrinterService`: printer discovery, supported resolutions, device options,
  duplex capabilities, driver properties and safe job submission.
- `IConfigurationEnvironment`: portable/system roots and storage-mode policy.
- `ISecurityKeyService`: available devices and challenge-response requests.
- `IPasswordInputLanguageScope`: optional keyboard-layout changes.
- Window frame insets, filesystem comparison/path normalization, font names,
  resource directory and platform-specific dependency descriptions.

Windows implementations are registered once by `WindowsHost.Configure()`
after an explicit OS guard. Shared code does not use Win32, Registry, native
print spoolers or C++ P/Invoke symbols directly. The architectural check in
`tools/build.ps1` scans **every shared C# source**, including the main window,
for disallowed native APIs, and treats `CA1416` as an error.

Avalonia handles ordinary cross-platform UI concepts (controls, layout,
styling, theme, file picking) where the framework provides a suitable API;
those are not wrapped unnecessarily.

## Rendering pipeline

1. KDBX services extract selected view-model records without writing secrets
   to diagnostic files.
2. Field rules and card layout produce logical card components.
3. Text runs are shaped with HarfBuzz; supported face instances are resolved
   by a unified font engine.
4. Shared Skia geometry renders card and sheet preview display lists and
   vector PDF. Preview caching is in memory with cancellation and viewport
   preservation.
5. The platform print service consumes ready-to-submit physical sheets and
   chooses a Windows vector or raster spool path.

The Windows XPS bridge is strictly a print backend, not a second layout
engine. Platform calls cannot alter shared card geometry or model selection.

## Security properties

- No raw KDBX entry values in application settings, diagnostic summaries or
  synthetic self-tests.
- A submitted print job is not marked physically printed until confirmed.
- No automatic retry after an uncertain spooler outcome.
- Printed/exported secrets and OS-managed spooler files require normal
  operational protection.
- The About dependency manifest is generated during compilation, with no
  dynamic assembly scan that would interfere with Native AOT.

## Adding a future host

A future host needs its own guarded startup, platform-service implementation,
printer backend, YubiKey permissions/testing, storage locations, native font
availability, runtime packages and CI. A new host must be validated on the
actual OS and with a real printer. No Linux/macOS placeholders are shipped now.
