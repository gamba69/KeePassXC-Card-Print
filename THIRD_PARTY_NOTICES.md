# Third-party notices

KeePassXC Card Print's original source and documentation use MIT. Third-party
assets, libraries and fonts remain subject to their respective licenses;
redistributors must review all applicable upstream licenses and notices.

## Software dependencies

The project currently declares Avalonia, Avalonia Desktop/Win32/Skia/HarfBuzz,
Avalonia Fluent theme, SkiaSharp, HarfBuzzSharp, FluentIcons.Avalonia,
DgNet.Keepass, Yubico.YubiKey SDK, QRCoder, ZXing.Net (Apache-2.0), FlashCap
and bblanchon.PDFium.Win32 (PDFium and bundled third-party components).
The exact NuGet package versions are generated into the About tab from the project references.
Find upstream package license information and redistribution requirements in
the corresponding official NuGet packages and repositories. This file does
not purport to relicense any dependency.

Network cameras use SharpRTSPClient 0.8.1 (MIT, https://github.com/jimm98y/SharpRealTimeStreaming)
and its SharpRTSP, SharpRTCP and SharpSRTP dependencies. Consult their NuGet
packages and upstream license texts for redistribution. The Windows Media
Foundation bridge is Card Print code linked against the system SDK.
The HEVC software fallback is libde265 1.1.3 (LGPL-3.0-or-later), dynamically
linked as a replaceable libde265.dll. The distribution contains the exact
corresponding upstream source archive, COPYING and build recipe in
licenses/libde265/; users may replace the DLL with a compatible modified build.
https://github.com/strukturag/libde265/releases/tag/v1.1.3
Optional FFmpeg is a separately executed, unmodified LGPL shared distribution,
not bundled in the portable ZIP. Explicit installation downloads the pinned
archive in NetworkFfmpegManager.cs, verifies its SHA-256 and retains upstream
notices and source references in the local component directory.
System.Security.Cryptography.ProtectedData is used for Windows DPAPI profile
storage (MIT, .NET runtime).

Direct hardware video uses the MIT-licensed nv-codec-headers (n12.2.72.0),
Intel oneVPL dispatcher (v2023.3.1, MIT) and AMD AMF headers (v1.4.35, MIT).
The SDK Git commits are pinned in tools/build-vendor-video.ps1; license notices
are shipped in licenses/vendor-video/. NVIDIA and AMD driver runtimes are not
redistributed; they are loaded from the installed Windows drivers. The Intel
dispatcher is bundled, but the hardware runtime is supplied by the Intel driver.

## Downloaded specialist fonts

The build downloads eight variable font files with licensing documents from
the corresponding upstream projects: Atkinson Hyperlegible Mono, Atkinson
Hyperlegible Next, JetBrains Mono, Recursive, IBM Plex Mono, Source Code Pro,
Geist Mono and Cascadia Mono. The distribution bundles license texts alongside
font files. See `fonts/README.txt`, the eight individual files in `fonts/licenses/`,
and `fonts/FONT_SHA256.txt` for exact bundled font revisions. Font files
are present in the source repository; no font download is performed at build time.
Font licenses and attribution are separate from the application's MIT license.

## Artwork

- `assets/CardPrint_black.ico`, `assets/CardPrint_white.ico` and associated
  frame images are supplied application brand artwork. Confirm distribution
  rights before publication; the code's MIT license does not itself prove
  ownership of uploaded icons.
- KDBX entry icons, when displayed from a user's database, are user-provided
  content rather than application-owned distributable artwork.

## Names and affiliations

KeePassXC and other named projects are trademarks or project names of their
respective owners. KeePassXC Card Print is independent and does not imply
official KeePassXC endorsement.
