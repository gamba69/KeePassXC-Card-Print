# Third-party notices

KeePassXC Card Print's original source and documentation use MIT. Third-party
assets, libraries and fonts remain subject to their respective licenses;
redistributors must review all applicable upstream licenses and notices.

## Software dependencies

The project currently declares Avalonia, Avalonia Desktop/Win32/Skia/HarfBuzz,
Avalonia Fluent theme, SkiaSharp, HarfBuzzSharp, FluentIcons.Avalonia,
DgNet.Keepass, Yubico.YubiKey SDK and QRCoder. The exact NuGet package
versions are generated into the About tab from the project references.
Find upstream package license information and redistribution requirements in
the corresponding official NuGet packages and repositories. This file does
not purport to relicense any dependency.

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
